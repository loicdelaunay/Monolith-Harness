using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Realtime;
using MonolithHarness.Server.Security;
using MonolithHarness.Server.Services;

namespace MonolithHarness.Server.Workers;

/// <summary>Runs one started request. Replaceable so the queue can be tested without spawning workers.</summary>
public interface IRunExecutor
{
    Task ExecuteAsync(Guid runId, ActiveRun active);
}

/// <summary>How a worker process is launched: this executable again, or <c>dotnet MonolithHarness.Server.dll</c> in development.</summary>
public sealed class WorkerLauncher
{
    public string FileName { get; private set; } = Environment.ProcessPath ?? throw new InvalidOperationException("The server executable path is unknown.");
    public IReadOnlyList<string> Prefix { get; private set; } =
        Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? [System.Reflection.Assembly.GetEntryAssembly()!.Location] : [];

    public void Use(string fileName, params string[] prefix) { FileName = fileName; Prefix = prefix; }
}

/// <summary>Knows where this server answers on the loopback interface: workers reach the provider relay there.</summary>
public sealed class ServerEndpoints
{
    public string LoopbackBase { get; private set; } = "";

    public void Capture(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features
            .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses ?? [];
        var http = addresses.Select(x => new Uri(x.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost"))).FirstOrDefault(x => x.Scheme == "http")
            ?? throw new InvalidOperationException("Cloud workers need the HTTP port: it must stay enabled beside HTTPS.");
        LoopbackBase = $"http://127.0.0.1:{http.Port}";
    }
}

/// <summary>Detects once per minute whether a container runtime (Docker or Podman) with the sandbox image is available.</summary>
public sealed class ContainerProbe
{
    readonly SemaphoreSlim gate = new(1, 1);
    DateTime checkedAt = DateTime.MinValue;
    bool available;
    string message = "";

    public async Task<(bool Available, string Message)> CheckAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - checkedAt < TimeSpan.FromMinutes(1)) return (available, message);
            try { message = await SandboxContainer.CheckAsync(ct); available = true; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { available = false; message = ex.Message.Split('\n')[0]; }
            checkedAt = DateTime.UtcNow;
            return (available, message);
        }
        finally { gate.Release(); }
    }
}

/// <summary>Counts time spent working, not time spent waiting for a human to approve something.</summary>
sealed class ActiveClock
{
    readonly Stopwatch watch = Stopwatch.StartNew();
    int waiting;
    public TimeSpan Elapsed => watch.Elapsed;
    public void Pause() { if (Interlocked.Increment(ref waiting) == 1) watch.Stop(); }
    public void Resume() { if (Interlocked.Decrement(ref waiting) == 0) watch.Start(); }
}

/// <summary>
/// Runs one request of a cloud project in an isolated worker process and reports its progress.
/// The worker only sees the project folder and a run-scoped token for the provider relay; the limits are enforced here.
/// </summary>
public sealed class CloudRunExecutor(ServerPaths paths, WorkerLauncher launcher, IDbContextFactory<ServerDb> factory, SettingsService settings, RunService runs, ApprovalService approvals,
    ProviderService providers, SessionService sessions, ProjectStore store, ProjectFiles files, ContainerProbe containers, ServerEndpoints endpoints, RealtimeHub hub,
    AuditService audit, ILogger<CloudRunExecutor> log) : IRunExecutor
{
    sealed class Holder { public WorkerProcess? Worker; public string? KillReason; }

    public async Task ExecuteAsync(Guid runId, ActiveRun active)
    {
        string state, error = "", changes = "";
        try { (state, error, changes) = await CoreAsync(runId, active); }
        catch (Exception ex)
        {
            log.LogError(ex, "Run {Run} failed", runId);
            state = active.Reason != null ? RunStates.Cancelled : RunStates.Failed;
            error = active.Reason ?? ex.Message;
        }
        finally
        {
            await approvals.ExpireRunAsync(runId);
            await sessions.RevokeRunAsync(runId);
        }
        await runs.SetStateAsync(runId, state, error, changes);
        await audit.WriteAsync("run." + state, runId.ToString(), error, state != RunStates.Failed);
    }

    async Task<(string State, string Error, string Changes)> CoreAsync(Guid runId, ActiveRun active)
    {
        RunRequest run;
        CloudProject project;
        await using (var db = await factory.CreateDbContextAsync())
        {
            run = await db.Runs.AsNoTracking().SingleAsync(x => x.Id == runId);
            project = await db.Projects.AsNoTracking().SingleAsync(x => x.Id == run.ProjectId);
        }
        var ct = active.Cancellation.Token;
        static (string, string, string) Fail(string message) => (RunStates.Failed, message, "");

        if (Environment.IsPrivilegedProcess && !settings.Bool(SettingKeys.AllowElevatedWorkers))
            return Fail("Workers refuse to start while the server runs with administrator privileges. Restart it as a normal user, or allow elevated workers in the settings.");
        var (sandbox, containerMessage) = await containers.CheckAsync(ct);
        if (!sandbox && settings.Bool(SettingKeys.RequireContainer))
            return Fail("Container isolation is required but unavailable. " + containerMessage);
        var quota = settings.Int(SettingKeys.ProjectDiskMb) * 1024L * 1024L;
        if (files.Size(project.Id) > quota) return Fail("The project exceeds its disk quota.");
        var (provider, model) = await providers.ResolveAsync(run.RequesterId, run.ProviderId, run.Model);

        // Code-executing tools exist only inside the container; without it the agent is limited to file tools.
        var skills = "sources,write_sources,code_search,patch_sources" + (sandbox ? ",terminal" : "") + (!sandbox && settings.Bool(SettingKeys.AllowNetworkTools) ? ",web" : "");
        var timeout = TimeSpan.FromSeconds(settings.Int(SettingKeys.TimeoutSeconds));
        var (token, _) = await sessions.CreateAsync(run.RequesterId, SessionKinds.Run, "run " + run.Id, "local", "worker", timeout + TimeSpan.FromMinutes(30), run.Id);
        var job = new WorkerJob(paths.ProjectDatabase(project.Id), paths.ProjectFiles(project.Id), project.Name, run.ChatId,
            $"{endpoints.LoopbackBase}/api/providers/{provider.Id}/relay", token, model.Id, model.ContextLimit, model.SupportsImages, skills, sandbox, PermissionModes.Ask);
        Directory.CreateDirectory(paths.Jobs);
        var jobFile = Path.Combine(paths.Jobs, run.Id.ToString("N") + ".json");
        await File.WriteAllTextAsync(jobFile, JsonSerializer.Serialize(job, WorkerMode.Json), ct);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(jobFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Directory.CreateDirectory(paths.ProjectTemp(project.Id));

        var clock = new ActiveClock();
        var holder = new Holder();
        var firstNewMessage = await store.MaxMessageIdAsync(project.Id, run.ChatId);
        using var stopWatchdog = new CancellationTokenSource();
        Task watchdog = Task.CompletedTask;
        try
        {
            holder.Worker = WorkerProcess.Start(paths, launcher, jobFile, paths.ProjectEngine(project.Id), paths.ProjectTemp(project.Id), paths.ProjectEngine(project.Id),
                (method, parameters) => HostRequestAsync(run, clock, method, parameters, ct), message => OnEvent(run, holder, clock, message, ct));
            var worker = holder.Worker;
            watchdog = Task.Run(() => WatchAsync(worker, holder, clock, project.Id, timeout, quota, stopWatchdog.Token));
            await worker.WaitReadyAsync(TimeSpan.FromSeconds(90), ct);
            var engineProvider = await store.EngineProviderIdAsync(project.Id);

            var send = worker.CallAsync("send", new { chatId = run.ChatId, providerId = engineProvider, text = run.Prompt }, CancellationToken.None);
            var cancelled = new TaskCompletionSource();
            using (ct.Register(() => cancelled.TrySetResult()))
            {
                if (await Task.WhenAny(send, cancelled.Task) == cancelled.Task && !send.IsCompleted)
                {
                    try { await worker.CallAsync("stop", new { chatId = run.ChatId }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
                    await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(8)));
                    worker.Kill();
                    return (RunStates.Cancelled, active.Reason ?? "Cancelled.", "");
                }
            }
            await send;
            var changes = sandbox && settings.Bool(SettingKeys.AutoApplySandbox) ? await ApplySandboxAsync(worker, run) : "";
            return (RunStates.Completed, "", changes);
        }
        catch (Exception ex) when (holder.KillReason != null) { _ = ex; return Fail(holder.KillReason); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var detail = holder.Worker?.ErrorTail.Trim() ?? "";
            return Fail(ex.Message + (detail.Length > 0 && !ex.Message.Contains(detail) ? "\n" + detail : ""));
        }
        finally
        {
            // The user message of this request belongs to whoever sent it (a safety net: the message event normally records it first).
            try { await store.FillAuthorsAsync(project.Id, run.ChatId, firstNewMessage, run.RequesterId); } catch (Exception ex) { log.LogWarning(ex, "Message authors were not recorded"); }
            stopWatchdog.Cancel();
            try { await watchdog; } catch (Exception) { }
            if (holder.Worker is { } worker)
            {
                worker.CloseInput();
                await worker.WaitForExitAsync(TimeSpan.FromSeconds(5));
                worker.Dispose();
            }
            try { File.Delete(jobFile); } catch (IOException) { }
        }
    }

    /// <summary>The sandbox works on a copy of the sources: its reviewed changes are merged into the project at the end.</summary>
    async Task<string> ApplySandboxAsync(WorkerProcess worker, RunRequest run)
    {
        var review = await worker.CallAsync("sandbox.review", new { chatId = run.ChatId }, CancellationToken.None);
        var count = review?["count"]?.GetValue<int>() ?? 0;
        if (count == 0) return "";
        await worker.CallAsync("sandbox.apply", new { chatId = run.ChatId, token = review!["token"]!.GetValue<string>() }, CancellationToken.None);
        return $"{count} file(s) changed.\n{review["diff"]?.GetValue<string>()}";
    }

    async Task WatchAsync(WorkerProcess worker, Holder holder, ActiveClock clock, Guid project, TimeSpan timeout, long diskQuota, CancellationToken stop)
    {
        var memoryLimit = settings.Int(SettingKeys.MemoryMb) * 1024L * 1024L;
        var cpuLimit = TimeSpan.FromSeconds(settings.Int(SettingKeys.CpuSeconds));
        var checks = 0;
        try
        {
            while (!stop.IsCancellationRequested && !worker.HasExited)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stop);
                string? reason = null;
                if (clock.Elapsed > timeout) reason = $"The time limit of {timeout.TotalMinutes:0.#} minutes was reached.";
                else if (worker.MemoryBytes() > memoryLimit) reason = $"The worker exceeded its memory limit of {memoryLimit / 1024 / 1024} MB.";
                else if (worker.CpuTime() > cpuLimit) reason = $"The worker exceeded its CPU limit of {cpuLimit.TotalSeconds:0} seconds.";
                else if (++checks % 5 == 0 && files.Size(project) > diskQuota) reason = "The project exceeded its disk quota.";
                if (reason == null) continue;
                holder.KillReason = reason;
                worker.Kill();
                return;
            }
        }
        catch (OperationCanceledException) { }
    }

    async Task<JsonNode?> HostRequestAsync(RunRequest run, ActiveClock clock, string method, JsonObject parameters, CancellationToken ct)
    {
        if (method != "permission") throw new NotSupportedException("Host operation unavailable on the server: " + method);
        var outcome = await WaitForUserAsync(run, clock, () => approvals.RequestAsync(run, "permission", Text(parameters["title"]), Text(parameters["details"]), Text(parameters["command"]),
            parameters["oneTimeOnly"]?.GetValue<bool>() ?? false, "", ct));
        return JsonValue.Create(outcome.Choice is "allow" or "always" ? outcome.Choice : "deny");
    }

    void OnEvent(RunRequest run, Holder holder, ActiveClock clock, JsonObject message, CancellationToken ct)
    {
        var name = message["event"]!.GetValue<string>();
        message.Remove("event");
        if (name == "question" && message["id"] is JsonValue id)
        {
            var questions = message["questions"]?.ToJsonString() ?? "[]";
            var engineId = id.GetValue<string>();
            _ = Task.Run(async () =>
            {
                var outcome = await WaitForUserAsync(run, clock, () => approvals.RequestAsync(run, "question", "Question de l’agent / Agent question", "", "", false, questions, ct));
                var answer = outcome.AnswerJson.Length > 0 ? JsonNode.Parse(outcome.AnswerJson)!.AsObject() : new JsonObject { ["cancelled"] = true, ["answers"] = new JsonArray() };
                answer["id"] = engineId;
                answer["chatId"] = run.ChatId;
                try { await holder.Worker!.CallAsync("question.answer", answer, CancellationToken.None); } catch (Exception ex) { log.LogWarning(ex, "Question answer was not accepted"); }
            }, CancellationToken.None);
        }
        // The sender of a user message is recorded before clients hear about it, so that they can show who wrote it.
        if (name == "message" && message["message"] is JsonObject sent && sent["role"]?.GetValue<string>() == "user" && sent["id"] is JsonValue sentId && sentId.TryGetValue<int>(out var messageId))
            try { store.RecordAuthorAsync(run.ProjectId, run.ChatId, messageId, run.RequesterId).GetAwaiter().GetResult(); }
            catch (Exception ex) { log.LogWarning(ex, "Message author was not recorded"); }
        hub.Publish(RealtimeHub.RunTopic(run.Id), name, message);
    }

    /// <summary>The run shows "awaiting approval" and its clock stops while a person decides.</summary>
    async Task<ApprovalOutcome> WaitForUserAsync(RunRequest run, ActiveClock clock, Func<Task<ApprovalOutcome>> ask)
    {
        clock.Pause();
        await runs.SetStateAsync(run.Id, RunStates.AwaitingApproval);
        try { return await ask(); }
        finally
        {
            clock.Resume();
            if (approvals.PendingCount(run.Id) == 0) await SetRunningAsync(run.Id);
        }
    }

    async Task SetRunningAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var current = await db.Runs.AsNoTracking().Where(x => x.Id == id).Select(x => x.State).SingleOrDefaultAsync();
        if (current == RunStates.AwaitingApproval) await runs.SetStateAsync(id, RunStates.Running);
    }

    /// <summary>
    /// The engine describes a terminal command as an object, other approvals as plain text: both become text for the approval
    /// (the object stays valid JSON so that clients can render it as a command).
    /// </summary>
    internal static string Text(JsonNode? node) => node switch
    {
        null => "",
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => node.ToJsonString()
    };
}
