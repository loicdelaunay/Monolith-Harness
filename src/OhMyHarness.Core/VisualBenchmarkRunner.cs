using System.Text.Json;
using System.Text.Json.Nodes;

namespace OhMyHarness.Core;

public static class VisualBenchmarkRunner
{
    public delegate Task<JsonNode?> Invoke(string method, object? arguments, CancellationToken ct);

    public static async Task<BenchmarkJudgment> RunAsync(VisualBenchmarkTask task, ModelBenchmarkLevel level, int seed, Invoke invoke, CancellationToken ct)
    {
        var checks = new List<BenchmarkCheck>(); var random = new Random(seed ^ StableKind(task.Kind)); var unresponsive = false;
        async Task Check(string name, Func<Task<bool>> check)
        {
            ct.ThrowIfCancellationRequested();
            if (unresponsive) { checks.Add(new(name, false, "Application interrompue après dépassement du délai.")); return; }
            try { checks.Add(new(name, await check())); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (TimeoutException ex) { unresponsive = true; checks.Add(new(name, false, ex.Message)); }
            catch (Exception ex) { checks.Add(new(name, false, ex.Message)); }
        }
        Task<JsonNode?> Call(string name, object? value = null) => invoke(name, value, ct);
        await Check("Rendu interactif présent", async () =>
        {
            var probe = await Call("$probe");
            return probe?["surfaces"]?.GetValue<int>() > 0 && probe?["controls"]?.GetValue<int>() >= 3;
        });
        switch (task.Kind)
        {
            case "rubik": await Rubik(); break;
            case "orbits": await Orbits(); break;
            case "circuits": await Circuits(); break;
            case "paths": await Paths(); break;
            default: throw new ArgumentException("Unknown visual task.");
        }
        await Check("Aucune erreur JavaScript non interceptée", async () => (await Call("$probe"))?["errors"]?.AsArray().Count == 0);
        return new(checks);

        async Task Camera()
        {
            await Call("camera", new { yaw = 10, pitch = 20, zoom = 1 }); var before = await Call("$probe");
            await Call("camera", new { yaw = 95, pitch = -25, zoom = 1.45 }); var after = await Call("$probe");
            if (before?["fingerprint"]?.GetValue<string>() == after?["fingerprint"]?.GetValue<string>()) throw new IOException("La vue rendue ne change pas avec la caméra.");
        }
        async Task Rubik()
        {
            await Check("Import du cube et mouvements Singmaster", async () =>
            {
                await Call("load", new { facelets = BenchmarkCube.Solved }); var state = BenchmarkCube.Solved;
                foreach (var move in new[] { "R", "U", "R'", "U'", "F2", "D", "L'", "B2" })
                {
                    await Call("turn", move); state = BenchmarkCube.Apply(state, move);
                    if ((await Call("state"))?.GetValue<string>() != state) return false;
                }
                return true;
            });
            foreach (var depth in new[] { 4, level == ModelBenchmarkLevel.Easy ? 6 : 10, level == ModelBenchmarkLevel.Hard ? 20 : level == ModelBenchmarkLevel.Medium ? 14 : 8 })
            {
                var state = BenchmarkCube.Apply(BenchmarkCube.Solved, BenchmarkCube.Scramble(random, depth));
                await Check($"Résolution sans historique · mélange de {depth} coups", async () =>
                {
                    await Call("load", new { facelets = state });
                    if ((await Call("state"))?.GetValue<string>() != state) return false;
                    var moves = (await Call("solve"))?.Deserialize<string[]>() ?? [];
                    if (moves.Length is 0 or > 160 || (await Call("state"))?.GetValue<string>() != state) return false;
                    var solved = BenchmarkCube.Apply(state, moves);
                    if (solved != BenchmarkCube.Solved) return false;
                    // Exercise the same public moves as the UI playback, not a color reset.
                    foreach (var move in moves) await Call("turn", move);
                    return (await Call("state"))?.GetValue<string>() == solved;
                });
            }
            await Check("Caméra orbitale visible", async () => { await Camera(); return true; });
            // End on an unsolved, inspectable scene for the user.
            if (!unresponsive) try { await Call("load", new { facelets = BenchmarkCube.Apply(BenchmarkCube.Solved, BenchmarkCube.Scramble(random, 12)) }); } catch (Exception) when (!ct.IsCancellationRequested) { }
        }
        async Task Orbits()
        {
            var fixtures = new[]
            {
                new[] { new Body("a", 1, [-.5,0,0], [0,-.7071067811865476,0]), new Body("b", 1, [.5,0,0], [0,.7071067811865476,0]) },
                Enumerable.Range(0, 5).Select(i => new Body("b" + i, random.Next(1, 6), [i * 1.7 - 3, random.NextDouble() * 2, random.NextDouble() * 3], [random.NextDouble() * .2, random.NextDouble() * .2, random.NextDouble() * .2])).ToArray()
            };
            for (var f = 0; f < fixtures.Length; f++)
            {
                var bodies = fixtures[f]; var steps = f == 0 ? 1000 : 250; const double dt = .002, softening = .05;
                var expected = bodies.Select(b => b with { position = b.position.ToArray(), velocity = b.velocity.ToArray() }).ToArray();
                Integrate(expected, dt, steps, 1, softening);
                await Check($"Intégration Verlet · {bodies.Length} corps, {steps} pas", async () =>
                {
                    await Call("load", new { bodies, G = 1, softening }); await Call("step", new { dt, steps });
                    var actual = (await Call("state"))?["bodies"]?.Deserialize<Body[]>() ?? [];
                    return actual.Length == expected.Length && expected.All(e => actual.Any(a => a.id == e.id && Near(a.mass, e.mass) && VectorNear(a.position, e.position) && VectorNear(a.velocity, e.velocity)));
                });
            }
            await Check("Caméra indépendante de la simulation", async () =>
            {
                var before = await Call("state"); await Camera(); return JsonNode.DeepEquals(before, await Call("state"));
            });
        }
        async Task Circuits()
        {
            var nodes = new List<Gate> { new("a", "INPUT", 40, 40), new("b", "INPUT", 40, 130), new("c", "INPUT", 40, 220),
                new("xor1", "XOR", 200, 60), new("sum", "XOR", 380, 60), new("and1", "AND", 200, 190), new("and2", "AND", 380, 190), new("carry", "OR", 550, 190) };
            var wires = new List<Wire> { new("a","xor1",0),new("b","xor1",1),new("xor1","sum",0),new("c","sum",1),
                new("a","and1",0),new("b","and1",1),new("c","and2",0),new("xor1","and2",1),new("and1","carry",0),new("and2","carry",1) };
            await Check("Additionneur complet · huit combinaisons", async () =>
            {
                await Call("load", new { nodes = nodes.AsEnumerable().Reverse().ToArray(), wires });
                for (var mask = 0; mask < 8; mask++)
                {
                    var inputs = new Dictionary<string, bool> { ["a"] = (mask & 1) != 0, ["b"] = (mask & 2) != 0, ["c"] = (mask & 4) != 0 };
                    var result = await Call("evaluate", inputs); var expected = Evaluate(nodes, wires, inputs);
                    if (result?["cycle"]?.GetValue<bool>() != false || !expected.All(p => result["values"]?[p.Key]?.GetValue<bool>() == p.Value)) return false;
                }
                return true;
            });
            await Check("Déplacement d’une porte et de son rendu", async () =>
            {
                var before = await Call("$probe"); await Call("moveNode", new { id = "sum", x = 470, y = 310 });
                var state = await Call("state"); var gate = state?["nodes"]?.AsArray().FirstOrDefault(n => n?["id"]?.GetValue<string>() == "sum");
                return gate?["x"]?.GetValue<double>() == 470 && gate?["y"]?.GetValue<double>() == 310 &&
                    before?["fingerprint"]?.GetValue<string>() != (await Call("$probe"))?["fingerprint"]?.GetValue<string>();
            });
            var randomNodes = Enumerable.Range(0, 5).Select(i => new Gate("i" + i, "INPUT", 20, i * 70 + 20)).ToList(); var randomWires = new List<Wire>();
            for (var i = 0; i < (level == ModelBenchmarkLevel.Hard ? 35 : 15); i++)
            {
                var type = new[] { "AND", "OR", "XOR", "NAND", "NOT" }[random.Next(5)]; var id = "g" + i;
                for (var port = 0; port < (type == "NOT" ? 1 : 2); port++) randomWires.Add(new(randomNodes[random.Next(randomNodes.Count)].id, id, port));
                randomNodes.Add(new(id, type, 120 + (i % 5) * 110, (i / 5) * 70 + 20));
            }
            await Check("Circuit inédit · ordre mélangé et propagation", async () =>
            {
                await Call("load", new { nodes = randomNodes.OrderBy(_ => random.Next()).ToArray(), wires = randomWires.OrderBy(_ => random.Next()).ToArray() });
                for (var k = 0; k < 12; k++)
                {
                    var inputs = Enumerable.Range(0, 5).ToDictionary(i => "i" + i, _ => random.Next(2) == 1);
                    var actual = await Call("evaluate", inputs); var expected = Evaluate(randomNodes, randomWires, inputs);
                    if (actual?["cycle"]?.GetValue<bool>() != false || !expected.All(p => actual["values"]?[p.Key]?.GetValue<bool>() == p.Value)) return false;
                }
                return true;
            });
            await Check("Cycle déconnecté détecté", async () =>
            {
                await Call("load", new { nodes = nodes.Concat([new Gate("loop", "NOT", 100, 300)]), wires = wires.Concat([new Wire("loop", "loop", 0)]) });
                return (await Call("evaluate", new { a = false, b = true, c = false }))?["cycle"]?.GetValue<bool>() == true;
            });
            if (!unresponsive) try { await Call("load", new { nodes, wires }); await Call("evaluate", new { a = true, b = true, c = false }); } catch (Exception) when (!ct.IsCancellationRequested) { }
        }
        async Task Paths()
        {
            var puzzles = new List<Puzzle>
            {
                new Puzzle(6,6,[],[new("a",0,35),new("b",35,0)]),
                new Puzzle(9,7,Enumerable.Range(0,7).Where(y=>y!=3).Select(y=>y*9+4).ToArray(),[new("a",9,53),new("b",17,45)]),
                new Puzzle(4,4,[4,5,6,7],[new("a",0,15),new("b",3,12)])
            };
            var side = level == ModelBenchmarkLevel.Hard ? 9 : 6;
            var corners = new[] { 0, side - 1, side * (side - 1), side * side - 1 };
            for (var scenario = 0; scenario < 2; scenario++)
            {
                var walls = Enumerable.Range(0, side * side).Where(cell => !corners.Contains(cell) && random.NextDouble() < .2).ToArray();
                puzzles.Add(new(side, side, walls, [new("a", corners[0], corners[3]), new("b", corners[1], corners[2])]));
            }
            for (var i = 0; i < puzzles.Count; i++)
            {
                var puzzle = puzzles[i]; var optimal = MinimumMakespan(puzzle);
                await Check($"Plan commun optimal · scénario {i + 1}", async () =>
                {
                    await Call("load", puzzle); var result = await Call("solve");
                    if (optimal < 0) return result is JsonObject obj && obj.ContainsKey("paths") && obj["paths"] == null;
                    var paths = result?["paths"]?.Deserialize<int[][]>() ?? [];
                    if (!ValidPaths(puzzle, paths, optimal)) return false;
                    var t = optimal / 2;
                    var positions = (await Call("seek", t))?["positions"]?.Deserialize<int[]>() ?? [];
                    return positions.SequenceEqual(paths.Select(p => p[t]));
                });
            }
            if (!unresponsive) try { await Call("load", puzzles[1]); } catch (Exception) when (!ct.IsCancellationRequested) { }
        }
    }

    static int StableKind(string value) { var hash = 17; foreach (var c in value) hash = unchecked(hash * 31 + c); return hash; }
    static bool Near(double a, double b) => double.IsFinite(a) && Math.Abs(a - b) <= 1e-5 * (1 + Math.Abs(b));
    static bool VectorNear(double[]? a, double[] b) => a?.Length == b.Length && a.Zip(b).All(p => Near(p.First, p.Second));
    public sealed record Body(string id, double mass, double[] position, double[] velocity);
    static void Integrate(Body[] bodies, double dt, int steps, double gravity, double softening)
    {
        double[][] Acceleration()
        {
            var values = bodies.Select(_ => new double[3]).ToArray();
            for (var i = 0; i < bodies.Length; i++) for (var j = i + 1; j < bodies.Length; j++)
            {
                var delta = Enumerable.Range(0, 3).Select(k => bodies[j].position[k] - bodies[i].position[k]).ToArray();
                var scale = gravity / Math.Pow(delta.Sum(d => d * d) + softening * softening, 1.5);
                for (var k = 0; k < 3; k++) { values[i][k] += scale * bodies[j].mass * delta[k]; values[j][k] -= scale * bodies[i].mass * delta[k]; }
            }
            return values;
        }
        for (var step = 0; step < steps; step++)
        {
            var before = Acceleration();
            for (var i = 0; i < bodies.Length; i++) for (var k = 0; k < 3; k++) bodies[i].position[k] += bodies[i].velocity[k] * dt + .5 * before[i][k] * dt * dt;
            var after = Acceleration();
            for (var i = 0; i < bodies.Length; i++) for (var k = 0; k < 3; k++) bodies[i].velocity[k] += .5 * (before[i][k] + after[i][k]) * dt;
        }
    }
    sealed record Gate(string id, string type, int x, int y, bool value = false);
    sealed record Wire(string from, string to, int port);
    static Dictionary<string, bool> Evaluate(List<Gate> nodes, List<Wire> wires, Dictionary<string, bool> inputs)
    {
        var result = new Dictionary<string, bool>();
        foreach (var n in nodes)
        {
            bool Port(int p) => result[wires.Single(w => w.to == n.id && w.port == p).from];
            result[n.id] = n.type switch { "INPUT" => inputs[n.id], "NOT" => !Port(0), "AND" => Port(0) & Port(1), "OR" => Port(0) | Port(1), "XOR" => Port(0) ^ Port(1), "NAND" => !(Port(0) & Port(1)), _ => false };
        }
        return result;
    }
    sealed record Agent(string id, int start, int goal);
    sealed record Puzzle(int width, int height, int[] walls, Agent[] agents);
    static int[] Neighbors(Puzzle p, int cell) => new[] { cell, cell - p.width, cell + p.width, cell % p.width > 0 ? cell - 1 : -1, cell % p.width < p.width - 1 ? cell + 1 : -1 }
        .Where(c => c >= 0 && c < p.width * p.height && !p.walls.Contains(c)).ToArray();
    static int MinimumMakespan(Puzzle p)
    {
        var queue = new Queue<(int A, int B, int T)>(); var seen = new HashSet<(int, int)>();
        queue.Enqueue((p.agents[0].start, p.agents[1].start, 0)); seen.Add((p.agents[0].start, p.agents[1].start));
        while (queue.TryDequeue(out var state))
        {
            if (state.A == p.agents[0].goal && state.B == p.agents[1].goal) return state.T;
            foreach (var a in Neighbors(p, state.A)) foreach (var b in Neighbors(p, state.B))
                if (a != b && !(a == state.B && b == state.A) && seen.Add((a, b))) queue.Enqueue((a, b, state.T + 1));
        }
        return -1;
    }
    static bool ValidPaths(Puzzle p, int[][] paths, int optimal)
    {
        if (paths.Length != 2 || paths.Any(path => path.Length != optimal + 1)) return false;
        for (var a = 0; a < 2; a++)
        {
            if (paths[a][0] != p.agents[a].start || paths[a][^1] != p.agents[a].goal) return false;
            for (var t = 1; t <= optimal; t++) if (!Neighbors(p, paths[a][t - 1]).Contains(paths[a][t])) return false;
        }
        for (var t = 0; t <= optimal; t++) if (paths[0][t] == paths[1][t] || (t > 0 && paths[0][t] == paths[1][t - 1] && paths[1][t] == paths[0][t - 1])) return false;
        return true;
    }
}
