using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace MonolithHarness.Core.Remote;

/// <summary>
/// Keeps the local database in step with the server: the shared providers become ordinary local providers that call the server relay
/// (so local runs can use them), and cloud projects and conversations are mirrored as local rows (the server stays the source of truth).
/// </summary>
public static class ServerSync
{
    public const string ServerProviderSuffix = " · serveur";
    public const string CloudProviderSuffix = " · cloud";

    // ---- Providers ----

    /// <summary>
    /// Creates, updates or removes the local providers that mirror the server catalog. Their key is the session token, never the real
    /// provider key, which stays on the server.
    /// </summary>
    public static async Task<List<Provider>> SyncProvidersAsync(HarnessDb db, ServerConnection connection, CancellationToken ct = default) =>
        (await SyncProvidersDetailedAsync(db, connection, ct)).Providers;

    /// <summary>What a provider synchronization did, so that hosts refresh their selectors only when something changed.</summary>
    public sealed record ProviderSync(List<Provider> Providers, bool Changed);

    public static async Task<ProviderSync> SyncProvidersDetailedAsync(HarnessDb db, ServerConnection connection, CancellationToken ct = default)
    {
        var token = connection.Token() ?? throw new ServerException("not_signed_in", "Connexion au serveur requise / Sign in to the server first.");
        var changed = false;
        var catalog = await connection.Api.ProvidersAsync(ct);
        var link = connection.Link;
        var baseUrl = connection.Api.BaseUri;
        var mapping = new Dictionary<string, int>(link.Providers);
        var existing = await db.Providers.Where(x => mapping.Values.Contains(x.Id)).ToListAsync(ct);
        var pairs = new List<(ServerProvider Item, Provider Provider)>();
        foreach (var item in catalog.Where(x => x.Models.Count > 0))
        {
            var key = item.Id.ToString();
            var provider = mapping.TryGetValue(key, out var localId) ? existing.FirstOrDefault(x => x.Id == localId) : null;
            if (provider == null)
            {
                provider = new Provider();
                db.Providers.Add(provider);
            }
            provider.Name = item.Name + (item.AllowLocalUse ? ServerProviderSuffix : CloudProviderSuffix);
            provider.Kind = "openai";
            provider.BaseUrl = new Uri(baseUrl, item.RelayPath.TrimStart('/')).ToString().TrimEnd('/');
            // The protected form differs on every call: only rewrite the key when the token itself changed.
            if (provider.ProtectedKey.Length == 0 || !TokenMatches(provider.ProtectedKey, token)) provider.ProtectedKey = KeyVault.Encrypt(token);
            provider.SupportsImages = item.Models.Any(x => x.SupportsImages);
            var ids = item.Models.Select(x => x.Id).ToList();
            ProviderModels.Refresh(provider, ids);
            ProviderModels.Select(provider, ids);
            if (!ids.Contains(provider.Model, StringComparer.Ordinal)) ModelContexts.Select(provider, ids[0]);
            foreach (var model in item.Models) ModelContexts.SetOverride(provider, model.ContextLimit, model.Id);
            ModelContexts.Sync(provider);
            pairs.Add((item, provider));
        }
        changed |= db.ChangeTracker.HasChanges();
        await db.SaveChangesAsync(ct);
        var updated = pairs.ToDictionary(x => x.Item.Id.ToString(), x => x.Provider.Id);
        // Providers the account lost access to disappear locally too.
        var stale = existing.Where(x => !updated.ContainsValue(x.Id)).ToList();
        if (stale.Count > 0) { db.Providers.RemoveRange(stale); await db.SaveChangesAsync(ct); changed = true; }
        if (!link.Providers.OrderBy(x => x.Key).SequenceEqual(updated.OrderBy(x => x.Key))) changed = true;
        connection.Store.Update(l => l.Providers = updated);
        return new ProviderSync(pairs.Select(x => x.Provider).ToList(), changed);
    }

    static bool TokenMatches(byte[] protectedKey, string token)
    {
        try { return KeyVault.Decrypt(protectedKey) == token; }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException or InvalidOperationException) { return false; }
    }

    /// <summary>Removes every server-managed provider (sign-out or change of server).</summary>
    public static async Task RemoveProvidersAsync(HarnessDb db, ServerConnection connection, CancellationToken ct = default)
    {
        var ids = connection.Link.Providers.Values.ToList();
        if (ids.Count > 0)
        {
            db.Providers.RemoveRange(await db.Providers.Where(x => ids.Contains(x.Id)).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }
        connection.Store.Update(l => l.Providers.Clear());
    }

    /// <summary>Why a provider cannot be used now (it belongs to the server and the server mode is off), or null. The engine checks it before each request.</summary>
    public static string? BlockedReason(ServerLinkData link, Provider provider) =>
        link is { Configured: true, Enabled: false } && ServerProviderOf(link, provider) != null
            ? "Mode serveur désactivé : réactivez-le (puce du serveur) pour utiliser ce provider / Server mode is off: turn it back on to use this provider."
            : null;

    /// <summary>The server provider id and model behind a local provider, or null for an ordinary local provider.</summary>
    public static (Guid ServerProvider, string Model)? ServerProviderOf(ServerLinkData link, Provider provider) =>
        link.Providers.FirstOrDefault(x => x.Value == provider.Id) is { Key: { Length: > 0 } key } && Guid.TryParse(key, out var id) ? (id, provider.Model) : null;

    // ---- Cloud projects ----

    public static bool IsCloud(ServerLinkData link, int localProjectId) => link.Projects.ContainsKey(localProjectId);

    /// <summary>What reconciling one project changed in the local mirror.</summary>
    public sealed record ProjectSync(Project Project, bool Created, bool Renamed, List<int> NewChats, List<int> RenamedChats, List<int> RemovedChats, IReadOnlyList<ServerChat> RemoteChats);

    /// <summary>Mirrors a server project (and its conversations, without messages) as a local project; idempotent.</summary>
    public static async Task<Project> BindProjectAsync(HarnessDb db, ServerConnection connection, ServerProject remote, CancellationToken ct = default) =>
        (await ReconcileProjectAsync(db, connection, remote, null, ct)).Project;

    /// <summary>
    /// Brings the local mirror of a server project in line: the project and its conversations are created, renamed or removed as on the
    /// server (which is the reference). A conversation the host is busy with (<paramref name="isChatBusy"/>) is left alone this time.
    /// </summary>
    public static async Task<ProjectSync> ReconcileProjectAsync(HarnessDb db, ServerConnection connection, ServerProject remote, Func<int, bool>? isChatBusy, CancellationToken ct = default)
    {
        var link = connection.Link;
        var key = remote.Id.ToString();
        bool created = false, renamed = false;
        // A link whose local project was deleted is dropped (otherwise it would shadow the mirror and a new copy would be made each time).
        Project? project = null;
        foreach (var candidate in link.Projects.Where(x => x.Value.ServerProjectId == key).ToList())
        {
            var found = await db.Projects.SingleOrDefaultAsync(x => x.Id == candidate.Key, ct);
            if (found == null) Forget(connection, projectId: candidate.Key);
            else project ??= found;
        }
        if (project == null)
        {
            project = new Project { Name = remote.Name, Icon = "cloud" };
            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
            connection.Store.Update(l => l.Projects[project.Id] = new CloudProjectLink { ServerProjectId = key, Access = remote.Access });
            created = true;
        }
        else
        {
            renamed = project.Name != remote.Name;
            project.Name = remote.Name;
            connection.Store.Update(l => l.Projects[project.Id].Access = remote.Access);
        }
        // Same for conversations: a link to a deleted local conversation is dropped, so that the server one is mirrored again.
        var existingChatIds = (await db.Chats.Where(x => x.ProjectId == project.Id).Select(x => x.Id).ToListAsync(ct)).ToHashSet();
        foreach (var orphan in connection.Link.Chats.Where(x => x.Value.ServerProjectId == key && !existingChatIds.Contains(x.Key)).Select(x => x.Key).ToList())
            Forget(connection, chatId: orphan);
        var known = connection.Link.Chats.Where(x => x.Value.ServerProjectId == key).GroupBy(x => x.Value.ServerChatId).ToDictionary(x => x.Key, x => x.First().Key);
        var remoteChats = await connection.Api.ChatsAsync(remote.Id, ct);
        List<int> added = [], retitled = [], removed = [];
        foreach (var chat in remoteChats)
        {
            if (known.TryGetValue(chat.Id, out var localId) && await db.Chats.FindAsync([localId], ct) is { } local)
            {
                // The server title wins only when it changed there: a rename made on this device is not undone by the next synchronization.
                var lastServerTitle = connection.Link.Chats.TryGetValue(localId, out var existingLink) ? existingLink.ServerTitle : null;
                if (lastServerTitle != chat.Title)
                {
                    if (local.Title != chat.Title) { local.Title = chat.Title; retitled.Add(localId); }
                    connection.Store.Update(l => { if (l.Chats.TryGetValue(localId, out var entry)) entry.ServerTitle = chat.Title; });
                }
                continue;
            }
            var createdChat = new Chat { ProjectId = project.Id, Title = chat.Title };
            db.Chats.Add(createdChat);
            await db.SaveChangesAsync(ct);
            connection.Store.Update(l => l.Chats[createdChat.Id] = new CloudChatLink { ServerProjectId = key, ServerChatId = chat.Id, ServerTitle = chat.Title });
            added.Add(createdChat.Id);
        }
        // Conversations deleted on the server disappear here too (their local copy only; nothing is sent back).
        var remoteIds = remoteChats.Select(x => x.Id).ToHashSet();
        foreach (var (serverChat, localId) in known.Where(x => !remoteIds.Contains(x.Key)))
        {
            if (isChatBusy?.Invoke(localId) == true) continue;
            if (await db.Chats.FindAsync([localId], ct) is { } gone) db.Chats.Remove(gone);
            Forget(connection, chatId: localId);
            removed.Add(localId);
        }
        await db.SaveChangesAsync(ct);
        return new ProjectSync(project, created, renamed, added, retitled, removed, remoteChats);
    }

    /// <summary>
    /// Removes the local mirrors of projects the server no longer lists (deleted there, or access withdrawn). Only call with a successful,
    /// complete project list. Returns the ids of the removed local projects.
    /// </summary>
    public static async Task<List<int>> RemoveMissingProjectsAsync(HarnessDb db, ServerConnection connection, IEnumerable<ServerProject> remote, Func<int, bool>? isChatBusy, CancellationToken ct = default)
    {
        var present = remote.Select(x => x.Id.ToString()).ToHashSet();
        var removed = new List<int>();
        foreach (var (localId, project) in connection.Link.Projects.Where(x => !present.Contains(x.Value.ServerProjectId)).ToList())
        {
            var chats = connection.Link.Chats.Where(x => x.Value.ServerProjectId == project.ServerProjectId).Select(x => x.Key).ToList();
            if (chats.Any(x => isChatBusy?.Invoke(x) == true)) continue;
            if (await db.Projects.FindAsync([localId], ct) is { } gone) db.Projects.Remove(gone);
            await db.SaveChangesAsync(ct);
            Forget(connection, projectId: localId);
            removed.Add(localId);
        }
        return removed;
    }

    // ---- Deleting from here deletes on the server: otherwise the next synchronization would bring it back ----

    /// <summary>Deletes the server conversation behind a local cloud chat. No-op for a chat that never reached the server or is local.</summary>
    public static async Task DeleteChatOnServerAsync(ServerConnection connection, int localChatId, CancellationToken ct = default)
    {
        var link = connection.Link;
        if (!link.Chats.TryGetValue(localChatId, out var chat)) return;
        try { await connection.Api.DeleteChatAsync(Guid.Parse(chat.ServerProjectId), chat.ServerChatId, ct); }
        catch (ServerException ex) when (ex.Status == 404) { }
    }

    /// <summary>Deletes a cloud project on the server (owner only). Throws a <see cref="ServerException"/> the host can show.</summary>
    public static async Task DeleteProjectOnServerAsync(ServerConnection connection, int localProjectId, CancellationToken ct = default)
    {
        var link = connection.Link;
        if (!link.Projects.TryGetValue(localProjectId, out var project)) return;
        if (project.Access != "owner") throw new ServerException("not_owner", "Seul le propriétaire peut supprimer ce projet cloud / Only the owner can delete this cloud project.");
        try { await connection.Api.DeleteProjectAsync(Guid.Parse(project.ServerProjectId), ct); }
        catch (ServerException ex) when (ex.Status == 404) { }
    }

    public static async Task<Project> CreateProjectAsync(HarnessDb db, ServerConnection connection, string name, CancellationToken ct = default) =>
        await BindProjectAsync(db, connection, await connection.Api.CreateProjectAsync(name, ct), ct);

    /// <summary>The server conversation of a local chat, created on first use.</summary>
    public static async Task<(Guid Project, int Chat)> EnsureChatAsync(HarnessDb db, ServerConnection connection, Chat chat, CancellationToken ct = default)
    {
        var link = connection.Link;
        if (link.Chats.TryGetValue(chat.Id, out var chatLink)) return (Guid.Parse(chatLink.ServerProjectId), chatLink.ServerChatId);
        if (!link.Projects.TryGetValue(chat.ProjectId, out var project)) throw new ServerException("not_cloud", "Ce projet n'est pas un projet cloud / This is not a cloud project.");
        var created = await connection.Api.CreateChatAsync(Guid.Parse(project.ServerProjectId), chat.Title, ct);
        connection.Store.Update(l => l.Chats[chat.Id] = new CloudChatLink { ServerProjectId = project.ServerProjectId, ServerChatId = created.Id });
        return (Guid.Parse(project.ServerProjectId), created.Id);
    }

    /// <summary>Forgets the links of deleted local entities (the server data is untouched).</summary>
    public static void Forget(ServerConnection connection, int? projectId = null, int? chatId = null) => connection.Store.Update(l =>
    {
        if (projectId is { } p)
        {
            l.Projects.Remove(p);
            foreach (var key in l.Chats.Where(x => !l.Projects.Values.Any(y => y.ServerProjectId == x.Value.ServerProjectId)).Select(x => x.Key).ToList()) { l.Chats.Remove(key); l.Shares.Remove(key); }
        }
        if (chatId is { } c) { l.Chats.Remove(c); l.Shares.Remove(c); }
    });

    // ---- Messages ----

    /// <summary>The server message id stored in a mirrored message, or null for a message that exists only locally.</summary>
    public static int? CloudId(Message message)
    {
        if (string.IsNullOrEmpty(message.WireJson) || !message.WireJson.Contains("cloud_id", StringComparison.Ordinal)) return null;
        try { return JsonNode.Parse(message.WireJson)?["cloud_id"]?.GetValue<int>(); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>The plumbing a mirrored message keeps: its server id, reasoning, author and the tool-call links a client needs to draw tool cards.</summary>
    static string Wire(ServerMessage source)
    {
        var wire = new JsonObject { ["cloud_id"] = source.Id, ["reasoning_content"] = source.Reasoning.Length > 0 ? source.Reasoning : null };
        if (source.AuthorEmail is { Length: > 0 })
            wire["author"] = new JsonObject { ["id"] = source.AuthorId?.ToString(), ["email"] = source.AuthorEmail, ["name"] = source.AuthorName };
        if (!string.IsNullOrEmpty(source.Wire))
            try
            {
                if (JsonNode.Parse(source.Wire) is JsonObject extra)
                    foreach (var key in new[] { "tool_calls", "tool_call_id" })
                        if (extra[key] is { } value) wire[key] = value.DeepClone();
            }
            catch (System.Text.Json.JsonException) { }
        return wire.ToJsonString();
    }

    /// <summary>Who wrote a mirrored user message, or null when unknown (a message of this device, or sent before authors were recorded).</summary>
    public static ServerAuthor? AuthorOf(Message message)
    {
        if (string.IsNullOrEmpty(message.WireJson) || !message.WireJson.Contains("\"author\"", StringComparison.Ordinal)) return null;
        try
        {
            if (JsonNode.Parse(message.WireJson)?["author"] is not JsonObject author || author["email"]?.GetValue<string>() is not { Length: > 0 } email) return null;
            return new ServerAuthor(Guid.TryParse(author["id"]?.GetValue<string>(), out var id) ? id : Guid.Empty, email, author["name"]?.GetValue<string>() ?? "");
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>The signed-in account as an author (for the messages this device sends to a cloud conversation).</summary>
    public static ServerAuthor? CurrentAuthor(ServerLinkData link) =>
        link.SignedIn && link.Email.Length > 0 ? new ServerAuthor(Guid.TryParse(link.UserId, out var id) ? id : Guid.Empty, link.Email, link.DisplayName) : null;

    /// <summary>
    /// A conversation renamed on this device is renamed on the server too (the title of the server copy that was last seen differs from the
    /// local one). A rename the server refuses (read-only access) is undone by the next reconciliation. Returns the local chats pushed.
    /// </summary>
    public static async Task<List<int>> PushRenamesAsync(HarnessDb db, ServerConnection connection, CancellationToken ct = default)
    {
        var pushed = new List<int>();
        var links = connection.Link.Chats.Where(x => x.Value.ServerTitle != null).ToDictionary(x => x.Key, x => x.Value);
        if (links.Count == 0) return pushed;
        var ids = links.Keys.ToList();
        foreach (var chat in await db.Chats.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Title }).ToListAsync(ct))
        {
            var entry = links[chat.Id];
            if (chat.Title == entry.ServerTitle || string.IsNullOrWhiteSpace(chat.Title)) continue;
            try
            {
                await connection.Api.RenameChatAsync(Guid.Parse(entry.ServerProjectId), entry.ServerChatId, chat.Title, ct);
                connection.Store.Update(l => { if (l.Chats.TryGetValue(chat.Id, out var stored)) stored.ServerTitle = chat.Title; });
                pushed.Add(chat.Id);
            }
            catch (ServerException ex) when (ex.Status is 400 or 403 or 404)
            {
                // Not allowed (read-only) or gone: forget what was seen so that the server title comes back.
                connection.Store.Update(l => { if (l.Chats.TryGetValue(chat.Id, out var stored)) stored.ServerTitle = null; });
            }
        }
        return pushed;
    }

    /// <summary>The local conversation mirroring a server conversation, or null.</summary>
    public static int? LocalChatOf(ServerLinkData link, Guid project, int serverChat) =>
        link.Chats.FirstOrDefault(x => x.Value.ServerProjectId == project.ToString() && x.Value.ServerChatId == serverChat) is { Value: not null } found ? found.Key : null;

    /// <summary>
    /// Merges server messages into the local conversation: known ones are updated, the optimistic local copy of what the user just sent
    /// is adopted by its server twin, the rest is inserted in order. Returns the rows that changed.
    /// </summary>
    public static async Task<List<Message>> SyncMessagesAsync(HarnessDb db, int chatId, IEnumerable<ServerMessage> remote, CancellationToken ct = default)
    {
        var local = await db.Messages.Where(x => x.ChatId == chatId).ToListAsync(ct);
        var byCloud = new Dictionary<int, Message>();
        foreach (var message in local) if (CloudId(message) is { } id) byCloud[id] = message;
        var adoptable = new Queue<Message>(local.Where(x => x.Role == "user" && CloudId(x) == null).OrderBy(x => x.Id));
        var changed = new List<Message>();
        foreach (var source in remote.OrderBy(x => x.Id))
        {
            Message? target = byCloud.GetValueOrDefault(source.Id);
            if (target == null && source.Role == "user" && adoptable.Count > 0 && adoptable.Peek().Content == source.Content) target = adoptable.Dequeue();
            var isNew = target == null;
            target ??= new Message { ChatId = chatId };
            var before = (target.Content, target.State, target.InputTokens, target.OutputTokens, target.Seconds, target.WireJson);
            target.Role = source.Role;
            target.Content = source.Content;
            target.State = source.State;
            target.InputTokens = source.InputTokens;
            target.OutputTokens = source.OutputTokens;
            target.CachedInputTokens = source.CachedInputTokens;
            target.Seconds = source.Seconds;
            target.CompletedUtc = source.CompletedUtc;
            target.WireJson = Wire(source);
            if (isNew) { db.Messages.Add(target); changed.Add(target); }
            else if (before != (target.Content, target.State, target.InputTokens, target.OutputTokens, target.Seconds, target.WireJson)) changed.Add(target);
        }
        await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>Pulls the whole server conversation into the local database (open, refresh).</summary>
    public static async Task<List<Message>> PullChatAsync(HarnessDb db, ServerConnection connection, Chat chat, CancellationToken ct = default)
    {
        var link = connection.Link;
        if (!link.Chats.TryGetValue(chat.Id, out var chatLink)) return [];
        // One pull at a time per conversation: the background synchronization and a run in progress must not insert the same message twice.
        var gate = PullGates.GetOrAdd(chat.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var remote = await connection.Api.MessagesAsync(Guid.Parse(chatLink.ServerProjectId), chatLink.ServerChatId, 0, ct);
            return await SyncMessagesAsync(db, chat.Id, remote.Where(x => x.Role != "compaction" || x.Content.Length > 0), ct);
        }
        finally { gate.Release(); }
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> PullGates = new();
}
