using System.Text.Json;
using MonolithHarness.Server.Security;
using MonolithHarness.Server.Services;

static class ShareChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        string Code((int Status, JsonElement Body) r) => r.Body.ValueKind == JsonValueKind.Object && r.Body.TryGetProperty("code", out var x) ? x.GetString()! : "";

        await server.Post("/api/admin/roles", new { name = "Lecteur", permissions = new[] { Permissions.SharingView } }, admin);
        var (_, host) = await server.NewUserAsync(admin, "hana@example.com");
        var (_, ivan) = await server.NewUserAsync(admin, "ivan@example.com", "Guest");
        var (_, kim) = await server.NewUserAsync(admin, "kim@example.com", "Lecteur");
        var (_, jules) = await server.NewUserAsync(admin, "jules@example.com", "Guest");
        var (_, lena) = await server.NewUserAsync(admin, "lena@example.com");

        // ---- Sharing management ----
        check(Code(await server.Post("/api/shares", new { title = "x" }, ivan)) == "missing_permission", "Partager une conversation exige sharing.host");
        var created = await server.Post("/api/shares", new { title = "Ma session", clientConversationId = "conv-42" }, host);
        var share = created.Body.GetProperty("id").GetGuid();
        check(created.Status == 201 && created.Body.GetProperty("isHost").GetBoolean() && !created.Body.GetProperty("live").GetBoolean(), "Déclaration d’une conversation partagée");
        check((await server.Get($"/api/shares/{share}", ivan)).Status == 404 && (await server.Get("/api/shares", ivan)).Body.GetArrayLength() == 0, "Une conversation non partagée avec soi est invisible");
        check(Code(await server.Post($"/api/shares/{share}/participants", new { email = "ghost@example.com" }, host)) == "unknown_user", "Invité inconnu refusé");
        check(Code(await server.Post($"/api/shares/{share}/participants", new { email = "hana@example.com" }, host)) == "self_invite", "On ne s’invite pas soi-même");
        check(Code(await server.Post($"/api/shares/{share}/participants", new { email = "ivan@example.com" }, jules)) == "missing_permission", "Inviter exige sharing.host");
        await server.Post($"/api/shares/{share}/participants", new { email = "ivan@example.com" }, host);
        await server.Post($"/api/shares/{share}/participants", new { email = "lena@example.com" }, host);
        check((await server.Post($"/api/shares/{share}/participants", new { email = "jules@example.com" }, lena)).Status == 404, "Même avec sharing.host, seul l’hôte de CETTE conversation invite");
        await server.Post($"/api/shares/{share}/participants", new { email = "kim@example.com" }, host);
        check((await server.Get("/api/shares", ivan)).Body.GetArrayLength() == 1 && !(await server.Get($"/api/shares/{share}", ivan)).Body.GetProperty("isHost").GetBoolean(), "Les invités voient la conversation partagée");
        check((await server.Get($"/api/shares/{share}", ivan)).Body.GetProperty("participants").GetArrayLength() == 0, "…sans la liste des autres spectateurs");

        // ---- Bridge and replay ----
        await using var hostWs = await server.ConnectAsync(host);
        await hostWs.SendAsync(new { type = "bridge.attach", shareId = share });
        check(await hostWs.WaitTypeAsync("bridge.attached") != null, "L’appareil de l’hôte attache son bridge");
        await hostWs.SendAsync(new { type = "bridge.snapshot", shareId = share, data = new { title = "Ma session", messages = new[] { new { id = 1, role = "user", content = "Salut" } } } });
        await hostWs.SendAsync(new { type = "bridge.event", shareId = share, name = "message", data = new { id = 2, role = "assistant", content = "Bonjour" } });
        await hostWs.SendAsync(new { type = "bridge.event", shareId = share, name = "stream", data = new { id = 3, text = "En cours" } });
        await Task.Delay(300);
        check((await server.Get($"/api/shares/{share}", host)).Body.GetProperty("live").GetBoolean(), "La conversation est indiquée en direct");

        var topic = $"share:{share}";
        await using var ivanWs = await server.ConnectAsync(ivan);
        await ivanWs.SendAsync(new { type = "subscribe", topic });
        check(await ivanWs.WaitTypeAsync("subscribed") != null, "Un invité s’abonne à la conversation");
        var bridgeEvent = await ivanWs.WaitEventAsync(topic, "bridge");
        var snapshot = await ivanWs.WaitEventAsync(topic, "snapshot");
        var replay = await ivanWs.WaitAsync(x => WsClient.Str(x, "name") == "stream");
        check(bridgeEvent?.GetProperty("data").GetProperty("online").GetBoolean() == true && snapshot?.GetProperty("data").GetProperty("messages").GetArrayLength() == 1, "Il reçoit l’état courant (instantané) à son arrivée…");
        var names = ivanWs.Seen.Where(x => WsClient.Str(x, "type") == "event").Select(x => WsClient.Str(x, "name")).ToList();
        check(replay != null && names.IndexOf("snapshot") < names.IndexOf("message") && names.IndexOf("message") < names.IndexOf("stream"), "…puis les événements récents dans l’ordre");

        await hostWs.SendAsync(new { type = "bridge.event", shareId = share, name = "stream", data = new { id = 3, text = "En cours, suite" } });
        check(await ivanWs.WaitAsync(x => WsClient.Str(x, "name") == "stream" && x.GetProperty("data").GetProperty("text").GetString() == "En cours, suite") != null, "Les nouveaux événements sont diffusés en direct");

        // ---- Viewers: read and comment only ----
        await using var kimWs = await server.ConnectAsync(kim);
        await kimWs.SendAsync(new { type = "subscribe", topic });
        check(await kimWs.WaitEventAsync(topic, "snapshot") != null, "Un spectateur qui arrive plus tard reçoit aussi l’état courant");
        await ivanWs.SendAsync(new { type = "bridge.event", shareId = share, name = "message", data = new { content = "usurpé" } });
        check(await ivanWs.WaitErrorAsync("not_bridge") != null && kimWs.Count(x => WsClient.Str(x, "name") == "message" && x.GetProperty("data").TryGetProperty("content", out var c) && c.GetString() == "usurpé") == 0, "Un spectateur ne peut pas émettre d’événements du bridge");
        await ivanWs.SendAsync(new { type = "bridge.attach", shareId = share });
        check(await ivanWs.WaitErrorAsync("missing_permission") != null, "Sans sharing.host, un spectateur ne peut pas devenir le bridge");
        await using var lenaWs = await server.ConnectAsync(lena);
        await lenaWs.SendAsync(new { type = "bridge.attach", shareId = share });
        check(await lenaWs.WaitErrorAsync("not_found") != null, "Même avec sharing.host, on n’attache pas le bridge de la conversation d’un autre");
        await ivanWs.SendAsync(new { type = "share.comment", shareId = share, text = "Joli travail" });
        var onHost = await hostWs.WaitEventAsync(topic, "comment");
        check(onHost?.GetProperty("data").GetProperty("text").GetString() == "Joli travail" && onHost?.GetProperty("data").GetProperty("author").GetString() == "ivan@example.com", "Un commentaire atteint l’appareil de l’hôte");
        check(await kimWs.WaitEventAsync(topic, "comment") != null, "…et les autres spectateurs");
        await ivanWs.SendAsync(new { type = "share.comment", shareId = share, text = "Trop vite" });
        check(await ivanWs.WaitErrorAsync("too_many_attempts") != null, "Les commentaires sont limités en fréquence");
        await kimWs.SendAsync(new { type = "share.comment", shareId = share, text = "Moi aussi" });
        check(await kimWs.WaitErrorAsync("missing_permission") != null, "Sans sharing.comment, on ne lit que");
        await Task.Delay(1100);
        await ivanWs.SendAsync(new { type = "share.comment", shareId = share, text = new string('x', ShareService.MaxCommentLength + 1) });
        check(await ivanWs.WaitErrorAsync("invalid_comment") != null, "Commentaire trop long refusé");
        await using var julesWs = await server.ConnectAsync(jules);
        await julesWs.SendAsync(new { type = "subscribe", topic });
        check(await julesWs.WaitErrorAsync("not_found") != null, "Un utilisateur non invité ne peut pas suivre la conversation");

        // ---- Leave, remove ----
        check((await server.Post($"/api/shares/{share}/leave", null, ivan)).Status == 204, "Un spectateur quitte la discussion");
        check(await ivanWs.WaitEventAsync($"user:{(await server.Get("/api/auth/me", ivan)).Body.GetProperty("id").GetGuid()}", "share.closed") != null, "…et en est informé");
        var before = ivanWs.Count(x => WsClient.Str(x, "type") == "event" && WsClient.Str(x, "topic") == topic);
        await hostWs.SendAsync(new { type = "bridge.event", shareId = share, name = "stream", data = new { id = 4, text = "après départ" } });
        await kimWs.WaitAsync(x => WsClient.Str(x, "name") == "stream" && x.GetProperty("data").GetProperty("id").GetInt32() == 4);
        check(ivanWs.Count(x => WsClient.Str(x, "type") == "event" && WsClient.Str(x, "topic") == topic) == before && (await server.Get($"/api/shares/{share}", ivan)).Status == 404, "Il ne reçoit plus rien et n’a plus accès");
        await ivanWs.SendAsync(new { type = "subscribe", topic });
        check(await ivanWs.WaitErrorAsync("not_found") != null, "Réabonnement impossible sans nouvelle invitation");
        var kimId = (await server.Get("/api/auth/me", kim)).Body.GetProperty("id").GetGuid();
        check((await server.Delete($"/api/shares/{share}/participants/{kimId}", host)).Status == 204, "L’hôte retire un spectateur");
        await hostWs.SendAsync(new { type = "bridge.event", shareId = share, name = "stream", data = new { id = 5, text = "après retrait" } });
        await Task.Delay(400);
        check(kimWs.Count(x => WsClient.Str(x, "name") == "stream" && x.GetProperty("data").TryGetProperty("id", out var i) && i.GetInt32() == 5) == 0, "Un spectateur retiré ne reçoit plus rien");

        // ---- Re-invite, then the host device goes away: nothing is kept ----
        await server.Post($"/api/shares/{share}/participants", new { email = "ivan@example.com" }, host);
        await using var ivanAgain = await server.ConnectAsync(ivan);
        await ivanAgain.SendAsync(new { type = "subscribe", topic });
        check(await ivanAgain.WaitEventAsync(topic, "snapshot") != null, "Un spectateur peut être réinvité");
        await hostWs.DisposeAsync();
        check(await ivanAgain.WaitAsync(x => WsClient.Str(x, "name") == "bridge" && x.GetProperty("data").GetProperty("online").GetBoolean() == false) != null, "Quand l’appareil de l’hôte se déconnecte, les spectateurs en sont informés");
        await using var late = await server.ConnectAsync(ivan);
        await late.SendAsync(new { type = "subscribe", topic });
        await late.WaitEventAsync(topic, "bridge");
        await Task.Delay(300);
        check(late.Count(x => WsClient.Str(x, "name") is "snapshot" or "message" or "stream") == 0 && !(await server.Get($"/api/shares/{share}", host)).Body.GetProperty("live").GetBoolean(),
            "Aucun historique n’est conservé sur le serveur après la déconnexion de l’appareil");
        await ivanAgain.SendAsync(new { type = "share.comment", shareId = share, text = "Quelqu’un ?" });
        check(await ivanAgain.WaitErrorAsync("host_offline") != null, "On ne commente pas une conversation dont l’appareil est hors ligne");

        // ---- Host reconnects: a clean buffer ----
        await using var hostAgain = await server.ConnectAsync(host, authenticateWithMessage: true);
        await hostAgain.WaitTypeAsync("authenticated");
        await hostAgain.SendAsync(new { type = "bridge.attach", shareId = share });
        await hostAgain.WaitTypeAsync("bridge.attached");
        await hostAgain.SendAsync(new { type = "bridge.snapshot", shareId = share, data = new { messages = Array.Empty<object>(), fresh = true } });
        check(await late.WaitAsync(x => WsClient.Str(x, "name") == "snapshot" && x.GetProperty("data").TryGetProperty("fresh", out _)) != null, "Reconnexion de l’hôte (authentification par message): nouvel instantané diffusé");

        // ---- Buffer cap ----
        await server.SetAsync(admin, "shares.bufferEvents", 10);
        for (var i = 0; i < 30; i++) await hostAgain.SendAsync(new { type = "bridge.event", shareId = share, name = "stream", data = new { n = i } });
        await Task.Delay(500);
        await using var capped = await server.ConnectAsync(ivan);
        await capped.SendAsync(new { type = "subscribe", topic });
        await Task.Delay(700);
        check(capped.Count(x => WsClient.Str(x, "name") == "stream") == 10, "Le tampon de relecture est borné");

        // ---- Max viewers ----
        await server.SetAsync(admin, "shares.maxViewers", 1);
        check(Code(await server.Post($"/api/shares/{share}/participants", new { email = "jules@example.com" }, host)) == "too_many_viewers", "Nombre de spectateurs limité");
        await server.SetAsync(admin, "shares.maxViewers", 20);

        // ---- Feature switched off ----
        await server.Put("/api/admin/features/sharing", new { enabled = false }, admin);
        check(await capped.WaitEventAsync(topic, "closed") != null, "Désactiver le partage ferme toutes les conversations en direct");
        await hostAgain.SendAsync(new { type = "bridge.event", shareId = share, name = "stream", data = new { n = 99 } });
        check(await hostAgain.WaitErrorAsync("not_bridge") != null, "…et coupe les bridges");
        check(Code(await server.Get("/api/shares", host)) == "feature_disabled" || (await server.Get("/api/shares", host)).Status == 200, "Liste des partages cohérente quand la fonctionnalité est coupée");
        await server.Put("/api/admin/features/sharing", new { enabled = true }, admin);

        // ---- Revocation by the host ----
        await using var viewer = await server.ConnectAsync(ivan);
        await hostAgain.SendAsync(new { type = "bridge.attach", shareId = share });
        await hostAgain.WaitAsync(x => WsClient.Str(x, "type") == "bridge.attached" && WsClient.Str(x, "shareId") == share.ToString());
        await viewer.SendAsync(new { type = "subscribe", topic });
        await viewer.WaitEventAsync(topic, "bridge");
        check((await server.Post($"/api/shares/{share}/revoke", null, ivan)).Status == 403 && (await server.Post($"/api/shares/{share}/revoke", null, lena)).Status == 404, "Seul l’hôte révoque");
        check((await server.Post($"/api/shares/{share}/revoke", null, host)).Status == 204, "L’hôte révoque le partage");
        check(await viewer.WaitEventAsync(topic, "closed") != null, "Les spectateurs sont déconnectés de la conversation");
        await viewer.SendAsync(new { type = "subscribe", topic });
        check(await viewer.WaitErrorAsync("not_found") != null && (await server.Get("/api/shares", ivan)).Body.GetArrayLength() == 0, "Une conversation révoquée n’est plus accessible");
        await hostAgain.SendAsync(new { type = "bridge.event", shareId = share, name = "stream", data = new { n = 100 } });
        check(await hostAgain.WaitErrorAsync("not_bridge") != null, "Le bridge d’un partage révoqué est coupé");
    }
}
