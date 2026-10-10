namespace MonolithHarness.Core;

public sealed record ModelSpeedSummary(int Calls, long OutputTokens, double Seconds, double? Minimum, double? Maximum, int Estimates)
{
    // Weight by request duration, rather than averaging ratios from short and long calls equally.
    public double? Average => Calls > 0 && Seconds > 0 ? OutputTokens / Seconds : null;
}

public sealed record ModelSpeedBucket(string Label, ModelSpeedSummary Summary);
public sealed record ModelSpeedGroup(TokenUsage Model, ModelSpeedSummary Summary);
public sealed record ModelSpeedReport(IReadOnlyList<TokenUsage> Entries, ModelSpeedSummary Summary,
    IReadOnlyList<ModelSpeedBucket> Timeline, IReadOnlyList<ModelSpeedGroup> Models, int ExcludedCalls);

public static class ModelSpeedReports
{
    public static double? Rate(TokenUsage entry) => entry.Status == "complete" && entry.Model.Length > 0 &&
        entry.OutputTokens > 0 && double.IsFinite(entry.Seconds) && entry.Seconds > 0 && double.IsFinite(entry.OutputTokens / entry.Seconds)
        ? entry.OutputTokens / entry.Seconds : null;

    public static ModelSpeedSummary Summarize(IEnumerable<TokenUsage> source)
    {
        var entries = source.Where(x => Rate(x).HasValue).ToArray();
        return new(entries.Length, entries.Sum(x => x.OutputTokens), entries.Sum(x => x.Seconds),
            entries.Length == 0 ? null : entries.Min(x => Rate(x)!.Value),
            entries.Length == 0 ? null : entries.Max(x => Rate(x)!.Value), entries.Count(x => x.OutputEstimated));
    }

    public static ModelSpeedReport Create(ConsumptionReport consumption, ConsumptionPeriod period)
    {
        var entries = consumption.Entries.Where(x => Rate(x).HasValue).ToArray();
        var timeline = ConsumptionReports.TimeBuckets(consumption.StartLocal, consumption.EndLocal, period).Select(bucket =>
            new ModelSpeedBucket(bucket.Label, Summarize(entries.Where(x => x.CompletedUtc >= bucket.FromUtc && x.CompletedUtc < bucket.UntilUtc)))).ToArray();
        var models = entries.GroupBy(x => (ConsumptionReports.ProviderKey(x), ConsumptionReports.ModelKey(x)))
            .Select(group => new ModelSpeedGroup(group.First(), Summarize(group)))
            .OrderByDescending(x => x.Summary.Average).ToArray();
        return new(entries, Summarize(entries), timeline, models, consumption.Entries.Count - entries.Length);
    }
}
