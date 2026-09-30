using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MonolithHarness.Core;

public static class DatabaseMaintenance
{
    public static async Task OptimizeAsync(string path, CancellationToken ct = default)
    {
        // Run outside transactions: VACUUM needs an exclusive SQLite write lock.
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 10 }.ToString());
        await connection.OpenAsync(ct);
        foreach (var sql in new[] { "PRAGMA wal_checkpoint(TRUNCATE)", "VACUUM", "PRAGMA optimize" })
        {
            await using var command = connection.CreateCommand(); command.CommandText = sql;
            await command.ExecuteNonQueryAsync(ct);
        }
    }
    public static async Task ResetProjectDataAsync(string path, CancellationToken ct = default)
    {
        await using var db = new HarnessDb(path);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.TokenUsages.ExecuteDeleteAsync(ct);
        await db.Memories.ExecuteDeleteAsync(ct);
        await db.PendingInputs.ExecuteDeleteAsync(ct);
        await db.Subagents.ExecuteDeleteAsync(ct);
        await db.ScheduledTasks.ExecuteDeleteAsync(ct);
        await db.RagChunks.ExecuteDeleteAsync(ct);
        await db.ExternalChatSessions.ExecuteDeleteAsync(ct);
        await db.Set<Attachment>().ExecuteDeleteAsync(ct);
        await db.Messages.ExecuteDeleteAsync(ct);
        await db.Chats.ExecuteDeleteAsync(ct);
        await db.Projects.ExecuteDeleteAsync(ct);
        await db.Templates.ExecuteDeleteAsync(ct);
        await db.PermissionGrants.ExecuteDeleteAsync(ct);
        var state = await db.States.SingleAsync(ct);
        state.ProjectId = state.ChatId = null;
        var settings = FeatureSettings.Read(state.FeaturesJson); settings.ChatGoals.Clear(); settings.ConsumptionLegacyMessageId = 0; state.FeaturesJson = settings.Json();
        db.Projects.AddRange(new Project { Name = "Espace personnel", Chats = [new Chat()] }, new Project { Name = "Conversations", IsInbox = true });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
