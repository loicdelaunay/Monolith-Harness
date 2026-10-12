using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
using MonolithHarness.Core.Hosting;
using MonolithHarness.Core.Remote;

/// <summary>The client side of the server integration, driven through the shared engine host (what the CLI uses) against a real server.</summary>
static class ClientChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        // ---- Address policy ----
        check(ServerUrl.Parse("monolith.example:5800", false).ToString() == "https://monolith.example:5800/", "Une adresse sans schéma est prise en HTTPS");
        check(ServerUrl.Parse("https://monolith.example/ignored?x=1", false).ToString() == "https://monolith.example/", "Chemin et paramètres ignorés");
        foreach (var rejected in new[] { "http://monolith.example:5800", "http://192.168.1.20", "ftp://x", "https://user:pw@host", "" })
        {
            try { ServerUrl.Parse(rejected, false); check(false, "Adresse refusée: " + rejected); }
            catch (ServerException) { check(true, "Adresse refusée: " + (rejected.Length == 0 ? "(vide)" : rejected)); }
        }
        check(ServerUrl.Parse("http://192.168.1.20:5800", true).Scheme == "http", "HTTP accepté si l’option « Accepter HTTP » est activée");
        check(ServerUrl.Parse("http://localhost:5800", false).Scheme == "http" && ServerUrl.Parse("http://127.0.0.1:5800", false).Scheme == "http", "La boucle locale n’exige pas HTTPS");

        await using var upstream = await FakeProvider.StartAsync();
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        await server.EnableAsync(admin, "cloud");
        await server.SetAsync(admin, "execution.requireContainerSandbox", false);
        var userRole = await server.RoleId("User", admin);
        async Task<Guid> Provider(string name, bool local)
        {
            var id = (await server.Post("/api/admin/providers", new { name, baseUrl = upstream.BaseUrl, secret = "sk-upstream-secret", allowLocalUse = local, models = new[] { new { id = "fake-model", contextLimit = 32000 } } }, admin)).Body.GetProperty("id").GetGuid();
            await server.Put($"/api/admin/providers/{id}/access", new { entries = new[] { new { kind = "role", id = userRole, name = "" } } }, admin);
            return id;
        }
        var sharedProvider = await Provider("Fake", true);
        var cloudProvider = await Provider("Cloud", false);
        var (_, corToken) = await server.NewUserAsync(admin, "cora@example.com");
        var (_, vicToken) = await server.NewUserAsync(admin, "vic@example.com", "Guest");

        upstream.Handler = body =>
        {
            var messages = body["messages"] as JsonArray ?? [];
            var lastUser = messages.ToList().FindLastIndex(m => m?["role"]?.GetValue<string>() == "user");
            var last = lastUser < 0 ? "" : messages[lastUser]?["content"]?.ToString() ?? "";
            // Only tool results of the current turn count: earlier turns of the same conversation are in the history too.
            var toolResult = messages.Skip(Math.Max(lastUser, 0)).Any(m => m?["role"]?.GetValue<string>() == "tool");
            if (last.Contains("slow")) return new FakeProvider.Reply("trop tard", DelayMs: 60_000);
            if (!toolResult && last.Contains("patch")) return FakeProvider.Reply.Tool("patch_sources", new { dry_run = false, edits = new[] { new { path = last.Contains("Refais") ? "refus.txt" : "client.txt", old_text = (string?)null, new_text = "Écrit depuis le client" } } });
            if (!toolResult && last.Contains("question")) return FakeProvider.Reply.Tool("question", new { questions = new[] { new { question = "Quelle couleur ?", options = new[] { new { label = "Rouge", description = "r" }, new { label = "Bleu", description = "b" } }, multiple = false, custom = false } } });
            return FakeProvider.Reply.Text(toolResult ? "Terminé côté serveur." : "Réponse du provider.");
        };

        // ---- The client (engine host, its own portable folder) ----
        var clientFolder = Path.Combine(Path.GetTempPath(), "mh-client-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(clientFolder);
        var clientDb = Path.Combine(clientFolder, "database.sqlite");
        var originalDatabase = Path.Combine(PortableStorage.Root, "database.sqlite");
        PortableStorage.UseDatabase(clientDb);
        var events = new List<JsonObject>();
        var permissions = new List<JsonObject>();
        string permissionAnswer = "allow";
        HarnessService NewService() => new(clientDb, (method, parameters, _) =>
        {
            if (method == "permission") { lock (permissions) permissions.Add((JsonObject)parameters.DeepClone()); return Task.FromResult<JsonNode?>(JsonValue.Create(permissionAnswer)); }
            throw new NotSupportedException(method);
        }, value =>
        {
            lock (events) events.Add(JsonNode.Parse(JsonSerializer.Serialize(value, HarnessService.Json))!.AsObject());
            return Task.CompletedTask;
        });
        List<JsonObject> Events(Func<JsonObject, bool> match) { lock (events) return events.Where(match).ToList(); }
        async Task<bool> WaitFor(Func<bool> condition, int milliseconds = 30000) { for (var waited = 0; waited < milliseconds && !condition(); waited += 50) await Task.Delay(50); return condition(); }
        JsonNode? Node(object? value) => value == null ? null : JsonNode.Parse(JsonSerializer.Serialize(value, HarnessService.Json));
        try
        {
            var service = NewService();
            await service.Initialize();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            async Task<JsonNode?> Call(string method, JsonObject? p = null) => Node(await service.Dispatch(method, p ?? [], timeout.Token));
            async Task<string> Fails(string method, JsonObject p)
            {
                try { await service.Dispatch(method, p, timeout.Token); return ""; }
                catch (Exception ex) { return ex.Message; }
            }

            // Optional: a fresh client is not linked to anything and stays local.
            var empty = await Call("server.status");
            check(empty!["status"]!["configured"]!.GetValue<bool>() == false && (await Call("snapshot"))!["server"]!["configured"]!.GetValue<bool>() == false, "Sans serveur configuré, le client reste entièrement local");

            // ---- Connection ----
            check((await Fails("server.connect", new() { ["url"] = "http://monolith.example:5800" })).Contains("HTTP"), "Un serveur distant en HTTP simple est refusé par défaut");
            var connected = await Call("server.connect", new() { ["url"] = server.Base });
            check(connected!["name"]!.GetValue<string>() == "Monolith Harness Server" && connected["status"]!["status"]!["reachable"]!.GetValue<bool>(), "Connexion au serveur: identité et version lues");
            check((await Fails("server.login", new() { ["email"] = "cora@example.com", ["password"] = "mauvais-mot-de-passe" })).Length > 0, "Mauvais mot de passe refusé");
            var login = await Call("server.login", new() { ["email"] = "cora@example.com", ["password"] = "password-1234" });
            check(login!["status"]!["status"]!["signedIn"]!.GetValue<bool>() && login["passwordChangeSuggested"]!.GetValue<bool>(), "Connexion réussie; le changement du mot de passe initial est proposé");
            var linkFile = File.ReadAllText(Path.Combine(clientFolder, "server-link.json"));
            check(!linkFile.Contains("mhs_") && !linkFile.Contains("password-1234") && linkFile.Contains("protectedToken"), "Le jeton est protégé dans le fichier de liaison; le mot de passe n’est jamais conservé");

            // ---- Shared providers become local providers ----
            Dictionary<int, Provider> Providers()
            {
                using var db = new HarnessDb(clientDb);
                return db.Providers.AsNoTracking().ToList().ToDictionary(x => x.Id);
            }
            var synced = Providers().Values.Where(x => x.Name.EndsWith(" · serveur") || x.Name.EndsWith(" · cloud")).ToList();
            check(synced.Count == 2 && synced.Any(x => x.Name == "Fake · serveur") && synced.Any(x => x.Name == "Cloud · cloud"), "Le catalogue du serveur devient des providers locaux (usage local / cloud seulement)");
            var local = synced.First(x => x.Name == "Fake · serveur");
            check(local.BaseUrl == $"{server.Base}/api/providers/{sharedProvider}/relay" && local.Model == "fake-model" && ProviderModels.Visible(local).Contains("fake-model"), "Leur adresse est le relais du serveur, avec les modèles autorisés");
            check(!KeyVault.Decrypt(local.ProtectedKey).Contains("sk-upstream-secret") && KeyVault.Decrypt(local.ProtectedKey).StartsWith("mhs_"), "La clé du provider reste sur le serveur: le client ne détient que son jeton");

            // ---- Local run through a shared provider ----
            int localChat, localProject;
            using (var db = new HarnessDb(clientDb)) { localChat = db.Chats.AsNoTracking().OrderBy(x => x.Id).First().Id; localProject = db.Chats.AsNoTracking().First(x => x.Id == localChat).ProjectId; }
            await service.Dispatch("send", new JsonObject { ["chatId"] = localChat, ["providerId"] = local.Id, ["text"] = "Bonjour" }, timeout.Token);
            using (var db = new HarnessDb(clientDb))
                check(db.Messages.AsNoTracking().Any(x => x.ChatId == localChat && x.Role == "assistant" && x.Content == "Réponse du provider."), "Exécution locale avec un provider partagé: la réponse passe par le relais du serveur");
            check(upstream.Requests.Last().Authorization == "Bearer sk-upstream-secret", "…qui ajoute la clé amont");
            var cloudOnly = synced.First(x => x.Name == "Cloud · cloud");
            var cloudOnlyFailure = await Fails("send", new() { ["chatId"] = localChat, ["providerId"] = cloudOnly.Id, ["text"] = "Bonjour" });
            check(cloudOnlyFailure.Length > 0, "Un provider « cloud seulement » n’est pas utilisable en exécution locale");

            // ---- Cloud project ----
            var created = await Call("server.project.create", new() { ["name"] = "Projet cloud client" });
            var cloudProjectId = created!["id"]!.GetValue<int>();
            var snapshot = await Call("snapshot");
            check(snapshot!["server"]!["cloudProjects"]![cloudProjectId.ToString()] != null && snapshot["server"]!["signedIn"]!.GetValue<bool>(), "Le projet cloud est créé sur le serveur et lié localement");
            var serverProjects = (await server.Get("/api/projects", corToken)).Body;
            check(serverProjects.GetArrayLength() == 1 && serverProjects[0].GetProperty("name").GetString() == "Projet cloud client", "…et il existe bien côté serveur");
            var cloudChat = (await Call("chat.save", new() { ["projectId"] = cloudProjectId, ["title"] = "Conversation cloud" }))!["id"]!.GetValue<int>();

            lock (events) events.Clear();
            await service.Dispatch("send", new JsonObject { ["chatId"] = cloudChat, ["providerId"] = local.Id, ["text"] = "Fais un patch du fichier" }, timeout.Token);
            check(Events(x => x["event"]?.ToString() == "started").Count == 1 && Events(x => x["event"]?.ToString() == "done" && x["error"]?.ToString() == "").Count == 1, "Une requête sur un projet cloud s’exécute sur le serveur (événements started/done)");
            check(Events(x => x["event"]?.ToString() == "status" && x["text"]!.ToString().Contains("serveur")).Count > 0, "L’état de la file et de l’exécution est annoncé");
            check(permissions.Count == 1 && permissions[0]["title"]!.ToString().Contains("Patch") && !permissions[0]["oneTimeOnly"]!.GetValue<bool>(), "L’approbation de l’outil arrive dans l’application, comme en local");
            check(Events(x => x["event"]?.ToString() == "stream").Count > 0 && Events(x => x["event"]?.ToString() == "stream").All(x => x["chatId"]!.GetValue<int>() == cloudChat), "Le streaming est rejoué avec les identifiants locaux");
            using (var db = new HarnessDb(clientDb))
            {
                var mirrored = db.Messages.AsNoTracking().Where(x => x.ChatId == cloudChat).OrderBy(x => x.Id).ToList();
                check(mirrored.Any(x => x.Role == "user" && x.Content == "Fais un patch du fichier") && mirrored.Any(x => x.Role == "assistant" && x.Content == "Terminé côté serveur.") && mirrored.All(x => ServerSync.CloudId(x) != null),
                    "La conversation est reflétée dans la base locale (le serveur reste la référence)");
                check(mirrored.Count(x => x.Role == "user") == 1, "Le message envoyé n’est pas dupliqué par la synchronisation");
                check(mirrored.OrderBy(x => x.Id).First().Role == "user", "L’ordre des messages est conservé");
            }
            var serverFile = Directory.EnumerateFiles(server.Paths.Projects, "client.txt", SearchOption.AllDirectories).FirstOrDefault();
            check(serverFile != null && File.ReadAllText(serverFile).Contains("Écrit depuis le client"), "L’outil a travaillé dans le workspace du projet, sur le serveur");
            check(!Directory.Exists(Path.Combine(clientFolder, "workspace", "projects")) || !Directory.EnumerateFiles(clientFolder, "client.txt", SearchOption.AllDirectories).Any(), "Rien n’a été écrit sur l’appareil");

            // Denial
            permissionAnswer = "deny"; lock (events) events.Clear();
            await service.Dispatch("send", new JsonObject { ["chatId"] = cloudChat, ["providerId"] = local.Id, ["text"] = "Refais un patch" }, timeout.Token);
            if (!(Events(x => x["event"]?.ToString() == "done").Count == 1 && permissions.Count == 2))
                Console.WriteLine($"  refus: done={Events(x => x["event"]?.ToString() == "done").Count} permissions={permissions.Count} events={string.Join(",", Events(_ => true).Select(x => x["event"]))} err={Events(x => x["event"]?.ToString() == "done").FirstOrDefault()?["error"]}");
            check(Events(x => x["event"]?.ToString() == "done").Count == 1 && permissions.Count == 2, "Un refus de l’utilisateur est transmis au serveur");
            permissionAnswer = "allow";

            // Agent question
            lock (events) events.Clear();
            var asking = service.Dispatch("send", new JsonObject { ["chatId"] = cloudChat, ["providerId"] = local.Id, ["text"] = "Pose-moi une question" }, timeout.Token);
            check(await WaitFor(() => Events(x => x["event"]?.ToString() == "question").Count == 1, 120000), "La question de l’agent arrive dans l’application");
            var question = Events(x => x["event"]?.ToString() == "question")[0];
            await service.Dispatch("question.answer", new JsonObject { ["id"] = question["id"]!.ToString(), ["chatId"] = cloudChat, ["cancelled"] = false, ["answers"] = new JsonArray(new JsonArray("Bleu")) }, timeout.Token);
            await asking;
            check(upstream.Requests.Last().Body!["messages"]!.AsArray().Last(m => m?["role"]?.GetValue<string>() == "tool")!["content"]!.ToString().Contains("Bleu") && Events(x => x["event"]?.ToString() == "question.closed").Count == 1, "La réponse est transmise à l’agent qui poursuit");

            // Stop
            lock (events) events.Clear();
            var slow = service.Dispatch("send", new JsonObject { ["chatId"] = cloudChat, ["providerId"] = local.Id, ["text"] = "slow" }, timeout.Token);
            for (var i = 0; i < 400 && !(await server.Get("/api/runs", corToken)).Body.EnumerateArray().Any(x => x.GetProperty("state").GetString() == "running"); i++) await Task.Delay(250);
            await Task.Delay(1500);
            await service.Dispatch("stop", new JsonObject { ["chatId"] = cloudChat }, timeout.Token);
            await slow;
            var stopped = Events(x => x["event"]?.ToString() == "done")[0];
            check(stopped["error"]!.ToString().Contains("arrêtée") && (await server.Get("/api/runs", corToken)).Body.EnumerateArray().Any(x => x.GetProperty("state").GetString() == "cancelled"), "« Stop » annule la requête sur le serveur");

            // Rules
            check((await Fails("send", new() { ["chatId"] = cloudChat, ["providerId"] = Providers().Values.First(x => !x.Name.Contains("·")).Id, ["text"] = "x" })).Contains("provider du serveur"), "Un projet cloud exige un provider du serveur");
            check((await Fails("send", new() { ["chatId"] = cloudChat, ["providerId"] = local.Id, ["text"] = "x", ["images"] = new JsonArray(new JsonObject()) })).Contains("images"), "Les images ne sont pas prises en charge dans un projet cloud");

            // ---- Opening a project that already exists on the server ----
            var remoteProject = (await server.Post("/api/projects", new { name = "Créé depuis le web" }, corToken)).Body.GetProperty("id").GetGuid();
            var remoteChat = (await server.Post($"/api/projects/{remoteProject}/chats", new { title = "Discussion web" }, corToken)).Body.GetProperty("id").GetInt32();
            var listed = await Call("server.projects");
            check(listed!.AsArray().Count == 2 && listed.AsArray().Any(x => x!["name"]!.ToString() == "Créé depuis le web" && x["localProjectId"] == null), "Les projets du serveur sont listés");
            var opened = await Call("server.project.open", new() { ["id"] = "Créé depuis le web" });
            int openedChat;
            using (var db = new HarnessDb(clientDb)) openedChat = db.Chats.AsNoTracking().Single(x => x.ProjectId == opened!["id"]!.GetValue<int>() && x.Title == "Discussion web").Id;
            check(openedChat > 0, "Ouvrir un projet du serveur le reflète localement, conversations comprises");
            check((await Call("server.project.open", new() { ["id"] = remoteProject.ToString() }))!["id"]!.GetValue<int>() == opened!["id"]!.GetValue<int>(), "Ouvrir deux fois ne crée pas de doublon");
            _ = remoteChat;

            // ---- Local shared conversation ----
            var started = await Call("server.share.start", new() { ["chatId"] = localChat, ["title"] = "Ma session locale" });
            var shareId = started!["shareId"]!.GetValue<Guid>();
            check((await server.Get($"/api/shares/{shareId}", corToken)).Body.GetProperty("live").GetBoolean(), "Le partage est déclaré et le bridge de l’appareil est attaché");
            await Call("server.share.invite", new() { ["chatId"] = localChat, ["email"] = "vic@example.com" });
            await using var viewer = await server.ConnectAsync(vicToken);
            await viewer.SendAsync(new { type = "subscribe", topic = $"share:{shareId}" });
            var snap = await viewer.WaitEventAsync($"share:{shareId}", "snapshot");
            check(snap?.GetProperty("data").GetProperty("messages").GetArrayLength() >= 2, "Un spectateur reçoit l’état courant de la conversation locale");
            await service.Dispatch("send", new JsonObject { ["chatId"] = localChat, ["providerId"] = local.Id, ["text"] = "Une demande en direct" }, timeout.Token);
            var live = await viewer.WaitAsync(x => WsClient.Str(x, "name") == "message" && x.GetProperty("data").TryGetProperty("message", out var m) && m.GetProperty("content").GetString() == "Réponse du provider." && m.GetProperty("role").GetString() == "assistant");
            check(live != null && !live.Value.ToString().Contains("\"html\""), "Les événements de la conversation locale sont transmis en direct (sans HTML ni pièces jointes)");
            await viewer.SendAsync(new { type = "share.comment", shareId, text = "Bravo !" });
            check(await WaitFor(() => Events(x => x["event"]?.ToString() == "share.comment" && x["text"]!.ToString() == "Bravo !" && x["author"]!.ToString() == "vic@example.com").Count == 1, 10000), "Le commentaire d’un spectateur revient à l’hôte, rien de plus");
            check((await server.Get($"/api/shares/{shareId}", corToken)).Body.GetProperty("participants").GetArrayLength() == 1, "L’invitation est enregistrée côté serveur");

            // The code to hand out, public conversations, joining and reading from a terminal
            var code = started["code"]!.GetValue<string>();
            check(code.Length == 11 && !started["isPublic"]!.GetValue<bool>(), "Démarrer un partage donne un code à transmettre; privé par défaut");
            check((await Call("server.share.info", new() { ["chatId"] = localChat }))!["code"]!.GetValue<string>() == code, "L’hôte relit son code");
            var renewed = await Call("server.share.code", new() { ["chatId"] = localChat, ["action"] = "new" });
            check(renewed!["code"]!.GetValue<string>() != code, "L’hôte génère un nouveau code");
            var currentCode = renewed["code"]!.GetValue<string>();
            var content = await Call("server.share.join", new() { ["code"] = currentCode.ToLowerInvariant() });
            check(content!["messages"]!.AsArray().Count >= 2 && content["share"]!["title"]!.GetValue<string>() == "Ma session locale", "Un code donne le contenu de la conversation partagée, lisible depuis un terminal");
            await Call("server.share.public", new() { ["chatId"] = localChat, ["isPublic"] = true });
            check((await Call("server.shares.public"))!.AsArray().Count == 0 && (await server.Get("/api/shares/public", vicToken)).Body.GetArrayLength() == 1, "Une conversation publique est listée pour les autres comptes (pas pour son hôte)");
            await Call("server.share.public", new() { ["chatId"] = localChat, ["isPublic"] = false });
            await Call("server.share.code", new() { ["chatId"] = localChat, ["action"] = "off" });
            check((await Fails("server.share.join", new() { ["code"] = currentCode })).Length > 0, "Un code désactivé est refusé");

            // Automatic synchronization of the engine host
            var syncState = await Call("server.sync.start");
            check(syncState!["state"]!.GetValue<string>() is "Connecting" or "Online", "Le moteur démarre la synchronisation automatique du lien");
            check(await WaitFor(() => Events(x => x["event"]?.ToString() == "server.state" && x["state"]?["state"]?.ToString() == "Online").Count > 0, 20000), "L’état de la connexion est annoncé à l’interface (événement server.state)");
            var interval = await Call("server.sync.interval", new() { ["minutes"] = 60 });
            check(interval!["minutes"]!.GetValue<int>() == 60 && (await Call("server.sync.status"))!["minutes"]!.GetValue<int>() == 60, "La fréquence de resynchronisation se règle et se mémorise");
            await Call("server.sync.interval", new() { ["minutes"] = 5 });
            var manual = await Call("server.sync.now");
            check(manual!["state"]!["lastSyncUtc"] != null && manual["state"]!["issues"]!.AsArray().All(x => x!["level"]!.ToString() != "Error"), "Synchroniser maintenant réussit sans erreur");
            var remoteName = "Créé ailleurs " + Guid.NewGuid().ToString("N")[..6];
            await server.Post("/api/projects", new { name = remoteName }, corToken);
            check(await WaitFor(() => Events(x => x["event"]?.ToString() == "server.changed").Count > 0, 20000) && (await Call("snapshot"))!["projects"]!.AsArray().Any(x => x!["name"]!.ToString() == remoteName), "Un projet cloud créé ailleurs apparaît dans l’espace de travail sans rien demander");

            // Restart of the application: the bridge attaches again by itself
            await service.DisposeAsync();
            check(await viewer.WaitAsync(x => WsClient.Str(x, "name") == "bridge" && !x.GetProperty("data").GetProperty("online").GetBoolean()) != null, "Fermer l’application met le partage hors ligne");
            var offlineAt = viewer.Seen.Count;
            service = NewService();
            await service.Initialize();
            var back = await WaitFor(() => viewer.Seen.Skip(offlineAt).Any(x => WsClient.Str(x, "name") == "bridge" && x.GetProperty("data").GetProperty("online").GetBoolean() && x.GetProperty("topic").GetString() == $"share:{shareId}"), 30000) ? (JsonElement?)default(JsonElement) : null;
            check(back != null, "Au redémarrage, la conversation encore partagée se rattache toute seule");
            await Call("server.share.stop", new() { ["chatId"] = localChat });
            check(await viewer.WaitEventAsync($"share:{shareId}", "closed") != null && (await server.Get($"/api/shares/{shareId}", corToken)).Body.GetProperty("revokedUtc").ValueKind == JsonValueKind.String, "L’hôte met fin au partage: les spectateurs sont déconnectés");

            // ---- Sign-out and lost sessions ----
            var token = KeyVault.Decrypt(Providers().Values.First(x => x.Name == "Fake · serveur").ProtectedKey);
            await Call("server.logout");
            check(Providers().Values.All(x => !x.Name.EndsWith(" · serveur") && !x.Name.EndsWith(" · cloud")), "La déconnexion retire les providers du serveur de la liste locale");
            check((await server.Get("/api/auth/me", token)).Status == 401, "…et révoque la connexion côté serveur");
            check(!(await Call("server.status"))!["status"]!["signedIn"]!.GetValue<bool>() && (await Call("snapshot"))!["server"]!["cloudProjects"]![cloudProjectId.ToString()] != null, "Les projets cloud restent liés pour une prochaine connexion");
            await Call("server.login", new() { ["email"] = "cora@example.com", ["password"] = "password-1234" });
            var current = KeyVault.Decrypt(Providers().Values.First(x => x.Name == "Fake · serveur").ProtectedKey);
            await server.Post("/api/auth/logout", null, current);
            var lost = await Call("server.status");
            check(!lost!["status"]!["signedIn"]!.GetValue<bool>() && lost["status"]!["errorCode"]!.ToString() == "session_lost", "Une session révoquée côté serveur est détectée (le client demande de se reconnecter)");

            var removedLinks = await Call("server.disconnect");
            check(!removedLinks!["status"]!["configured"]!.GetValue<bool>(), "« Déconnecter » oublie complètement le serveur");
            await service.DisposeAsync();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            PortableStorage.UseDatabase(originalDatabase);
            try { Directory.Delete(clientFolder, true); } catch (IOException) { }
        }
    }
}
