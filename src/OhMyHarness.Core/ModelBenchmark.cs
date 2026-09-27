using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace OhMyHarness.Core;

public sealed record ModelBenchmarkCase(string Id, string Category, string FrenchTitle, string EnglishTitle,
    string Prompt, string? ExpectedJson, string Explanation, string[]? AcceptedPatches = null,
    string Source = "OhMyHarness", string? SourceUrl = null, string? SourceItem = null, string? Adaptation = null)
{
    [JsonIgnore] public Func<JsonObject, BenchmarkJudgment>? Validator { get; init; }
    [JsonIgnore] public VisualBenchmarkTask? Visual { get; init; }
    public bool IsScored => ExpectedJson != null || Validator != null || Visual != null;
    public ModelToolRequest Request => Visual != null ? Visual.Request : new(
        "Complete this isolated benchmark task. Do not use tools, external sources or prior tasks. " +
        (ExpectedJson == null ? "Follow the requested output length." : "Return only the requested JSON object, without Markdown or commentary."), Prompt);
    public bool? Grade(string response)
    {
        if (Validator != null)
        {
            try { return Validator(ModelUtilityPrompts.ParseObject(response)).Passed; }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException or IndexOutOfRangeException or OverflowException) { return false; }
        }
        if (ExpectedJson == null) return null;
        try
        {
            var answer = ModelUtilityPrompts.ParseObject(response);
            var expected = ModelUtilityPrompts.ParseObject(ExpectedJson);
            if (AcceptedPatches != null)
            {
                var patch = answer["patch"]?.GetValue<string>() ?? "";
                static string Compact(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c))).TrimEnd(';');
                if (!AcceptedPatches.Any(p => Compact(p) == Compact(patch))) return false;
                answer.Remove("patch");
            }
            return JsonNode.DeepEquals(answer, expected);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return false; }
    }
}

public static class ModelBenchmark
{
    public const string SuiteVersion = "omh-model-tools-v3-frontier";
    public static IReadOnlyList<ModelBenchmarkCase> Cases { get; } = [
        new("throughput", "speed", "Génération continue", "Sustained generation",
            "In English, write a clear explanation of how a rainwater collection system works, from the roof to watering a garden. Write between 250 and 350 words, in paragraphs, without a title or a list.",
            null, "Unscored throughput sample. Output tokens divided by total request duration, including latency."),
        new("deduction", "logic", "Déduction logique", "Logical deduction",
            "Every zog is a mip. No mip is a tav. Can any zog be a tav? Return {\"answer\":true} or {\"answer\":false}.",
            "{\"answer\":false}", "Zogs belong to mips, which are disjoint from tavs."),
        new("ordering", "logic", "Ordre sous contraintes", "Constraint ordering",
            "Four tasks A, B, C, D must run sequentially. A precedes B. C follows B. D precedes A. Return their unique order as {\"order\":[...]}, with task names as strings.",
            "{\"order\":[\"D\",\"A\",\"B\",\"C\"]}", "D must precede A, which precedes B, which precedes C."),
        new("rates", "logic", "Raisonnement de débit", "Rate reasoning",
            "Three identical printers produce three parts in three minutes. At the same constant rate, how many minutes do nine printers need to produce 27 parts? Return {\"minutes\":number}.",
            "{\"minutes\":9}", "Each printer produces one part in three minutes; nine printers need three cycles."),
        new("python-boundary", "bug", "Python · borne de boucle", "Python · loop boundary",
            "This Python function should sum all integers from 0 through n, inclusive, for n >= 0:\n" +
            "def sum_to(n):\n    total = 0\n    for i in range(n):\n        total += i\n    return total\n" +
            "Replace only the expression range(n). Return {\"patch\":\"corrected range expression\",\"outputs\":[sum_to(0),sum_to(1),sum_to(5)]}, with numeric outputs.",
            "{\"outputs\":[0,1,15]}", "range excludes its upper bound; the loop must include n.", ["range(n + 1)", "range(0, n + 1)", "range(0, n + 1, 1)"]),
        new("js-sort", "bug", "JavaScript · tri numérique", "JavaScript · numeric sorting",
            "JavaScript's [10, 2, 30].sort() returns [10, 2, 30] instead of ascending numeric order. " +
            "Fix it with .sort((a, b) => EXPRESSION). Return {\"patch\":\"EXPRESSION only\",\"outputs\":[sorted numbers]}, without mutating element values.",
            "{\"outputs\":[2,10,30]}", "The default sort compares strings; a - b provides ascending numeric order.", ["a - b", "(a - b)"]),
        new("csharp-null", "bug", "C# · valeur nulle", "C# · null safety",
            "C# bool IsEmpty(string? name) => name.Length == 0 || name == null; throws for null. " +
            "Replace the complete condition with a null-safe one. Return {\"patch\":\"condition only\",\"outputs\":[IsEmpty(null),IsEmpty(\"\"),IsEmpty(\"abc\")]}, with Boolean outputs.",
            "{\"outputs\":[true,true,false]}", "Check for null before dereferencing, or use string.IsNullOrEmpty.",
            ["name == null || name.Length == 0", "name is null || name.Length == 0", "string.IsNullOrEmpty(name)", "String.IsNullOrEmpty(name)"])
    ];
}

public sealed record ModelBenchmarkEntry(string Id, string Category, string Title, string Prompt,
    ModelToolResult? Result, bool? Passed, string? Error, string Expected, string Explanation,
    string Source = "OhMyHarness", string? SourceUrl = null, string? SourceItem = null, string? Adaptation = null,
    IReadOnlyList<BenchmarkCheck>? Checks = null, string? PreviewHtml = null, double? ElapsedSeconds = null);

public sealed record BenchmarkCheck(string Name, bool Passed, string Detail = "");
public sealed record BenchmarkJudgment(IReadOnlyList<BenchmarkCheck> Checks)
{
    public bool Passed => Checks.Count > 0 && Checks.All(c => c.Passed);
}

public sealed record ModelBenchmarkReport(string Suite, string ApplicationVersion, DateTimeOffset StartedAt,
    string Provider, string Model, IReadOnlyList<ModelBenchmarkEntry> Entries, bool Completed,
    string Level = "easy", int PlannedGraded = 6, int Seed = 1729, string Scope = "reasoning", int TimeoutSeconds = 180, IReadOnlyList<string>? CaseIds = null)
{
    public string Settings => "Provider defaults for sampling and reasoning; isolated requests; no agent tools; throughput includes latency. Visual code runs in an isolated offline preview; functional checks do not certify visual fidelity. Public ARC subset uses one attempt and is not an official ARC score.";
    public int InputTokens => Entries.Sum(e => e.Result?.InputTokens ?? 0);
    public int OutputTokens => Entries.Sum(e => e.Result?.OutputTokens ?? 0);
    public double Seconds => Entries.Sum(e => e.Result?.Seconds ?? 0);
    public double TokensPerSecond => Seconds > 0 ? OutputTokens / Seconds : 0;
    public bool Estimated => Entries.Any(e => e.Result?.Estimated == true);
    public int Passed => Entries.Count(e => e.Passed == true);
    public int Graded => Entries.Count(e => e.Category != "speed");
    public double? SuccessPercent => Graded > 0 ? Passed * 100.0 / Graded : null;
    public bool Provisional => !Completed;
    public string Grade => SuccessPercent switch { null => "—", >= 100 => "S", >= 80 => "A", >= 60 => "B", >= 40 => "C", >= 20 => "D", > 0 => "E", _ => "F" };
    public string GradeScale => "S = 100%; A = [80,100)%; B = [60,80)%; C = [40,60)%; D = [20,40)%; E = (0,20)%; F = 0%. Request errors count as failed; unfinished runs are provisional.";
    public string Json() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
}
