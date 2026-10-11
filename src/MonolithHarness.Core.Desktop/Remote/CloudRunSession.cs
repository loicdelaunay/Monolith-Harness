using System.Collections.Concurrent;
using System.Text.Json;

namespace MonolithHarness.Core.Remote;

public sealed record CloudAnswer(bool Cancelled, List<List<string>> Answers);

/// <summary>What a host does with the progress of a cloud run (the Desktop paints its chat, the CLI/engine host emits events).</summary>
public interface ICloudSink
{
    /// <summary>Queue and run states: queued (with position), running, awaiting approval, completed, failed, cancelled.</summary>
    Task StateAsync(ServerRun run);
    /// <summary>Engine <c>stream</c> payload: messageId, text, reasoning, html, speed, tokens...</summary>
    Task StreamAsync(JsonElement data);
    /// <summary>Engine <c>message</c> payload: <c>{chatId, message:{id, role, content, state, ...}}</c>.</summary>
    Task MessageAsync(JsonElement data);
    Task StatusAsync(string text);
    /// <summary>A tool approval for this run. Returns "allow", "always" or "deny"; may take as long as the user needs.</summary>
    Task<string> PermissionAsync(ServerApproval approval, CancellationToken ct);
    Task<CloudAnswer> QuestionAsync(ServerApproval approval, CancellationToken ct);
}

/// <summary>
/// Sends one request to a cloud project and follows it to the end: real-time events when available, polling as a safety net,
/// approvals and questions routed to the host (the run is the user's: nobody else is asked), cancellation propagated to the server.
/// </summary>
public sealed class CloudRunSession(ServerConnection connection, ICloudSink sink)
{
    static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(3);
    readonly ConcurrentDictionary<Guid, CancellationTokenSource> approvals = new();
    readonly ConcurrentDictionary<Guid, byte> handled = new();
    readonly TaskCompletionSource<ServerRun> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    ServerRun? current;
    CancellationToken lifetime;

    public Guid? RunId => current?.Id;

    public async Task<ServerRun> RunAsync(Guid project, int chat, string text, Guid provider, string model, CancellationToken ct)
    {
        lifetime = ct;
        var api = connection.Api;
        using var accountWatch = await connection.WatchAsync("user", OnAccountEventAsync);
        current = await api.SendRequestAsync(project, chat, text, provider, model, CancellationToken.None);
        await Guard(() => sink.StateAsync(current));
        using var runWatch = await connection.WatchAsync("run:" + current.Id, OnRunEventAsync);
        using var cancel = ct.Register(() => _ = CancelAsync(api, current.Id));
        try
        {
            // Events may have been missed while subscribing: look at what is pending and at the current state once.
            await CatchUpAsync(api);
            while (true)
            {
                var done = await Task.WhenAny(finished.Task, Task.Delay(PollEvery));
                if (done == finished.Task) return await finished.Task;
                try
                {
                    var latest = await api.RunAsync(current.Id);
                    if (latest.State != current.State || latest.QueuePosition != current.QueuePosition) await OnStateAsync(latest);
                    if (IsFinal(latest.State)) finished.TrySetResult(latest);
                }
                catch (ServerException ex) when (ex.Code is "unreachable" or "timeout") { /* the real-time channel or the next poll will recover */ }
            }
        }
        finally { foreach (var item in approvals.Values) item.Cancel(); }
    }

    async Task CancelAsync(ServerApi api, Guid run)
    {
        try { await api.CancelRunAsync(run); }
        catch (ServerException) { /* the run already ended or the server is gone */ }
    }

    async Task CatchUpAsync(ServerApi api)
    {
        foreach (var item in await api.ApprovalsAsync())
            if (item.RunId == current!.Id) HandleApproval(item);
        var latest = await api.RunAsync(current!.Id);
        if (latest.State != current.State) await OnStateAsync(latest);
        if (IsFinal(latest.State)) finished.TrySetResult(latest);
    }

    async Task OnAccountEventAsync(ServerEvent evt)
    {
        if (current == null) return;
        if (evt.Name == "approval.requested" && Deserialize<ServerApproval>(evt.Data) is { } approval && approval.RunId == current.Id) HandleApproval(approval);
        else if (evt.Name == "approval.closed" && Deserialize<ServerApproval>(evt.Data) is { } closed && approvals.TryRemove(closed.Id, out var source)) source.Cancel();
        await Task.CompletedTask;
    }

    async Task OnRunEventAsync(ServerEvent evt)
    {
        switch (evt.Name)
        {
            case "run.state":
                if (Deserialize<ServerRun>(evt.Data) is { } run) await OnStateAsync(run);
                break;
            case "stream": await Guard(() => sink.StreamAsync(evt.Data)); break;
            case "message": await Guard(() => sink.MessageAsync(evt.Data)); break;
            case "status":
                if (evt.Data.ValueKind == JsonValueKind.Object && evt.Data.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    await Guard(() => sink.StatusAsync(text.GetString() ?? ""));
                break;
        }
    }

    async Task OnStateAsync(ServerRun run)
    {
        current = run;
        await Guard(() => sink.StateAsync(run));
        if (IsFinal(run.State)) finished.TrySetResult(run);
    }

    void HandleApproval(ServerApproval approval)
    {
        if (!handled.TryAdd(approval.Id, 0)) return;
        var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        approvals[approval.Id] = source;
        // Never block the event reader: a person may take minutes to answer.
        _ = Task.Run(async () =>
        {
            try
            {
                var api = connection.Api;
                if (approval.Kind == "question")
                {
                    var answer = await sink.QuestionAsync(approval, source.Token);
                    await api.AnswerAsync(approval.Id, answer.Cancelled, answer.Answers.Select(x => (IReadOnlyList<string>)x).ToList());
                }
                else await api.DecideAsync(approval.Id, await sink.PermissionAsync(approval, source.Token));
            }
            catch (OperationCanceledException) { }
            catch (ServerException ex) when (ex.Code is "approval_closed" or "not_found") { }
            catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "server.approval_failed", ex); }
            finally { approvals.TryRemove(approval.Id, out _); }
        }, CancellationToken.None);
    }

    static bool IsFinal(string state) => state is "completed" or "failed" or "cancelled";

    static T? Deserialize<T>(JsonElement data) where T : class
    {
        try { return data.ValueKind == JsonValueKind.Object ? data.Deserialize<T>(ServerApi.Json) : null; }
        catch (JsonException) { return null; }
    }

    static async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { AppLog.Write(AppLogLevel.Warning, "server.sink_failed", ex); }
    }
}
