using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core.Remote;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core.Hosting;

/// <summary>
/// Optional link to a Monolith Harness Server for the engine hosts (CLI, Electron): shared providers become ordinary local providers,
/// conversations of a cloud project run on the server through the usual <c>send</c>, and a local conversation can be shared live.
/// Without a configured server nothing here is active.
/// </summary>
public sealed partial class HarnessService
{
    sealed class CloudRun(CancellationTokenSource source)
    {
        public CancellationToken Token => source.Token;
        public void Cancel() => source.Cancel();
    }

    readonly ConcurrentDictionary<int, CloudRun> cloudRuns = new();
    readonly ConcurrentDictionary<string, TaskCompletionSource<CloudAnswer>> cloudQuestions = new();
    readonly ConcurrentDictionary<int, Guid> sharedChats = new();
    readonly Lazy<ServerConnection> serverLink = new(() => new ServerConnection(new ServerLinkStore(ServerLinkStore.PathFor(database)), KeyVault.Encrypt, KeyVault.Decrypt));
    ShareBridge? shareBridge;
    Func<object, Task>? emitTee;
    ServerSyncService? syncService;

    /// <summary>Automatic synchronization with the server. Created on demand; it only runs once started (see <c>server.sync.start</c>).</summary>
    ServerSyncService Sync => syncService ??= CreateSync();

    ServerSyncService CreateSync()
    {
        var service = new ServerSyncService(Server, () => Db()) { IsChatBusy = chatId => cloudRuns.ContainsKey(chatId) || runs.ContainsKey(chatId) };
        service.StateChanged += state => _ = EmitQuietly(new { @event = "server.state", state = SyncView(state) });
        service.Changed += changes => EmitQuietly(new { @event = "server.changed", changes = ChangesView(changes) });
        service.RemoteRunProgress += progress => EmitQuietly(RemoteView(progress));
        return service;
    }

    async Task EmitQuietly(object value)
    {
        try { await emitTarget(value); }
        catch (Exception ex) when (ex is not OperationCanceledException) { AppLog.Write(AppLogLevel.Information, "server.sync_event_skipped", ex); }
    }

    /// <summary>The synchronization state as a host shows it: names instead of numbers, and the last events.</summary>
    public static object SyncView(SyncSnapshot s) => new
    {
        state = s.State.ToString(), s.Syncing, s.Realtime, s.LastSyncUtc, s.NextSyncUtc, minutes = s.SyncMinutes, s.ServerName, s.ServerVersion, s.Url, s.Email,
        s.Providers, s.Projects, s.SharedWithMe, s.PublicShares, worst = s.Worst?.ToString(),
        issues = s.Issues.Select(x => new { x.Key, level = x.Level.ToString(), x.Area, x.Message, x.SinceUtc, x.LastUtc, x.Count }).ToList(),
        log = s.Log.Take(30).Select(x => new { x.TimeUtc, level = x.Level.ToString(), x.Area, x.Message }).ToList()
    };

    /// <summary>A request sent from elsewhere, as the events a local generation would emit (the streaming message has the id -1 until the real ones arrive).</summary>
    static object RemoteView(RemoteProgress p) => p.Finished
        ? new { @event = "server.remote.end", chatId = p.LocalChatId }
        : p.Text.Length > 0 || p.Reasoning.Length > 0
            ? new { @event = "stream", chatId = p.LocalChatId, messageId = -1, text = p.Text, reasoning = p.Reasoning, remote = true, requester = p.Requester }
            : (object)new { @event = "status", chatId = p.LocalChatId, text = $"{p.Requester} · " + (p.State == "queued" ? "en attente sur le serveur / queued" : "requête en cours / request running") };

    static object ChangesView(SyncChanges c) => new { c.Providers, c.Projects, c.RemovedProjects, c.Chats, c.RemovedChats, c.Messages, c.Shares };

    ServerConnection? serverConnection => serverLink.IsValueCreated ? serverLink.Value : null;
    ServerConnection Server => serverLink.Value;
    ShareBridge Bridge => shareBridge ??= new ShareBridge(Server);

    // Every engine event goes to the host; events of a shared conversation also go to the server for its viewers.
    Func<object, Task> emit => emitTee ??= EmitAndShareAsync;

    async Task EmitAndShareAsync(object value)
    {
        await emitTarget(value);
        // A conversation renamed (by the naming model) goes to the server and to the viewers of a shared conversation.
        if (value.GetType().GetProperty("event")?.GetValue(value) as string == "chat.renamed"
            && value.GetType().GetProperty("chatId")?.GetValue(value) is int renamedChat && value.GetType().GetProperty("title")?.GetValue(value) is string newTitle)
            OnTitleChanged(renamedChat, newTitle);
        if (sharedChats.IsEmpty) return;
        try
        {
            if (JsonNode.Parse(JsonSerializer.Serialize(value, Json)) is not JsonObject node || node["chatId"] is not JsonValue chat || !chat.TryGetValue<int>(out var chatId)
                || !sharedChats.TryGetValue(chatId, out var share) || node["event"]?.GetValue<string>() is not { } name) return;
            node.Remove("event");
            node.Remove("html");
            // A message goes as the viewers draw it (reasoning, tool calls, timings, sender), read back from the conversation.
            if (name == "message" && node["message"]?["id"] is JsonValue messageId && messageId.TryGetValue<int>(out var id))
            {
                await using var db = Db();
                if (await db.Messages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) is { } stored)
                {
                    await Bridge.ForwardAsync(share, "message", new { chatId, message = ShareContent.Of(stored, ServerSync.CurrentAuthor(Server.Link)) });
                    return;
                }
            }
            if (node["message"] is JsonObject message) { message.Remove("html"); message.Remove("attachments"); }
            await Bridge.ForwardAsync(share, name, node);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { AppLog.Write(AppLogLevel.Warning, "server.share_forward_failed", ex); }
    }

    /// <summary>A conversation title changed here: a cloud conversation is renamed on the server, a shared one is renamed for its viewers.</summary>
    void OnTitleChanged(int chatId, string title)
    {
        try
        {
            if (Server.Link.Chats.ContainsKey(chatId)) Sync.RequestPush(chatId);
            if (sharedChats.TryGetValue(chatId, out var share)) _ = ForwardSafely(share, "title", new { title });
        }
        catch (Exception ex) when (ex is ServerException or InvalidOperationException or IOException) { AppLog.Write(AppLogLevel.Information, "server.title_skipped", ex); }
    }

    async Task ForwardSafely(Guid share, string name, object data)
    {
        try { await Bridge.ForwardAsync(share, name, data); }
        catch (Exception ex) when (ex is ServerException or InvalidOperationException) { AppLog.Write(AppLogLevel.Information, "server.share_forward_skipped", ex); }
    }

    Task OnShareViewers(int chatId, int count) => EmitQuietly(new { @event = "share.viewers", chatId, count });

    // The host callbacks are constructor parameters: nested helper classes reach them through these members.
    Task<JsonNode?> HostCall(string method, JsonObject parameters, CancellationToken ct) => host(method, parameters, ct);

    /// <summary>The persisted link to the optional server (address, account, links to cloud projects and shares); never the token itself.</summary>
    public ServerLinkData ServerLink() => Server.Link;

    object ServerSummary()
    {
        var link = Server.Link;
        return new
        {
            configured = link.Configured, signedIn = link.SignedIn, url = link.Url, email = link.Email, allowHttp = link.AllowHttp,
            cloudProjects = link.Projects.ToDictionary(x => x.Key, x => x.Value.ServerProjectId), shares = link.Shares, providers = link.Providers.Values
        };
    }

    bool IsCloudChat(int chatId)
    {
        var link = Server.Link;
        if (link.Projects.Count == 0) return false;
        using var db = Db();
        var project = db.Chats.AsNoTracking().Where(x => x.Id == chatId).Select(x => (int?)x.ProjectId).FirstOrDefault();
        return project is { } id && link.Projects.ContainsKey(id);
    }

    void StopCloud(int chatId) { if (cloudRuns.TryGetValue(chatId, out var run)) run.Cancel(); }

    void ForgetServerLinks(int? projectId = null, int? chatId = null)
    {
        if (!serverLink.Value.Configured) return;
        ServerSync.Forget(Server, projectId, chatId);
        if (chatId is { } chat && sharedChats.TryRemove(chat, out var share)) _ = Bridge.Detach(share);
    }

    // ---- Cloud conversations ----

    async Task<object> SendCloud(JsonObject p, CancellationToken lifetime)
    {
        var chatId = I(p, "chatId");
        var text = S(p, "text").Trim();
        if (text.Length == 0) throw new ArgumentException("Message required.");
        if ((p["images"] as JsonArray)?.Count > 0) throw new ArgumentException("Les images ne sont pas prises en charge dans un projet cloud / Images are not supported in a cloud project.");
        if (!Server.SignedIn) throw new InvalidOperationException("Connectez-vous au serveur : /server login / Sign in to the server first.");
        await using var db = Db();
        var chat = await db.Chats.SingleAsync(x => x.Id == chatId, lifetime);
        var provider = await db.Providers.SingleAsync(x => x.Id == I(p, "providerId"), lifetime);
        var target = ServerSync.ServerProviderOf(Server.Link, provider)
            ?? throw new ArgumentException("Choisissez un provider du serveur pour un projet cloud / Choose a server provider for a cloud project.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        if (!cloudRuns.TryAdd(chatId, new CloudRun(cancellation))) throw new InvalidOperationException("This conversation is already running.");
        var error = "";
        try
        {
            await emit(new { @event = "started", chatId });
            var (project, serverChat) = await ServerSync.EnsureChatAsync(db, Server, chat, cancellation.Token);
            var user = new Message { ChatId = chatId, Role = "user", Content = text };
            db.Messages.Add(user);
            chat.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellation.Token);
            await emit(new { @event = "message", chatId, title = chat.Title, message = MessageView(user) });
            var sink = new CloudEvents(this, chatId, chat.Title);
            var run = await new CloudRunSession(Server, sink).RunAsync(project, serverChat, text, target.ServerProvider, target.Model, cancellation.Token);
            await sink.FinishAsync();
            if (run.State == "failed") error = string.IsNullOrWhiteSpace(run.Error) ? "Échec de la requête cloud / Cloud request failed." : run.Error.Split('\n')[0];
            else if (run.State == "cancelled") error = "Génération arrêtée / Generation stopped";
        }
        catch (OperationCanceledException) { error = "Génération arrêtée / Generation stopped"; }
        catch (ServerException ex) { error = ex.Message; throw new InvalidOperationException(ex.Message, ex); }
        finally
        {
            cloudRuns.TryRemove(chatId, out _);
            await emit(new { @event = "done", chatId, error, status = "" });
        }
        return true;
    }

    /// <summary>Turns the progress of a cloud run into the events a local run would emit, with local identifiers.</summary>
    sealed class CloudEvents(HarnessService owner, int chatId, string title) : ICloudSink
    {
        readonly Dictionary<int, int> localByCloud = [];
        readonly SemaphoreSlim pull = new(1, 1);

        public Task StateAsync(ServerRun run) => owner.emit(new
        {
            @event = "status", chatId,
            text = run.State switch
            {
                "queued" => run.QueuePosition is { } position ? $"En attente sur le serveur (rang {position}) / Queued on the server (position {position})" : "En attente sur le serveur / Queued on the server",
                "running" => "Exécution sur le serveur / Running on the server",
                "awaiting_approval" => "Approbation requise / Approval required",
                _ => ""
            }
        });

        public async Task StreamAsync(JsonElement data)
        {
            if (data.ValueKind != JsonValueKind.Object || JsonNode.Parse(data.GetRawText()) is not JsonObject node) return;
            if (node["messageId"] is not JsonValue cloud || !cloud.TryGetValue<int>(out var cloudId)) return;
            if (!localByCloud.TryGetValue(cloudId, out var local))
            {
                // The streaming message exists locally from its first update, like an interrupted local generation.
                await using var db = owner.Db();
                var message = new Message { ChatId = chatId, Role = "assistant", State = "interrupted", WireJson = new JsonObject { ["cloud_id"] = cloudId }.ToJsonString() };
                db.Messages.Add(message);
                await db.SaveChangesAsync();
                localByCloud[cloudId] = local = message.Id;
            }
            node["event"] = "stream";
            node["chatId"] = chatId;
            node["messageId"] = local;
            await owner.emit(node);
        }

        public async Task MessageAsync(JsonElement data) => await PullAsync(emitMessages: true);

        public Task StatusAsync(string text) => owner.emit(new { @event = "status", chatId, text });

        /// <summary>Merges the server conversation into the local one and announces what changed.</summary>
        async Task PullAsync(bool emitMessages)
        {
            await pull.WaitAsync();
            try
            {
                await using var db = owner.Db();
                var chat = await db.Chats.SingleAsync(x => x.Id == chatId);
                var changed = await ServerSync.PullChatAsync(db, owner.Server, chat);
                foreach (var message in changed.OrderBy(x => x.Id))
                {
                    if (ServerSync.CloudId(message) is { } cloudId) localByCloud[cloudId] = message.Id;
                    if (emitMessages) await owner.emit(new { @event = "message", chatId, title, message = MessageView(message) });
                }
            }
            finally { pull.Release(); }
        }

        public Task FinishAsync() => PullAsync(emitMessages: true);

        public async Task<string> PermissionAsync(ServerApproval approval, CancellationToken ct)
        {
            JsonNode? command = approval.Command.StartsWith('{') ? SafeParse(approval.Command) : approval.Command.Length > 0 ? JsonValue.Create(approval.Command) : null;
            var reply = await owner.HostCall("permission", Obj(new { title = approval.Title, details = approval.Details, command, oneTimeOnly = approval.OneTimeOnly, cloud = true }), ct);
            ct.ThrowIfCancellationRequested();
            return reply?.GetValue<string>() is "allow" or "always" ? reply.GetValue<string>() : "deny";
        }

        public async Task<CloudAnswer> QuestionAsync(ServerApproval approval, CancellationToken ct)
        {
            var id = approval.Id.ToString();
            var completion = new TaskCompletionSource<CloudAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
            owner.cloudQuestions[id] = completion;
            try
            {
                await owner.emit(new { @event = "question", id, chatId, questions = approval.Questions });
                await owner.emit(new { @event = "status", chatId, text = "Réponse attendue / Waiting for your answer" });
                using var registration = ct.Register(() => completion.TrySetCanceled(ct));
                return await completion.Task;
            }
            finally
            {
                owner.cloudQuestions.TryRemove(id, out _);
                await owner.emit(new { @event = "question.closed", id, chatId });
            }
        }

        static JsonNode? SafeParse(string json)
        {
            try { return JsonNode.Parse(json); }
            catch (JsonException) { return JsonValue.Create(json); }
        }
    }

    object? AnswerCloudQuestion(JsonObject p)
    {
        if (!cloudQuestions.TryGetValue(S(p, "id"), out var completion)) return null;
        var answers = (p["answers"] as JsonArray ?? []).Select(x => (x as JsonArray ?? []).Select(y => y?.GetValue<string>() ?? "").ToList()).ToList();
        if (!completion.TrySetResult(new CloudAnswer(B(p, "cancelled"), answers))) throw new InvalidOperationException("Réponse déjà envoyée / Already answered.");
        return true;
    }

    // ---- RPC: server.* ----

    async Task<object?> DispatchServer(string method, JsonObject p, CancellationToken ct)
    {
        try { return await DispatchServerCore(method, p, ct); }
        catch (ServerException ex) { throw new InvalidOperationException(ex.Message, ex); }
    }

    async Task<object?> DispatchServerCore(string method, JsonObject p, CancellationToken ct)
    {
        switch (method)
        {
            case "server.status": return await StatusView(ct);
            case "server.connect":
                var info = await Server.ConnectAsync(S(p, "url"), B(p, "allowHttp"), ct);
                return new { info.Name, info.Version, status = await StatusView(ct) };
            case "server.login":
            {
                var login = await Server.LoginAsync(S(p, "email"), S(p, "password"), S(p, "label", "Monolith Harness " + Environment.MachineName), ct);
                string? syncError = null;
                try { await using var db = Db(); await ServerSync.SyncProvidersAsync(db, Server, ct); }
                catch (ServerException ex) { syncError = ex.Message; }
                _ = ResumeSharesAsync();
                Sync.LinkChanged(); Sync.Start();
                return new { login.PasswordChangeSuggested, login.PasswordChangeRequired, syncError, status = await StatusView(ct) };
            }
            case "server.password":
                await Server.Api.ChangePasswordAsync(S(p, "current"), S(p, "new"), ct);
                return await StatusView(ct);
            case "server.logout":
            {
                if (!cloudRuns.IsEmpty) throw new InvalidOperationException("Arrêtez les requêtes cloud en cours avant de vous déconnecter / Stop running cloud requests first.");
                foreach (var chat in sharedChats.Keys.ToList()) if (sharedChats.TryRemove(chat, out var share)) await Bridge.Detach(share);
                await Server.LogoutAsync(ct);
                await using var db = Db();
                await ServerSync.RemoveProvidersAsync(db, Server, ct);
                Sync.LinkChanged();
                return await StatusView(ct);
            }
            case "server.disconnect":
            {
                if (!cloudRuns.IsEmpty) throw new InvalidOperationException("Arrêtez les requêtes cloud en cours / Stop running cloud requests first.");
                if (Server.SignedIn) { await Server.LogoutAsync(ct); await using var db = Db(); await ServerSync.RemoveProvidersAsync(db, Server, ct); }
                sharedChats.Clear();
                Server.Store.Update(l => { l.Url = ""; l.Email = ""; l.ProtectedToken = ""; l.TokenExpiresUtc = null; l.ServerName = ""; l.ServerVersion = ""; l.Providers.Clear(); l.Projects.Clear(); l.Chats.Clear(); l.Shares.Clear(); });
                Sync.LinkChanged();
                return await StatusView(ct);
            }
            case "server.sync":
            {
                await using var db = Db();
                var providers = await ServerSync.SyncProvidersAsync(db, Server, ct);
                return providers.Select(ProviderView).ToList();
            }
            case "server.projects":
            {
                var link = Server.Link;
                var projects = await Server.Api.ProjectsAsync(ct);
                return projects.Select(x => new
                {
                    x.Id, x.Name, x.OwnerEmail, x.Access, x.FilesBytes,
                    localProjectId = link.Projects.FirstOrDefault(y => y.Value.ServerProjectId == x.Id.ToString()) is { Value: not null } found ? (int?)found.Key : null
                }).ToList();
            }
            case "server.project.open":
            {
                var remote = (await Server.Api.ProjectsAsync(ct)).FirstOrDefault(x => x.Id.ToString() == S(p, "id") || x.Name.Equals(S(p, "id"), StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException("Projet cloud introuvable / Cloud project not found.");
                await using var db = Db();
                var project = await ServerSync.BindProjectAsync(db, Server, remote, ct);
                return new { project.Id, project.Name, serverProjectId = remote.Id };
            }
            case "server.project.create":
            {
                await using var db = Db();
                var project = await ServerSync.CreateProjectAsync(db, Server, S(p, "name").Trim(), ct);
                return new { project.Id, project.Name };
            }
            case "server.chat.pull":
            {
                await using var db = Db();
                var chat = await db.Chats.SingleAsync(x => x.Id == I(p, "chatId"), ct);
                return new { changed = (await ServerSync.PullChatAsync(db, Server, chat, ct)).Count };
            }
            case "server.sync.start":
                if (Server.Link is { Configured: true, SignedIn: true }) Sync.Start();
                return SyncView(Sync.Snapshot);
            case "server.sync.status": return SyncView(Sync.Snapshot);
            case "server.sync.now":
            {
                var changes = await Sync.SyncNowAsync(ct);
                return new { changes = ChangesView(changes), state = SyncView(Sync.Snapshot) };
            }
            case "server.sync.interval":
                Sync.SetMinutes(I(p, "minutes"));
                return SyncView(Sync.Snapshot);
            case "server.sync.clear":
                Sync.ClearIssues();
                return SyncView(Sync.Snapshot);
            case "server.share.start": return await StartShare(I(p, "chatId"), S(p, "title"), B(p, "isPublic"), ct);
            case "server.share.info":
            {
                if (!sharedChats.TryGetValue(I(p, "chatId"), out var shared)) throw new InvalidOperationException("Cette conversation n'est pas partagée / This conversation is not shared.");
                return await Server.Api.ShareAsync(shared, ct);
            }
            case "server.share.public":
            {
                if (!sharedChats.TryGetValue(I(p, "chatId"), out var shared)) throw new InvalidOperationException("Cette conversation n'est pas partagée / This conversation is not shared.");
                return await Server.Api.SetSharePublicAsync(shared, B(p, "isPublic"), ct);
            }
            case "server.share.code":
            {
                if (!sharedChats.TryGetValue(I(p, "chatId"), out var shared)) throw new InvalidOperationException("Cette conversation n'est pas partagée / This conversation is not shared.");
                return S(p, "action") == "off" ? await Server.Api.DisableShareCodeAsync(shared, ct) : await Server.Api.NewShareCodeAsync(shared, ct);
            }
            case "server.share.join": return await ReadSharedConversation(await Server.Api.JoinShareAsync(S(p, "code"), ct), ct);
            case "server.share.view": return await ReadSharedConversation(await Server.Api.ShareAsync(Guid.Parse(S(p, "id")), ct), ct);
            case "server.share.comment":
                await Server.Api.CommentShareAsync(Guid.Parse(S(p, "id")), S(p, "text"), ct);
                return true;
            case "server.share.leave":
                await Server.Api.LeaveShareAsync(Guid.Parse(S(p, "id")), ct);
                return true;
            case "server.shares.public": return await Server.Api.PublicSharesAsync(ct);
            case "server.share.stop":
            {
                var chatId = I(p, "chatId");
                if (!sharedChats.TryRemove(chatId, out var share)) throw new InvalidOperationException("Cette conversation n'est pas partagée / This conversation is not shared.");
                Server.Store.Update(l => l.Shares.Remove(chatId));
                await Bridge.StopAsync(share, ct);
                return true;
            }
            case "server.share.invite":
            {
                if (!sharedChats.TryGetValue(I(p, "chatId"), out var share)) throw new InvalidOperationException("Cette conversation n'est pas partagée / This conversation is not shared.");
                await Server.Api.InviteAsync(share, S(p, "email"), ct);
                return true;
            }
            case "server.shares": return await Server.Api.SharesAsync(ct);
            default: throw new ArgumentException("Unknown method: " + method);
        }
    }

    async Task<object> StatusView(CancellationToken ct)
    {
        var status = await Server.StatusAsync(ct);
        return new { status, summary = ServerSummary() };
    }

    // ---- Local shared conversations ----

    /// <summary>The current content of a shared conversation (waits briefly for the host's snapshot): for reading once from a terminal.</summary>
    async Task<object> ReadSharedConversation(ServerShare share, CancellationToken ct)
    {
        using var viewer = await ShareViewer.OpenAsync(Server, share);
        for (var waited = 0; waited < 4000 && viewer.Messages.Count == 0 && viewer.ClosedReason == null; waited += 100) await Task.Delay(100, ct);
        return new
        {
            share = new { share.Id, share.Title, share.HostEmail, share.IsPublic }, title = viewer.Title, online = viewer.Online, closed = viewer.ClosedReason,
            messages = viewer.Messages.Select(x => new { x.Role, x.Content }).ToList(), comments = viewer.Comments.Select(x => new { x.Author, x.Text, x.TimeUtc }).ToList()
        };
    }

    async Task<object> StartShare(int chatId, string title, bool isPublic, CancellationToken ct)
    {
        if (sharedChats.ContainsKey(chatId)) throw new InvalidOperationException("Cette conversation est déjà partagée / This conversation is already shared.");
        string name;
        await using (var db = Db()) name = string.IsNullOrWhiteSpace(title) ? (await db.Chats.AsNoTracking().SingleAsync(x => x.Id == chatId, ct)).Title : title.Trim();
        var share = await Bridge.StartAsync(name, chatId.ToString(), () => ShareSnapshot(chatId), comment => OnShareComment(chatId, comment), ct, isPublic, count => OnShareViewers(chatId, count));
        sharedChats[chatId] = share.Id;
        Server.Store.Update(l => l.Shares[chatId] = share.Id.ToString());
        return new { shareId = share.Id, title = share.Title, code = share.Code, isPublic = share.IsPublic };
    }

    async Task<object> ShareSnapshot(int chatId)
    {
        await using var db = Db();
        var chat = await db.Chats.AsNoTracking().SingleAsync(x => x.Id == chatId);
        var history = await db.Messages.AsNoTracking().Where(x => x.ChatId == chatId).OrderBy(x => x.Id).ToListAsync();
        return ShareContent.Snapshot(chat.Title, history, ServerSync.CurrentAuthor(Server.Link));
    }

    /// <summary>A viewer's comment is shown to the host; it never reaches the model or the tools.</summary>
    Task OnShareComment(int chatId, ShareComment comment) =>
        emit(new { @event = "share.comment", chatId, author = comment.Author, text = comment.Text, status = $"{comment.Author} : {comment.Text}" });

    /// <summary>After a restart, conversations that were being shared attach their bridge again (if the share still exists).</summary>
    async Task ResumeSharesAsync()
    {
        try
        {
            var link = Server.Link;
            if (!link.SignedIn || link.Shares.Count == 0) return;
            var live = (await Server.Api.SharesAsync()).Where(x => x.IsHost && x.RevokedUtc == null).Select(x => x.Id).ToHashSet();
            foreach (var (chatId, id) in link.Shares.ToList())
            {
                if (!Guid.TryParse(id, out var share) || !live.Contains(share)) { Server.Store.Update(l => l.Shares.Remove(chatId)); continue; }
                sharedChats[chatId] = share;
                var chat = chatId;
                await Bridge.AttachAsync(share, () => ShareSnapshot(chat), comment => OnShareComment(chat, comment), count => OnShareViewers(chat, count));
            }
        }
        catch (Exception ex) when (ex is ServerException or InvalidOperationException) { AppLog.Write(AppLogLevel.Information, "server.share_resume_skipped", ex); }
    }
}
