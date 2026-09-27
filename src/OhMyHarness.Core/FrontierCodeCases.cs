using System.Text.Json;

namespace OhMyHarness.Core;

internal static class FrontierCodeCases
{
    internal static IEnumerable<ModelBenchmarkCase> Create(Random random)
    {
        yield return ShortestPaths(random); yield return AffineUpdates(random); yield return Components(random);
    }

    static ModelBenchmarkCase ShortestPaths(Random random)
    {
        const int n = 12, inf = 1_000_000;
        var edges = new List<int[]> { new[] { 0, 1, 9 }, new[] { 0, 2, 1 }, new[] { 2, 1, 1 } };
        for (var i = 0; i < n; i++) edges.Add([i, (i + 1) % n, random.Next(20, 60)]);
        for (var i = 0; i < 27; i++)
        {
            var a = random.Next(n); var b = random.Next(n);
            if (a != b) edges.Add([a, b, random.Next(10, 90)]);
        }
        var distance = Enumerable.Range(0, n).Select(i => Enumerable.Range(0, n).Select(j => i == j ? 0 : inf).ToArray()).ToArray();
        foreach (var e in edges) distance[e[0]][e[1]] = Math.Min(distance[e[0]][e[1]], e[2]);
        for (var k = 0; k < n; k++) for (var i = 0; i < n; i++) for (var j = 0; j < n; j++) distance[i][j] = Math.Min(distance[i][j], distance[i][k] + distance[k][j]);
        var wrong = new List<int[]>();
        for (var source = 0; source < n; source++)
        {
            var seen = new HashSet<int> { source }; var buggy = Enumerable.Repeat(inf, n).ToArray(); buggy[source] = 0;
            var queue = new PriorityQueue<int, (int, int)>(); queue.Enqueue(source, (0, source));
            while (queue.TryDequeue(out var u, out _))
                foreach (var edge in edges.Where(e => e[0] == u))
                    if (seen.Add(edge[1])) { buggy[edge[1]] = buggy[u] + edge[2]; queue.Enqueue(edge[1], (buggy[edge[1]], edge[1])); }
            for (var target = 0; target < n; target++) if (buggy[target] != distance[source][target]) wrong.Add([source, target]);
        }
        return new("frontier-dijkstra", "bug", "Dijkstra · régression complète", "Dijkstra · full regression",
            "Audit this incorrect shortest-path routine. It sets seen[source]=true, distance[source]=0 and pushes (0,source). " +
            "Pop the minimum (distance,vertex), then scan outgoing edges in the given order. For every not-seen neighbor v, immediately mark v seen, set distance[v]=distance[u]+weight, and push it. " +
            "The priority queue compares distance then vertex; unreachable distances start at infinity. This incorrectly finalizes vertices on insertion. " +
            "Repair the semantics and return ALL correct shortest-path distances and ALL (source,target) pairs where the faulty routine differs. " +
            "Return {\"distances\":[12 rows of 12 integers],\"wrongPairs\":[[s,t],...]}, with pairs sorted by source then target. Directed edges allow parallel edges.\n" + JsonSerializer.Serialize(new { vertices = n, edges }),
            JsonSerializer.Serialize(new { distances = distance, wrongPairs = wrong }), "Independent Floyd–Warshall oracle, plus simulation of the faulty queue in the specified tie and edge order. Every distance and mismatch pair must match.");
    }

    static ModelBenchmarkCase AffineUpdates(Random random)
    {
        const long mod = 1_000_000_007;
        var initial = Enumerable.Range(0, 19).Select(_ => (long)random.Next(1, 100)).ToArray();
        var current = initial.ToArray(); var operations = new List<object>(); var sums = new List<long>();
        for (var i = 0; i < 48; i++)
        {
            var left = random.Next(0, 18); var right = random.Next(left + 1, 20);
            if (i % 3 != 2)
            {
                var a = random.Next(0, 7); var b = random.Next(0, 100);
                operations.Add(new { type = "affine", l = left, r = right, a, b });
                for (var j = left; j < right; j++) current[j] = (a * current[j] + b) % mod;
            }
            else { operations.Add(new { type = "sum", l = left, r = right }); sums.Add(current[left..right].Sum() % mod); }
        }
        var probes = Enumerable.Range(0, 6).Select(_ => new { before = new[] { random.Next(0, 9), random.Next(1, 40) }, after = new[] { random.Next(0, 9), random.Next(1, 40) } }).ToArray();
        var composed = probes.Select(p => new long[] { p.after[0] * p.before[0], p.after[0] * p.before[1] + p.after[1] }).ToArray();
        return new("frontier-affine-tree", "bug", "Lazy propagation · composition affine", "Lazy propagation · affine composition",
            "A range-sum segment tree stores lazy transforms x -> a*x+b modulo 1000000007. The buggy compose(before,after) returns " +
            "(before.a*after.a, before.a*after.b+before.b), incorrectly reversing time order. Correct its semantics and compute the resulting affine coefficients for every probe, " +
            "then the answers to EVERY sum query after the supplied sequence of range updates. Intervals are zero-based half-open [l,r); updates use the current array. " +
            "Return {\"composed\":[[a,b],...],\"sums\":[integer,...],\"final\":[19 integers]}. All values are reduced modulo 1000000007.\n" + JsonSerializer.Serialize(new { initial, probes, operations }),
            JsonSerializer.Serialize(new { composed, sums, final = current }), "Oracle directly updates each array element without a segment tree. Checks noncommutative composition, all 16 query results and all final values.");
    }

    static ModelBenchmarkCase Components(Random random)
    {
        const int n = 24;
        var permutation = Enumerable.Range(0, n).OrderBy(_ => random.Next()).ToArray(); var edges = new List<int[]>();
        var components = Enumerable.Range(0, 8).Select(i => permutation.Skip(i * 3).Take(3).Order().ToArray()).OrderBy(c => c[0]).ToArray();
        foreach (var component in components)
            for (var i = 0; i < component.Length; i++) edges.Add([component[i], component[(i + 1) % component.Length]]);
        for (var i = 1; i < components.Length; i++)
        {
            edges.Add([components[i][random.Next(3)], components[i - 1][random.Next(3)]]);
            for (var j = 0; j < i - 1; j++) if (random.Next(2) == 1) edges.Add([components[i][random.Next(3)], components[j][random.Next(3)]]);
        }
        edges = edges.OrderBy(_ => random.Next()).ToList();
        var condensation = edges.Select(e => new[] { Array.FindIndex(components, c => c.Contains(e[0])), Array.FindIndex(components, c => c.Contains(e[1])) })
            .Where(e => e[0] != e[1]).DistinctBy(e => (e[0], e[1])).OrderBy(e => e[0]).ThenBy(e => e[1]).ToArray();
        return new("frontier-tarjan", "bug", "Tarjan · composantes et graphe condensé", "Tarjan · components and condensation graph",
            "A Tarjan SCC implementation lowers low[u] on EVERY edge to a visited vertex, including vertices already removed from the DFS stack. " +
            "This merges or loses components. Correct the semantics: only back-edges to vertices still on the stack may lower low[u] to index[v]. " +
            "For this directed graph compute all strongly connected components and the complete condensation graph (without duplicates or self edges). " +
            "Return {\"components\":[sorted vertex arrays, ordered by minimum vertex],\"edges\":[[componentIndexFrom,componentIndexTo],...]}. " +
            "Condensation indices are zero-based positions in your sorted components array; edges must be sorted lexicographically.\n" + JsonSerializer.Serialize(new { vertices = n, edges }),
            JsonSerializer.Serialize(new { components, edges = condensation }), "The generator constructs disjoint strongly connected cycles with a one-way acyclic component graph; these constitute the exact partition certificate. All components and all condensation edges are required.");
    }
}
