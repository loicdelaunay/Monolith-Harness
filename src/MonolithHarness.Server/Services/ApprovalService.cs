using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Realtime;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

public sealed record ApprovalView(Guid Id, Guid RunId, Guid ProjectId, string Kind, string Title, string Details, string Command, JsonElement? Questions, bool OneTimeOnly, string State, DateTime CreatedUtc, DateTime? DecidedUtc);

public sealed record ApprovalOutcome(string State, string Choice, string AnswerJson);

/// <summary>
/// Tool approvals and agent questions of cloud runs. They go to the user who requested the run (never to an administrator
/// or another member) and stay pending until answered or until the configured delay expires.
/// </summary>
public sealed class ApprovalService(IDbContextFactory<ServerDb> factory, RealtimeHub hub, SettingsService settings, AuditService audit)
{
    sealed record Waiting(Guid RunId, Guid UserId, bool Question, TaskCompletionSource<ApprovalOutcome> Completion);

    readonly ConcurrentDictionary<Guid, Waiting> waiting = new();

    public int PendingCount(Guid runId) => waiting.Values.Count(x => x.RunId == runId);

    /// <summary>Creates the approval, notifies the user and waits for the outcome. Never throws for expiry or cancellation: it returns an outcome.</summary>
    public async Task<ApprovalOutcome> RequestAsync(RunRequest run, string kind, string title, string details, string command, bool oneTimeOnly, string questionsJson, CancellationToken ct)
    {
        var approval = new Approval
        {
            RunId = run.Id, UserId = run.RequesterId, Kind = kind, Title = Clip(title, 300), Details = Clip(details, 8000),
            Command = Clip(command, 32000), QuestionsJson = questionsJson, OneTimeOnly = oneTimeOnly
        };
        var entry = new Waiting(run.Id, run.RequesterId, kind == "question", new(TaskCreationOptions.RunContinuationsAsynchronously));
        waiting[approval.Id] = entry;
        try
        {
            await using (var db = await factory.CreateDbContextAsync())
            {
                db.Approvals.Add(approval);
                await db.SaveChangesAsync();
            }
            Notify("approval.requested", approval, run.ProjectId);
            var timeout = TimeSpan.FromMinutes(settings.Int(SettingKeys.ApprovalTimeoutMinutes));
            using var expiry = CancellationTokenSource.CreateLinkedTokenSource(ct);
            expiry.CancelAfter(timeout);
            try { return await entry.Completion.Task.WaitAsync(expiry.Token); }
            catch (OperationCanceledException)
            {
                await CloseAsync(approval.Id, ApprovalStates.Expired, null);
                approval.State = ApprovalStates.Expired;
                Notify("approval.closed", approval, run.ProjectId);
                return new ApprovalOutcome(ApprovalStates.Expired, "deny", "");
            }
        }
        finally { waiting.TryRemove(approval.Id, out _); }
    }

    public async Task<ApprovalView> DecideAsync(Guid userId, Guid approvalId, string? choice)
    {
        if (choice is not ("allow" or "deny" or "always")) throw ApiException.BadRequest("invalid_choice", "The choice must be allow, deny or always.");
        var (approval, entry) = await RequirePending(userId, approvalId, question: false);
        if (choice == "always" && approval.OneTimeOnly) throw ApiException.BadRequest("one_time_only", "This command can only be allowed once.");
        var state = choice == "deny" ? ApprovalStates.Denied : ApprovalStates.Allowed;
        await CloseAsync(approvalId, state, userId);
        entry.Completion.TrySetResult(new ApprovalOutcome(state, choice, ""));
        await audit.WriteAsync("approval." + state, approval.Title, $"run {approval.RunId}; choice {choice}");
        return await ViewAsync(approvalId);
    }

    public async Task<ApprovalView> AnswerAsync(Guid userId, Guid approvalId, bool cancelled, IReadOnlyList<IReadOnlyList<string>>? answers)
    {
        var (approval, entry) = await RequirePending(userId, approvalId, question: true);
        var json = JsonSerializer.Serialize(new { cancelled, answers = cancelled ? [] : answers ?? [] }, RealtimeHub.Json);
        await CloseAsync(approvalId, ApprovalStates.Answered, userId);
        entry.Completion.TrySetResult(new ApprovalOutcome(ApprovalStates.Answered, cancelled ? "cancel" : "answer", json));
        await audit.WriteAsync("approval.answered", approval.Title, $"run {approval.RunId}");
        return await ViewAsync(approvalId);
    }

    public async Task<List<ApprovalView>> ListAsync(Guid userId, bool pendingOnly)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.Approvals.AsNoTracking().Where(x => x.UserId == userId);
        if (pendingOnly) query = query.Where(x => x.State == ApprovalStates.Pending);
        var rows = await query.OrderByDescending(x => x.CreatedUtc).Take(200).ToListAsync();
        var runs = rows.Select(x => x.RunId).Distinct().ToList();
        var projects = await db.Runs.AsNoTracking().Where(x => runs.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.ProjectId);
        return rows.Select(x => ToView(x, projects.GetValueOrDefault(x.RunId))).ToList();
    }

    /// <summary>The run ended (or the server stopped): nothing can answer these approvals any more.</summary>
    public async Task ExpireRunAsync(Guid runId)
    {
        foreach (var item in waiting.Where(x => x.Value.RunId == runId).ToList()) item.Value.Completion.TrySetResult(new ApprovalOutcome(ApprovalStates.Expired, "deny", ""));
        await using var db = await factory.CreateDbContextAsync();
        await db.Approvals.Where(x => x.RunId == runId && x.State == ApprovalStates.Pending)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.State, ApprovalStates.Expired).SetProperty(x => x.DecidedUtc, DateTime.UtcNow));
    }

    async Task<(Approval, Waiting)> RequirePending(Guid userId, Guid approvalId, bool question)
    {
        await using var db = await factory.CreateDbContextAsync();
        var approval = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == approvalId && x.UserId == userId) ?? throw ApiException.NotFound("Approval not found.");
        if (approval.State != ApprovalStates.Pending || !waiting.TryGetValue(approvalId, out var entry))
            throw ApiException.Conflict("approval_closed", "This request is no longer waiting for an answer.");
        if (entry.Question != question) throw ApiException.BadRequest("wrong_kind", question ? "This is a tool approval, not a question." : "This is a question: answer it instead.");
        return (approval, entry);
    }

    async Task CloseAsync(Guid id, string state, Guid? by)
    {
        await using var db = await factory.CreateDbContextAsync();
        await db.Approvals.Where(x => x.Id == id).ExecuteUpdateAsync(set => set.SetProperty(x => x.State, state).SetProperty(x => x.DecidedById, by).SetProperty(x => x.DecidedUtc, DateTime.UtcNow));
    }

    async Task<ApprovalView> ViewAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var approval = await db.Approvals.AsNoTracking().SingleAsync(x => x.Id == id);
        var project = await db.Runs.AsNoTracking().Where(x => x.Id == approval.RunId).Select(x => x.ProjectId).SingleAsync();
        var view = ToView(approval, project);
        Notify("approval.closed", approval, project);
        return view;
    }

    void Notify(string name, Approval approval, Guid project)
    {
        var view = ToView(approval, project);
        hub.PublishToUser(approval.UserId, name, view);
        hub.Publish(RealtimeHub.RunTopic(approval.RunId), name, view);
    }

    static ApprovalView ToView(Approval a, Guid project) => new(a.Id, a.RunId, project, a.Kind, a.Title, a.Details, a.Command,
        string.IsNullOrEmpty(a.QuestionsJson) ? null : JsonDocument.Parse(a.QuestionsJson).RootElement.Clone(), a.OneTimeOnly, a.State, a.CreatedUtc, a.DecidedUtc);

    static string Clip(string value, int length) => value.Length <= length ? value : value[..length];
}
