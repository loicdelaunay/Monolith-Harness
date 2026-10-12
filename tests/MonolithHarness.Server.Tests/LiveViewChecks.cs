using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>Who wrote a message, renamed conversations, what viewers see and how many of them there are.</summary>
static class LiveViewChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var upstream = await FakeProvider.StartAsync();
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        await server.EnableAsync(admin, "cloud");
        await server.SetAsync(admin, "execution.requireContainerSandbox", false);
        var userRole = await server.RoleId("User", admin);
        var provider = (await server.Post("/api/admin/providers", new { name = "Fake", baseUrl = upstream.BaseUrl, secret = "sk", allowLocalUse = true, models = new[] { new { id = "fake-model" } } }, admin)).Body.GetProperty("id").GetGuid();
        await server.Put($"/api/admin/providers/{provider}/access", new { entries = new[] { new { kind = "role", id = userRole, name = "" } } }, admin);
        var (_, olga) = await server.NewUserAsync(admin, "olga@example.com");
        var (_, cora) = await server.NewUserAsync(admin, "cora@example.com");
        var (_, rene) = await server.NewUserAsync(admin, "rene@example.com");
        string ErrorCode((int Status, JsonElement Body) r) => r.Body.ValueKind == JsonValueKind.Object && r.Body.TryGetProperty("code", out var x) ? x.GetString() ?? "" : "";
        static bool Area(JsonElement frame, string area) => WsClient.Str(frame, "type") == "event" && WsClient.Str(frame, "name") == "sync"
            && frame.TryGetProperty("data", out var data) && data.TryGetProperty("area", out var a) && a.GetString() == area;
        async Task<bool> WaitRun(Guid project, int chat, int messages, int ms = 60000)
        {
            for (var waited = 0; waited < ms; waited += 250)
            {
                // Tools ask for permission: whoever started the run allows them.
                foreach (var token in new[] { olga, cora })
                    foreach (var pending in (await server.Get("/api/approvals?pending=true", token)).Body.EnumerateArray())
                        await server.Post($"/api/approvals/{pending.GetProperty("id").GetGuid()}/decision", new { choice = "allow" }, token);
                var all = (await server.Get($"/api/projects/{project}/chats/{chat}/messages", olga)).Body;
                var runs = (await server.Get("/api/runs?scope=mine", olga)).Body.EnumerateArray().Concat((await server.Get("/api/runs?scope=mine", cora)).Body.EnumerateArray());
                if (all.GetArrayLength() >= messages && runs.All(x => RunDone(x.GetProperty("state").GetString()!))) return true;
                await Task.Delay(250);
            }
            return false;
        }
        static bool RunDone(string state) => state is "completed" or "failed" or "cancelled";

        upstream.Handler = body =>
        {
            var messages = body["messages"] as JsonArray ?? [];
            var lastUser = messages.ToList().FindLastIndex(m => m?["role"]?.GetValue<string>() == "user");
            var last = lastUser < 0 ? "" : messages[lastUser]?["content"]?.ToString() ?? "";
            var toolResult = messages.Skip(Math.Max(lastUser, 0)).Any(m => m?["role"]?.GetValue<string>() == "tool");
            if (!toolResult && last.Contains("patch")) return FakeProvider.Reply.Tool("patch_sources", new { dry_run = false, edits = new[] { new { path = "note.txt", old_text = (string?)null, new_text = "contenu" } } });
            return FakeProvider.Reply.Text("Fait.");
        };

        // ---- Who wrote what ----
        var project = (await server.Post("/api/projects", new { name = "Équipe" }, olga)).Body.GetProperty("id").GetGuid();
        await server.Put($"/api/projects/{project}/members", new { members = new[] { new { email = "cora@example.com", access = "run" }, new { email = "rene@example.com", access = "read" } } }, olga);
        var chat = (await server.Post($"/api/projects/{project}/chats", new { title = "Plan" }, olga)).Body.GetProperty("id").GetInt32();
        async Task Send(string token, string text) => await server.Post($"/api/projects/{project}/chats/{chat}/requests", new { text, providerId = provider, model = "fake-model" }, token);
        await Send(olga, "Premier message d'Olga");
        check(await WaitRun(project, chat, 2), "Olga envoie une requête dans le projet partagé");
        await Send(cora, "Un message de Cora avec patch");
        check(await WaitRun(project, chat, 5), "Cora répond dans la même conversation (avec un outil)");
        var messages = (await server.Get($"/api/projects/{project}/chats/{chat}/messages", rene)).Body.EnumerateArray().ToList();
        var users = messages.Where(x => x.GetProperty("role").GetString() == "user").ToList();
        check(users.Count == 2 && users[0].GetProperty("authorEmail").GetString() == "olga@example.com" && users[1].GetProperty("authorEmail").GetString() == "cora@example.com", "Chaque message utilisateur indique qui l’a envoyé (même pour un membre en lecture seule)");
        check(users[0].GetProperty("authorName").GetString() == "olga@example.com" && users[1].GetProperty("authorId").ValueKind == JsonValueKind.String, "L’auteur porte son nom et son identifiant");
        check(messages.Where(x => x.GetProperty("role").GetString() == "assistant").All(x => x.GetProperty("authorEmail").ValueKind == JsonValueKind.Null), "Les réponses du modèle n’ont pas d’auteur");
        var assistantWithTool = messages.FirstOrDefault(x => x.GetProperty("role").GetString() == "assistant" && x.TryGetProperty("wire", out var w) && w.ValueKind == JsonValueKind.String && w.GetString()!.Contains("tool_calls"));
        var toolResultMessage = messages.FirstOrDefault(x => x.GetProperty("role").GetString() == "tool");
        check(assistantWithTool.ValueKind == JsonValueKind.Object && assistantWithTool.GetProperty("wire").GetString()!.Contains("patch_sources") && !assistantWithTool.GetProperty("wire").GetString()!.Contains("reasoning"), "Les appels d’outils sont transmis (nom, arguments), sans le reste du détail interne");
        check(toolResultMessage.ValueKind == JsonValueKind.Object && toolResultMessage.GetProperty("wire").GetString()!.Contains("tool_call_id"), "Le résultat d’un outil est relié à son appel");

        // ---- Renaming a conversation ----
        await using var coraWs = await server.ConnectAsync(cora);
        await using var reneWs = await server.ConnectAsync(rene);
        var renamed = await server.SendAsync(HttpMethod.Patch, $"/api/projects/{project}/chats/{chat}", new { title = "Plan renommé par Olga" }, olga);
        check(renamed.Status == 204 && (await server.Get($"/api/projects/{project}/chats", cora)).Body.EnumerateArray().Any(x => x.GetProperty("title").GetString() == "Plan renommé par Olga"), "Un membre renomme la conversation: tous les membres voient le nouveau titre");
        check(await coraWs.WaitAsync(x => Area(x, "chats") && x.GetProperty("data").GetProperty("chatId").GetInt32() == chat) != null && await reneWs.WaitAsync(x => Area(x, "chats")) != null, "Les autres clients sont avertis du renommage (signal de synchronisation)");
        check(ErrorCode(await server.SendAsync(HttpMethod.Patch, $"/api/projects/{project}/chats/{chat}", new { title = "Interdit" }, rene)) == "project_access_denied", "Un membre en lecture seule ne renomme pas");
        check(ErrorCode(await server.SendAsync(HttpMethod.Patch, $"/api/projects/{project}/chats/{chat}", new { title = "   " }, olga)) == "invalid_title" && (await server.SendAsync(HttpMethod.Patch, $"/api/projects/{project}/chats/999", new { title = "x" }, olga)).Status == 404, "Un titre vide ou une conversation inconnue sont refusés");
        check((await server.SendAsync(HttpMethod.Patch, $"/api/projects/{project}/chats/{chat}", new { title = "Plan renommé par Olga" }, olga)).Status == 204, "Renommer avec le même titre est sans effet");

        // ---- Followers of a shared conversation: count, title ----
        var share = (await server.Post("/api/shares", new { title = "Ma session", clientConversationId = "1", isPublic = true }, olga)).Body.GetProperty("id").GetGuid();
        await using var host = await server.ConnectAsync(olga);
        await host.SendAsync(new { type = "bridge.attach", shareId = share });
        await host.WaitTypeAsync("bridge.attached");
        await host.SendAsync(new { type = "bridge.snapshot", shareId = share, data = new { title = "Ma session", messages = new[] { new { id = 1, role = "user", content = "Salut" } } } });
        var topic = $"share:{share}";
        static int Count(JsonElement frame) => frame.GetProperty("data").GetProperty("count").GetInt32();
        check(await host.WaitAsync(x => WsClient.Str(x, "name") == "viewers" && WsClient.Str(x, "topic") == topic && Count(x) == 0, 5000) != null, "L’hôte connaît le nombre de spectateurs dès l’attachement (aucun)");
        await using var first = await server.ConnectAsync(cora);
        await first.SendAsync(new { type = "subscribe", topic });
        check(await host.WaitAsync(x => WsClient.Str(x, "name") == "viewers" && Count(x) == 1) != null, "Un spectateur arrive: l’hôte voit 1 spectateur en direct");
        await using var firstAgain = await server.ConnectAsync(cora);
        await firstAgain.SendAsync(new { type = "subscribe", topic });
        await using var second = await server.ConnectAsync(rene);
        await second.SendAsync(new { type = "subscribe", topic });
        check(await host.WaitAsync(x => WsClient.Str(x, "name") == "viewers" && Count(x) == 2) != null && host.Seen.Count(x => WsClient.Str(x, "name") == "viewers" && Count(x) == 3) == 0, "Une même personne sur deux appareils ne compte qu’une fois");
        check(await first.WaitAsync(x => WsClient.Str(x, "name") == "viewers" && Count(x) == 2) != null, "Les spectateurs reçoivent aussi le nombre de personnes présentes");
        var hostView = (await server.Get($"/api/shares/{share}", olga)).Body;
        check(hostView.GetProperty("viewers").GetInt32() == 2, "La liste de l’hôte indique le nombre de spectateurs en direct");
        await using var hostSecond = await server.ConnectAsync(olga);
        await hostSecond.SendAsync(new { type = "subscribe", topic });
        await Task.Delay(300);
        check((await server.Get($"/api/shares/{share}", olga)).Body.GetProperty("viewers").GetInt32() == 2, "L’hôte lui-même, sur un autre appareil, n’est pas compté");
        await second.DisposeAsync();
        var dropped = false;
        for (var waited = 0; waited < 6000 && !dropped; waited += 100) { dropped = host.Seen.Count(x => WsClient.Str(x, "name") == "viewers" && Count(x) == 1) >= 2; if (!dropped) await Task.Delay(100); }
        check(dropped, "Un spectateur qui part: le compteur redescend");
        await using var late = await server.ConnectAsync(cora);
        await late.SendAsync(new { type = "subscribe", topic });
        check(await late.WaitAsync(x => WsClient.Str(x, "name") == "viewers") != null, "Un arrivant reçoit le nombre actuel avec l’état courant");

        await host.SendAsync(new { type = "bridge.event", shareId = share, name = "title", data = new { title = "Titre renommé sur l’appareil" } });
        check(await first.WaitAsync(x => WsClient.Str(x, "name") == "title" && x.GetProperty("data").GetProperty("title").GetString() == "Titre renommé sur l’appareil") != null, "Un renommage chez l’hôte est transmis aux spectateurs");
        var after = false;
        for (var waited = 0; waited < 4000 && !after; waited += 100) { after = (await server.Get($"/api/shares/{share}", cora)).Body.GetProperty("title").GetString() == "Titre renommé sur l’appareil"; if (!after) await Task.Delay(100); }
        check(after && (await server.Get("/api/shares/public", rene)).Body.EnumerateArray().Any(x => x.GetProperty("title").GetString() == "Titre renommé sur l’appareil"), "…et devient le titre du partage dans les listes");
        await host.SendAsync(new { type = "bridge.event", shareId = share, name = "viewers", data = new { count = 99 } });
        check(await host.WaitErrorAsync("invalid_event") != null, "Le nombre de spectateurs ne peut pas être imposé par un client");
        await host.SendAsync(new { type = "bridge.event", shareId = share, name = "message", data = new { id = 2, role = "assistant", content = "Réponse", reasoning = "Je réfléchis", toolCalls = new[] { new { id = "c1", name = "lire", arguments = "{}" } } } });
        check(await first.WaitAsync(x => WsClient.Str(x, "name") == "message" && x.GetProperty("data").TryGetProperty("reasoning", out var r) && r.GetString() == "Je réfléchis" && x.GetProperty("data").TryGetProperty("toolCalls", out var t) && t.GetArrayLength() == 1) != null, "Les messages portent la réflexion et les appels d’outils jusqu’aux spectateurs");
    }
}
