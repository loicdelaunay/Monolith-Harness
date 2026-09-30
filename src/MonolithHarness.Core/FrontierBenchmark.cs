using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

/// <summary>Reproducible hard tasks with exact answers or independently checked witnesses.</summary>
public static class FrontierBenchmark
{
    public static IReadOnlyList<ModelBenchmarkCase> Create(int seed)
    {
        var random = new Random(seed);
        var result = new List<ModelBenchmarkCase> { ModelBenchmark.Cases[0] };
        result.AddRange(Arc(random));
        result.Add(Tour(random)); result.Add(Sat(random)); result.Add(KillerSudoku(random));
        result.AddRange(FrontierCodeCases.Create(random));
        return result;
    }

    static IEnumerable<ModelBenchmarkCase> Arc(Random random)
    {
        using var stream = typeof(FrontierBenchmark).Assembly.GetManifestResourceStream("MonolithHarness.Core.Benchmarks.arc-agi-2-subset.json")!;
        using var reader = new StreamReader(stream);
        var dataset = JsonNode.Parse(reader.ReadToEnd())!;
        var revision = dataset["Revision"]!.GetValue<string>();
        var items = dataset["Tasks"]!.AsArray().OrderBy(_ => random.Next()).Take(6).ToArray();
        foreach (var item in items)
        {
            var id = item!["Id"]!.GetValue<string>(); var data = item["Data"]!;
            var palette = new[] { 0 }.Concat(Enumerable.Range(1, 9).OrderBy(_ => random.Next())).ToArray();
            var rotation = random.Next(4);
            int[][] Transform(JsonNode node)
            {
                var grid = node.Deserialize<int[][]>()!.Select(row => row.Select(v => palette[v]).ToArray()).ToArray();
                for (var r = 0; r < rotation; r++) grid = Enumerable.Range(0, grid[0].Length).Select(x => Enumerable.Range(0, grid.Length).Select(y => grid[grid.Length - 1 - y][x]).ToArray()).ToArray();
                return grid;
            }
            var examples = data["train"]!.AsArray().Select(p => new { input = Transform(p!["input"]!), output = Transform(p["output"]!) }).ToArray();
            var inputs = data["test"]!.AsArray().Select(p => Transform(p!["input"]!)).ToArray();
            var outputs = data["test"]!.AsArray().Select(p => Transform(p!["output"]!)).ToArray();
            yield return new("arc2-" + id, "logic", "ARC-AGI-2 · " + id, "ARC-AGI-2 · " + id,
                "Infer the transformation shared by the training grid pairs and apply it to EVERY test input. " +
                "Digits 0..9 are color labels, not quantities. Dimensions may change. Return only {\"outputs\":[grid_for_test_0,...]} " +
                "with rectangular integer grids. Every cell and every output dimension must be correct.\n" + JsonSerializer.Serialize(new { train = examples, test = inputs }),
                JsonSerializer.Serialize(new { outputs }), "Exact grid comparison, all test outputs required. One attempt. Consistent palette permutation (0 fixed) and rotation across examples and tests.",
                Source: "ARC-AGI-2 · Apache-2.0", SourceUrl: $"https://github.com/arcprize/ARC-AGI-2/blob/{revision}/data/evaluation/{id}.json", SourceItem: id,
                Adaptation: $"Public evaluation subset; one attempt; rotation {rotation * 90}°; palette [{string.Join(',', palette)}]. Not an official ARC evaluation or a calibrated frontier leaderboard.");
        }
    }

    static ModelBenchmarkCase Tour(Random random)
    {
        const int n = 14, infinity = 1_000_000;
        var costs = Enumerable.Range(0, n).Select(i => Enumerable.Range(0, n).Select(j => i == j ? 0 : random.Next(7, 100)).ToArray()).ToArray();
        var count = 1 << (n - 1); var dp = new int[count, n]; var previous = new int[count, n];
        for (var mask = 0; mask < count; mask++) for (var j = 0; j < n; j++) dp[mask, j] = infinity;
        for (var j = 1; j < n; j++) dp[1 << (j - 1), j] = costs[0][j];
        for (var mask = 1; mask < count; mask++)
            for (var last = 1; last < n; last++)
                if ((mask & (1 << (last - 1))) != 0)
                    for (var next = 1; next < n; next++)
                    {
                        if ((mask & (1 << (next - 1))) != 0) continue;
                        var target = mask | (1 << (next - 1)); var value = dp[mask, last] + costs[last][next];
                        if (value < dp[target, next]) { dp[target, next] = value; previous[target, next] = last; }
                    }
        var end = Enumerable.Range(1, n - 1).MinBy(j => dp[count - 1, j] + costs[j][0]);
        var optimum = dp[count - 1, end] + costs[end][0];
        var tour = new List<int> { 0 }; var bits = count - 1; var cursor = end;
        while (bits != 0) { tour.Add(cursor); var p = previous[bits, cursor]; bits ^= 1 << (cursor - 1); cursor = p; }
        tour.Add(0); tour.Reverse();
        return new("frontier-atsp", "logic", "Optimisation exacte · tournée asymétrique", "Exact optimization · asymmetric tour",
            "Find a minimum-cost directed Hamiltonian cycle through all 14 vertices. Start and finish at vertex 0; visit every other vertex exactly once. " +
            "Costs are directional (cost[i][j] need not equal cost[j][i]). Return {\"route\":[0,...,0],\"cost\":integer}. A feasible but nonoptimal tour fails.\n" + JsonSerializer.Serialize(new { costs }),
            JsonSerializer.Serialize(new { route = tour, cost = optimum }), "The optimum is computed independently with Held–Karp dynamic programming. Any optimal tour is accepted.")
        {
            Validator = answer =>
            {
                var route = answer["route"]?.Deserialize<int[]>() ?? [];
                var valid = route.Length == n + 1 && route[0] == 0 && route[^1] == 0 && route.Take(n).Distinct().Count() == n && route.All(v => v >= 0 && v < n);
                var cost = valid ? Enumerable.Range(0, n).Sum(i => costs[route[i]][route[i + 1]]) : -1;
                return new([new("Cycle hamiltonien", valid), new("Coût annoncé exact", valid && answer["cost"]?.GetValue<int>() == cost), new("Optimalité prouvée", valid && cost == optimum, $"Optimum = {optimum}")]);
            }
        };
    }

    static ModelBenchmarkCase Sat(Random random)
    {
        const int n = 42;
        var witness = Enumerable.Range(0, n).Select(_ => random.Next(2) == 1).ToArray();
        var clauses = new List<int[]>();
        for (var i = 0; i < 190; i++)
        {
            var variables = Enumerable.Range(1, n).OrderBy(_ => random.Next()).Take(3).ToArray();
            var clause = variables.Select(v => random.Next(2) == 0 ? v : -v).ToArray();
            if (!clause.Any(l => witness[Math.Abs(l) - 1] == (l > 0))) clause[0] *= -1;
            clauses.Add(clause);
        }
        return new("frontier-3sat", "logic", "Contraintes · 42 variables, 190 clauses", "Constraints · 42 variables, 190 clauses",
            "Find a Boolean assignment satisfying EVERY 3-SAT clause. Variables are x1..x42; positive k means xk and negative -k means NOT xk. " +
            "Each clause is an OR; all clauses are joined by AND. The instance is guaranteed satisfiable. Return {\"assignment\":[42 booleans in variable order]}.\n" + JsonSerializer.Serialize(new { clauses }),
            JsonSerializer.Serialize(new { assignment = witness }), "A satisfying witness is planted at generation time; every submitted clause is checked independently. Any satisfying assignment is accepted.")
        {
            Validator = answer =>
            {
                var assignment = answer["assignment"]?.Deserialize<bool[]>() ?? [];
                var matches = assignment.Length == n ? clauses.Count(c => c.Any(l => assignment[Math.Abs(l) - 1] == (l > 0))) : 0;
                return new([new("42 variables", assignment.Length == n), new("190 clauses satisfaites", matches == clauses.Count, $"{matches}/{clauses.Count}")]);
            }
        };
    }

    static ModelBenchmarkCase KillerSudoku(Random random)
    {
        var digits = Enumerable.Range(1, 9).OrderBy(_ => random.Next()).ToArray();
        var rows = Enumerable.Range(0, 3).OrderBy(_ => random.Next()).SelectMany(b => Enumerable.Range(b * 3, 3).OrderBy(_ => random.Next())).ToArray();
        var columns = Enumerable.Range(0, 3).OrderBy(_ => random.Next()).SelectMany(b => Enumerable.Range(b * 3, 3).OrderBy(_ => random.Next())).ToArray();
        var solution = rows.Select(r => columns.Select(c => digits[(r * 3 + r / 3 + c) % 9]).ToArray()).ToArray();
        var unused = Enumerable.Range(0, 81).ToHashSet(); var cages = new List<(int[] Cells, int Sum)>();
        while (unused.Count > 0)
        {
            var first = unused.Order().ElementAt(random.Next(unused.Count)); var cells = new List<int> { first }; unused.Remove(first);
            var size = random.Next(3, 6);
            while (cells.Count < size)
            {
                var candidates = cells.SelectMany(c => new[] { c - 9, c + 9, c % 9 > 0 ? c - 1 : -1, c % 9 < 8 ? c + 1 : -1 })
                    .Where(c => unused.Contains(c) && !cells.Any(x => solution[x / 9][x % 9] == solution[c / 9][c % 9])).Distinct().Order().ToArray();
                if (candidates.Length == 0) break;
                var next = candidates[random.Next(candidates.Length)]; cells.Add(next); unused.Remove(next);
            }
            cages.Add((cells.ToArray(), cells.Sum(c => solution[c / 9][c % 9])));
        }
        return new("frontier-killer-sudoku", "logic", "Killer Sudoku · contraintes croisées", "Killer Sudoku · intersecting constraints",
            "Solve this 9x9 Killer Sudoku. Rows, columns and 3x3 boxes must each contain 1..9 exactly once. Each cage's cells must be DISTINCT and sum to its target. " +
            "Cell indices are 0..80 in row-major order. The instance is solvable; any valid solution is accepted. Return {\"grid\":[9 arrays of 9 integers]}.\n" +
            JsonSerializer.Serialize(new { cages = cages.Select(c => new { cells = c.Cells, sum = c.Sum }) }),
            JsonSerializer.Serialize(new { grid = solution }), "Checks all 27 Sudoku units, all cage sums and cage distinctness. The provided witness proves satisfiability; a different valid solution is allowed.")
        {
            Validator = answer =>
            {
                var grid = answer["grid"]?.Deserialize<int[][]>() ?? [];
                if (grid.Length != 9 || grid.Any(r => r.Length != 9 || r.Any(v => v is < 1 or > 9))) return new([new("Grille 9×9 valide", false)]);
                bool Unique(IEnumerable<int> values) => values.Distinct().Count() == 9;
                return new([
                    new("Lignes et colonnes", Enumerable.Range(0, 9).All(i => Unique(grid[i]) && Unique(grid.Select(r => r[i])))),
                    new("Neuf blocs 3×3", Enumerable.Range(0, 9).All(b => Unique(Enumerable.Range(0, 9).Select(k => grid[b / 3 * 3 + k / 3][b % 3 * 3 + k % 3])))),
                    new("Sommes et unicité des cages", cages.All(c => c.Cells.Sum(i => grid[i / 9][i % 9]) == c.Sum && c.Cells.Select(i => grid[i / 9][i % 9]).Distinct().Count() == c.Cells.Length))]);
            }
        };
    }
}
