using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
using MonolithHarness.Core.Remote;
using MonolithHarness.Server.Services;

/// <summary>
/// What a client does with the new server features: the message of the moment, the server mode switch, the rank of a call waiting in the queue,
/// and the sharing of the speed and consumption dashboard.
/// </summary>
static class ClientFeatureChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var upstream = await FakeProvider.StartAsync();
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        var userRole = await server.RoleId("User", admin);
        var provider = (await server.Post("/api/admin/providers", new { name = "Partagé", baseUrl = upstream.BaseUrl, secret = "sk-x", allowLocalUse = true, models = new[] { new { id = "fake-model", contextLimit = 32000 } } }, admin)).Body.GetProperty("id").GetGuid();
        await server.Put($"/api/admin/providers/{provider}/access", new { entries = new[] { new { kind = "role", id = userRole, name = "" } } }, admin);
        var (_, rival) = await server.NewUserAsync(admin, "rival@example.com");
        await server.NewUserAsync(admin, "dora@example.com");
        await server.Put("/api/admin/announcement", new { text = "Avant le démarrage du client", level = "info" }, admin);

        var folder = Path.Combine(Path.GetTempPath(), "mh-clientfeat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var clientDb = Path.Combine(folder, "database.sqlite");
        var original = Path.Combine(PortableStorage.Root, "database.sqlite");
        PortableStorage.UseDatabase(clientDb);
        try
        {
            await using (var init = new HarnessDb(clientDb)) await init.InitializeAsync();
            await using var connection = new ServerConnection(new ServerLinkStore(ServerLinkStore.PathFor(clientDb)), KeyVault.Encrypt, KeyVault.Decrypt);
            await connection.ConnectAsync(server.Base, false);
            await connection.LoginAsync("dora@example.com", "password-1234", "client features");
            await using var sync = new ServerSyncService(connection, () => new HarnessDb(clientDb));
            async Task<bool> WaitFor(Func<bool> condition, int milliseconds = 15000) { for (var waited = 0; waited < milliseconds && !condition(); waited += 50) await Task.Delay(50); return condition(); }
            List<Provider> Providers() { using var db = new HarnessDb(clientDb); return db.Providers.AsNoTracking().ToList(); }

            sync.Start();
            check(await WaitFor(() => sync.Snapshot.State == LinkState.Online && sync.Snapshot.LastSyncUtc != null), "Le client se connecte");

            // ---- The message of the moment ----
            check(sync.Snapshot.Announcement is { Level: "info" } first && first.Text == "Avant le démarrage du client", "Un message déjà publié est reçu à la première synchronisation");
            await server.Put("/api/admin/announcement", new { text = "Panne en cours", level = "error", expiresMinutes = 30 }, admin);
            check(await WaitFor(() => sync.Snapshot.Announcement is { Level: "error", Text: "Panne en cours" }), "Un nouveau message arrive en direct, avec son niveau d’alerte");
            var announced = sync.Snapshot.Announcement!;
            check(announced.ExpiresUtc is { } end && end > DateTime.UtcNow.AddMinutes(25) && announced.By == "admin@admin.com", "…avec sa durée et son auteur");
            sync.DismissAnnouncement();
            check(sync.Snapshot.Announcement == null && connection.Link.DismissedAnnouncement == announced.Stamp, "L’utilisateur ferme le message: il n’est plus affiché et son identifiant est retenu");
            await sync.SyncNowAsync();
            check(sync.Snapshot.Announcement == null, "Une resynchronisation ne le rouvre pas");
            await server.Put("/api/admin/announcement", new { text = "Panne en cours", level = "error" }, admin);
            check(await WaitFor(() => sync.Snapshot.Announcement is { Text: "Panne en cours" }), "Un message republié (même texte) est un nouveau message: il s’affiche à nouveau");
            await server.Delete("/api/admin/announcement", admin);
            check(await WaitFor(() => sync.Snapshot.Announcement == null), "Le retrait par l’administrateur fait disparaître le bandeau");
            await server.Put("/api/admin/announcement", new { text = "Bientôt fini", level = "warning", expiresMinutes = 60 }, admin);
            check(await WaitFor(() => sync.Snapshot.Announcement is { Level: "warning" }), "Un avertissement arrive aussi");
            await server.Service<SettingsService>().SetTextAsync("announcement.expiresUtc", DateTime.UtcNow.AddSeconds(-5).ToString("O"));
            await sync.SyncNowAsync();
            check(await WaitFor(() => sync.Snapshot.Announcement == null, 5000), "Un message dont le temps est écoulé disparaît");
            await server.Delete("/api/admin/announcement", admin);

            // ---- The server mode ----
            var shared = Providers().Single(x => x.Name == "Partagé · serveur");
            var local = new Provider { Name = "Mon modèle local", Kind = "openai", BaseUrl = "http://127.0.0.1:1/v1", Model = "m" };
            check(ServerSync.BlockedReason(connection.Link, shared) == null && ServerSync.BlockedReason(connection.Link, local) == null, "Mode serveur activé: tous les providers sont utilisables");
            await sync.SetEnabledAsync(false);
            check(sync.Snapshot.State == LinkState.Paused && !connection.Link.Enabled && connection.Link.SignedIn, "Mode serveur désactivé: l’état est « en pause », le compte est conservé");
            check(await WaitFor(() => !connection.IsRealtimeConnected, 8000), "Le canal temps réel est fermé: plus aucun échange avec le serveur");
            check(ServerSync.BlockedReason(connection.Link, shared) is { Length: > 0 } && ServerSync.BlockedReason(connection.Link, local) == null, "Les providers du serveur sont fermés, les providers locaux restent utilisables");
            check(Providers().Any(x => x.Name == "Partagé · serveur"), "…sans être supprimés (la réactivation retrouve les mêmes)");
            var later = (await server.Post("/api/admin/providers", new { name = "Pendant la pause", baseUrl = upstream.BaseUrl, secret = "sk-y", allowLocalUse = true, models = new[] { new { id = "fake-model" } } }, admin)).Body.GetProperty("id").GetGuid();
            await server.Put($"/api/admin/providers/{later}/access", new { entries = new[] { new { kind = "role", id = userRole, name = "" } } }, admin);
            await Task.Delay(1500);
            check(Providers().All(x => x.Name != "Pendant la pause · serveur") && sync.Snapshot.Announcement == null, "En pause, le client ne reçoit rien du serveur");
            await server.Put("/api/admin/announcement", new { text = "Pendant la pause", level = "info" }, admin);
            await Task.Delay(800);
            check(sync.Snapshot.Announcement == null, "…pas même le message du moment");
            await sync.SetEnabledAsync(true);
            check(await WaitFor(() => sync.Snapshot.State == LinkState.Online && Providers().Any(x => x.Name == "Pendant la pause · serveur")), "Réactivé, le client se resynchronise tout de suite et retrouve ce qui a changé");
            check(await WaitFor(() => sync.Snapshot.Announcement is { Text: "Pendant la pause" }) && ServerSync.BlockedReason(connection.Link, shared) == null, "…le message du moment et les providers redeviennent disponibles");
            await server.Delete("/api/admin/announcement", admin);

            // ---- The rank of a call waiting in the queue ----
            upstream.Handler = _ => new FakeProvider.Reply("OK", null, null, 200, 1800);
            await server.SetAsync(admin, "queue.maxConcurrentUsers", 4);
            await server.SetAsync(admin, "queue.maxConcurrentRuns", 1);
            await server.SetAsync(admin, "queue.maxRunsPerUser", 1);
            var seen = new List<IReadOnlyList<RelayWait>>();
            sync.RelayQueueChanged += waits => { lock (seen) seen.Add(waits); };
            async Task<int> Call(string token)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/providers/{provider}/relay/chat/completions")
                { Content = new StringContent("""{"model":"fake-model","stream":true,"messages":[{"role":"user","content":"hi"}]}""", Encoding.UTF8, "application/json") };
                request.Headers.Authorization = new("Bearer", token);
                using var response = await server.Http.SendAsync(request);
                await response.Content.ReadAsStringAsync();
                return (int)response.StatusCode;
            }
            var holder = Call(rival);
            for (var waited = 0; waited < 5000 && (await server.Get("/api/queue", admin)).Body.GetProperty("running").GetInt32() == 0; waited += 50) await Task.Delay(50);
            var mine = Call(connection.Token()!);
            check(await WaitFor(() => sync.RelayWaits.Count == 1), "Un appel de ce client mis en file d’attente est signalé");
            var wait = sync.RelayWaits.Single();
            check(wait.Position == 1 && wait.ProviderId == provider && wait.Model == "fake-model", "…avec sa position, le provider et le modèle attendus");
            check(sync.Snapshot.RelayWaits is { Count: 1 }, "L’état de synchronisation porte aussi la file d’attente");
            check(await mine == 200 && await holder == 200, "Les deux appels aboutissent");
            check(await WaitFor(() => sync.RelayWaits.Count == 0), "Quand l’appel démarre, il disparaît de la file");
            lock (seen) check(seen.Count >= 2 && seen[^1].Count == 0 && seen.Any(x => x.Count == 1 && x[0].Position == 1), "L’interface est prévenue de l’arrivée puis du départ");

            // ---- Sharing the speed and consumption dashboard ----
            long Count() { using var db = new HarnessDb(clientDb); return db.TokenUsages.Count(); }
            using (var db = new HarnessDb(clientDb))
            {
                db.TokenUsages.AddRange(
                    new TokenUsage { StartedUtc = DateTime.UtcNow.AddMinutes(-10), CompletedUtc = DateTime.UtcNow.AddMinutes(-9), ProviderId = shared.Id, ProviderName = shared.Name, Model = "fake-model", InputTokens = 1000, OutputTokens = 300, CachedInputTokens = 200, Seconds = 10, ProjectName = "Secret", ChatTitle = "Confidentiel" },
                    new TokenUsage { StartedUtc = DateTime.UtcNow.AddMinutes(-5), CompletedUtc = DateTime.UtcNow.AddMinutes(-4), ProviderName = "Perso", Model = "model-b", InputTokens = 500, OutputTokens = 100, Seconds = 5 },
                    new TokenUsage { StartedUtc = DateTime.UtcNow.AddDays(-60), CompletedUtc = DateTime.UtcNow.AddDays(-60), ProviderName = "Ancien", Model = "old", InputTokens = 9, OutputTokens = 9, Seconds = 1 },
                    new TokenUsage { StartedUtc = DateTime.UtcNow.AddMinutes(-3), CompletedUtc = DateTime.UtcNow.AddMinutes(-2), ProviderName = "Hérité", Model = "legacy", InputTokens = 7, OutputTokens = 7, Seconds = 1, Legacy = true });
                db.SaveChanges();
            }
            var doraToken = connection.Token()!;
            async Task<JsonElement> Mine() => (await server.Get("/api/usage/summary?scope=me&period=7d", doraToken)).Body;
            await sync.PushUsageAsync();
            check((await Mine()).GetProperty("totals").GetProperty("calls").GetInt32() == 0, "Rien n’est envoyé tant que le partage n’est pas activé");
            check(!connection.Link.ShareUsage, "Le partage est désactivé par défaut");
            sync.SetShareUsage(true);
            check(connection.Link.ShareUsage && connection.Link.DeviceId.Length > 0 && connection.Link.UsageSinceUtc is { } since && since < DateTime.UtcNow.AddDays(-29), "L’activation mémorise l’appareil et couvre les 30 derniers jours");
            await sync.PushUsageAsync();
            var summary = await Mine();
            var models = summary.GetProperty("models").EnumerateArray().ToList();
            check(summary.GetProperty("totals").GetProperty("calls").GetInt32() == 2 && summary.GetProperty("totals").GetProperty("inputTokens").GetInt64() == 1500, "Seuls les deux appels des 30 derniers jours sont envoyés (ni l’ancien, ni l’importé d’un ancien historique)");
            check(models.Any(x => x.GetProperty("provider").GetString() == "Partagé" && x.GetProperty("model").GetString() == "fake-model") && models.Any(x => x.GetProperty("provider").GetString() == "Perso"),
                "Les noms de provider sont envoyés sans le suffixe « · serveur »");
            check(!summary.GetRawText().Contains("Secret") && !summary.GetRawText().Contains("Confidentiel"), "Aucun nom de projet ni de conversation n’est transmis");
            check(connection.Link.UsageSent == 2 && connection.Link.UsageLastSentUtc != null && sync.Snapshot is { ShareUsage: true, UsageSent: 2 }, "Le client compte ce qu’il a envoyé");
            await sync.PushUsageAsync();
            check((await Mine()).GetProperty("totals").GetProperty("calls").GetInt32() == 2 && connection.Link.UsageSent == 2, "Un second envoi sans nouveauté n’envoie rien");
            using (var db = new HarnessDb(clientDb))
            {
                db.TokenUsages.Add(new TokenUsage { StartedUtc = DateTime.UtcNow, CompletedUtc = DateTime.UtcNow, ProviderName = "Perso", Model = "model-b", InputTokens = 50, OutputTokens = 25, Seconds = 1 });
                db.SaveChanges();
            }
            await sync.PushUsageAsync();
            check((await Mine()).GetProperty("totals").GetProperty("calls").GetInt32() == 3 && connection.Link.UsageSent == 3, "Les nouveaux appels partent à l’envoi suivant");
            var viaServer = (await server.Get("/api/usage/summary?scope=all&period=7d", admin)).Body;
            check(viaServer.GetProperty("users").EnumerateArray().Any(x => x.GetProperty("email").GetString() == "dora@example.com"), "L’administrateur retrouve ces mesures dans la vue par utilisateur");

            sync.SetShareUsage(false);
            using (var db = new HarnessDb(clientDb))
            {
                db.TokenUsages.Add(new TokenUsage { StartedUtc = DateTime.UtcNow, CompletedUtc = DateTime.UtcNow, ProviderName = "Perso", Model = "model-b", InputTokens = 1, OutputTokens = 1, Seconds = 1 });
                db.SaveChanges();
            }
            await sync.PushUsageAsync();
            check((await Mine()).GetProperty("totals").GetProperty("calls").GetInt32() == 3, "Le partage désactivé n’envoie plus rien");
            sync.SetShareUsage(true);
            var deleted = await sync.EraseSharedUsageAsync();
            check(deleted == 3 && (await Mine()).GetProperty("totals").GetProperty("calls").GetInt32() == 0 && connection.Link.UsageSent == 0, "L’utilisateur efface ses mesures du serveur");
            await sync.PushUsageAsync();
            check((await Mine()).GetProperty("totals").GetProperty("calls").GetInt32() == 0, "Ce qui a été effacé n’est pas renvoyé");
            using (var db = new HarnessDb(clientDb))
            {
                db.TokenUsages.Add(new TokenUsage { StartedUtc = DateTime.UtcNow, CompletedUtc = DateTime.UtcNow, ProviderName = "Perso", Model = "model-b", InputTokens = 4, OutputTokens = 4, Seconds = 1 });
                db.SaveChanges();
            }
            await sync.PushUsageAsync();
            check((await Mine()).GetProperty("totals").GetProperty("calls").GetInt32() == 1, "…mais le partage reprend avec les appels suivants");

            await server.Put("/api/admin/features/usage", new { enabled = false }, admin);
            using (var db = new HarnessDb(clientDb))
            {
                db.TokenUsages.Add(new TokenUsage { StartedUtc = DateTime.UtcNow, CompletedUtc = DateTime.UtcNow, ProviderName = "Perso", Model = "model-b", InputTokens = 1, OutputTokens = 1, Seconds = 1 });
                db.SaveChanges();
            }
            await sync.PushUsageAsync();
            check(sync.Snapshot.Issues.Any(x => x.Key == "usage" && x.Level == SyncLevel.Info), "Si le serveur n’accepte pas les statistiques, le client le dit sans erreur bruyante");
            check(Count() > 0, "Les données locales restent intactes");
        }
        finally
        {
            PortableStorage.UseDatabase(original);
            try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
