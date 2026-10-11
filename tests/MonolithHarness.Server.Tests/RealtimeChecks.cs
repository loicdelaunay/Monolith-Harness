using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

static class RealtimeChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        var (_, user) = await server.NewUserAsync(admin, "walt@example.com");

        // ---- Handshake and authentication ----
        var plain = await (await server.Http.GetAsync("/ws")).Content.ReadAsStringAsync();
        check(plain.Contains("websocket_required"), "/ws sans WebSocket explique ce qu’il faut faire");

        await using (var anonymous = await server.ConnectAsync())
        {
            check((await anonymous.WaitTypeAsync("hello"))?.GetProperty("authenticate").GetBoolean() == true, "Sans jeton, le serveur attend une authentification");
            await anonymous.SendAsync(new { type = "subscribe", topic = "server" });
            check(await anonymous.WaitErrorAsync("unauthorized") != null, "Aucun abonnement avant authentification");
            await anonymous.SendAsync(new { type = "auth", token = "mhs_wrong" });
            check(await anonymous.WaitErrorAsync("invalid_token") != null, "Jeton invalide refusé");
            await anonymous.SendAsync(new { type = "ping" });
            check(await anonymous.WaitTypeAsync("pong") != null, "Ping / pong");
            await anonymous.SendAsync(new { type = "auth", token = user, @ref = "r1" });
            var ok = await anonymous.WaitTypeAsync("authenticated");
            check(ok?.GetProperty("user").GetString() == "walt@example.com" && ok?.GetProperty("ref").GetString() == "r1", "Authentification par message (clients sans en-têtes)");
        }
        await using (var header = await server.ConnectAsync(user))
            check((await header.WaitTypeAsync("hello"))?.GetProperty("user").GetString() == "walt@example.com", "Authentification à la poignée de main par en-tête Bearer");

        // ---- Topics ----
        await using var ws = await server.ConnectAsync(user);
        await ws.SendAsync(new { type = "subscribe", topic = "nonsense" });
        check(await ws.WaitErrorAsync("unknown_topic") != null, "Sujet inconnu refusé");
        await ws.SendAsync(new { type = "subscribe", topic = "project:not-a-guid" });
        check(await ws.WaitErrorAsync("invalid_topic") != null, "Identifiant invalide refusé");
        await ws.SendAsync(new { type = "subscribe", topic = "queue" });
        check(await ws.WaitErrorAsync("missing_permission") != null, "Le sujet de la file exige queue.manage");
        await ws.SendAsync(new { type = "subscribe", topic = $"run:{Guid.NewGuid()}" });
        check(await ws.WaitErrorAsync("feature_disabled") != null, "Les sujets d’exécution suivent l’état de la fonctionnalité cloud");
        await ws.SendAsync(new { type = "frobnicate" });
        check(await ws.WaitErrorAsync("unknown_type") != null, "Type de message inconnu signalé");
        await ws.SendAsync(new { nothing = true });
        check(await ws.WaitErrorAsync("unknown_type") != null, "Message sans type signalé");

        await using var adminWs = await server.ConnectAsync(admin);
        await adminWs.SendAsync(new { type = "subscribe", topic = "queue" });
        check(await adminWs.WaitTypeAsync("subscribed") != null, "Un administrateur suit la file");

        // ---- Server notifications ----
        await server.Put("/api/admin/features/schedules", new { enabled = true }, admin);
        var changed = await ws.WaitEventAsync("server", "feature.changed");
        check(changed?.GetProperty("data").GetProperty("feature").GetString() == "schedules" && changed?.GetProperty("data").GetProperty("enabled").GetBoolean() == true, "Les changements de fonctionnalités sont diffusés");

        // ---- Revocation closes sockets ----
        await using var doomed = await server.ConnectAsync(user);
        await doomed.WaitTypeAsync("hello");
        var second = await server.LoginAsync("walt@example.com", "password-1234");
        await using var secondWs = await server.ConnectAsync(second);
        await secondWs.WaitTypeAsync("hello");
        await server.Post("/api/auth/sessions/revoke-others", null, second);
        check(await doomed.WaitClosedAsync(), "Révoquer une connexion ferme son WebSocket immédiatement");
        check(secondWs.State == WebSocketState.Open, "…sans toucher à la session courante");
        await server.Post("/api/auth/logout", null, second);
        check(await secondWs.WaitClosedAsync(), "La déconnexion ferme aussi le WebSocket");

        // ---- Disabled account ----
        var thirdToken = await server.LoginAsync("walt@example.com", "password-1234");
        await using var thirdWs = await server.ConnectAsync(thirdToken);
        await thirdWs.WaitTypeAsync("hello");
        var waltId = (await server.Get("/api/auth/me", thirdToken)).Body.GetProperty("id").GetGuid();
        await server.SendAsync(HttpMethod.Patch, $"/api/admin/users/{waltId}", new { isActive = false }, admin);
        check(await thirdWs.WaitClosedAsync(), "Désactiver un compte ferme ses WebSockets");

        // ---- Limits ----
        var raw = new System.Net.WebSockets.ClientWebSocket();
        raw.Options.SetRequestHeader("Authorization", "Bearer " + admin);
        await raw.ConnectAsync(new Uri(server.Base.Replace("http", "ws") + "/ws"), CancellationToken.None);
        var huge = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "ping", pad = new string('x', 600 * 1024) }));
        await raw.SendAsync(huge, WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[1024];
        WebSocketReceiveResult? result = null;
        try { while (raw.State == WebSocketState.Open || raw.State == WebSocketState.CloseSent) { result = await raw.ReceiveAsync(buffer, CancellationToken.None); if (result.MessageType == WebSocketMessageType.Close) break; } }
        catch (WebSocketException) { }
        check(raw.CloseStatus == WebSocketCloseStatus.MessageTooBig || raw.State is WebSocketState.Aborted or WebSocketState.Closed, "Un message trop volumineux ferme la connexion");
        raw.Dispose();

        // ---- Cross-site handshake with a cookie is refused ----
        using var cross = new HttpRequestMessage(HttpMethod.Get, "/ws");
        cross.Headers.TryAddWithoutValidation("Cookie", "mh_session=" + admin);
        cross.Headers.TryAddWithoutValidation("Origin", "http://evil.example");
        cross.Headers.TryAddWithoutValidation("Connection", "Upgrade");
        cross.Headers.TryAddWithoutValidation("Upgrade", "websocket");
        cross.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
        cross.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", Convert.ToBase64String(Guid.NewGuid().ToByteArray()));
        check((int)(await server.Http.SendAsync(cross)).StatusCode == 403, "Une poignée de main WebSocket inter-sites avec cookie est refusée");
    }
}
