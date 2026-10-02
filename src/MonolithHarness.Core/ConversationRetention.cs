using Microsoft.EntityFrameworkCore;

namespace MonolithHarness.Core;

public sealed record ConversationRetentionResult(IReadOnlyList<int> Archived, IReadOnlyList<int> Deleted, int ArtifactsRemoved = 0);

public static class ConversationRetention
{
    public static async Task<ConversationRetentionResult> ApplyAsync(string database, FeatureSettings settings, IEnumerable<int> excludedChatIds, DateTime nowUtc, CancellationToken ct = default)
    {
        if (!settings.AutoArchiveConversations && !settings.AutoDeleteConversations && !settings.AutoCleanArtifacts) return new([], []);
        settings.Json(); // Validate delays before performing any change.
        var excluded = excludedChatIds.Distinct().ToArray();
        int artifactsRemoved = await ConversationArtifacts.CleanAsync(database, settings, excluded, nowUtc, ct);
        if (!settings.AutoArchiveConversations && !settings.AutoDeleteConversations) return new([], [], artifactsRemoved);
        await using var db = new HarnessDb(database);
        await db.Database.OpenConnectionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON", ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Pending work, live generations and pinned/favorite conversations are preserved.
        var eligible = db.Chats.Where(c => !c.IsPinned && !c.IsFavorite && !excluded.Contains(c.Id) && c.UpdatedUtc != null
            && !db.PendingInputs.Any(p => p.ChatId == c.Id)
            && !db.Messages.Any(m => m.ChatId == c.Id && m.State == "streaming")
            && !db.Subagents.Any(a => a.ChatId == c.Id && (a.Status == "running" || a.Status == "queued")));
        List<int> archived = [], deleted = [];
        if (settings.AutoArchiveConversations)
        {
            var cutoff = nowUtc.AddDays(-settings.AutoArchiveDays);
            var rows = eligible.Where(c => !c.IsArchived && c.UpdatedUtc <= cutoff && !db.Messages.Any(m => m.ChatId == c.Id && m.CompletedUtc > cutoff));
            archived = await rows.Select(c => c.Id).ToListAsync(ct);
            await rows.ExecuteUpdateAsync(set => set.SetProperty(c => c.IsArchived, true), ct);
        }
        if (settings.AutoDeleteConversations)
        {
            var cutoff = nowUtc.AddDays(-settings.AutoDeleteDays);
            var rows = eligible.Where(c => c.IsArchived && c.UpdatedUtc <= cutoff && !db.Messages.Any(m => m.ChatId == c.Id && m.CompletedUtc > cutoff));
            deleted = await rows.Select(c => c.Id).ToListAsync(ct);
            await rows.ExecuteDeleteAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return new(archived.Except(deleted).ToArray(), deleted, artifactsRemoved);
    }
}
