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
    public static async Task<List<Provider>> SyncProvidersAsync(HarnessDb db, ServerConnection connection, CancellationToken ct = default)
    {
        var token = connection.Token() ?? throw new ServerException("not_signed_in", "Connexion au serveur requise / Sign in to the server first.");
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
            provider.ProtectedKey = KeyVault.Encrypt(token);
            provider.SupportsImages = item.Models.Any(x => x.SupportsImages);
            var ids = item.Models.Select(x => x.Id).ToList();
            ProviderModels.Refresh(provider, ids);
            ProviderModels.Select(provider, ids);
            if (!ids.Contains(provider.Model, StringComparer.Ordinal)) ModelContexts.Select(provider, ids[0]);
            foreach (var model in item.Models) ModelContexts.SetOverride(provider, model.ContextLimit, model.Id);
            ModelContexts.Sync(provider);
            pairs.Add((item, provider));
        }
        await db.SaveChangesAsync(ct);
        var updated = pairs.ToDictionary(x => x.Item.Id.ToString(), x => x.Provider.Id);
        // Providers the account lost access to disappear locally too.
        var stale = existing.Where(x => !updated.ContainsValue(x.Id)).ToList();
        if (stale.Count > 0) { db.Providers.RemoveRange(stale); await db.SaveChangesAsync(ct); }
        connection.Store.Update(l => l.Providers = updated);
        return pairs.Select(x => x.Provider).ToList();
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

    /// <summary>The server provider id and model behind a local provider, or null for an ordinary local provider.</summary>
    public static (Guid ServerProvider, string Model)? ServerProviderOf(ServerLinkData link, Provider provider) =>
        link.Providers.FirstOrDefault(x => x.Value == provider.Id) is { Key: { Length: > 0 } key } && Guid.TryParse(key, out var id) ? (id, provider.Model) : null;

    // ---- Cloud projects ----

    public static bool IsCloud(ServerLinkData link, int localProjectId) => link.Projects.ContainsKey(localProjectId);

    /// <summary>Mirrors a server project (and its conversations, without messages) as a local project; idempotent.</summary>
    public static async Task<Project> BindProjectAsync(HarnessDb db, ServerConnection connection, ServerProject remote, CancellationToken ct = default)
    {
        var link = connection.Link;
        var key = remote.Id.ToString();
        var project = link.Projects.FirstOrDefault(x => x.Value.ServerProjectId == key) is { Value: not null } existing
            ? await db.Projects.Include(x => x.Chats).SingleOrDefaultAsync(x => x.Id == existing.Key, ct) : null;
        if (project == null)
        {
            project = new Project { Name = remote.Name, Icon = "cloud" };
            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
            connection.Store.Update(l => l.Projects[project.Id] = new CloudProjectLink { ServerProjectId = key, Access = remote.Access });
        }
        else
        {
            project.Name = remote.Name;
            connection.Store.Update(l => l.Projects[project.Id].Access = remote.Access);
        }
        var known = connection.Link.Chats.Where(x => x.Value.ServerProjectId == key).ToDictionary(x => x.Value.ServerChatId, x => x.Key);
        var remoteChats = await connection.Api.ChatsAsync(remote.Id, ct);
        foreach (var chat in remoteChats)
        {
            if (known.TryGetValue(chat.Id, out var localId) && await db.Chats.FindAsync([localId], ct) is { } local) { local.Title = chat.Title; continue; }
            var created = new Chat { ProjectId = project.Id, Title = chat.Title };
            db.Chats.Add(created);
            await db.SaveChangesAsync(ct);
            connection.Store.Update(l => l.Chats[created.Id] = new CloudChatLink { ServerProjectId = key, ServerChatId = chat.Id });
        }
        await db.SaveChangesAsync(ct);
        return project;
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

    static string Wire(ServerMessage source) =>
        new JsonObject { ["cloud_id"] = source.Id, ["reasoning_content"] = source.Reasoning.Length > 0 ? source.Reasoning : null }.ToJsonString();

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
            var before = (target.Content, target.State, target.InputTokens, target.OutputTokens, target.Seconds);
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
            else if (before != (target.Content, target.State, target.InputTokens, target.OutputTokens, target.Seconds)) changed.Add(target);
        }
        await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>Pulls the whole server conversation into the local database (open, refresh).</summary>
    public static async Task<List<Message>> PullChatAsync(HarnessDb db, ServerConnection connection, Chat chat, CancellationToken ct = default)
    {
        var link = connection.Link;
        if (!link.Chats.TryGetValue(chat.Id, out var chatLink)) return [];
        var remote = await connection.Api.MessagesAsync(Guid.Parse(chatLink.ServerProjectId), chatLink.ServerChatId, 0, ct);
        return await SyncMessagesAsync(db, chat.Id, remote.Where(x => x.Role != "compaction" || x.Content.Length > 0), ct);
    }
}
