using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

/// <summary>One call a client shares. The identifier is the client's own (device + local number), only used to avoid counting a call twice.</summary>
public sealed record UsageInput(string? Id, DateTime CompletedUtc, string? Provider, string? Model, string? Activity, long InputTokens, long OutputTokens,
    long? CachedInputTokens, double Seconds, string? Status, bool Estimated, bool ViaServer);

public sealed record UsageUpload(string? DeviceId, List<UsageInput>? Samples);

public sealed record UsageAccepted(int Accepted, int Duplicates, int Rejected);

public sealed record UsageTotals(int Calls, int RatedCalls, long InputTokens, long OutputTokens, long CachedInputTokens, double Seconds, double? AverageSpeed,
    double? MinSpeed, double? MaxSpeed, int Users, int Estimated, DateTime? FirstUtc, DateTime? LastUtc);

public sealed record UsageBucket(DateTime FromUtc, DateTime UntilUtc, int Calls, long InputTokens, long OutputTokens, double? AverageSpeed);

public sealed record UsageModelRow(string Provider, string Model, int Calls, long InputTokens, long OutputTokens, double? AverageSpeed, double? MinSpeed, double? MaxSpeed);

public sealed record UsageUserRow(Guid UserId, string Email, string DisplayName, int Calls, long InputTokens, long OutputTokens, double? AverageSpeed, DateTime? LastUtc);

/// <param name="Scope">"me" (the caller), "all" (every user together) or "user" (one user).</param>
public sealed record UsageSummary(string Scope, string Period, DateTime FromUtc, DateTime UntilUtc, UsageTotals Totals, IReadOnlyList<UsageBucket> Timeline,
    IReadOnlyList<UsageModelRow> Models, IReadOnlyList<UsageUserRow>? Users, Guid? UserId);

public sealed record UsageStatus(bool Enabled, int Samples, DateTime? FirstUtc, DateTime? LastUtc, int RetentionDays);

/// <summary>
/// Speed and consumption statistics that clients choose to share. Everybody who shares can read their own numbers; the common view
/// (all users together) and the per-user view need the <c>usage.viewAll</c> permission, so that the numbers of a person are never shown to
/// someone who was not given the right to look.
/// </summary>
public sealed class UsageService(IDbContextFactory<ServerDb> factory, SettingsService settings, AccessService access)
{
    public const int MaxBatch = 1000;
    public static readonly string[] Periods = ["24h", "7d", "30d", "1y"];
    static readonly TimeSpan PurgeEvery = TimeSpan.FromHours(1);
    readonly ConcurrentDictionary<Guid, (DateTime Start, int Count)> uploads = new();
    DateTime lastPurge = DateTime.MinValue;

    // ---- Receiving ----

    public async Task<UsageAccepted> AcceptAsync(ClaimsPrincipal user, UsageUpload upload)
    {
        RequireShare(user);
        var userId = user.RequireUserId();
        var samples = upload.Samples ?? [];
        if (samples.Count == 0) return new(0, 0, 0);
        if (samples.Count > MaxBatch) throw ApiException.BadRequest("batch_too_large", $"At most {MaxBatch} measurements per request.");
        Throttle(userId);
        var device = Text(upload.DeviceId, 64);
        var now = DateTime.UtcNow;
        var oldest = now.AddDays(-Math.Max(settings.Int(SettingKeys.UsageRetentionDays), 1));
        var rows = new List<UsageSample>();
        var rejected = 0;
        foreach (var item in samples)
        {
            var completed = item.CompletedUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(item.CompletedUtc, DateTimeKind.Utc) : item.CompletedUtc.ToUniversalTime();
            var key = Text(item.Id, 96);
            if (key.Length == 0 || completed < oldest || completed > now.AddDays(1) || item.InputTokens is < 0 or > 2_000_000_000 || item.OutputTokens is < 0 or > 2_000_000_000
                || item.CachedInputTokens is < 0 or > 2_000_000_000 || !double.IsFinite(item.Seconds) || item.Seconds is < 0 or > 86_400) { rejected++; continue; }
            var status = Text(item.Status, 16).ToLowerInvariant();
            if (status is not ("complete" or "cancelled" or "error")) status = "complete";
            rows.Add(new UsageSample
            {
                UserId = userId, SampleKey = device.Length > 0 ? device + ":" + key : key, CompletedUtc = completed, Provider = Text(item.Provider, 120), Model = Text(item.Model, 160),
                Activity = Text(item.Activity, 32) is { Length: > 0 } activity ? activity : "chat", InputTokens = item.InputTokens, OutputTokens = item.OutputTokens,
                CachedInputTokens = item.CachedInputTokens, Seconds = item.Seconds, Status = status, Estimated = item.Estimated, ViaServer = item.ViaServer,
                Speed = status == "complete" && item.Model?.Length > 0 && item.OutputTokens > 0 && item.Seconds > 0 && double.IsFinite(item.OutputTokens / item.Seconds) ? item.OutputTokens / item.Seconds : null
            });
        }
        await using var db = await factory.CreateDbContextAsync();
        var keys = rows.Select(x => x.SampleKey).ToList();
        var known = (await db.UsageSamples.AsNoTracking().Where(x => x.UserId == userId && keys.Contains(x.SampleKey)).Select(x => x.SampleKey).ToListAsync()).ToHashSet();
        var fresh = rows.Where(x => known.Add(x.SampleKey)).ToList();
        db.UsageSamples.AddRange(fresh);
        await db.SaveChangesAsync();
        await PurgeIfDueAsync();
        return new(fresh.Count, rows.Count - fresh.Count, rejected);
    }

    void RequireShare(ClaimsPrincipal user)
    {
        access.RequireFeature(Features.Usage);
        if (!user.IsAuthenticated()) throw ApiException.Unauthorized();
        if (user.SessionKind() == SessionKinds.Run) throw ApiException.Forbidden();
    }

    /// <summary>A client sends in small batches every minute or so: far more than that is a bug or abuse.</summary>
    void Throttle(Guid userId)
    {
        var now = DateTime.UtcNow;
        var state = uploads.AddOrUpdate(userId, (now, 1), (_, previous) => now - previous.Start > TimeSpan.FromMinutes(10) ? (now, 1) : (previous.Start, previous.Count + 1));
        if (state.Count > 120) throw new ApiException(429, "too_many_uploads", "Too many uploads. Try again in a few minutes.");
    }

    async Task PurgeIfDueAsync()
    {
        if (DateTime.UtcNow - lastPurge < PurgeEvery) return;
        lastPurge = DateTime.UtcNow;
        var limit = DateTime.UtcNow.AddDays(-settings.Int(SettingKeys.UsageRetentionDays));
        await using var db = await factory.CreateDbContextAsync();
        await db.UsageSamples.Where(x => x.CompletedUtc < limit).ExecuteDeleteAsync();
    }

    static string Text(string? value, int length)
    {
        var clean = new string((value ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length <= length ? clean : clean[..length];
    }

    // ---- Reading ----

    public async Task<UsageStatus> StatusAsync(ClaimsPrincipal user)
    {
        var userId = user.RequireUserId();
        await using var db = await factory.CreateDbContextAsync();
        var mine = db.UsageSamples.AsNoTracking().Where(x => x.UserId == userId);
        return new(settings.FeatureEnabled(Features.Usage), await mine.CountAsync(), await mine.MinAsync(x => (DateTime?)x.CompletedUtc), await mine.MaxAsync(x => (DateTime?)x.CompletedUtc),
            settings.Int(SettingKeys.UsageRetentionDays));
    }

    public bool CanViewAll(ClaimsPrincipal? user) => access.Can(user, Permissions.UsageViewAll);

    public async Task<UsageSummary> SummaryAsync(ClaimsPrincipal user, string? scope, string? period, Guid? targetUser)
    {
        access.RequireFeature(Features.Usage);
        if (!user.IsAuthenticated()) throw ApiException.Unauthorized();
        period = Periods.Contains(period ?? "") ? period! : "7d";
        scope = (scope ?? "me").ToLowerInvariant();
        var me = user.RequireUserId();
        Guid? only = null;
        switch (scope)
        {
            case "me": only = me; break;
            case "all": access.Require(user, Permissions.UsageViewAll); break;
            case "user":
                if (targetUser != me) access.Require(user, Permissions.UsageViewAll);
                only = targetUser ?? throw ApiException.BadRequest("user_required", "userId is required for the 'user' scope.");
                break;
            default: throw ApiException.BadRequest("invalid_scope", "The scope must be me, all or user.");
        }
        var until = DateTime.UtcNow;
        var (from, step, monthly) = period switch
        {
            "24h" => (until.AddHours(-24), TimeSpan.FromHours(1), false),
            "7d" => (until.Date.AddDays(-6), TimeSpan.FromDays(1), false),
            "30d" => (until.Date.AddDays(-29), TimeSpan.FromDays(1), false),
            _ => (new DateTime(until.Year, until.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-11), TimeSpan.Zero, true)
        };
        await using var db = await factory.CreateDbContextAsync();
        IQueryable<UsageSample> Query() => db.UsageSamples.AsNoTracking().Where(x => x.CompletedUtc >= from && x.CompletedUtc <= until && (only == null || x.UserId == only));

        var totals = await TotalsAsync(Query());
        var timeline = new List<UsageBucket>();
        for (var start = from; start < until;)
        {
            var end = monthly ? start.AddMonths(1) : start + step;
            var bucketStart = start;
            var bucket = await Query().Where(x => x.CompletedUtc >= bucketStart && x.CompletedUtc < end).GroupBy(_ => 1).Select(g => new
            {
                Calls = g.Count(), Input = g.Sum(x => x.InputTokens), Output = g.Sum(x => x.OutputTokens),
                RatedOutput = g.Sum(x => x.Speed != null ? x.OutputTokens : 0), RatedSeconds = g.Sum(x => x.Speed != null ? x.Seconds : 0.0)
            }).SingleOrDefaultAsync();
            timeline.Add(new UsageBucket(start, end, bucket?.Calls ?? 0, bucket?.Input ?? 0, bucket?.Output ?? 0, Average(bucket?.RatedOutput ?? 0, bucket?.RatedSeconds ?? 0)));
            start = end;
        }
        var models = (await Query().GroupBy(x => new { x.Provider, x.Model }).Select(g => new
        {
            g.Key.Provider, g.Key.Model, Calls = g.Count(), Input = g.Sum(x => x.InputTokens), Output = g.Sum(x => x.OutputTokens),
            RatedOutput = g.Sum(x => x.Speed != null ? x.OutputTokens : 0), RatedSeconds = g.Sum(x => x.Speed != null ? x.Seconds : 0.0), Min = g.Min(x => x.Speed), Max = g.Max(x => x.Speed)
        }).ToListAsync()).Select(x => new UsageModelRow(x.Provider, x.Model, x.Calls, x.Input, x.Output, Average(x.RatedOutput, x.RatedSeconds), x.Min, x.Max))
            .OrderByDescending(x => x.OutputTokens).ToList();

        List<UsageUserRow>? users = null;
        if (scope == "all")
        {
            var rows = await Query().GroupBy(x => x.UserId).Select(g => new
            {
                UserId = g.Key, Calls = g.Count(), Input = g.Sum(x => x.InputTokens), Output = g.Sum(x => x.OutputTokens),
                RatedOutput = g.Sum(x => x.Speed != null ? x.OutputTokens : 0), RatedSeconds = g.Sum(x => x.Speed != null ? x.Seconds : 0.0), Last = g.Max(x => (DateTime?)x.CompletedUtc)
            }).ToListAsync();
            var ids = rows.Select(x => x.UserId).ToList();
            var names = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => (x.Email, x.DisplayName));
            users = rows.Select(x => new UsageUserRow(x.UserId, names.GetValueOrDefault(x.UserId).Email ?? "", names.GetValueOrDefault(x.UserId).DisplayName ?? "", x.Calls, x.Input, x.Output,
                Average(x.RatedOutput, x.RatedSeconds), x.Last)).OrderByDescending(x => x.OutputTokens).ToList();
        }
        return new UsageSummary(scope, period, from, until, totals, timeline, models, users, only);
    }

    static async Task<UsageTotals> TotalsAsync(IQueryable<UsageSample> query)
    {
        var row = await query.GroupBy(_ => 1).Select(g => new
        {
            Calls = g.Count(), Rated = g.Count(x => x.Speed != null), Input = g.Sum(x => x.InputTokens), Output = g.Sum(x => x.OutputTokens), Cached = g.Sum(x => x.CachedInputTokens ?? 0),
            Seconds = g.Sum(x => x.Seconds), RatedOutput = g.Sum(x => x.Speed != null ? x.OutputTokens : 0), RatedSeconds = g.Sum(x => x.Speed != null ? x.Seconds : 0.0),
            Min = g.Min(x => x.Speed), Max = g.Max(x => x.Speed), Users = g.Select(x => x.UserId).Distinct().Count(), Estimated = g.Count(x => x.Estimated),
            First = g.Min(x => (DateTime?)x.CompletedUtc), Last = g.Max(x => (DateTime?)x.CompletedUtc)
        }).SingleOrDefaultAsync();
        return row == null
            ? new UsageTotals(0, 0, 0, 0, 0, 0, null, null, null, 0, 0, null, null)
            : new UsageTotals(row.Calls, row.Rated, row.Input, row.Output, row.Cached, row.Seconds, Average(row.RatedOutput, row.RatedSeconds), row.Min, row.Max, row.Users, row.Estimated, row.First, row.Last);
    }

    /// <summary>Weighted by duration, like the Desktop dashboard: long calls count for more than short ones.</summary>
    static double? Average(long output, double seconds) => seconds > 0 && output > 0 ? output / seconds : null;

    // ---- Erasing ----

    /// <summary>Everything a user shared, at their request or at an administrator's.</summary>
    public async Task<int> DeleteAsync(ClaimsPrincipal user, Guid? targetUser)
    {
        if (!user.IsAuthenticated()) throw ApiException.Unauthorized();
        var me = user.RequireUserId();
        var target = targetUser ?? me;
        if (target != me) access.Require(user, Permissions.UsersManage);
        await using var db = await factory.CreateDbContextAsync();
        return await db.UsageSamples.Where(x => x.UserId == target).ExecuteDeleteAsync();
    }
}
