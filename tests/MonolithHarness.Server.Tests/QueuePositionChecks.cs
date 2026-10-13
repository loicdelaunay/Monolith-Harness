using System.Text;
using System.Text.Json;

/// <summary>A request waiting in the queue is told its rank, and told again each time the queue moves (live rank in the clients).</summary>
static class QueuePositionChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var upstream = await FakeProvider.StartAsync();
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        var provider = (await server.Post("/api/admin/providers", new { name = "Rank", baseUrl = upstream.BaseUrl, secret = "sk-rank", allowLocalUse = true, models = new[] { new { id = "fake-model" } } }, admin)).Body.GetProperty("id").GetGuid();
        var userRole = await server.RoleId("User", admin);
        await server.Put($"/api/admin/providers/{provider}/access", new { entries = new[] { new { kind = "role", id = userRole, name = "" } } }, admin);
        var (_, alice) = await server.NewUserAsync(admin, "rank-a@example.com");
        var (_, bob) = await server.NewUserAsync(admin, "rank-b@example.com");
        var (carolId, carol) = await server.NewUserAsync(admin, "rank-c@example.com");
        upstream.Handler = _ => new FakeProvider.Reply("OK", null, null, 200, 1500);
        await server.SetAsync(admin, "queue.maxConcurrentUsers", 4);
        await server.SetAsync(admin, "queue.maxConcurrentRuns", 1);
        await server.SetAsync(admin, "queue.maxRunsPerUser", 1);

        async Task<int> Call(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/providers/{provider}/relay/chat/completions")
            { Content = new StringContent("""{"model":"fake-model","stream":true,"messages":[{"role":"user","content":"hi"}]}""", Encoding.UTF8, "application/json") };
            request.Headers.Authorization = new("Bearer", token);
            using var response = await server.Http.SendAsync(request);
            await response.Content.ReadAsStringAsync();
            return (int)response.StatusCode;
        }
        async Task WaitFor(Func<JsonElement, bool> until)
        {
            for (var waited = 0; waited < 5000; waited += 50)
            {
                if (until((await server.Get("/api/queue", admin)).Body)) return;
                await Task.Delay(50);
            }
        }
        int Count(JsonElement overview, string state) => overview.GetProperty("items").EnumerateArray().Count(x => x.GetProperty("state").GetString() == state);

        await using var socket = await server.ConnectAsync(carol);
        var first = Call(alice);
        await WaitFor(x => Count(x, "running") == 1);
        var second = Call(bob);
        await WaitFor(x => Count(x, "queued") == 1);
        var third = Call(carol);
        await WaitFor(x => Count(x, "queued") == 2);

        bool Mine(JsonElement frame, string state, int? position) =>
            WsClient.Str(frame, "type") == "event" && WsClient.Str(frame, "topic") == "user:" + carolId && WsClient.Str(frame, "name") == "run.state"
            && frame.GetProperty("data").GetProperty("source").GetString() == "relay" && frame.GetProperty("data").GetProperty("state").GetString() == state
            && (position == null || (frame.GetProperty("data").GetProperty("queuePosition") is { ValueKind: JsonValueKind.Number } value && value.GetInt32() == position));

        var behind = await socket.WaitAsync(x => Mine(x, "queued", 2));
        check(behind != null, "Un appel en attente derrière un autre est informé de son rang (2)");
        check(behind is { } info && info.GetProperty("data").GetProperty("providerId").GetGuid() == provider && info.GetProperty("data").GetProperty("model").GetString() == "fake-model",
            "L’événement indique le provider et le modèle attendus (pour rattacher le rang à la bonne conversation)");
        var moved = await socket.WaitAsync(x => Mine(x, "queued", 1), 12000);
        check(moved != null, "Quand la file avance, le rang est renvoyé tout seul (1) sans que le client interroge le serveur");
        var started = await socket.WaitAsync(x => Mine(x, "running", null), 12000);
        check(started != null, "Puis l’appel passe en cours");
        var statuses = await Task.WhenAll(first, second, third);
        check(statuses.All(x => x == 200), "Les trois appels aboutissent dans l’ordre");

        var ranks = socket.Seen.Where(x => Mine(x, "queued", null)).Select(x => x.GetProperty("data").GetProperty("queuePosition").GetInt32()).ToList();
        check(ranks.SequenceEqual(ranks.Distinct()) && ranks.SequenceEqual(ranks.OrderByDescending(x => x)), "Chaque rang n’est annoncé qu’une fois, et ne fait que diminuer");
    }
}
