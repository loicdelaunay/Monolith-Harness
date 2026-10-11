using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Realtime;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

public sealed record RunView(Guid Id, Guid ProjectId, int ChatId, Guid RequesterId, string Requester, string Source, Guid ProviderId, string Model, string Prompt,
    string State, int? QueuePosition, DateTime CreatedUtc, DateTime? StartedUtc, DateTime? FinishedUtc, string Error, string ChangeSummary);

public sealed record QueueOverview(int Queued, int Running, int AwaitingApproval, int MaxConcurrentRuns, int MaxConcurrentUsers, bool LimitsEnabled, IReadOnlyList<RunView> Items);

/// <summary>A started run the server can still cancel. The executor owns the worker behind it.</summary>
public sealed class ActiveRun
{
    public CancellationTokenSource Cancellation { get; } = new();
    public string? Reason { get; private set; }
    public void Cancel(string reason) { Reason ??= reason; Cancellation.Cancel(); }
}

/// <summary>Requests of cloud projects: validation, queue, fair scheduling order, states and notifications.</summary>
public sealed class RunService(IDbContextFactory<ServerDb> factory, SettingsService settings, ProjectService projects, ProviderService providers, ProjectStore store,
    AccessService access, AccountService accounts, RealtimeHub hub, AuditService audit)
{
    const int HardCeiling = 64;
    readonly ConcurrentDictionary<Guid, ActiveRun> active = new();
    readonly object sequenceLock = new();
    long sequence;
    public event Func<RunRequest, Task>? Finished;
    public event Action? WorkAvailable;

    public IReadOnlyDictionary<Guid, ActiveRun> Active => active;

    public async Task InitializeAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        sequence = await db.Runs.MaxAsync(x => (long?)x.Sequence) ?? 0;
        // A restart cannot resume a worker: running requests are marked failed, queued ones simply stay queued.
        var interrupted = await db.Runs.Where(x => x.State == RunStates.Running || x.State == RunStates.AwaitingApproval).ToListAsync();
        foreach (var run in interrupted) { run.State = RunStates.Failed; run.Error = "Interrupted by a server restart."; run.FinishedUtc = DateTime.UtcNow; }
        await db.Approvals.Where(x => x.State == ApprovalStates.Pending).ExecuteUpdateAsync(set => set.SetProperty(x => x.State, ApprovalStates.Expired).SetProperty(x => x.DecidedUtc, DateTime.UtcNow));
        await db.SaveChangesAsync();
        foreach (var run in interrupted) Finished?.Invoke(run);
    }

    // ---- Submitting ----

    public async Task<RunView> EnqueueAsync(ClaimsPrincipal user, Guid projectId, int chatId, string? text, Guid providerId, string? model)
    {
        access.Require(user, Permissions.CloudUse);
        access.Require(user, Permissions.ProvidersUse);
        await projects.RequireAsync(user, projectId, ProjectAccess.Run);
        return await EnqueueCoreAsync(user.RequireUserId(), projectId, chatId, text, providerId, model, "interactive", null);
    }

    /// <summary>Scheduler entry point: the same rules apply, evaluated for the schedule owner at run time.</summary>
    public async Task<RunView> EnqueueScheduledAsync(Guid ownerId, Guid projectId, int chatId, string text, Guid providerId, string model, Guid scheduleId)
    {
        var permissions = await accounts.PermissionsOfAsync(ownerId) ?? throw ApiException.Forbidden("account_disabled", "The schedule owner is disabled.");
        if (!permissions.Contains(Permissions.CloudUse) || !permissions.Contains(Permissions.SchedulesManage) || !permissions.Contains(Permissions.ProvidersUse))
            throw ApiException.Forbidden("missing_permission", "The schedule owner no longer holds the required permissions.");
        if (!settings.FeatureEnabled(Features.Cloud)) throw new ApiException(403, "feature_disabled", "Cloud execution is disabled.");
        await projects.RequireForAsync(ownerId, projectId, ProjectAccess.Run);
        return await EnqueueCoreAsync(ownerId, projectId, chatId, text, providerId, model, "schedule", scheduleId);
    }

    async Task<RunView> EnqueueCoreAsync(Guid userId, Guid projectId, int chatId, string? text, Guid providerId, string? model, string source, Guid? scheduleId)
    {
        if (!settings.FeatureEnabled(Features.Cloud)) throw new ApiException(403, "feature_disabled", "Cloud execution is disabled on this server.");
        text = (text ?? "").Trim();
        if (text.Length == 0) throw ApiException.BadRequest("empty_request", "The request is empty.");
        if (text.Length > 100_000) throw ApiException.BadRequest("request_too_long", "The request is limited to 100,000 characters.");
        if (!await store.ChatExistsAsync(projectId, chatId)) throw ApiException.NotFound("Conversation not found.");
        var (provider, selected) = await providers.ResolveAsync(userId, providerId, model ?? "");

        await using var db = await factory.CreateDbContextAsync();
        if (await db.Runs.AnyAsync(x => x.ChatId == chatId && x.ProjectId == projectId && (x.State == RunStates.Queued || x.State == RunStates.Running || x.State == RunStates.AwaitingApproval)))
            throw ApiException.Conflict("conversation_busy", "This conversation already has a request in progress.");
        if (settings.FeatureEnabled(Features.Queue))
        {
            var queued = await db.Runs.CountAsync(x => x.State == RunStates.Queued);
            if (queued >= settings.Int(SettingKeys.MaxQueueLength)) throw new ApiException(503, "queue_full", "The queue is full. Try again later.");
            if (await db.Runs.CountAsync(x => x.State == RunStates.Queued && x.RequesterId == userId) >= settings.Int(SettingKeys.MaxQueuedPerUser))
                throw new ApiException(429, "too_many_queued", "You have too many requests waiting.");
        }
        var run = new RunRequest
        {
            ProjectId = projectId, ChatId = chatId, RequesterId = userId, Source = source, ScheduleId = scheduleId,
            ProviderId = provider.Id, Model = selected.Id, Prompt = text
        };
        lock (sequenceLock) run.Sequence = ++sequence;
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        var view = await ViewAsync(run.Id);
        Publish(view);
        WorkAvailable?.Invoke();
        return view;
    }

    // ---- Reading ----

    public async Task<RunView> GetAsync(ClaimsPrincipal user, Guid id)
    {
        var view = await ViewAsync(id);
        await EnsureCanSee(user, view);
        return view;
    }

    public async Task<List<RunView>> ListAsync(ClaimsPrincipal user, bool all, int take = 100)
    {
        var userId = user.RequireUserId();
        await using var db = await factory.CreateDbContextAsync();
        var query = db.Runs.AsNoTracking().AsQueryable();
        if (all) access.Require(user, Permissions.QueueManage);
        else query = query.Where(x => x.RequesterId == userId);
        var rows = await query.OrderByDescending(x => x.Sequence).Take(Math.Clamp(take, 1, 500)).ToListAsync();
        return await ToViews(db, rows);
    }

    public async Task<QueueOverview> OverviewAsync(ClaimsPrincipal user)
    {
        access.Require(user, Permissions.QueueManage);
        await using var db = await factory.CreateDbContextAsync();
        var open = await db.Runs.AsNoTracking().Where(x => x.State == RunStates.Queued || x.State == RunStates.Running || x.State == RunStates.AwaitingApproval).OrderBy(x => x.Sequence).ToListAsync();
        return new QueueOverview(open.Count(x => x.State == RunStates.Queued), open.Count(x => x.State == RunStates.Running), open.Count(x => x.State == RunStates.AwaitingApproval),
            settings.Int(SettingKeys.MaxConcurrentRuns), settings.Int(SettingKeys.MaxConcurrentUsers), settings.FeatureEnabled(Features.Queue), await ToViews(db, open));
    }

    async Task EnsureCanSee(ClaimsPrincipal user, RunView view)
    {
        if (view.RequesterId == user.UserId() || access.Can(user, Permissions.QueueManage)) return;
        // Project members may follow the progress of the project's requests.
        try { await projects.RequireAsync(user, view.ProjectId, ProjectAccess.Read); }
        catch (ApiException) { throw ApiException.NotFound("Request not found."); }
    }

    async Task<RunView> ViewAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Request not found.");
        return (await ToViews(db, [run]))[0];
    }

    async Task<List<RunView>> ToViews(ServerDb db, List<RunRequest> rows)
    {
        var emails = await db.Users.AsNoTracking().Where(x => rows.Select(r => r.RequesterId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Email);
        var positions = rows.Any(x => x.State == RunStates.Queued) ? await QueuePositions(db) : [];
        return rows.Select(x => new RunView(x.Id, x.ProjectId, x.ChatId, x.RequesterId, emails.GetValueOrDefault(x.RequesterId, ""), x.Source, x.ProviderId, x.Model,
            x.Prompt.Length > 300 ? x.Prompt[..300] : x.Prompt, x.State, positions.TryGetValue(x.Id, out var position) ? position : null, x.CreatedUtc, x.StartedUtc, x.FinishedUtc, x.Error, x.ChangeSummary)).ToList();
    }

    async Task<Dictionary<Guid, int>> QueuePositions(ServerDb db)
    {
        var running = await db.Runs.AsNoTracking().Where(x => x.State == RunStates.Running || x.State == RunStates.AwaitingApproval).Select(x => x.RequesterId).ToListAsync();
        var queued = await db.Runs.AsNoTracking().Where(x => x.State == RunStates.Queued).OrderBy(x => x.Sequence).ToListAsync();
        var order = FairOrder(queued, running);
        return order.Select((run, index) => (run.Id, Position: index + 1)).ToDictionary(x => x.Id, x => x.Position);
    }

    /// <summary>
    /// Fair dispatch order: the next request always comes from the user with the fewest running (or already planned) requests;
    /// ties go to the oldest waiting request. One user flooding the queue cannot starve the others.
    /// </summary>
    public static List<RunRequest> FairOrder(IEnumerable<RunRequest> queued, IEnumerable<Guid> runningUsers)
    {
        var counts = runningUsers.GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
        var lines = queued.OrderBy(x => x.Sequence).GroupBy(x => x.RequesterId).ToDictionary(x => x.Key, x => new Queue<RunRequest>(x));
        var order = new List<RunRequest>();
        while (lines.Count > 0)
        {
            var next = lines.OrderBy(x => counts.GetValueOrDefault(x.Key)).ThenBy(x => x.Value.Peek().Sequence).First();
            order.Add(next.Value.Dequeue());
            counts[next.Key] = counts.GetValueOrDefault(next.Key) + 1;
            if (next.Value.Count == 0) lines.Remove(next.Key);
        }
        return order;
    }

    // ---- Dispatching ----

    /// <summary>Queued requests that may start now under the configured limits, in fair order.</summary>
    public async Task<List<RunRequest>> NextToStartAsync()
    {
        if (!settings.FeatureEnabled(Features.Cloud)) return [];
        await using var db = await factory.CreateDbContextAsync();
        var running = await db.Runs.AsNoTracking().Where(x => x.State == RunStates.Running || x.State == RunStates.AwaitingApproval).ToListAsync();
        var queued = await db.Runs.AsNoTracking().Where(x => x.State == RunStates.Queued).OrderBy(x => x.Sequence).ToListAsync();
        if (queued.Count == 0) return [];
        var limited = settings.FeatureEnabled(Features.Queue);
        var maxRuns = limited ? settings.Int(SettingKeys.MaxConcurrentRuns) : HardCeiling;
        var maxUsers = limited ? settings.Int(SettingKeys.MaxConcurrentUsers) : HardCeiling;
        var perUser = limited ? settings.Int(SettingKeys.MaxRunsPerUser) : HardCeiling;
        var perProvider = limited ? settings.Int(SettingKeys.MaxRunsPerProvider) : 0;

        var activeUsers = running.GroupBy(x => x.RequesterId).ToDictionary(x => x.Key, x => x.Count());
        var activeProviders = running.GroupBy(x => x.ProviderId).ToDictionary(x => x.Key, x => x.Count());
        var total = running.Count;
        var start = new List<RunRequest>();
        foreach (var candidate in FairOrder(queued, running.Select(x => x.RequesterId)))
        {
            if (total >= Math.Min(maxRuns, HardCeiling)) break;
            var userCount = activeUsers.GetValueOrDefault(candidate.RequesterId);
            if (userCount >= perUser) continue;
            if (userCount == 0 && activeUsers.Count >= maxUsers) continue;
            if (perProvider > 0 && activeProviders.GetValueOrDefault(candidate.ProviderId) >= perProvider) continue;
            start.Add(candidate);
            total++;
            activeUsers[candidate.RequesterId] = userCount + 1;
            activeProviders[candidate.ProviderId] = activeProviders.GetValueOrDefault(candidate.ProviderId) + 1;
        }
        return start;
    }

    public async Task<ActiveRun?> BeginAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        // The state change is conditional: a request cancelled meanwhile never starts.
        var changed = await db.Runs.Where(x => x.Id == id && x.State == RunStates.Queued)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.State, RunStates.Running).SetProperty(x => x.StartedUtc, DateTime.UtcNow));
        if (changed == 0) return null;
        var run = new ActiveRun();
        active[id] = run;
        Publish(await ViewAsync(id));
        return run;
    }

    public async Task SetStateAsync(Guid id, string state, string error = "", string? changes = null)
    {
        await using (var db = await factory.CreateDbContextAsync())
        {
            var run = await db.Runs.SingleAsync(x => x.Id == id);
            if (RunStates.IsFinal(run.State)) return;
            run.State = state;
            if (RunStates.IsFinal(state)) run.FinishedUtc = DateTime.UtcNow;
            if (error.Length > 0) run.Error = error.Length > 2000 ? error[..2000] : error;
            if (changes != null) run.ChangeSummary = changes.Length > 6000 ? changes[..6000] : changes;
            await db.SaveChangesAsync();
            if (RunStates.IsFinal(state))
            {
                active.TryRemove(id, out _);
                if (Finished is { } handler) await handler(run);
            }
        }
        Publish(await ViewAsync(id));
        if (RunStates.IsFinal(state)) WorkAvailable?.Invoke();
    }

    public async Task<RunView> CancelAsync(ClaimsPrincipal user, Guid id)
    {
        var view = await ViewAsync(id);
        var userId = user.RequireUserId();
        if (view.RequesterId != userId && !access.Can(user, Permissions.QueueManage)) throw ApiException.NotFound("Request not found.");
        if (RunStates.IsFinal(view.State)) return view;
        if (active.TryGetValue(id, out var running)) running.Cancel($"Cancelled by {user.Email()}.");
        else await SetStateAsync(id, RunStates.Cancelled, $"Cancelled by {user.Email()}.");
        await audit.WriteAsync("run.cancelled", id.ToString(), view.State);
        return await ViewAsync(id);
    }

    /// <summary>Disabling the cloud feature stops what is queued; requests already running finish normally.</summary>
    public async Task CancelQueuedAsync(string reason)
    {
        await using var db = await factory.CreateDbContextAsync();
        var ids = await db.Runs.Where(x => x.State == RunStates.Queued).Select(x => x.Id).ToListAsync();
        foreach (var id in ids) await SetStateAsync(id, RunStates.Cancelled, reason);
    }

    void Publish(RunView view)
    {
        hub.PublishToUser(view.RequesterId, "run.state", view);
        hub.Publish(RealtimeHub.RunTopic(view.Id), "run.state", view);
        hub.Publish(RealtimeHub.ProjectTopic(view.ProjectId), "run.state", view);
        hub.Publish(RealtimeHub.QueueTopic, "run.state", view);
    }
}
