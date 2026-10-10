using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace MonolithHarness.Core;

public sealed class TokenUsage
{
    public long Id { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime CompletedUtc { get; set; }
    public int? ProviderId { get; set; }
    public string ProviderName { get; set; } = "";
    public string Model { get; set; } = "";
    public string Activity { get; set; } = "chat";
    public int? ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public int? ChatId { get; set; }
    public string ChatTitle { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long? CachedInputTokens { get; set; }
    public bool InputEstimated { get; set; }
    public bool OutputEstimated { get; set; }
    public double Seconds { get; set; }
    public string Status { get; set; } = "complete";
    public bool Legacy { get; set; }
    public long TotalTokens => InputTokens + OutputTokens;
    public bool Estimated => InputEstimated || OutputEstimated;
}

// Call metadata only: no prompts, replies, attachments, URLs or credentials are stored.
// AsyncLocal keeps simultaneous chats and inherited subagents in their own usage scopes.
public static class TokenConsumption
{
    sealed record Scope(string Database, string Activity, int? ProjectId, string ProjectName, int? ChatId, string ChatTitle);
    static readonly AsyncLocal<Scope?> current = new();
    sealed class Restore(Scope? previous) : IDisposable { public void Dispose() => current.Value = previous; }
    public static IDisposable Begin(string database, string activity, Project? project = null, Chat? chat = null)
    {
        var previous = current.Value;
        current.Value = new(database, activity, project?.Id, project?.Name ?? "", chat?.Id, chat?.Title ?? "");
        return new Restore(previous);
    }
    public static IDisposable Activity(string activity)
    {
        var previous = current.Value;
        if (previous != null) current.Value = previous with { Activity = activity };
        return new Restore(previous);
    }

    internal static async Task<Completion> TrackAsync(Provider provider, int inputEstimate,
        Func<Action<GenerationUpdate>, Task<Completion>> request, Action<GenerationUpdate> progress)
    {
        var scope = current.Value;
        if (scope == null) return await request(progress);
        var entry = new TokenUsage { StartedUtc = DateTime.UtcNow, ProviderId = provider.Id > 0 ? provider.Id : null,
            ProviderName = provider.Name, Model = provider.Model, Activity = scope.Activity, ProjectId = scope.ProjectId,
            ProjectName = scope.ProjectName, ChatId = scope.ChatId, ChatTitle = scope.ChatTitle };
        var timer = Stopwatch.StartNew();
        GenerationUpdate? latest = null; Completion? result = null;
        void Update(GenerationUpdate update) { if (update.Retry == null) latest = update; progress(update); }
        try { result = await request(Update); return result; }
        catch (OperationCanceledException) { entry.Status = "cancelled"; throw; }
        catch { entry.Status = "error"; throw; }
        finally
        {
            entry.CompletedUtc = DateTime.UtcNow; entry.Seconds = timer.Elapsed.TotalSeconds;
            // Failed requests with no returned text or counters have no known consumption.
            if (result != null || latest is { InputTokens: not null } or { OutputTokens: not null } ||
                latest != null && (latest.Text.Length > 0 || latest.Reasoning.Length > 0))
            {
                long? input = result?.Usage is { } measured ? measured.Input : result?.InputTokens ?? latest?.InputTokens;
                long? output = result?.Usage is { } reported ? reported.Output : result?.OutputTokens ?? latest?.OutputTokens;
                var text = result == null ? latest!.Text + latest.Reasoning :
                    (result.Message["content"]?.ToString() ?? "") + (result.Message["reasoning_content"]?.ToString() ?? "") +
                    (result.Message["tool_calls"]?.ToJsonString() ?? "");
                entry.InputTokens = Math.Max(0, input ?? inputEstimate);
                entry.OutputTokens = Math.Max(0, output ?? ContextWindow.EstimateText(text));
                entry.CachedInputTokens = result?.Usage is { } aggregate ? aggregate.CachedInputTokens
                    : result?.CachedInputTokens ?? latest?.CachedInputTokens;
                entry.InputEstimated = result?.Usage?.InputEstimated ?? (input == null);
                entry.OutputEstimated = result?.Usage?.OutputEstimated ?? (output == null);
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await using var db = new HarnessDb(scope.Database);
                    db.TokenUsages.Add(entry); await db.SaveChangesAsync(timeout.Token);
                }
                catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "consumption.record_failed", ex, scope.ChatId); }
            }
        }
    }
}

public enum ConsumptionPeriod { Today, ThreeDays, SevenDays, ThirtyDays, Year }
public sealed record ConsumptionFilter(ConsumptionPeriod Period, string Provider = "", string Model = "", string Activity = "", int? ProjectId = null);
public sealed record TokenBucket(string Label, long Input, long Output, int Calls)
{
    public long Total => Input + Output;
}
public sealed record ConsumptionReport(DateTime StartLocal, DateTime EndLocal, IReadOnlyList<TokenUsage> Available,
    IReadOnlyList<TokenUsage> Entries, IReadOnlyList<TokenBucket> Timeline, int UndatedLegacy)
{
    public long Input => Entries.Sum(x => x.InputTokens);
    public long Output => Entries.Sum(x => x.OutputTokens);
    public long Total => Input + Output;
    public int Estimates => Entries.Count(x => x.Estimated);
}

public static class ConsumptionReports
{
    public static async Task<ConsumptionReport> ReadAsync(string database, ConsumptionFilter filter, CancellationToken ct = default)
    {
        var today = DateTime.Today;
        var start = filter.Period switch { ConsumptionPeriod.ThreeDays => today.AddDays(-2), ConsumptionPeriod.SevenDays => today.AddDays(-6),
            ConsumptionPeriod.ThirtyDays => today.AddDays(-29), ConsumptionPeriod.Year => today.AddYears(-1).AddDays(1), _ => today };
        var end = today.AddDays(1); var from = start.ToUniversalTime(); var until = end.ToUniversalTime();
        await using var db = new HarnessDb(database);
        var available = await db.TokenUsages.AsNoTracking().Where(x => x.CompletedUtc >= from && x.CompletedUtc < until).OrderByDescending(x => x.CompletedUtc).ThenByDescending(x => x.Id).ToListAsync(ct);
        var settings = FeatureSettings.Read(await db.States.Select(x => x.FeaturesJson).SingleAsync(ct));
        var undated = await db.Messages.CountAsync(x => x.Id <= settings.ConsumptionLegacyMessageId && x.CompletedUtc == null && (x.Role == "assistant" || x.Role == "compaction") &&
            (x.InputTokens != null || x.OutputTokens != null), ct);
        var entries = available.Where(x => (filter.Provider.Length == 0 || ProviderKey(x) == filter.Provider) &&
            (filter.Model.Length == 0 || ModelKey(x) == filter.Model) && (filter.Activity.Length == 0 || x.Activity == filter.Activity) &&
            (filter.ProjectId == null || x.ProjectId == filter.ProjectId)).ToArray();
        var timeline = TimeBuckets(start, end, filter.Period).Select(bucket =>
        {
            var values = entries.Where(x => x.CompletedUtc >= bucket.FromUtc && x.CompletedUtc < bucket.UntilUtc).ToArray();
            return new TokenBucket(bucket.Label, values.Sum(x => x.InputTokens), values.Sum(x => x.OutputTokens), values.Length);
        }).ToArray();
        return new(start, end, available, entries, timeline, undated);
    }

    internal static IEnumerable<(DateTime FromUtc, DateTime UntilUtc, string Label)> TimeBuckets(DateTime start, DateTime end, ConsumptionPeriod period)
    {
        // Both dashboard tabs share UTC bounds and local calendar labels, including daylight-saving transitions.
        if (period == ConsumptionPeriod.Today)
        {
            // Hour buckets are UTC instants, so the repeated autumn hour is counted separately.
            for (var instant = start.ToUniversalTime(); instant < end.ToUniversalTime(); instant = instant.AddHours(1))
                yield return (instant, instant.AddHours(1), DateTime.SpecifyKind(instant, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm"));
        }
        else if (period == ConsumptionPeriod.Year)
        {
            for (var month = new DateTime(start.Year, start.Month, 1); month < end; month = month.AddMonths(1))
                yield return ((month < start ? start : month).ToUniversalTime(),
                    (month.AddMonths(1) > end ? end : month.AddMonths(1)).ToUniversalTime(), month.ToString("MMM yyyy"));
        }
        else for (var day = start; day < end; day = day.AddDays(1))
            yield return (day.ToUniversalTime(), day.AddDays(1).ToUniversalTime(), day.ToString("dd/MM"));
    }
    public static string ProviderKey(TokenUsage entry) => entry.ProviderId is int id ? $"provider:{id}" : "unrecorded:" + entry.ProviderName;
    public static string ModelKey(TokenUsage entry) => entry.Model.Length > 0 ? entry.Model : "model:unrecorded";
}
