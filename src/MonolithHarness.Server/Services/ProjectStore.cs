using Microsoft.EntityFrameworkCore;
using MonolithHarness.Core;

namespace MonolithHarness.Server.Services;

public sealed record ChatView(int Id, string Title, DateTime? UpdatedUtc);

public sealed record MessageItem(int Id, string Role, string Content, string State, DateTime? CompletedUtc, int? InputTokens, int? OutputTokens,
    int? CachedInputTokens = null, double Seconds = 0, string Reasoning = "");

/// <summary>
/// Access to the engine database of each cloud project (conversations and messages). One SQLite database per project keeps
/// conversations isolated; the server reads it directly and the project's worker writes to it during a run.
/// </summary>
public sealed class ProjectStore(ServerPaths paths)
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
        return rows.Select(x => new MessageItem(x.Id, x.Role, x.Content, x.State, x.CompletedUtc, x.InputTokens, x.OutputTokens, x.CachedInputTokens, x.Seconds, Reasoning(x.WireJson))).ToList();
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
