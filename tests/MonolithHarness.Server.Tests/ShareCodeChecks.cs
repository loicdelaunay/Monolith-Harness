using System.Text.RegularExpressions;
using System.Text.Json;
using MonolithHarness.Server.Security;

/// <summary>Sharing codes, public conversations, the account search used by pickers and the change signals sent to clients.</summary>
static class ShareCodeChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        string Code((int Status, JsonElement Body) r) => r.Body.ValueKind == JsonValueKind.Object && r.Body.TryGetProperty("code", out var x) && x.ValueKind == JsonValueKind.String ? x.GetString()! : "";
        string ErrorCode((int Status, JsonElement Body) r) => r.Body.ValueKind == JsonValueKind.Object && r.Body.TryGetProperty("code", out var x) ? x.GetString() ?? "" : "";
        Task<(int Status, JsonElement Body)> Patch(string path, object body, string token) => server.SendAsync(HttpMethod.Patch, path, body, token);
        static bool Area(JsonElement frame, string area) => WsClient.Str(frame, "type") == "event" && WsClient.Str(frame, "name") == "sync"
            && frame.TryGetProperty("data", out var data) && data.TryGetProperty("area", out var a) && a.GetString() == area;

        var (hostId, host) = await server.NewUserAsync(admin, "host@example.com");
        var (viewerId, viewer) = await server.NewUserAsync(admin, "viewer@example.com");
        var (_, outsider) = await server.NewUserAsync(admin, "outsider@example.com");
        var (_, guest) = await server.NewUserAsync(admin, "guest@example.com", "Guest");
        await using var hostWs = await server.ConnectAsync(host);
        await using var viewerWs = await server.ConnectAsync(viewer);
        await using var outsiderWs = await server.ConnectAsync(outsider);

        // ---- A code at creation ----
        var created = await server.Post("/api/shares", new { title = "Ma conversation", clientConversationId = "42" }, host);
        var share = created.Body.GetProperty("id").GetGuid();
        var code = created.Body.GetProperty("code").GetString()!;
        check(created.Status == 201 && Regex.IsMatch(code, "^[A-HJKMNP-Z2-9]{5}-[A-HJKMNP-Z2-9]{5}$"), "Un partage reçoit un code de 10 symboles sans caractères ambigus (XXXXX-XXXXX)");
        check(!created.Body.GetProperty("isPublic").GetBoolean(), "Un partage est privé par défaut");
        check((await server.Get($"/api/shares/{share}", host)).Body.GetProperty("code").GetString() == code, "L’hôte relit son code");
        check((await server.Get($"/api/shares/{share}", viewer)).Status == 404 && (await server.Get("/api/shares/public", viewer)).Body.GetArrayLength() == 0, "Sans le code, un compte étranger ne voit pas la conversation privée");

        // ---- Joining with the code ----
        var joined = await server.Post("/api/shares/join", new { code = code.ToLowerInvariant().Replace("-", " ") }, viewer);
        check(joined.Status == 200 && joined.Body.GetProperty("id").GetGuid() == share && joined.Body.GetProperty("joined").GetBoolean() && joined.Body.GetProperty("hostEmail").GetString() == "host@example.com",
            "Le code, saisi en minuscules ou avec des espaces, donne accès à la conversation");
        check(!joined.Body.TryGetProperty("code", out var leaked) || leaked.ValueKind == JsonValueKind.Null, "Un spectateur ne reçoit jamais le code du partage");
        check((await server.Get("/api/shares", viewer)).Body.GetArrayLength() == 1 && (await server.Get($"/api/shares/{share}", viewer)).Status == 200, "La conversation rejointe apparaît dans la liste du spectateur");
        var hostView = (await server.Get($"/api/shares/{share}", host)).Body;
        check(hostView.GetProperty("participants").EnumerateArray().Any(x => x.GetProperty("email").GetString() == "viewer@example.com"), "L’hôte voit qui a rejoint");
        check(await viewerWs.WaitAsync(x => Area(x, "shares")) != null, "Le spectateur est averti qu’il doit relire ses partages (signal de synchronisation)");
        check(await hostWs.WaitAsync(x => WsClient.Str(x, "name") == "share.joined") != null, "L’hôte est averti de l’arrivée");
        check(ErrorCode(await server.Post("/api/shares/join", new { code = "ZZZZZ-ZZZZZ" }, outsider)) == "invalid_share_code", "Un code inconnu est refusé");
        check(ErrorCode(await server.Post("/api/shares/join", new { code = "abc" }, outsider)) == "invalid_share_code", "Un code mal formé est refusé");
        check((await server.Post("/api/shares/join", new { code }, host)).Status == 200, "L’hôte qui saisit son propre code est accepté");
        check((await server.Get($"/api/shares/{share}", host)).Body.GetProperty("participants").GetArrayLength() == 1, "…il reste hôte, sans doublon");

        // ---- The bridge reaches viewers who joined by code ----
        await hostWs.SendAsync(new { type = "bridge.attach", shareId = share });
        await hostWs.WaitTypeAsync("bridge.attached");
        await hostWs.SendAsync(new { type = "bridge.snapshot", shareId = share, data = new { title = "Ma conversation", messages = new[] { new { id = 1, role = "user", content = "Salut" } } } });
        await viewerWs.SendAsync(new { type = "subscribe", topic = $"share:{share}" });
        check(await viewerWs.WaitEventAsync($"share:{share}", "snapshot") != null, "Un spectateur arrivé par code reçoit le contenu en direct");
        await viewerWs.SendAsync(new { type = "share.comment", shareId = share, text = "Bravo" });
        check(await hostWs.WaitAsync(x => WsClient.Str(x, "name") == "comment") != null, "Il peut commenter, et le commentaire atteint l’hôte");

        // ---- Throttling of wrong codes ----
        var (_, guesser) = await server.NewUserAsync(admin, "guesser@example.com");
        var failures = new List<string>();
        for (var attempt = 0; attempt < 8; attempt++) failures.Add(ErrorCode(await server.Post("/api/shares/join", new { code = "BBBBB-BBBBB" }, guesser)));
        var blocked = await server.Post("/api/shares/join", new { code }, guesser);
        check(failures.All(x => x == "invalid_share_code") && blocked.Status == 429, "Après 8 codes faux, les tentatives sont bloquées un moment, même avec le bon code");
        check((await server.Post("/api/shares/join", new { code }, outsider)).Status == 200, "Le blocage ne touche que le compte qui tâtonne");
        await server.SetAsync(admin, "shares.maxViewers", 2);
        var (_, late) = await server.NewUserAsync(admin, "late@example.com");
        check(ErrorCode(await server.Post("/api/shares/join", new { code }, late)) == "too_many_viewers", "Le nombre maximal de spectateurs s’applique aux codes");
        await server.SetAsync(admin, "shares.maxViewers", 20);
        check((await server.Post("/api/shares/join", new { code }, late)).Status == 200, "…et se libère quand la limite remonte");
        check((await server.Post($"/api/shares/{share}/leave", null, late)).Status is 200 or 204, "Un spectateur quitte la discussion");
        check((await server.Post("/api/shares/join", new { code }, late)).Status == 200, "…et peut la rejoindre de nouveau avec le code");

        // ---- Changing and turning off the code ----
        var regenerated = await server.Post($"/api/shares/{share}/code", null, host);
        var newCode = regenerated.Body.GetProperty("code").GetString()!;
        check(regenerated.Status == 200 && newCode != code, "L’hôte génère un nouveau code");
        var (_, newcomer) = await server.NewUserAsync(admin, "newcomer@example.com");
        check(ErrorCode(await server.Post("/api/shares/join", new { code }, newcomer)) == "invalid_share_code" && (await server.Get($"/api/shares/{share}", viewer)).Status == 200, "L’ancien code ne marche plus, mais ceux qui ont rejoint restent");
        check((await server.Post("/api/shares/join", new { code = newCode }, newcomer)).Status == 200, "Le nouveau code fonctionne");
        check((await server.Post($"/api/shares/{share}/code", null, viewer)).Status == 404, "Seul l’hôte change le code");
        var disabled = await server.Delete($"/api/shares/{share}/code", host);
        check(disabled.Status == 200 && Code(disabled) == "" && (await server.Get($"/api/shares/{share}", host)).Body.GetProperty("code").ValueKind == JsonValueKind.Null, "L’hôte désactive le code");
        var (_, tooLate) = await server.NewUserAsync(admin, "toolate@example.com");
        check(ErrorCode(await server.Post("/api/shares/join", new { code = newCode }, tooLate)) == "invalid_share_code", "Un code désactivé ne donne plus accès");
        check((await server.Post($"/api/shares/{share}/code", null, host)).Body.GetProperty("code").GetString() is { Length: 11 }, "Un nouveau code peut être généré ensuite");

        // ---- Public conversations ----
        await using var tooLateWs = await server.ConnectAsync(tooLate);
        var published = await Patch($"/api/shares/{share}", new { isPublic = true }, host);
        check(published.Status == 200 && published.Body.GetProperty("isPublic").GetBoolean(), "L’hôte rend la conversation publique");
        var listed = (await server.Get("/api/shares/public", tooLate)).Body;
        check(listed.GetArrayLength() == 1 && listed[0].GetProperty("id").GetGuid() == share && !listed[0].GetProperty("joined").GetBoolean(), "Tout compte autorisé la voit dans la liste publique");
        check((await server.Get("/api/shares/public", host)).Body.GetArrayLength() == 0, "L’hôte n’a pas à la retrouver dans la liste publique");
        check((await server.Get($"/api/shares/{share}", tooLate)).Status == 200, "Un compte étranger peut ouvrir une conversation publique");
        check(await tooLateWs.WaitAsync(x => Area(x, "shares")) != null, "Les clients reçoivent un signal quand une conversation devient publique");
        var guestView = await server.Get("/api/shares/public", guest);
        check(guestView.Status == 200 && guestView.Body.GetArrayLength() == 1, "Un invité (lecture seule) voit aussi les conversations publiques");
        await tooLateWs.SendAsync(new { type = "subscribe", topic = $"share:{share}" });
        check(await tooLateWs.WaitEventAsync($"share:{share}", "snapshot") != null, "Il suit la conversation publique en direct, sans code ni invitation");
        await Patch($"/api/shares/{share}", new { isPublic = false }, host);
        check((await server.Get($"/api/shares/{share}", tooLate)).Status == 404 && (await server.Get("/api/shares/public", tooLate)).Body.GetArrayLength() == 0, "Redevenue privée, elle disparaît pour ceux qui ne l’ont pas rejointe");
        var before = tooLateWs.Count(x => WsClient.Str(x, "name") == "stream");
        await hostWs.SendAsync(new { type = "bridge.event", shareId = share, name = "stream", data = new { id = 9, text = "après" } });
        await Task.Delay(400);
        check(tooLateWs.Count(x => WsClient.Str(x, "name") == "stream") == before && viewerWs.Count(x => WsClient.Str(x, "name") == "stream" && x.GetProperty("data").GetProperty("text").GetString() == "après") == 1,
            "La conversation redevenue privée n’est plus diffusée aux simples visiteurs, mais l’est toujours à ceux qui ont rejoint");
        check((await Patch($"/api/shares/{share}", new { isPublic = true }, viewer)).Status == 404, "Seul l’hôte change la visibilité");

        // ---- Revoking ends everything ----
        await Patch($"/api/shares/{share}", new { isPublic = true }, host);
        await server.Post($"/api/shares/{share}/revoke", null, host);
        check(ErrorCode(await server.Post("/api/shares/join", new { code = (await server.Get($"/api/shares/{share}", host)).Body.GetProperty("code").GetString() }, tooLate)) == "invalid_share_code"
            && (await server.Get("/api/shares/public", tooLate)).Body.GetArrayLength() == 0, "Un partage révoqué ne se rejoint plus et quitte la liste publique");

        // ---- Account search for pickers ----
        var found = await server.Get("/api/users/search?query=view", host);
        check(found.Status == 200 && found.Body.GetArrayLength() == 1 && found.Body[0].GetProperty("email").GetString() == "viewer@example.com", "La recherche de comptes trouve par fragment d’adresse");
        var everyone = (await server.Get("/api/users/search", host)).Body;
        check(everyone.EnumerateArray().All(x => x.GetProperty("email").GetString() != "host@example.com") && everyone.GetArrayLength() >= 5, "Sans texte, elle liste les autres comptes actifs (jamais soi-même)");
        check((await server.Get("/api/users/search?query=zzz-no-one", host)).Body.GetArrayLength() == 0, "Aucun résultat pour un texte inconnu");
        check(ErrorCode(await server.Get("/api/users/search?query=a", guest)) == "missing_permission", "Un compte sans droit de cloud ni de partage ne peut pas explorer l’annuaire");
        check((await server.Get("/api/users/search?query=a")).Status == 401, "L’annuaire exige d’être connecté");
        await server.SendAsync(HttpMethod.Patch, $"/api/admin/users/{viewerId}", new { isActive = false }, admin);
        check((await server.Get("/api/users/search?query=view", host)).Body.GetArrayLength() == 0, "Les comptes désactivés ne sont pas proposés");

        // ---- Change signals for providers, projects and conversations ----
        await server.EnableAsync(admin, "cloud");
        var (memberId, member) = await server.NewUserAsync(admin, "member@example.com");
        await using var memberWs = await server.ConnectAsync(member);
        await using var adminWs = await server.ConnectAsync(admin);
        var provider = await server.Post("/api/admin/providers", new { name = "Sync", baseUrl = "http://127.0.0.1:9/v1", models = new[] { new { id = "m" } } }, admin);
        check(await memberWs.WaitAsync(x => Area(x, "providers")) != null, "Un changement du catalogue de providers est signalé à tous les clients connectés");
        var project = (await server.Post("/api/projects", new { name = "Sync" }, host)).Body.GetProperty("id").GetGuid();
        check(await hostWs.WaitAsync(x => Area(x, "projects")) != null, "Créer un projet cloud est signalé à son propriétaire");
        await server.Put($"/api/projects/{project}/members", new { members = new[] { new { email = "member@example.com", access = "run" } } }, host);
        check(await memberWs.WaitAsync(x => Area(x, "projects")) != null, "Ajouter un membre le signale au nouveau membre");
        var chat = (await server.Post($"/api/projects/{project}/chats", new { title = "t" }, host)).Body.GetProperty("id").GetInt32();
        check(await memberWs.WaitAsync(x => Area(x, "chats") && x.GetProperty("data").GetProperty("chatId").GetInt32() == chat) != null, "Une nouvelle conversation est signalée aux membres du projet");
        check((await server.Delete($"/api/projects/{project}/chats/{chat}", host)).Status is 200 or 204, "Suppression de la conversation");
        check(memberWs.Count(x => Area(x, "chats") && x.GetProperty("data").GetProperty("chatId").GetInt32() == chat) >= 2, "La suppression d’une conversation est aussi signalée");
        var removedCount = memberWs.Count(x => Area(x, "projects"));
        await server.Put($"/api/projects/{project}/members", new { members = Array.Empty<object>() }, host);
        check(await memberWs.WaitAsync(x => Area(x, "projects") && memberWs.Count(y => Area(y, "projects")) > removedCount, 4000) != null, "Retirer un membre le signale à celui qui perd l’accès");
        await server.Delete($"/api/projects/{project}", host);
        check(hostWs.Count(x => Area(x, "projects")) >= 2, "Supprimer un projet est signalé");
    }
}
