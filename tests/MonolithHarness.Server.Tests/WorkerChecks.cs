using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;

/// <summary>End to end: a real worker process runs the shared engine against a scripted provider, through the server relay.</summary>
static class WorkerChecks
{
    const string UpstreamSecret = "sk-upstream-secret";

    public static async Task Run(Action<bool, string> check)
    {
        await using var upstream = await FakeProvider.StartAsync();
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        string Code((int Status, JsonElement Body) r) => r.Body.ValueKind == JsonValueKind.Object && r.Body.TryGetProperty("code", out var x) ? x.GetString()! : "";
        await server.EnableAsync(admin, "cloud");

        var provider = (await server.Post("/api/admin/providers", new { name = "Fake", baseUrl = upstream.BaseUrl, secret = UpstreamSecret, models = new[] { new { id = "fake-model", contextLimit = 32000 } } }, admin)).Body.GetProperty("id").GetGuid();
        await server.Put($"/api/admin/providers/{provider}/access", new { entries = new[] { new { kind = "role", id = await server.RoleId("User", admin), name = "" } } }, admin);
        var (zoeId, zoe) = await server.NewUserAsync(admin, "zoe@example.com");
        var (_, yan) = await server.NewUserAsync(admin, "yan@example.com");
        var project = (await server.Post("/api/projects", new { name = "Atelier cloud" }, zoe)).Body.GetProperty("id").GetGuid();

        async Task<int> Chat(string title) => (await server.Post($"/api/projects/{project}/chats", new { title }, zoe)).Body.GetProperty("id").GetInt32();
        async Task<JsonElement> GetRun(Guid id) => (await server.Get($"/api/runs/{id}", zoe)).Body;
        async Task<JsonElement> WaitFinal(Guid id, int seconds = 150)
        {
            JsonElement run = default;
            for (var i = 0; i < seconds * 4; i++)
            {
                run = await GetRun(id);
                if (run.GetProperty("state").GetString() is "completed" or "failed" or "cancelled") return run;
                await Task.Delay(250);
            }
            return run;
        }
        static bool HasToolResult(JsonObject body) => body["messages"] is JsonArray messages && messages.Any(m => m?["role"]?.GetValue<string>() == "tool");

        // ---- Worker environment ----
        Environment.SetEnvironmentVariable("MONOLITH_SERVER_CERT_PASSWORD", "leak-me");
        Environment.SetEnvironmentVariable("SERVER_API_SECRET", "leak-me-too");
        var env = MonolithHarness.Server.Workers.WorkerProcess.EnvironmentFor(server.Paths, @"C:\work	mp", @"C:\work\home");
        check(!env.ContainsKey("MONOLITH_SERVER_CERT_PASSWORD") && !env.ContainsKey("SERVER_API_SECRET") && env.Values.All(x => !x.Contains("leak-me")), "L’environnement d’un worker ne contient aucun secret du serveur");
        check(env["TEMP"] == @"C:\work	mp" && env["HOME"] == @"C:\work\home" && env.ContainsKey("PATH"), "…ses dossiers temporaire et personnel sont dans le projet");

        // ---- Fail closed: no container runtime, no code execution ----
        var containerOk = false;
        try { await SandboxContainer.CheckAsync(CancellationToken.None); containerOk = true; } catch (Exception) { }
        if (!containerOk)
        {
            var first = await Chat("fermé");
            var blocked = (await server.Post($"/api/projects/{project}/chats/{first}/requests", new { text = "Bonjour", providerId = provider, model = "fake-model" }, zoe)).Body.GetProperty("id").GetGuid();
            var blockedRun = await WaitFinal(blocked, 60);
            check(blockedRun.GetProperty("state").GetString() == "failed" && blockedRun.GetProperty("error").GetString()!.Contains("Container isolation is required") && upstream.Requests.IsEmpty,
                "Sans runtime de conteneur, l’exécution est refusée avant tout appel (fail closed)");
        }
        else check(true, "Runtime de conteneur détecté: le test « fail closed » est ignoré");
        await server.SetAsync(admin, "execution.requireContainerSandbox", false);

        // ---- A full run: tool approval, file written in the project workspace, streamed events ----
        upstream.Handler = body => HasToolResult(body) ? FakeProvider.Reply.Text("Fichier créé.") : FakeProvider.Reply.Tool("patch_sources", new { dry_run = false, edits = new[] { new { path = "hello.txt", old_text = (string?)null, new_text = "Bonjour depuis le cloud" } } });
        var chatId = await Chat("Écriture");
        await using var ws = await server.ConnectAsync(zoe);
        await ws.SendAsync(new { type = "subscribe", topic = $"project:{project}" });
        await ws.WaitTypeAsync("subscribed");
        var accepted = await server.Post($"/api/projects/{project}/chats/{chatId}/requests", new { text = "Crée hello.txt", providerId = provider, model = "fake-model" }, zoe);
        check(accepted.Status == 202, "La requête cloud est acceptée");
        var runId = accepted.Body.GetProperty("id").GetGuid();
        await ws.SendAsync(new { type = "subscribe", topic = $"run:{runId}" });
        check(await ws.WaitTypeAsync("subscribed") != null, "Suivi de la requête par WebSocket");

        var approvalEvent = await ws.WaitEventAsync($"user:{zoeId}", "approval.requested", 120000);
        check(approvalEvent != null, "La demande d’approbation de l’outil est transmise à l’utilisateur concerné");
        if (approvalEvent == null)
        {
            var failed = await GetRun(runId);
            Console.WriteLine("  état: " + failed.GetProperty("state").GetString() + " — " + failed.GetProperty("error").GetString());
            return;
        }
        var approval = approvalEvent.Value.GetProperty("data");
        var approvalId = approval.GetProperty("id").GetGuid();
        check(approval.GetProperty("kind").GetString() == "permission" && approval.GetProperty("runId").GetGuid() == runId, "…avec le contexte de la requête");
        check((await GetRun(runId)).GetProperty("state").GetString() == "awaiting_approval", "La requête est « en attente d’approbation »");
        check((await server.Get("/api/approvals", yan)).Body.GetArrayLength() == 0 && (await server.Post($"/api/approvals/{approvalId}/decision", new { choice = "allow" }, yan)).Status == 404, "Un autre utilisateur ne voit ni ne décide cette approbation");
        check((await server.Post($"/api/approvals/{approvalId}/decision", new { choice = "allow" }, admin)).Status == 404, "…l’administrateur non plus");
        check((await server.Get("/api/approvals", zoe)).Body.GetArrayLength() == 1, "L’utilisateur retrouve l’approbation en attente");
        check(Code(await server.Post($"/api/approvals/{approvalId}/decision", new { choice = "maybe" }, zoe)) == "invalid_choice", "Choix invalide refusé");
        check((await server.Post($"/api/approvals/{approvalId}/decision", new { choice = "allow" }, zoe)).Status == 200, "L’utilisateur autorise l’action");
        check(Code(await server.Post($"/api/approvals/{approvalId}/decision", new { choice = "allow" }, zoe)) == "approval_closed", "Une approbation ne se décide qu’une fois");

        var final = await WaitFinal(runId);
        if (final.GetProperty("state").GetString() != "completed") Console.WriteLine("  état: " + final.GetProperty("state").GetString() + " — " + final.GetProperty("error").GetString());
        check(final.GetProperty("state").GetString() == "completed", "La requête se termine (completed)");
        var written = Path.Combine(server.Paths.ProjectFiles(project), "hello.txt");
        check(File.Exists(written) && File.ReadAllText(written).TrimEnd() == "Bonjour depuis le cloud", "L’outil a écrit le fichier dans le workspace du projet, sur le serveur");
        var messages = (await server.Get($"/api/projects/{project}/chats/{chatId}/messages", zoe)).Body.EnumerateArray().ToList();
        check(messages.Any(x => x.GetProperty("role").GetString() == "user" && x.GetProperty("content").GetString() == "Crée hello.txt")
            && messages.Any(x => x.GetProperty("role").GetString() == "assistant" && x.GetProperty("content").GetString() == "Fichier créé."), "La conversation reste sur le serveur (messages utilisateur et assistant)");
        check(await ws.WaitEventAsync($"run:{runId}", "stream", 3000) != null && await ws.WaitEventAsync($"run:{runId}", "done", 3000) != null, "Les événements du moteur sont diffusés en temps réel (stream, done)");
        var states = ws.Seen.Where(x => WsClient.Str(x, "name") == "run.state" && WsClient.Str(x, "topic") == $"project:{project}").Select(x => x.GetProperty("data").GetProperty("state").GetString()).Distinct().ToList();
        check(states.Contains("running") && states.Contains("awaiting_approval") && states.Contains("completed"), "États diffusés: en cours, en attente d’approbation, terminée");

        check(upstream.Requests.Count >= 2 && upstream.Requests.All(x => x.Authorization == "Bearer " + UpstreamSecret), "Les appels sortent avec la clé du serveur…");
        check(upstream.Requests.All(x => !x.Authorization.Contains("mhs_")), "…le worker n’a jamais vu cette clé (il n’a qu’un jeton de run)");
        var asked = upstream.Requests.First().Body!;
        check(asked["model"]!.GetValue<string>() == "fake-model", "Le modèle demandé est celui du catalogue");
        await using (var db = await server.Service<IDbContextFactory<ServerDb>>().CreateDbContextAsync())
        {
            var tokens = await db.Sessions.Where(x => x.RunId == runId).ToListAsync();
            check(tokens.Count == 1 && tokens[0].Kind == SessionKinds.Run && tokens[0].RevokedUtc != null, "Le jeton de run est révoqué à la fin de l’exécution");
        }
        check(!Directory.EnumerateFiles(server.Paths.Jobs).Any(), "Le fichier de job (qui contenait le jeton) est supprimé");
        using (var engine = new HarnessDb(server.Paths.ProjectDatabase(project)))
        {
            var providers = await engine.Providers.AsNoTracking().ToListAsync();
            check(providers.Count == 1 && providers[0].BaseUrl.Contains($"/api/providers/{provider}/relay") && providers[0].BaseUrl.StartsWith("http://127.0.0.1:"), "Dans le projet, le moteur ne connaît que le relais du serveur");
            check(!(await engine.Projects.AsNoTracking().FirstAsync()).GetSourceFolders().Except([server.Paths.ProjectFiles(project)]).Any(), "…et un seul dossier source: le workspace du projet");
        }

        // ---- Denial ----
        upstream.Handler = body => HasToolResult(body) ? FakeProvider.Reply.Text("Compris, je n’écris pas.") : FakeProvider.Reply.Tool("patch_sources", new { dry_run = false, edits = new[] { new { path = "denied.txt", old_text = (string?)null, new_text = "interdit" } } });
        var denyChat = await Chat("Refus");
        var denyRun = (await server.Post($"/api/projects/{project}/chats/{denyChat}/requests", new { text = "Crée denied.txt", providerId = provider, model = "fake-model" }, zoe)).Body.GetProperty("id").GetGuid();
        var denyApproval = await ws.WaitAsync(x => WsClient.Str(x, "name") == "approval.requested" && x.GetProperty("data").GetProperty("runId").GetGuid() == denyRun, 120000);
        await server.Post($"/api/approvals/{denyApproval!.Value.GetProperty("data").GetProperty("id").GetGuid()}/decision", new { choice = "deny" }, zoe);
        var denied = await WaitFinal(denyRun);
        check(denied.GetProperty("state").GetString() == "completed" && !File.Exists(Path.Combine(server.Paths.ProjectFiles(project), "denied.txt")), "Un refus empêche l’action; la requête continue et se termine");

        // ---- Agent question ----
        upstream.Handler = body => HasToolResult(body) ? FakeProvider.Reply.Text("Réponse reçue.")
            : FakeProvider.Reply.Tool("question", new { questions = new[] { new { question = "Quelle couleur ?", options = new[] { new { label = "Rouge", description = "r" }, new { label = "Bleu", description = "b" } }, multiple = false, custom = false } } });
        var askChat = await Chat("Question");
        var askRun = (await server.Post($"/api/projects/{project}/chats/{askChat}/requests", new { text = "Demande-moi une couleur", providerId = provider, model = "fake-model" }, zoe)).Body.GetProperty("id").GetGuid();
        var asked2 = await ws.WaitAsync(x => WsClient.Str(x, "name") == "approval.requested" && x.GetProperty("data").GetProperty("runId").GetGuid() == askRun, 120000);
        var question = asked2!.Value.GetProperty("data");
        check(question.GetProperty("kind").GetString() == "question" && question.GetProperty("questions")[0].GetProperty("question").GetString() == "Quelle couleur ?", "Une question de l’agent est transmise à l’utilisateur");
        var questionId = question.GetProperty("id").GetGuid();
        check(Code(await server.Post($"/api/approvals/{questionId}/decision", new { choice = "allow" }, zoe)) == "wrong_kind", "Une question ne se « décide » pas: on y répond");
        check((await server.Post($"/api/approvals/{questionId}/answer", new { cancelled = false, answers = new[] { new[] { "Bleu" } } }, zoe)).Status == 200, "L’utilisateur répond");
        var answered = await WaitFinal(askRun);
        var lastTool = upstream.Requests.Last().Body!["messages"]!.AsArray().Last(m => m?["role"]?.GetValue<string>() == "tool")!["content"]!.GetValue<string>();
        check(answered.GetProperty("state").GetString() == "completed" && lastTool.Contains("Bleu"), "La réponse parvient à l’agent, qui poursuit");

        // ---- Cancellation while waiting for the model ----
        upstream.Handler = _ => new FakeProvider.Reply("trop tard", DelayMs: 60_000);
        var slowChat = await Chat("Lent");
        var requestsBefore = upstream.Requests.Count;
        var slowRun = (await server.Post($"/api/projects/{project}/chats/{slowChat}/requests", new { text = "Réfléchis longtemps", providerId = provider, model = "fake-model" }, zoe)).Body.GetProperty("id").GetGuid();
        for (var i = 0; i < 480 && (await GetRun(slowRun)).GetProperty("state").GetString() != "running"; i++) await Task.Delay(250);
        for (var i = 0; i < 480 && upstream.Requests.Count <= requestsBefore; i++) await Task.Delay(250);
        var cancelAt = DateTime.UtcNow;
        await server.Post($"/api/runs/{slowRun}/cancel", null, zoe);
        var cancelled = await WaitFinal(slowRun, 60);
        if (cancelled.GetProperty("state").GetString() != "cancelled") Console.WriteLine("  état: " + cancelled.GetProperty("state").GetString() + " — " + cancelled.GetProperty("error").GetString() + $" ({(DateTime.UtcNow - cancelAt).TotalSeconds:0.0} s)");
        check(cancelled.GetProperty("state").GetString() == "cancelled" && DateTime.UtcNow - cancelAt < TimeSpan.FromSeconds(40), "Annuler une requête arrête le worker rapidement");
        upstream.Handler = _ => FakeProvider.Reply.Text("OK");
    }
}
