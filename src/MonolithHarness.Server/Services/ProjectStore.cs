using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

public sealed record ChatView(int Id, string Title, DateTime? UpdatedUtc);

/// <summary>
/// A message as clients see it. <see cref="Wire"/> keeps only what lets a client draw tool cards like the host does (the tool calls of an
/// assistant message, the call id of a tool result); the hidden plumbing of the engine never leaves the server. The author is known for
/// the user messages sent through the server.
/// </summary>
public sealed record MessageItem(int Id, string Role, string Content, string State, DateTime? CompletedUtc, int? InputTokens, int? OutputTokens,
    int? CachedInputTokens = null, double Seconds = 0, string Reasoning = "", Guid? AuthorId = null, string? AuthorEmail = null, string? AuthorName = null, string? Wire = null);

/// <summary>
/// Access to the engine database of each cloud project (conversations and messages). One SQLite database per project keeps
/// conversations isolated; the server reads it directly and the project's worker writes to it during a run.
/// </summary>
public sealed class ProjectStore(ServerPaths paths, SyncSignals signals, IDbContextFactory<ServerDb> factory)
{
    HarnessDb Open(Guid project) => new(paths.ProjectDatabase(project));

    public async Task CreateAsync(Guid project, string name)
    {
        Directory.CreateDirectory(paths.ProjectFiles(project));
        Directory.CreateDirectory(paths.ProjectEngine(project));
        Directory.CreateDirectory(paths.ProjectTemp(project));
        await using var db = Open(project);
        await db.InitializeAsync();
        var first = await db.Projects.OrderBy(x => x.Id).FirstAsync();
        first.Name = name;
        first.SetSourceFolders([paths.ProjectFiles(project)]);
        await db.SaveChangesAsync();
    }

    public async Task RenameAsync(Guid project, string name)
    {
        await using var db = Open(project);
        var first = await db.Projects.OrderBy(x => x.Id).FirstAsync();
        first.Name = name;
        await db.SaveChangesAsync();
    }

    public async Task<List<ChatView>> ChatsAsync(Guid project)
    {
        if (!File.Exists(paths.ProjectDatabase(project))) return [];
        await using var db = Open(project);
        return await db.Chats.AsNoTracking().OrderByDescending(x => x.UpdatedUtc).ThenByDescending(x => x.Id)
            .Select(x => new ChatView(x.Id, x.Title, x.UpdatedUtc)).ToListAsync();
    }

    public async Task<ChatView> CreateChatAsync(Guid project, string? title)
    {
        await using var db = Open(project);
        var owner = await db.Projects.OrderBy(x => x.Id).FirstAsync();
        var chat = new Chat { ProjectId = owner.Id, Title = string.IsNullOrWhiteSpace(title) ? "Nouvelle conversation" : title.Trim()[..Math.Min(title.Trim().Length, 120)] };
        db.Chats.Add(chat);
        await db.SaveChangesAsync();
        await signals.ProjectAsync(project, "chats", chat.Id);
        return new ChatView(chat.Id, chat.Title, chat.UpdatedUtc);
    }

    public async Task<bool> ChatExistsAsync(Guid project, int chat)
    {
        if (!File.Exists(paths.ProjectDatabase(project))) return false;
        await using var db = Open(project);
        return await db.Chats.AnyAsync(x => x.Id == chat);
    }

    public async Task DeleteChatAsync(Guid project, int chat)
    {
        await using var db = Open(project);
        await db.Chats.Where(x => x.Id == chat).ExecuteDeleteAsync();
        await signals.ProjectAsync(project, "chats", chat);
    }

    /// <summary>
    /// Messages after <paramref name="after"/>. Hidden tool plumbing (wire JSON) and attachment bytes are never returned;
    /// only the model's reasoning text is extracted from the wire JSON so that clients can show it.
    /// </summary>
    public async Task<List<MessageItem>> MessagesAsync(Guid project, int chat, int after = 0, int take = 500)
    {
        await using var db = Open(project);
        var rows = await db.Messages.AsNoTracking().Where(x => x.ChatId == chat && x.Id > after).OrderBy(x => x.Id).Take(Math.Clamp(take, 1, 2000))
            .Select(x => new { x.Id, x.Role, x.Content, x.State, x.CompletedUtc, x.InputTokens, x.OutputTokens, x.CachedInputTokens, x.Seconds, x.WireJson }).ToListAsync();
        var ids = rows.Where(x => x.Role == "user").Select(x => x.Id).ToList();
        var authors = new Dictionary<int, (Guid Id, string Email, string Name)>();
        if (ids.Count > 0)
        {
            await using var server = await factory.CreateDbContextAsync();
            var found = await (from a in server.MessageAuthors.AsNoTracking() join u in server.Users.AsNoTracking() on a.UserId equals u.Id
                               where a.ProjectId == project && ids.Contains(a.MessageId) select new { a.MessageId, u.Id, u.Email, u.DisplayName }).ToListAsync();
            foreach (var author in found) authors[author.MessageId] = (author.Id, author.Email, author.DisplayName);
        }
        return rows.Select(x => authors.TryGetValue(x.Id, out var author)
            ? new MessageItem(x.Id, x.Role, x.Content, x.State, x.CompletedUtc, x.InputTokens, x.OutputTokens, x.CachedInputTokens, x.Seconds, Reasoning(x.WireJson), author.Id, author.Email, author.Name, DisplayWire(x.WireJson))
            : new MessageItem(x.Id, x.Role, x.Content, x.State, x.CompletedUtc, x.InputTokens, x.OutputTokens, x.CachedInputTokens, x.Seconds, Reasoning(x.WireJson), Wire: DisplayWire(x.WireJson))).ToList();
    }

    /// <summary>The tool calls of an assistant message and the call id of a tool result, without the (possibly large) content.</summary>
    internal static string? DisplayWire(string wire)
    {
        if (string.IsNullOrEmpty(wire) || !(wire.Contains("tool_calls", StringComparison.Ordinal) || wire.Contains("tool_call_id", StringComparison.Ordinal))) return null;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(wire) is not System.Text.Json.Nodes.JsonObject node) return null;
            var kept = new System.Text.Json.Nodes.JsonObject();
            if (node["tool_call_id"] is { } callId) kept["tool_call_id"] = callId.DeepClone();
            if (node["tool_calls"] is System.Text.Json.Nodes.JsonArray calls)
            {
                var trimmed = new System.Text.Json.Nodes.JsonArray();
                foreach (var call in calls.OfType<System.Text.Json.Nodes.JsonObject>())
                    trimmed.Add(new System.Text.Json.Nodes.JsonObject
                    {
                        ["id"] = call["id"]?.DeepClone(), ["type"] = "function",
                        ["function"] = new System.Text.Json.Nodes.JsonObject { ["name"] = call["function"]?["name"]?.DeepClone(), ["arguments"] = call["function"]?["arguments"]?.DeepClone() }
                    });
                kept["tool_calls"] = trimmed;
            }
            return kept.Count == 0 ? null : kept.ToJsonString();
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    // ---- Authors of user messages ----

    public async Task RecordAuthorAsync(Guid project, int chat, int messageId, Guid user)
    {
        await using var server = await factory.CreateDbContextAsync();
        if (await server.MessageAuthors.AnyAsync(x => x.ProjectId == project && x.MessageId == messageId)) return;
        server.MessageAuthors.Add(new MessageAuthor { ProjectId = project, ChatId = chat, MessageId = messageId, UserId = user });
        await server.SaveChangesAsync();
    }

    public async Task<int> MaxMessageIdAsync(Guid project, int chat)
    {
        if (!File.Exists(paths.ProjectDatabase(project))) return 0;
        await using var db = Open(project);
        return await db.Messages.AsNoTracking().Where(x => x.ChatId == chat).MaxAsync(x => (int?)x.Id) ?? 0;
    }

    /// <summary>After a run: the user messages added since <paramref name="after"/> that nobody claimed yet belong to the requester.</summary>
    public async Task FillAuthorsAsync(Guid project, int chat, int after, Guid user)
    {
        if (!File.Exists(paths.ProjectDatabase(project))) return;
        List<int> ids;
        await using (var db = Open(project)) ids = await db.Messages.AsNoTracking().Where(x => x.ChatId == chat && x.Role == "user" && x.Id > after).Select(x => x.Id).ToListAsync();
        foreach (var id in ids) await RecordAuthorAsync(project, chat, id, user);
    }

    // ---- Conversations ----

    public async Task RenameChatAsync(Guid project, int chat, string? title)
    {
        title = (title ?? "").Trim();
        if (title.Length is 0 or > 120) throw ApiException.BadRequest("invalid_title", "A conversation title has 1 to 120 characters.");
        await using (var db = Open(project))
        {
            var found = await db.Chats.SingleOrDefaultAsync(x => x.Id == chat) ?? throw ApiException.NotFound("Conversation not found.");
            if (found.Title == title) return;
            found.Title = title;
            await db.SaveChangesAsync();
        }
        await signals.ProjectAsync(project, "chats", chat);
    }

    static string Reasoning(string wire)
    {
        if (string.IsNullOrEmpty(wire) || !wire.Contains("reasoning_content", StringComparison.Ordinal)) return "";
        try { return System.Text.Json.Nodes.JsonNode.Parse(wire)?["reasoning_content"]?.GetValue<string>() ?? ""; }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return ""; }
    }

    public async Task<int> EngineProviderIdAsync(Guid project)
    {
        await using var db = Open(project);
        return await db.Providers.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
    }

    /// <summary>Deletes the whole project folder after verifying that it really is a project folder below the workspace.</summary>
    public void Destroy(Guid project)
    {
        var root = Path.GetFullPath(paths.ProjectRoot(project));
        var parent = Path.GetFullPath(paths.Projects);
        if (!root.StartsWith(parent + Path.DirectorySeparatorChar, PlatformSupport.PathComparison) || !Directory.Exists(root)) return;
        SqliteConnectionRelease();
        // A link planted in the project must never make the deletion reach its target: remove links themselves first.
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, ReturnSpecialDirectories = false }).ToList())
        {
            if (!entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            if (entry is DirectoryInfo) Directory.Delete(entry.FullName, false); else File.Delete(entry.FullName);
        }
        Directory.Delete(root, true);
    }

    // Pooled SQLite connections would otherwise keep the database file locked on Windows.
    static void SqliteConnectionRelease() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
}
