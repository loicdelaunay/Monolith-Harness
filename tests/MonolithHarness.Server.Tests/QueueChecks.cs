using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Services;
using MonolithHarness.Server.Workers;

/// <summary>Holds each started request until the test releases it; stands in for the worker processes.</summary>
sealed class FakeExecutor : IRunExecutor
{
    public ConcurrentDictionary<Guid, TaskCompletionSource> Gates { get; } = new();
    public ConcurrentQueue<Guid> Started { get; } = new();
    public RunService Runs { get; set; } = null!;

    public async Task ExecuteAsync(Guid runId, ActiveRun active)
    {
        Started.Enqueue(runId);
        var gate = Gates.GetOrAdd(runId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            await gate.Task.WaitAsync(active.Cancellation.Token);
            await Runs.SetStateAsync(runId, RunStates.Completed);
        }
        catch (OperationCanceledException) { await Runs.SetStateAsync(runId, RunStates.Cancelled, active.Reason ?? "Cancelled."); }
    }

    public void Release(Guid runId) => Gates.GetOrAdd(runId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
}

static class QueueChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        // ---- Fair order (pure function) ----
        RunRequest R(Guid user, long sequence) => new() { RequesterId = user, Sequence = sequence };
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var order = RunService.FairOrder([R(a, 1), R(a, 2), R(a, 3), R(a, 4), R(b, 5), R(c, 6)], []);
        check(order.Take(3).Select(x => x.RequesterId).SequenceEqual([a, b, c]) && order.Select(x => x.Sequence).SequenceEqual([1L, 5, 6, 2, 3, 4]), "Ordre équitable: un utilisateur qui inonde la file ne prive pas les autres");
        order = RunService.FairOrder([R(a, 1), R(b, 2), R(c, 3)], [a, a]);
        check(order[0].RequesterId != a && order.Last().RequesterId == a, "Les exécutions déjà en cours comptent dans l’équité");
        check(RunService.FairOrder([R(a, 1), R(a, 2)], []).Select(x => x.Sequence).SequenceEqual([1L, 2]), "Au sein d’un utilisateur, l’ordre d’arrivée est respecté");

        // ---- Limits, states, cancellation (workers replaced by a fake) ----
        var fake = new FakeExecutor();
        await using var server = await TestServer.StartAsync(builder => builder.Services.AddSingleton<IRunExecutor>(fake));
        fake.Runs = server.Service<RunService>();
        var admin = await server.LoginAsync();
        string Code((int Status, JsonElement Body) r) => r.Body.ValueKind == JsonValueKind.Object && r.Body.TryGetProperty("code", out var x) ? x.GetString()! : "";
        await server.EnableAsync(admin, "cloud");

        var provider = (await server.Post("/api/admin/providers", new { name = "P", baseUrl = "http://127.0.0.1:9/v1", models = new[] { new { id = "m" } } }, admin)).Body.GetProperty("id").GetGuid();
        var userRole = await server.RoleId("User", admin);
        await server.Put($"/api/admin/providers/{provider}/access", new { entries = new[] { new { kind = "role", id = userRole, name = "" } } }, admin);

        var users = new List<(string Token, Guid Project)>();
        foreach (var name in new[] { "u1", "u2", "u3" })
        {
            var (_, token) = await server.NewUserAsync(admin, $"{name}@example.com");
            users.Add((token, (await server.Post("/api/projects", new { name }, token)).Body.GetProperty("id").GetGuid()));
        }
        async Task<int> Chat(int user, string title = "c") => (await server.Post($"/api/projects/{users[user].Project}/chats", new { title }, users[user].Token)).Body.GetProperty("id").GetInt32();
        async Task<(int Status, JsonElement Body)> Send(int user, int chat, string text = "Bonjour") =>
            await server.Post($"/api/projects/{users[user].Project}/chats/{chat}/requests", new { text, providerId = provider, model = "m" }, users[user].Token);
        async Task<string> State(int user, Guid run) => (await server.Get($"/api/runs/{run}", users[user].Token)).Body.GetProperty("state").GetString()!;
        async Task WaitUntil(Func<Task<bool>> condition, int ms = 10000)
        {
            for (var waited = 0; waited < ms && !await condition(); waited += 50) await Task.Delay(50);
        }

        await server.SetAsync(admin, "queue.maxConcurrentUsers", 2);
        await server.SetAsync(admin, "queue.maxConcurrentRuns", 3);
        await server.SetAsync(admin, "queue.maxRunsPerUser", 1);
        await server.SetAsync(admin, "queue.maxQueuedPerUser", 2);
        var c1 = await Chat(0);
        var c1b = await Chat(0, "second");
        var c2 = await Chat(1);
        var c3 = await Chat(2);

        var r1 = (await Send(0, c1)).Body.GetProperty("id").GetGuid();
        var r2 = (await Send(1, c2)).Body.GetProperty("id").GetGuid();
        var r3Response = await Send(2, c3);
        var r3 = r3Response.Body.GetProperty("id").GetGuid();
        check(r3Response.Status == 202, "La requête est acceptée et mise en file");
        await WaitUntil(async () => await State(0, r1) == "running" && await State(1, r2) == "running");
        check(await State(0, r1) == "running" && await State(1, r2) == "running", "Deux utilisateurs s’exécutent en même temps (limite d’utilisateurs)");
        var waiting = (await server.Get($"/api/runs/{r3}", users[2].Token)).Body;
        check(waiting.GetProperty("state").GetString() == "queued" && waiting.GetProperty("queuePosition").GetInt32() == 1, "Le troisième utilisateur attend en position 1");
        check(Code(await Send(0, c1)) == "conversation_busy", "Une conversation n’exécute qu’une requête à la fois");

        var r1b = (await Send(0, c1b)).Body.GetProperty("id").GetGuid();
        await Task.Delay(600);
        check(await State(0, r1b) == "queued", "Limite par utilisateur: sa seconde requête attend malgré la capacité libre");
        var thirdQueued = await Send(0, await Chat(0, "third"));
        var fourth = await Send(0, await Chat(0, "fourth"));
        check(thirdQueued.Status == 202 && fourth.Status == 429 && Code(fourth) == "too_many_queued", "Limite de requêtes en attente par utilisateur");

        // Fairness: u1's 2nd request is older than u3's, but u3 has nothing running.
        fake.Release(r1);
        await WaitUntil(async () => await State(0, r1) == "completed");
        await WaitUntil(async () => await State(2, r3) == "running");
        check(await State(0, r1) == "completed" && await State(2, r3) == "running" && await State(0, r1b) == "queued", "Quand une place se libère, l’utilisateur sans exécution passe avant");

        // Cancel
        var cancelQueued = await server.Post($"/api/runs/{r1b}/cancel", null, users[0].Token);
        check(cancelQueued.Body.GetProperty("state").GetString() == "cancelled", "Annuler une requête en attente");
        check((await server.Post($"/api/runs/{r2}/cancel", null, users[2].Token)).Status == 404, "Impossible d’annuler la requête d’un autre");
        var cancelRunning = await server.Post($"/api/runs/{r2}/cancel", null, users[1].Token);
        await WaitUntil(async () => await State(1, r2) == "cancelled");
        check(await State(1, r2) == "cancelled" && cancelRunning.Status == 200, "Annuler une exécution en cours");

        // Views
        check(Code(await server.Get("/api/queue", users[0].Token)) == "missing_permission", "La file complète exige queue.manage");
        var overview = (await server.Get("/api/queue", admin)).Body;
        check(overview.GetProperty("limitsEnabled").GetBoolean() && overview.GetProperty("items").GetArrayLength() >= 1, "Vue d’ensemble de la file pour l’administrateur");
        check((await server.Get("/api/runs?scope=all", users[0].Token)).Status == 403 && (await server.Get("/api/runs", users[0].Token)).Body.EnumerateArray().All(x => x.GetProperty("requester").GetString() == "u1@example.com"), "Chacun ne liste que ses requêtes");
        check((await server.Get($"/api/runs/{r3}", users[0].Token)).Status == 404, "…et ne lit pas celles des autres");

        // ---- Queue switched off: no limits ----
        await server.Put("/api/admin/features/queue", new { enabled = false }, admin);
        var burst = new List<Guid>();
        for (var i = 0; i < 3; i++) burst.Add((await Send(1, await Chat(1, "b" + i))).Body.GetProperty("id").GetGuid());
        await WaitUntil(async () => (await Task.WhenAll(burst.Select(x => State(1, x)))).All(x => x == "running"));
        check((await Task.WhenAll(burst.Select(x => State(1, x)))).All(x => x == "running"), "File désactivée: aucune limite de simultanéité");
        await server.Put("/api/admin/features/queue", new { enabled = true }, admin);

        // ---- Cloud disabled: queued requests are cancelled, running ones finish ----
        var runningNow = burst[0];
        var late = (await Send(0, await Chat(0, "late"))).Body.GetProperty("id").GetGuid();
        var spare = await Chat(0, "spare");
        await server.Put("/api/admin/features/cloud", new { enabled = false }, admin);
        await WaitUntil(async () => await State(0, late) == "cancelled");
        check(await State(0, late) == "cancelled" && await State(1, runningNow) == "running", "Désactiver le cloud annule la file mais laisse finir ce qui tourne");
        check(Code(await Send(0, spare)) == "feature_disabled", "Plus de nouvelle requête tant que le cloud est désactivé");
        await server.Put("/api/admin/features/cloud", new { enabled = true }, admin);
        foreach (var id in fake.Gates.Keys.ToList()) fake.Release(id);
        await Task.Delay(500);

        // ---- Restart after a crash: running requests are marked failed ----
        Guid crashed;
        await using (var db = await server.Service<Microsoft.EntityFrameworkCore.IDbContextFactory<ServerDb>>().CreateDbContextAsync())
        {
            var owner = (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Users, x => x.Email == "u3@example.com")).Id;
            var run = new RunRequest { Sequence = 1_000_000, ProjectId = users[2].Project, ChatId = c3, RequesterId = owner, ProviderId = provider, Model = "m", Prompt = "x", State = RunStates.Running, StartedUtc = DateTime.UtcNow };
            db.Runs.Add(run);
            await db.SaveChangesAsync();
            crashed = run.Id;
        }
        await server.RestartAsync();
        users[2] = (await server.LoginAsync("u3@example.com", "password-1234"), users[2].Project);
        var after = (await server.Get($"/api/runs/{crashed}", users[2].Token)).Body;
        check(after.GetProperty("state").GetString() == "failed" && after.GetProperty("error").GetString()!.Contains("restart"), "Après un redémarrage, une exécution interrompue est marquée échouée");
    }
}
