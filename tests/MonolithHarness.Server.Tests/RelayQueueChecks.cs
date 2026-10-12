using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MonolithHarness.Server.Services;

/// <summary>Calls of local conversations through the provider relay take part in the queue like cloud requests.</summary>
static class RelayQueueChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var upstream = await FakeProvider.StartAsync();
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        var provider = (await server.Post("/api/admin/providers", new { name = "Relay", baseUrl = upstream.BaseUrl, secret = "sk-relay", allowLocalUse = true, models = new[] { new { id = "fake-model" } } }, admin)).Body.GetProperty("id").GetGuid();
        var userRole = await server.RoleId("User", admin);
        await server.Put($"/api/admin/providers/{provider}/access", new { entries = new[] { new { kind = "role", id = userRole, name = "" } } }, admin);
        var (_, alice) = await server.NewUserAsync(admin, "relay-a@example.com");
        var (_, bob) = await server.NewUserAsync(admin, "relay-b@example.com");
        upstream.Handler = _ => new FakeProvider.Reply("OK", null, null, 200, 1200);

        // One call of a local conversation; returns the status and how long it lasted.
        async Task<(int Status, string Code, double Seconds)> Call(string token)
        {
            var clock = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/providers/{provider}/relay/chat/completions")
            { Content = new StringContent("""{"model":"fake-model","stream":true,"messages":[{"role":"user","content":"hi"}]}""", Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new("Bearer", token);
            try
            {
                using var response = await server.Http.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                string code = "";
                if (!response.IsSuccessStatusCode) try { code = JsonDocument.Parse(text).RootElement.GetProperty("code").GetString() ?? ""; } catch (JsonException) { }
                return ((int)response.StatusCode, code, clock.Elapsed.TotalSeconds);
            }
            catch (HttpRequestException) { return (0, "aborted", clock.Elapsed.TotalSeconds); }
        }
        async Task<JsonElement> Overview(Func<JsonElement, bool>? until = null)
        {
            JsonElement body = default;
            for (var waited = 0; waited < 5000; waited += 50)
            {
                body = (await server.Get("/api/queue", admin)).Body;
                if (until == null || until(body)) return body;
                await Task.Delay(50);
            }
            return body;
        }
        int Count(JsonElement overview, string state) => overview.GetProperty("items").EnumerateArray().Count(x => x.GetProperty("state").GetString() == state && x.GetProperty("source").GetString() == "relay");

        // ---- The queue limits apply to relayed calls ----
        await server.SetAsync(admin, "queue.maxConcurrentUsers", 4);
        await server.SetAsync(admin, "queue.maxConcurrentRuns", 1);
        await server.SetAsync(admin, "queue.maxRunsPerUser", 1);
        var first = Call(alice);
        var running = await Overview(x => Count(x, "running") == 1);
        var second = Call(bob);
        var waiting = await Overview(x => Count(x, "queued") == 1);
        check(Count(running, "running") == 1 && Count(waiting, "running") == 1 && Count(waiting, "queued") == 1, "Les appels des conversations locales apparaissent dans la file (un en cours, un en attente)");
        var item = waiting.GetProperty("items").EnumerateArray().First(x => x.GetProperty("state").GetString() == "queued");
        check(item.GetProperty("requester").GetString() == "relay-b@example.com" && item.GetProperty("queuePosition").GetInt32() == 1 && item.GetProperty("prompt").GetString()!.Contains("Relay"),
            "L’appel en attente indique l’utilisateur, son rang et le provider");
        var results = await Task.WhenAll(first, second);
        check(results.All(x => x.Status == 200), "Les deux appels aboutissent");
        check(results[1].Seconds > results[0].Seconds + 0.8, "La capacité d’une exécution simultanée fait attendre le second appel");
        var idle = await Overview(x => Count(x, "running") == 0 && Count(x, "queued") == 0);
        check(Count(idle, "running") == 0 && Count(idle, "queued") == 0, "La file se vide quand les appels sont terminés");

        // ---- Per-user limit ----
        await server.SetAsync(admin, "queue.maxConcurrentRuns", 4);
        var sameUser = await Task.WhenAll(Call(alice), Call(alice));
        check(sameUser.All(x => x.Status == 200) && Math.Max(sameUser[0].Seconds, sameUser[1].Seconds) > 2.2, "Une exécution simultanée par utilisateur: ses appels s’enchaînent");
        var differentUsers = await Task.WhenAll(Call(alice), Call(bob));
        check(differentUsers.All(x => x.Status == 200) && differentUsers.Max(x => x.Seconds) < 2.2, "Deux utilisateurs différents passent en parallèle quand la capacité le permet");

        // ---- Queue limits ----
        await server.SetAsync(admin, "queue.maxQueuedPerUser", 1);
        // One call at a time, so that each is in its place (running, waiting, refused) before the next one arrives.
        var burst = new List<Task<(int Status, string Code, double Seconds)>> { Call(alice) };
        await Overview(x => Count(x, "running") == 1);
        burst.Add(Call(alice));
        await Overview(x => Count(x, "queued") == 1);
        burst.Add(Call(alice));
        var burstResults = await Task.WhenAll(burst);
        check(burstResults.Count(x => x.Status == 200) == 2 && burstResults.Count(x => x.Status == 429 && x.Code == "too_many_queued") == 1, "Trop d’appels en attente pour un utilisateur: refus 429 immédiat");
        await server.SetAsync(admin, "queue.maxQueuedPerUser", 10);

        // ---- Cancelling from the queue ----
        var holder = Call(alice);
        await Overview(x => Count(x, "running") == 1);
        var queuedCall = Call(alice);
        var withQueued = await Overview(x => Count(x, "queued") == 1);
        var queuedId = withQueued.GetProperty("items").EnumerateArray().First(x => x.GetProperty("state").GetString() == "queued").GetProperty("id").GetGuid();
        check((await server.Post($"/api/runs/{queuedId}/cancel", null, admin)).Status == 200, "L’administrateur annule un appel en attente");
        var cancelled = await queuedCall;
        check(cancelled.Status == 409 && cancelled.Code == "cancelled", "L’appel annulé reçoit une erreur claire");
        var runningId = (await Overview(x => Count(x, "running") == 1)).GetProperty("items").EnumerateArray().First(x => x.GetProperty("state").GetString() == "running").GetProperty("id").GetGuid();
        await server.Post($"/api/runs/{runningId}/cancel", null, admin);
        var stopped = await holder;
        check(stopped.Seconds < 1.1 && stopped.Status != 200, "L’annulation d’un appel en cours l’interrompt sans attendre la fin du provider");
        var afterCancel = await Overview(x => Count(x, "running") == 0 && Count(x, "queued") == 0);
        check(Count(afterCancel, "running") == 0 && Count(afterCancel, "queued") == 0, "Les annulations libèrent la place dans la file");

        // ---- Waiting too long ----
        await server.SetAsync(admin, "queue.maxConcurrentUsers", 4);
        await server.SetAsync(admin, "queue.maxConcurrentRuns", 1);
        await server.SetAsync(admin, "queue.maxRunsPerUser", 4);
        await server.SetAsync(admin, "queue.relayWaitSeconds", 5);
        upstream.Handler = _ => new FakeProvider.Reply("OK", null, null, 200, 6500);
        var slow = Call(alice);
        await Overview(x => Count(x, "running") == 1);
        var impatient = await Call(bob);
        check(impatient.Status == 503 && impatient.Code == "queue_timeout" && impatient.Seconds is > 4.5 and < 8, "Un appel qui attend trop longtemps reçoit queue_timeout");
        check((await slow).Status == 200, "L’appel en cours n’est pas perturbé par l’abandon d’un autre");
        await server.SetAsync(admin, "queue.relayWaitSeconds", 300);

        // ---- Queue feature off: no limit, but the calls still show ----
        upstream.Handler = _ => new FakeProvider.Reply("OK", null, null, 200, 1200);
        await server.Put("/api/admin/features/queue", new { enabled = false }, admin);
        var parallel = new[] { Call(alice), Call(alice), Call(bob) };
        var open = await Overview(x => Count(x, "running") == 3);
        check(Count(open, "running") == 3 && !open.GetProperty("limitsEnabled").GetBoolean(), "File désactivée: les appels sont suivis mais jamais retenus");
        var parallelResults = await Task.WhenAll(parallel);
        check(parallelResults.All(x => x.Status == 200) && parallelResults.Max(x => x.Seconds) < 2.2, "File désactivée: tous les appels s’exécutent en parallèle");
        await server.Put("/api/admin/features/queue", new { enabled = true }, admin);

        check(upstream.Requests.Count >= 12 && upstream.Requests.All(x => x.Authorization == "Bearer sk-relay"), "Le provider amont reçoit les appels relayés avec la clé du serveur, jamais celle du client");
    }
}
