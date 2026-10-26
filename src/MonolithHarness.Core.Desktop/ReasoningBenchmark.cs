using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonolithHarness.Core;

public sealed record ReasoningBenchmarkEntry(string CaseId, string Title, string Prompt, int Repetition,
    string Requested, ModelToolResult? Result, bool? Passed, string? Error, double ElapsedSeconds);
public sealed record ReasoningBenchmarkSummary(string Level, int Attempts, int Responses, int Passed, int Graded,
    int Errors, double? AverageSeconds, double? AverageFirstElementSeconds, int InputTokens, int OutputTokens,
    int? CachedInputTokens, bool Estimated, string[] SentLevels, int Fallbacks);
public sealed record ReasoningBenchmarkReport(string Suite, string ApplicationVersion, string Provider, string Model,
    DateTimeOffset StartedAt, int Seed, string Difficulty, int Repetitions, int TimeoutSeconds,
    string[] Levels, IReadOnlyList<ModelBenchmarkCase> Cases, IReadOnlyList<ReasoningBenchmarkEntry> Entries, bool Completed)
{
    public string Method => "Identical prompts, model and provider for every level. Isolated sessions; no tools. Rotating level order between cases/repetitions reduces order bias but does not remove caching, provider load or model nondeterminism. Quality is the fraction of verifiable tasks passed, including failed requests in the denominator; it is not a general model score. First element may be reasoning or answer text. Tokens marked estimated are not exact billing. No automatic model judge.";
    public ReasoningBenchmarkSummary[] Summaries => Levels.Select(level =>
    {
        var entries = Entries.Where(x => x.Requested == level).ToArray();
        var results = entries.Where(x => x.Result != null).Select(x => x.Result!).ToArray();
        var first = results.Where(x => x.FirstTokenSeconds.HasValue).ToArray();
        return new ReasoningBenchmarkSummary(level, entries.Length, results.Length, entries.Count(x => x.Passed == true),
            entries.Count(x => Cases.Any(c => c.Id == x.CaseId && c.IsScored)), entries.Count(x => x.Error != null),
            results.Length > 0 ? results.Average(x => x.Seconds) : null,
            first.Length > 0 ? first.Average(x => x.FirstTokenSeconds!.Value) : null,
            results.Sum(x => x.InputTokens), results.Sum(x => x.OutputTokens),
            results.Length > 0 && results.All(x => x.CachedInputTokens.HasValue) ? results.Sum(x => x.CachedInputTokens!.Value) : null,
            results.Any(x => x.Estimated), results.Select(x => x.ReasoningSelection?.Sent ?? "unknown").Distinct().ToArray(),
            results.Count(x => x.ReasoningSelection?.Fallback.Length > 0));
    }).ToArray();
    public string Json() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals });
}
