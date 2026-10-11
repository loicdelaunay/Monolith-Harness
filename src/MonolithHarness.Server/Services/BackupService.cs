using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

public sealed record BackupInfo(string Name, long Bytes, DateTime CreatedUtc);

/// <summary>
/// Consistent snapshots of the server database (accounts, roles, settings, provider catalog, audit). Project files and
/// conversations live in the workspace folder, which is backed up by copying the folder while the server is stopped.
/// </summary>
public sealed class BackupService(ServerPaths paths, IDbContextFactory<ServerDb> factory, AuditService audit)
{
    public async Task<BackupInfo> CreateAsync()
    {
        Directory.CreateDirectory(paths.Backups);
        var name = $"database-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sqlite";
        var target = Path.Combine(paths.Backups, name);
        await using (var db = await factory.CreateDbContextAsync())
            await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", target);
        var info = new FileInfo(target);
        await audit.WriteAsync("backup.created", name, $"{info.Length} bytes");
        return new BackupInfo(name, info.Length, info.CreationTimeUtc);
    }

    public List<BackupInfo> List() => !Directory.Exists(paths.Backups) ? [] :
        new DirectoryInfo(paths.Backups).EnumerateFiles("database-*.sqlite").OrderByDescending(x => x.Name).Select(x => new BackupInfo(x.Name, x.Length, x.CreationTimeUtc)).ToList();

    public async Task DeleteAsync(string name)
    {
        File.Delete(Resolve(name));
        await audit.WriteAsync("backup.deleted", name);
    }

    public string Resolve(string name)
    {
        if (name != Path.GetFileName(name) || !name.StartsWith("database-", StringComparison.Ordinal) || !name.EndsWith(".sqlite", StringComparison.Ordinal))
            throw ApiException.BadRequest("invalid_backup", "Invalid backup name.");
        var path = Path.Combine(paths.Backups, name);
        return File.Exists(path) ? path : throw ApiException.NotFound("Backup not found.");
    }

    /// <summary>Offline operation (<c>restore --from</c>): the server must be stopped. The previous database is kept beside the new one.</summary>
    public static string Restore(ServerPaths paths, string source)
    {
        source = Path.GetFullPath(source);
        if (!File.Exists(source)) throw new FileNotFoundException("Backup file not found.", source);
        using (var probe = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={source};Mode=ReadOnly;Pooling=False"))
        {
            probe.Open();
            using var command = probe.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            if (!"ok".Equals(command.ExecuteScalar()?.ToString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The backup failed its integrity check.");
        }
        string? kept = null;
        if (File.Exists(paths.Database))
        {
            kept = paths.Database + ".before-restore-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            File.Move(paths.Database, kept);
        }
        foreach (var suffix in new[] { "-wal", "-shm" }) if (File.Exists(paths.Database + suffix)) File.Delete(paths.Database + suffix);
        File.Copy(source, paths.Database);
        return kept ?? "";
    }
}
