using System.Security.Claims;
using Cronos;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

public sealed record ScheduleView(Guid Id, string Name, Guid ProjectId, string Instruction, string Cron, string TimeZoneId, bool Enabled, Guid ProviderId, string Model,
    string MissedPolicy, DateTime? NextRunUtc, DateTime? LastRunUtc, Guid? LastRunId, string LastResult);

public sealed record ScheduleRequest(string? Name, Guid? ProjectId, string? Instruction, string? Cron, string? TimeZoneId, bool? Enabled, Guid? ProviderId, string? Model, string? MissedPolicy);

/// <summary>
/// Cloud scheduled tasks. Each occurrence is claimed by inserting a unique (task, time) row in the same transaction that advances
/// the task, so an occurrence can never run twice, a task never overlaps itself, and a backlog is never replayed.
/// </summary>
public sealed class ScheduleService(IDbContextFactory<ServerDb> factory, SettingsService settings, AccessService access, ProjectService projects, ProviderService providers,
    ProjectStore store, RunService runs, AuditService audit, ILogger<ScheduleService> log)
{
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);

    public void Subscribe() => runs.Finished += OnRunFinishedAsync;

    // ---- Management ----

    public async Task<List<ScheduleView>> ListAsync(ClaimsPrincipal user)
    {
        access.Require(user, Permissions.SchedulesManage);
        var owner = user.RequireUserId();
        await using var db = await factory.CreateDbContextAsync();
        return (await db.Schedules.AsNoTracking().Where(x => x.OwnerId == owner).OrderBy(x => x.Name).ToListAsync()).Select(ToView).ToList();
    }

    public async Task<ScheduleView> CreateAsync(ClaimsPrincipal user, ScheduleRequest request)
    {
        access.Require(user, Permissions.SchedulesManage);
        access.Require(user, Permissions.CloudUse);
        var owner = user.RequireUserId();
        var schedule = new CloudSchedule { OwnerId = owner };
        await Apply(user, schedule, request, creating: true);
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Schedules.CountAsync(x => x.OwnerId == owner) >= settings.Int(SettingKeys.MaxSchedulesPerUser))
            throw new ApiException(429, "too_many_schedules", "You reached the maximum number of scheduled tasks.");
        db.Schedules.Add(schedule);
        await db.SaveChangesAsync();
        await audit.WriteAsync("schedule.created", schedule.Name, $"{schedule.Cron} {schedule.TimeZoneId}");
        return ToView(schedule);
    }

    public async Task<ScheduleView> UpdateAsync(ClaimsPrincipal user, Guid id, ScheduleRequest request)
    {
        access.Require(user, Permissions.SchedulesManage);
        await using var db = await factory.CreateDbContextAsync();
        var schedule = await Own(db, user, id);
        await Apply(user, schedule, request, creating: false);
        await db.SaveChangesAsync();
        await audit.WriteAsync("schedule.updated", schedule.Name, $"{schedule.Cron} {schedule.TimeZoneId}; enabled={schedule.Enabled}");
        return ToView(schedule);
    }

    public async Task DeleteAsync(ClaimsPrincipal user, Guid id)
    {
        access.Require(user, Permissions.SchedulesManage);
        await using var db = await factory.CreateDbContextAsync();
        var schedule = await Own(db, user, id);
        db.Schedules.Remove(schedule);
        await db.SaveChangesAsync();
        await audit.WriteAsync("schedule.deleted", schedule.Name);
    }

    public async Task<RunView> RunNowAsync(ClaimsPrincipal user, Guid id)
    {
        access.Require(user, Permissions.SchedulesManage);
        await using var db = await factory.CreateDbContextAsync();
        var schedule = await Own(db, user, id);
        var run = await StartAsync(db, schedule, DateTime.UtcNow);
        await db.SaveChangesAsync();
        return run;
    }

    static async Task<CloudSchedule> Own(ServerDb db, ClaimsPrincipal user, Guid id)
    {
        var owner = user.RequireUserId();
        return await db.Schedules.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == owner) ?? throw ApiException.NotFound("Scheduled task not found.");
    }

    async Task Apply(ClaimsPrincipal user, CloudSchedule schedule, ScheduleRequest request, bool creating)
    {
        var owner = user.RequireUserId();
        if (request.Name != null || creating)
        {
            var name = (request.Name ?? "").Trim();
            if (name.Length is 0 or > 100) throw ApiException.BadRequest("invalid_name", "A task name has 1 to 100 characters.");
            schedule.Name = name;
        }
        if (request.Instruction != null || creating)
        {
            var instruction = (request.Instruction ?? "").Trim();
            if (instruction.Length is 0 or > 20000) throw ApiException.BadRequest("invalid_instruction", "The instruction has 1 to 20,000 characters.");
            schedule.Instruction = instruction;
        }
        if (request.ProjectId is { } projectId)
        {
            await projects.RequireAsync(user, projectId, ProjectAccess.Run);
            schedule.ProjectId = projectId;
        }
        else if (creating) throw ApiException.BadRequest("project_required", "A project is required.");
        if (request.ProviderId != null || request.Model != null || creating)
        {
            var (provider, model) = await providers.ResolveAsync(owner, request.ProviderId ?? schedule.ProviderId, request.Model ?? schedule.Model);
            schedule.ProviderId = provider.Id;
            schedule.Model = model.Id;
        }
        if (request.MissedPolicy != null)
            schedule.MissedPolicy = request.MissedPolicy is "skip" or "run-once" ? request.MissedPolicy : throw ApiException.BadRequest("invalid_policy", "The missed-run policy is 'skip' or 'run-once'.");
        if (request.Enabled != null) schedule.Enabled = request.Enabled.Value;
        if (request.Cron != null || request.TimeZoneId != null || creating)
        {
            schedule.Cron = (request.Cron ?? schedule.Cron).Trim();
            schedule.TimeZoneId = (request.TimeZoneId ?? schedule.TimeZoneId).Trim();
            ValidateTiming(schedule.Cron, schedule.TimeZoneId);
        }
        schedule.NextRunUtc = schedule.Enabled ? Next(schedule.Cron, schedule.TimeZoneId, DateTime.UtcNow) : null;
    }

    void ValidateTiming(string cron, string timeZone)
    {
        CronExpression expression;
        try { expression = CronExpression.Parse(cron, CronFormat.Standard); }
        catch (CronFormatException) { throw ApiException.BadRequest("invalid_cron", "The CRON expression must have five fields (minute hour day month weekday)."); }
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { throw ApiException.BadRequest("invalid_timezone", "Unknown time zone: " + timeZone); }
        var minimum = TimeSpan.FromMinutes(settings.Int(SettingKeys.MinScheduleMinutes));
        var occurrences = expression.GetOccurrences(DateTime.UtcNow, DateTime.UtcNow.AddDays(400), zone).Take(12).ToList();
        if (occurrences.Count == 0) throw ApiException.BadRequest("invalid_cron", "This expression has no upcoming occurrence.");
        for (var i = 1; i < occurrences.Count; i++)
            if (occurrences[i] - occurrences[i - 1] < minimum) throw ApiException.BadRequest("too_frequent", $"Occurrences must be at least {minimum.TotalMinutes:0} minutes apart.");
    }

    public static DateTime? Next(string cron, string timeZone, DateTime afterUtc) =>
        CronExpression.Parse(cron, CronFormat.Standard).GetNextOccurrence(DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(timeZone));

    // ---- Execution ----

    /// <summary>Processes every task that is due. Safe to call from several places at once: occurrence claims are unique.</summary>
    public async Task TickAsync(DateTime nowUtc)
    {
        if (!settings.FeatureEnabled(Features.Schedules) || !settings.FeatureEnabled(Features.Cloud)) return;
        List<Guid> due;
        await using (var db = await factory.CreateDbContextAsync())
            due = await db.Schedules.AsNoTracking().Where(x => x.Enabled && x.NextRunUtc != null && x.NextRunUtc <= nowUtc).Select(x => x.Id).ToListAsync();
        foreach (var id in due)
        {
            try { await ProcessAsync(id, nowUtc); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Scheduled task {Task} failed", id); }
        }
    }

    async Task ProcessAsync(Guid id, DateTime nowUtc)
    {
        await using var db = await factory.CreateDbContextAsync();
        var schedule = await db.Schedules.SingleOrDefaultAsync(x => x.Id == id);
        if (schedule is not { Enabled: true, NextRunUtc: { } occurrence } || occurrence > nowUtc) return;

        // Claim the occurrence and move the schedule forward in one commit; the backlog collapses into this single occurrence.
        schedule.NextRunUtc = Next(schedule.Cron, schedule.TimeZoneId, nowUtc);
        var entry = new ScheduleOccurrence { ScheduleId = id, OccurrenceUtc = occurrence };
        db.Occurrences.Add(entry);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { return; } // another executor claimed it, or the task was removed

        if (nowUtc - occurrence > Grace && schedule.MissedPolicy == "skip")
        {
            entry.Outcome = "missed";
            schedule.LastResult = $"Missed occurrence of {occurrence:u} skipped (server was stopped).";
            await db.SaveChangesAsync();
            return;
        }
        try { entry.RunId = (await StartAsync(db, schedule, nowUtc)).Id; entry.Outcome = "queued"; }
        catch (ApiException ex)
        {
            entry.Outcome = ex.Code == "overlap" ? "skipped" : "failed";
            schedule.LastResult = "Not started: " + ex.Message;
            if (ex.Code is "missing_permission" or "account_disabled") schedule.Enabled = false;
        }
        await db.SaveChangesAsync();
    }

    async Task<RunView> StartAsync(ServerDb db, CloudSchedule schedule, DateTime nowUtc)
    {
        if (schedule.LastRunId is { } previous)
        {
            var state = await db.Runs.AsNoTracking().Where(x => x.Id == previous).Select(x => x.State).SingleOrDefaultAsync();
            if (state is RunStates.Queued or RunStates.Running or RunStates.AwaitingApproval)
                throw ApiException.Conflict("overlap", "The previous execution of this task is still in progress.");
        }
        var chat = await store.CreateChatAsync(schedule.ProjectId, $"{schedule.Name} — {nowUtc:yyyy-MM-dd HH:mm}");
        var run = await runs.EnqueueScheduledAsync(schedule.OwnerId, schedule.ProjectId, chat.Id, schedule.Instruction, schedule.ProviderId, schedule.Model, schedule.Id);
        schedule.LastRunId = run.Id;
        schedule.LastRunUtc = nowUtc;
        schedule.ChatId = chat.Id;
        schedule.LastResult = "Queued.";
        return run;
    }

    async Task OnRunFinishedAsync(RunRequest run)
    {
        if (run.ScheduleId is not { } id) return;
        await using var db = await factory.CreateDbContextAsync();
        var result = run.State + (run.Error.Length > 0 ? ": " + run.Error.Split('\n')[0] : "");
        await db.Schedules.Where(x => x.Id == id && x.LastRunId == run.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.LastResult, result));
    }

    static ScheduleView ToView(CloudSchedule x) => new(x.Id, x.Name, x.ProjectId, x.Instruction, x.Cron, x.TimeZoneId, x.Enabled, x.ProviderId, x.Model, x.MissedPolicy, x.NextRunUtc, x.LastRunUtc, x.LastRunId, x.LastResult);
}

/// <summary>Wakes the scheduler every 15 seconds.</summary>
public sealed class ScheduleRunner(ScheduleService schedules, ILogger<ScheduleRunner> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        schedules.Subscribe();
        while (!stop.IsCancellationRequested)
        {
            try { await schedules.TickAsync(DateTime.UtcNow); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Scheduler tick failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stop); }
            catch (OperationCanceledException) { break; }
        }
    }
}
