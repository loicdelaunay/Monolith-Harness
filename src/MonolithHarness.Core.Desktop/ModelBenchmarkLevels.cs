using System.Text.Json;

namespace MonolithHarness.Core;

public enum ModelBenchmarkLevel { Easy, Medium, Hard }

public static class ModelBenchmarkLevels
{
    const string HumanEvalCommit = "6d43fb980f9fee3c892a914eda09951f772ad10d";
    static readonly Lazy<IReadOnlyList<ModelBenchmarkCase>> medium = new(() => Build(ModelBenchmarkLevel.Medium));
    public static IReadOnlyList<ModelBenchmarkCase> Cases(ModelBenchmarkLevel level, int seed = 1729) => level switch
    { ModelBenchmarkLevel.Medium => medium.Value, ModelBenchmarkLevel.Hard => FrontierBenchmark.Create(seed), _ => ModelBenchmark.Cases };
    public static string Id(ModelBenchmarkLevel level) => level.ToString().ToLowerInvariant();

    static IReadOnlyList<ModelBenchmarkCase> Build(ModelBenchmarkLevel level)
    {
        var cases = new List<ModelBenchmarkCase> { ModelBenchmark.Cases[0] };
        using var bbh = Resource("bbh-subset.json");
        foreach (var item in bbh.RootElement.GetProperty("Examples").EnumerateArray())
        {
            if (item.GetProperty("Level").GetString() != Id(level)) continue;
            var task = item.GetProperty("Task").GetString()!; var index = item.GetProperty("Index").GetInt32();
            var (fr, en) = task switch
            {
                "boolean_expressions" => ("Expressions booléennes", "Boolean expressions"),
                "logical_deduction_five_objects" => ("Déduction · cinq objets", "Deduction · five objects"),
                "logical_deduction_seven_objects" => ("Déduction · sept objets", "Deduction · seven objects"),
                "date_understanding" => ("Raisonnement sur les dates", "Date reasoning"),
                "reasoning_about_colored_objects" => ("Contraintes de couleur", "Colored object reasoning"),
                "tracking_shuffled_objects_seven_objects" => ("Suivi de sept objets permutés", "Tracking seven shuffled objects"),
                "dyck_languages" => ("Fermeture de parenthèses imbriquées", "Nested bracket completion"),
                _ => ("Calculs imbriqués à plusieurs étapes", "Nested multistep arithmetic")
            };
            var answer = item.GetProperty("Target").GetString()!;
            cases.Add(new($"bbh-{task}-{index}", "logic", $"BBH · {fr} · #{index}", $"BBH · {en} · #{index}",
                item.GetProperty("Input").GetString() + "\nReturn {\"answer\":\"your answer\"}. " +
                "For multiple choice use the parenthesized option letter, e.g. (A). For arithmetic use the integer as a string; " +
                "for Boolean expressions use True or False as a string; for bracket completion separate closing brackets with a single space.",
                JsonSerializer.Serialize(new { answer }), "Exact target from the published BBH dataset (zero-based example index).",
                Source: "BIG-Bench Hard · MIT", SourceUrl: item.GetProperty("Source").GetString(), SourceItem: $"{task}/examples[{index}]",
                Adaptation: "Original English question and target; response wrapped in JSON. Selected subset, not an official full BBH score."));
        }
        using var humanEval = Resource("humaneval-source.json");
        ModelBenchmarkCase Bug(int id, string titleFr, string titleEn, string code, string expected, string explanation, string[] patches)
        {
            var taskId = "HumanEval/" + id;
            var original = humanEval.RootElement.EnumerateArray().Single(e => e.GetProperty("task_id").GetString() == taskId).GetProperty("prompt").GetString();
            return new("humaneval-bug-" + id, "bug", "HumanEval · " + titleFr, "HumanEval · " + titleEn,
                "Original HumanEval specification:\n" + original + "\nBug-fixing adaptation (the bug is introduced by Monolith Harness):\n" + code,
                expected, explanation, patches, "HumanEval · MIT", $"https://github.com/openai/human-eval/blob/{HumanEvalCommit}/data/HumanEval.jsonl.gz",
                taskId, "Original specification; locally introduced bug and fixed regression inputs. Exact patch matching, no code execution; not HumanEval pass@1.");
        }
        if (level == ModelBenchmarkLevel.Medium)
        {
            cases.Add(Bug(12, "égalité de longueur", "length ties", """
                def longest(strings):
                    best = None
                    for s in strings:
                        if best is None or len(s) >= len(best):
                            best = s
                    return best
                Fix the complete if-condition to keep the FIRST longest string. Return
                {"patch":"correct condition only","outputs":[longest([]),longest(["aa","bb","c"]),longest(["x","hello","world"])]}.
                """, """{"outputs":[null,"aa","hello"]}""", "Strict comparison preserves the first maximum; guard None before len(best).",
                ["best is None or len(s) > len(best)", "best == None or len(s) > len(best)"]));
            cases.Add(Bug(20, "doublons et distance minimale", "duplicates and minimum distance", """
                def find_closest_elements(numbers):
                    best, distance = None, float('inf')
                    for i, x in enumerate(numbers):
                        for j, y in enumerate(numbers):
                            if x != y and abs(x-y) < distance:
                                best, distance = tuple(sorted((x,y))), abs(x-y)
                    return best
                Replace only x != y so distinct positions with equal values are allowed. Return
                {"patch":"replacement condition","outputs":[find_closest_elements([1,2,5,2]),find_closest_elements([-8,-1,0,6]),find_closest_elements([1,3,1.2])]}.
                Encode tuples as JSON arrays.
                """, """{"outputs":[[2,2],[-1,0],[1,1.2]]}""", "Compare indices rather than values: duplicates at distinct indices can form the closest pair.", ["i != j", "j != i"]));
            cases.Add(Bug(115, "arrondi par puits", "per-well rounding", """
                def max_fill(grid, capacity):
                    return sum(sum(row) // capacity for row in grid)
                Fix the complete return expression using integer arithmetic and a sum over rows.
                Return {"patch":"expression only","outputs":[max_fill([[1,0],[0,1]],2),max_fill([[0,0],[0,0]],3),max_fill([[1,1,1],[1,1,0]],2)]}.
                """, """{"outputs":[2,0,3]}""", "Round each well upwards independently; water cannot be pooled across wells.",
                ["sum((sum(row) + capacity - 1) // capacity for row in grid)", "sum(-(-sum(row) // capacity) for row in grid)", "sum((sum(r) + capacity - 1) // capacity for r in grid)"]));
        }
        else
        {
            cases.Add(Bug(10, "palindrome minimal", "shortest palindrome", """
                def make_palindrome(string):
                    if not string: return ''
                    beginning_of_suffix = 0
                    while not is_palindrome(string[:beginning_of_suffix]):
                        beginning_of_suffix += 1
                    return string + string[:beginning_of_suffix][::-1]
                Replace the complete while-condition. is_palindrome is defined in the specification.
                Return {"patch":"condition only","outputs":[make_palindrome("race"),make_palindrome("abac"),make_palindrome("aaaa"),make_palindrome("abcc")]}.
                """, """{"outputs":["racecar","abacaba","aaaa","abccba"]}""", "Find the longest palindromic suffix, then append only the reversed remaining prefix.",
                ["not is_palindrome(string[beginning_of_suffix:])", "is_palindrome(string[beginning_of_suffix:]) == False"]));
            cases.Add(Bug(32, "invariant de dichotomie", "bisection invariant", """
                def find_zero(xs):
                    begin, end = -1., 1.
                    while poly(xs, begin) * poly(xs, end) > 0:
                        begin *= 2.; end *= 2.
                    while end - begin > 1e-10:
                        center = (begin + end) / 2.
                        if poly(xs, center) > 0:
                            begin = center
                        else:
                            end = center
                    return begin
                Fix the complete if-condition for polynomials with either positive or negative leading coefficient.
                Return {"patch":"condition only","outputs":[round(find_zero([1,2]),2),round(find_zero([8,0,0,-1]),2),round(find_zero([8,0,0,1]),2)]}.
                """, """{"outputs":[-0.5,2,-2]}""", "Keep the half interval that brackets a sign change; absolute sign alone assumes a leading coefficient sign.",
                ["poly(xs, center) * poly(xs, begin) > 0", "poly(xs, begin) * poly(xs, center) > 0", "poly(xs, center) * poly(xs, begin) > 0.0"]));
            cases.Add(Bug(129, "chemin lexicographique", "lexicographic path", """
                Faulty algorithm: locate 1; set val to the smallest OTHER value anywhere in the grid;
                return [1 if i % 2 == 0 else val for i in range(k)]. It may jump to a non-neighbor.
                At row r, column c of 1, fix val using precisely one of these options:
                A: minimum among all non-1 cells, with diagonals allowed
                B: minimum among in-bounds cells sharing an edge with (r,c); revisits allowed
                C: minimum unvisited neighbor, removing each visited cell from future choices
                D: minimum among in-bounds diagonal cells only
                Return {"patch":"option letter","outputs":[minPath([[1,9,2],[8,7,3],[6,5,4]],7),minPath([[5,9,3],[4,1,6],[7,8,2]],6),minPath([[4,3],[2,1]],1)]}.
                """, """{"outputs":[[1,8,1,8,1,8,1],[1,4,1,4,1,4],[1]]}""", "Lexicographic order first requires 1, then its smallest edge-neighbor, then repeated visits to 1. The global second-smallest value may be unreachable in one step.", ["B"]));
        }
        return cases;
    }
    static JsonDocument Resource(string name)
    {
        using var stream = typeof(ModelBenchmarkLevels).Assembly.GetManifestResourceStream("MonolithHarness.Core.Benchmarks." + name) ?? throw new IOException("Benchmark resource missing: " + name);
        using var reader = new StreamReader(stream);
        return JsonDocument.Parse(reader.ReadToEnd());
    }
}
