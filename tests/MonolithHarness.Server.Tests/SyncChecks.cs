using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
using MonolithHarness.Core.Remote;

/// <summary>The automatic synchronization of a client with its server: live changes, periodic resync setting, link state and problems.</summary>
static class SyncChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var upstream = await FakeProvider.StartAsync();
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        await server.EnableAsync(admin, "cloud");
        await server.SetAsync(admin, "execution.requireContainerSandbox", false);
        var userRole = await server.RoleId("User", admin);
        async Task<Guid> Provider(string name, bool local = true)
        {
            var id = (await server.Post("/api/admin/providers", new { name, baseUrl = upstream.BaseUrl, secret = "sk-secret", allowLocalUse = local, models = new[] { new { id = "fake-model", contextLimit = 32000 } } }, admin)).Body.GetProperty("id").GetGuid();
            await server.Put($"/api/admin/providers/{id}/access", new { entries = new[] { new { kind = "role", id = userRole, name = "" } } }, admin);
            return id;
        }
        await Provider("Fake");
        await Provider("Cloud", local: false);
        var (_, olga) = await server.NewUserAsync(admin, "olga@example.com");
        var (coraId, _) = await server.NewUserAsync(admin, "cora@example.com");

        var folder = Path.Combine(Path.GetTempPath(), "mh-sync-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var clientDb = Path.Combine(folder, "database.sqlite");
        var original = Path.Combine(PortableStorage.Root, "database.sqlite");
        PortableStorage.UseDatabase(clientDb);
        try
        {
            await using (var init = new HarnessDb(clientDb)) await init.InitializeAsync();
            await using var connection = new ServerConnection(new ServerLinkStore(ServerLinkStore.PathFor(clientDb)), KeyVault.Encrypt, KeyVault.Decrypt);
            await connection.ConnectAsync(server.Base, false);
            await connection.LoginAsync("cora@example.com", "password-1234", "sync test");
            await using var sync = new ServerSyncService(connection, () => new HarnessDb(clientDb));
            var changes = new List<SyncChanges>();
            sync.Changed += change => { lock (changes) changes.Add(change); return Task.CompletedTask; };
            async Task<bool> WaitFor(Func<bool> condition, int milliseconds = 15000) { for (var waited = 0; waited < milliseconds && !condition(); waited += 50) await Task.Delay(50); return condition(); }
            List<Project> Projects() { using var db = new HarnessDb(clientDb); return db.Projects.AsNoTracking().Where(x => x.Icon == "cloud").ToList(); }
            List<Chat> Chats() { using var db = new HarnessDb(clientDb); return db.Chats.AsNoTracking().ToList(); }
            List<Provider> Providers() { using var db = new HarnessDb(clientDb); return db.Providers.AsNoTracking().ToList(); }
            int MessageCount(int chat) { using var db = new HarnessDb(clientDb); return db.Messages.Count(x => x.ChatId == chat); }
            List<string> Levels() => sync.Snapshot.Issues.Select(x => x.Level.ToString()).ToList();

            // ---- Before anything starts ----
            check(sync.Snapshot.State == LinkState.Connecting && sync.Minutes == 5, "Avant le démarrage: état « connexion en cours » et resynchronisation complète toutes les 5 minutes par défaut");

            // ---- First synchronization ----
            sync.Start();
            check(await WaitFor(() => sync.Snapshot.State == LinkState.Online && sync.Snapshot.LastSyncUtc != null), "Le service se connecte et termine une première synchronisation complète");
            check(Providers().Count(x => x.Name.EndsWith(" · serveur") || x.Name.EndsWith(" · cloud")) == 2 && sync.Snapshot.Providers == 2, "Les providers du serveur sont reçus sans rien demander");
            check(sync.Snapshot.Realtime && sync.Snapshot.ServerName == "Monolith Harness Server" && sync.Snapshot.Email == "cora@example.com", "L’état indique le canal temps réel, le serveur et le compte");
            check(sync.Snapshot.Issues.All(x => x.Level == SyncLevel.Info) && sync.Snapshot.Worst is null or SyncLevel.Info, "Aucun problème, hormis la remarque sur le mot de passe temporaire");
            check(sync.Snapshot.NextSyncUtc is { } next && next > DateTime.UtcNow.AddMinutes(4) && next < DateTime.UtcNow.AddMinutes(6), "La prochaine synchronisation complète est annoncée dans 5 minutes");

            // ---- Providers follow the catalog ----
            var added = await Provider("Nouveau");
            check(await WaitFor(() => Providers().Any(x => x.Name == "Nouveau · serveur")), "Un provider ajouté sur le serveur apparaît tout seul");
            await server.Put($"/api/admin/providers/{added}/access", new { entries = Array.Empty<object>() }, admin);
            check(await WaitFor(() => Providers().All(x => x.Name != "Nouveau · serveur")), "Un provider dont l’accès est retiré disparaît tout seul");

            // ---- A project shared by another account ----
            var project = (await server.Post("/api/projects", new { name = "Équipe" }, olga)).Body.GetProperty("id").GetGuid();
            await Task.Delay(300);
            check(Projects().Count == 0, "Un projet dont on n’est pas membre n’apparaît pas");
            await server.Put($"/api/projects/{project}/members", new { members = new[] { new { email = "cora@example.com", access = "run" } } }, olga);
            check(await WaitFor(() => Projects().Any(x => x.Name == "Équipe")), "Être ajouté à un projet cloud le fait apparaître automatiquement");
            check(connection.Link.Projects.Values.Any(x => x.ServerProjectId == project.ToString() && x.Access == "run") && sync.Snapshot.Projects == 1, "…avec le droit accordé");
            check(await WaitFor(() => { lock (changes) return changes.Any(x => x.Projects.Count > 0); }), "L’interface est prévenue du changement (pour rafraîchir la liste)");
            var mirror = Projects().Single(x => x.Name == "Équipe");

            // ---- Conversations and messages ----
            var chat = (await server.Post($"/api/projects/{project}/chats", new { title = "Plan" }, olga)).Body.GetProperty("id").GetInt32();
            check(await WaitFor(() => Chats().Any(x => x.ProjectId == mirror.Id && x.Title == "Plan")), "Une conversation créée sur le serveur apparaît automatiquement");
            var local = Chats().Single(x => x.ProjectId == mirror.Id && x.Title == "Plan");
            var remoteRuns = new List<ServerRun>();
            var progress = new List<RemoteProgress>();
            var pulled = new List<SyncChanges>();
            sync.RemoteRun += run => { lock (remoteRuns) remoteRuns.Add(run); return Task.CompletedTask; };
            sync.RemoteRunProgress += step => { lock (progress) progress.Add(step); return Task.CompletedTask; };
            sync.Changed += change => { lock (pulled) pulled.Add(change); return Task.CompletedTask; };
            // A slow first token keeps the request running long enough to be followed.
            upstream.Handler = _ => new FakeProvider.Reply("Réponse du provider suivie en direct.", null, null, 200, 1500);
            var send = await server.Post($"/api/projects/{project}/chats/{chat}/requests", new { text = "Bonjour", providerId = (await server.Get("/api/providers", olga)).Body.EnumerateArray().First(x => x.GetProperty("name").GetString() == "Fake").GetProperty("id").GetGuid(), model = "fake-model" }, olga);
            check(send.Status == 202, "L’autre compte lance une requête dans le projet");
            check(await WaitFor(() => MessageCount(local.Id) >= 2, 60000), "Les messages de la conversation arrivent sans ouvrir la conversation");
            check(await WaitFor(() => { lock (progress) return progress.Any(x => x.Finished); }, 60000), "La fin de la requête d’un autre membre est annoncée au client");
            await using (var db = new HarnessDb(clientDb))
                check(await db.Messages.AnyAsync(x => x.ChatId == local.Id && x.Role == "assistant" && x.Content == "Réponse du provider suivie en direct."), "…y compris la réponse du modèle");
            lock (progress)
            {
                check(progress.Count > 0 && progress.All(x => x.LocalChatId == local.Id && x.Requester == "olga@example.com") && progress.Any(x => x.State == "running" && !x.Finished), "Une requête lancée par un autre membre est suivie dans la conversation locale (son auteur et son état sont annoncés)");
                check(progress.Any(x => x.Text.Length > 0 && !x.Finished), "Le texte en cours d’écriture arrive au client pendant la requête");
                check(progress.Count(x => x.Finished) == 1 && progress.Last().Finished, "La fin de la requête est annoncée une seule fois");
            }
            lock (pulled) check(pulled.Count(x => x.Messages.Contains(local.Id)) >= 2, "Les messages sont relus pendant la requête, pas seulement à la fin");
            lock (remoteRuns) check(remoteRuns.Any(x => x.State == "completed" && x.Requester == "olga@example.com" && x.RequesterId != null) && remoteRuns.Any(x => x.State is "queued" or "running"), "Les requêtes lancées par un autre membre sont annoncées au client pendant qu’elles tournent (pour les suivre en direct)");
            await using (var db = new HarnessDb(clientDb))
            {
                var typed = await db.Messages.FirstAsync(x => x.ChatId == local.Id && x.Role == "user");
                check(ServerSync.AuthorOf(typed) is { Email: "olga@example.com" } who && who.DisplayName == "olga@example.com" && who.Id != Guid.Empty, "Le message d’un autre membre garde son auteur dans la copie locale (pour afficher son avatar)");
                check(ServerSync.AuthorOf(await db.Messages.FirstAsync(x => x.ChatId == local.Id && x.Role == "assistant")) == null, "Les réponses du modèle n’ont pas d’auteur");
            }
            check(ServerSync.CurrentAuthor(connection.Link) is { Email: "cora@example.com" } me && me.Id != Guid.Empty, "Le compte connecté sait se présenter comme auteur de ses propres messages");
            var settled = MessageCount(local.Id);
            await sync.SyncNowAsync();
            check(MessageCount(local.Id) == settled, "Une resynchronisation n’ajoute pas de doublons");

            // ---- A rename on the server comes through, a rename here is not undone ----
            await server.SendAsync(HttpMethod.Patch, $"/api/projects/{project}", new { name = "Équipe produit" }, olga);
            check(await WaitFor(() => Projects().Any(x => x.Name == "Équipe produit")), "Un projet renommé sur le serveur est renommé ici");
            await using (var db = new HarnessDb(clientDb)) { var edited = await db.Chats.SingleAsync(x => x.Id == local.Id); edited.Title = "Mon titre"; await db.SaveChangesAsync(); }
            await sync.SyncNowAsync();
            check(Chats().Single(x => x.Id == local.Id).Title == "Mon titre", "Un titre modifié ici n’est pas écrasé par la synchronisation");
            check((await server.Get($"/api/projects/{project}/chats", olga)).Body.EnumerateArray().Any(x => x.GetProperty("id").GetInt32() == chat && x.GetProperty("title").GetString() == "Mon titre"), "…il est envoyé au serveur: les autres membres le voient aussi");
            await server.SendAsync(HttpMethod.Patch, $"/api/projects/{project}/chats/{chat}", new { title = "Titre d’Olga" }, olga);
            check(await WaitFor(() => Chats().Single(x => x.Id == local.Id).Title == "Titre d’Olga"), "Un renommage fait par un autre membre arrive tout seul");
            await using (var db = new HarnessDb(clientDb)) { var edited = await db.Chats.SingleAsync(x => x.Id == local.Id); edited.Title = "Renommé vite"; await db.SaveChangesAsync(); }
            sync.RequestPush(local.Id);
            var pushed = false;
            for (var waited = 0; waited < 8000 && !pushed; waited += 100) { pushed = (await server.Get($"/api/projects/{project}/chats", olga)).Body.EnumerateArray().Any(x => x.GetProperty("title").GetString() == "Renommé vite"); if (!pushed) await Task.Delay(100); }
            check(pushed, "Un renommage fait ici est envoyé presque tout de suite (sans attendre la resynchronisation)");

            // ---- A conversation the host is busy with is left alone ----
            sync.IsChatBusy = id => id == local.Id;
            await server.Delete($"/api/projects/{project}/chats/{chat}", olga);
            await Task.Delay(1500);
            await sync.SyncNowAsync();
            check(Chats().Any(x => x.Id == local.Id), "Une conversation en cours d’utilisation n’est pas retirée sous les pieds de l’utilisateur");
            sync.IsChatBusy = null;
            await sync.SyncNowAsync();
            check(Chats().All(x => x.Id != local.Id), "…puis elle disparaît dès qu’elle est libre");
            check(!connection.Link.Chats.ContainsKey(local.Id), "Son lien avec le serveur est oublié");

            // ---- Deleting from here deletes there ----
            var second = (await server.Post($"/api/projects/{project}/chats", new { title = "À supprimer" }, olga)).Body.GetProperty("id").GetInt32();
            await WaitFor(() => Chats().Any(x => x.Title == "À supprimer"));
            var doomed = Chats().Single(x => x.Title == "À supprimer");
            await ServerSync.DeleteChatOnServerAsync(connection, doomed.Id);
            check((await server.Get($"/api/projects/{project}/chats", olga)).Body.EnumerateArray().All(x => x.GetProperty("id").GetInt32() != second), "Supprimer une conversation cloud ici la supprime aussi sur le serveur");
            try { await ServerSync.DeleteProjectOnServerAsync(connection, mirror.Id); check(false, "Un membre ne supprime pas le projet"); }
            catch (ServerException ex) { check(ex.Code == "not_owner", "Seul le propriétaire supprime un projet cloud"); }

            // ---- The project disappears when it is deleted or access is withdrawn ----
            await server.Put($"/api/projects/{project}/members", new { members = Array.Empty<object>() }, olga);
            check(await WaitFor(() => Projects().All(x => x.Name != "Équipe produit")) && !connection.Link.Projects.ContainsKey(mirror.Id), "Un accès retiré fait disparaître la copie locale du projet");
            check(await WaitFor(() => { lock (changes) return changes.Any(x => x.RemovedProjects.Contains(mirror.Id)); }), "L’interface est prévenue de la disparition");

            // ---- Shared conversations: invitations, public ones, codes ----
            var share = await server.Post("/api/shares", new { title = "Revue de code", clientConversationId = "7" }, olga);
            var shareId = share.Body.GetProperty("id").GetGuid();
            var shareCode = share.Body.GetProperty("code").GetString()!;
            await Task.Delay(300);
            check(sync.SharedWithMe.Count == 0 && sync.PublicShares.Count == 0, "Un partage privé non destiné au compte n’apparaît pas");
            await server.SendAsync(HttpMethod.Patch, $"/api/shares/{shareId}", new { isPublic = true }, olga);
            check(await WaitFor(() => sync.PublicShares.Any(x => x.Id == shareId)), "Une conversation devenue publique apparaît dans la liste publique");
            var joined = await connection.Api.JoinShareAsync(shareCode.ToLowerInvariant());
            check(joined.Joined && joined.Id == shareId, "Le code de partage donne accès à la conversation");
            check(await WaitFor(() => sync.SharedWithMe.Any(x => x.Id == shareId) && sync.PublicShares.All(x => x.Id != shareId)), "Elle passe de « publique » à « partagée avec moi » une fois rejointe");
            await using var hostWs = await server.ConnectAsync(olga);
            await hostWs.SendAsync(new { type = "bridge.attach", shareId });
            await hostWs.WaitTypeAsync("bridge.attached");
            await hostWs.SendAsync(new { type = "bridge.snapshot", shareId, data = new { title = "Revue de code", messages = new[] { new { id = 1, role = "user", content = "Regarde ça" }, new { id = 2, role = "assistant", content = "C’est bon" } } } });
            using var viewer = await ShareViewer.OpenAsync(connection, sync.SharedWithMe.First(x => x.Id == shareId));
            check(await WaitFor(() => viewer.Online && viewer.Messages.Count == 2), "Le visionneur reçoit l’état courant de la conversation partagée");
            await hostWs.SendAsync(new { type = "bridge.event", shareId, name = "stream", data = new { messageId = 3, text = "En train d’écrire" } });
            check(await WaitFor(() => viewer.Messages.Any(x => x.Key == "3" && x.Streaming && x.Content == "En train d’écrire")), "…puis le texte qui arrive en direct");
            await hostWs.SendAsync(new { type = "bridge.event", shareId, name = "message", data = new { chatId = 1, message = new { id = 3, role = "assistant", content = "Terminé" } } });
            check(await WaitFor(() => viewer.Messages.Any(x => x.Key == "3" && !x.Streaming && x.Content == "Terminé")), "…remplacé par le message définitif, sans doublon");
            await hostWs.SendAsync(new { type = "bridge.event", shareId, name = "stream", data = new { chatId = 1, messageId = 4, text = "Je réponds", reasoning = "Je réfléchis d’abord" } });
            check(await WaitFor(() => viewer.Messages.Any(x => x.Key == "4" && x.Streaming && x.Reasoning == "Je réfléchis d’abord")), "La réflexion en cours arrive aussi aux spectateurs");
            await hostWs.SendAsync(new { type = "bridge.event", shareId, name = "tool", data = new { chatId = 1, id = "call_9", name = "lire_fichier", arguments = "{\"path\":\"a.txt\"}" } });
            check(await WaitFor(() => viewer.RunningTools.Any(x => x.Name == "lire_fichier")), "Un outil qui démarre chez l’hôte s’affiche comme en cours");
            await hostWs.SendAsync(new { type = "bridge.event", shareId, name = "message", data = new { chatId = 1, message = new { id = 4, role = "assistant", content = "", state = "complete", seconds = 1.5, reasoning = "Je réfléchis d’abord", toolCalls = new[] { new { id = "call_9", name = "lire_fichier", arguments = "{\"path\":\"a.txt\"}" } } } } });
            await hostWs.SendAsync(new { type = "bridge.event", shareId, name = "message", data = new { chatId = 1, message = new { id = 5, role = "tool", content = "lire_fichier\nContenu de a.txt", state = "complete", seconds = 0.2, cachedInputTokens = (int?)null, completedUtc = (DateTime?)null, toolCallId = "call_9" } } });
            check(await WaitFor(() => viewer.Messages.Any(x => x.Key == "5" && x.Role == "tool" && x.ToolCallId == "call_9") && viewer.RunningTools.Count == 0), "Le résultat de l’outil remplace la carte en cours");
            var assistantWithTool = viewer.Messages.First(x => x.Key == "4").ToMessage(1, 3);
            check(assistantWithTool.Content == "" && JsonNode.Parse(assistantWithTool.WireJson)!["tool_calls"]![0]!["function"]!["name"]!.ToString() == "lire_fichier" && JsonNode.Parse(assistantWithTool.WireJson)!["reasoning_content"]!.ToString() == "Je réfléchis d’abord" && assistantWithTool.Seconds == 1.5, "Le message reçu se redessine comme chez l’hôte (appels d’outils, réflexion, durée)");
            check(viewer.Messages.First(x => x.Key == "5").ToMessage(1, 4).WireJson.Contains("call_9"), "Le résultat d’outil garde le lien avec son appel");
            await hostWs.SendAsync(new { type = "bridge.event", shareId, name = "title", data = new { title = "Renommée par l’hôte" } });
            check(await WaitFor(() => viewer.Title == "Renommée par l’hôte"), "Un renommage chez l’hôte met à jour le titre du spectateur");
            check(await WaitFor(() => viewer.Viewers == 1), "Le spectateur sait combien de personnes suivent");
            await viewer.CommentAsync("Merci !");
            check(await hostWs.WaitAsync(x => WsClient.Str(x, "name") == "comment") != null && await WaitFor(() => viewer.Comments.Any(x => x.Text == "Merci !")), "Un commentaire du client atteint l’hôte et revient dans la conversation");
            await connection.Api.LeaveShareAsync(shareId);
            check(await WaitFor(() => sync.SharedWithMe.All(x => x.Id != shareId)), "Quitter une conversation la retire de la liste");
            await server.Post($"/api/shares/{shareId}/revoke", null, olga);

            // ---- What a host sends ----
            var transcript = new List<Message>
            {
                new() { Id = 1, ChatId = 1, Role = "user", Content = "Lis a.txt" },
                new() { Id = 2, ChatId = 1, Role = "assistant", Content = "", State = "complete", WireJson = """{"reasoning_content":"Je dois lire","tool_calls":[{"id":"c1","type":"function","function":{"name":"lire","arguments":"{}"}}],"content":"secret interne"}""" },
                new() { Id = 3, ChatId = 1, Role = "tool", Content = "lire\n" + new string('x', 9000), State = "complete", WireJson = """{"role":"tool","tool_call_id":"c1","content":"gros"}""" },
                new() { Id = 4, ChatId = 1, Role = "assistant", Content = "Voilà", State = "complete", Seconds = 2 }
            };
            var author = new ServerAuthor(Guid.NewGuid(), "cora@example.com", "Cora");
            var sent = JsonNode.Parse(JsonSerializer.Serialize(ShareContent.Snapshot("T", transcript, author), ServerApi.Json))!;
            var sentMessages = sent["messages"]!.AsArray();
            check(sentMessages.Count == 4 && sentMessages[0]!["author"]!["email"]!.ToString() == "cora@example.com" && sentMessages[3]!["author"] == null, "L’hôte envoie l’auteur des messages utilisateur, pas celui des réponses");
            check(sentMessages[1]!["reasoning"]!.ToString() == "Je dois lire" && sentMessages[1]!["toolCalls"]![0]!["name"]!.ToString() == "lire" && !sentMessages[1]!.ToJsonString().Contains("secret interne"), "Il envoie la réflexion et les appels d’outils, sans le détail interne du moteur");
            check(sentMessages[2]!["toolCallId"]!.ToString() == "c1" && sentMessages[2]!["content"]!.ToString().Length < 6100, "Un long résultat d’outil est raccourci");
            var tiny = JsonNode.Parse(JsonSerializer.Serialize(ShareContent.Snapshot("T", transcript, author, budgetBytes: 5000), ServerApi.Json))!["messages"]!.AsArray();
            check(tiny.Count == 1 && tiny[0]!["content"]!.ToString() == "Voilà", "Un instantané trop gros ne garde que les messages les plus récents");

            // ---- The periodic resynchronization setting ----
            sync.SetMinutes(60);
            check(connection.Link.SyncMinutes == 60 && sync.Minutes == 60 && sync.Snapshot.NextSyncUtc is { } hour && hour > DateTime.UtcNow.AddMinutes(55), "Le délai de resynchronisation choisi est mémorisé (1 heure)");
            sync.SetMinutes(7);
            check(sync.Minutes == ServerSyncService.DefaultMinutes, "Une valeur inconnue revient au défaut (5 minutes)");
            sync.SetMinutes(0);
            check(sync.Minutes == 0 && sync.Snapshot.NextSyncUtc == null, "« Jamais » supprime la resynchronisation périodique");
            var live = await Provider("Live");
            check(await WaitFor(() => Providers().Any(x => x.Name == "Live · serveur")), "…mais les changements continuent d’arriver en direct");
            check(File.ReadAllText(ServerLinkStore.PathFor(clientDb)).Contains("\"syncMinutes\": 0"), "Le réglage est conservé dans le fichier de liaison, à côté de la base");
            sync.SetMinutes(5);
            check(ServerSyncService.IntervalChoices.SequenceEqual([0, 1, 5, 60, 1440]), "Les choix proposés sont Jamais, 1 minute, 5 minutes, 1 heure et 1 jour");

            // ---- The server goes away, then comes back ----
            var address = connection.Link.Url;
            connection.Store.Update(l => l.Url = "http://127.0.0.1:1/");
            await connection.StopRealtimeAsync();
            await sync.SyncNowAsync();
            check(sync.Snapshot.State == LinkState.Offline && sync.Snapshot.Issues.Any(x => x.Key == "link" && x.Level == SyncLevel.Warning), "Serveur injoignable: état « hors ligne » avec un avertissement");
            check(Projects().Count == 0 && Providers().Any(x => x.Name == "Live · serveur"), "Hors ligne, rien n’est retiré de ce qui est déjà là");
            connection.Store.Update(l => l.Url = address);
            await sync.SyncNowAsync();
            check(sync.Snapshot.State == LinkState.Online && sync.Snapshot.Issues.All(x => x.Key != "link"), "Au retour du serveur, l’état redevient « connecté » et l’avertissement disparaît");

            // ---- The session is revoked ----
            await server.Post($"/api/admin/users/{coraId}/revoke-sessions", null, admin);
            await sync.SyncNowAsync();
            check(sync.Snapshot.State == LinkState.SessionLost && sync.Snapshot.Worst == SyncLevel.Error && sync.Snapshot.Issues.Any(x => x.Message.Contains("reconnectez-vous")), "Une session révoquée est signalée comme erreur, avec la marche à suivre");
            await connection.LoginAsync("cora@example.com", "password-1234", "sync test");
            sync.LinkChanged();
            check(await WaitFor(() => sync.Snapshot.State == LinkState.Online && sync.Snapshot.Issues.All(x => x.Level != SyncLevel.Error)), "Après une nouvelle connexion, la synchronisation reprend toute seule");

            // ---- Sign-out and forget ----
            sync.ClearIssues();
            check(sync.Snapshot.Issues.Count == 0 || sync.Snapshot.Issues.All(x => x.Level == SyncLevel.Info), "L’utilisateur peut vider la liste des problèmes");
            await connection.LogoutAsync();
            check(await WaitFor(() => sync.Snapshot.State == LinkState.SignedOut), "Après une déconnexion, l’état est « non connecté »");
            check(sync.Snapshot.Log.Any(x => x.Message.Contains("Connecté à")), "Le journal garde la trace des événements");
        }
        finally
        {
            PortableStorage.UseDatabase(original);
            try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
