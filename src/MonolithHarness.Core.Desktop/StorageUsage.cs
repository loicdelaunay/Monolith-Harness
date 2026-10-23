namespace MonolithHarness.Core;

public enum StorageCategory { Database, Models, Runtimes, Browser, Attachments, Workspaces, Skills, Logs, Cache, Other }
public sealed record StorageUsageRow(StorageCategory Category, long Bytes, long Files);
public sealed record StorageUsageSnapshot(string Root, StorageUsageRow[] Rows, int Skipped, DateTime MeasuredUtc)
{
    public long TotalBytes => Rows.Sum(row => row.Bytes);
}

/// <summary>On-demand file metadata scan; never reads file contents or follows links.</summary>
public static class StorageUsage
{
    public static Task<StorageUsageSnapshot> MeasureAsync(string database, CancellationToken ct) => Task.Run(() => Measure(database, ct), ct);
    static StorageUsageSnapshot Measure(string database, CancellationToken ct)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(database))!;
        var totals = Enum.GetValues<StorageCategory>().ToDictionary(category => category, _ => (Bytes: 0L, Files: 0L));
        int skipped = 0;
        var visited = new HashSet<string>(PlatformSupport.PathComparer);
        void Add(FileInfo file, StorageCategory category)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; return; }
                if (!visited.Add(file.FullName)) return;
                var value = totals[category]; totals[category] = (value.Bytes + file.Length, value.Files + 1);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
        }
        static StorageCategory Category(string relative)
        {
            var parts = relative.Replace('\\', '/').Split('/');
            var name = parts[0].ToLowerInvariant();
            if (name == "legacy-imports" && parts.Length > 2) name = parts[2].ToLowerInvariant();
            return name switch
            {
                "model" or "models" => StorageCategory.Models,
                "runtimes" => StorageCategory.Runtimes,
                "chrome" or "browser" or "webview2" => StorageCategory.Browser,
                "images" or "captures" or "assets" => StorageCategory.Attachments,
                "opencodeworkspaces" or "acpworkspaces" or "conversations" or "worktrees" or "sandboxes" or "proposals" => StorageCategory.Workspaces,
                "skills" or "scripts" => StorageCategory.Skills,
                "logs" => StorageCategory.Logs,
                "temp" => StorageCategory.Cache,
                _ => StorageCategory.Other
            };
        }
        void Scan(string path, string relative)
        {
            var pending = new Stack<(string Path, string Relative)>(); pending.Push((path, relative));
            while (pending.TryPop(out var item))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var directory = new DirectoryInfo(item.Path);
                    if (!directory.Exists) continue;
                    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                    if (!visited.Add(directory.FullName)) continue;
                    foreach (var entry in directory.EnumerateFileSystemInfos())
                    {
                        ct.ThrowIfCancellationRequested();
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                        var child = item.Relative.Length == 0 ? entry.Name : item.Relative + "/" + entry.Name;
                        if (entry is DirectoryInfo) pending.Push((entry.FullName, child));
                        else if (entry is FileInfo file) Add(file, Category(child));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
            }
        }
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var file = new FileInfo(database + suffix); if (file.Exists) Add(file, StorageCategory.Database);
        }
        Scan(Path.Combine(root, "workspace"), "");
        // Include unmigrated application data without traversing arbitrary project folders.
        foreach (var folder in new[] { "skills", "scripts", "temp", "exports", "terminal-profiles", "fonts", "OpenCodeWorkspaces", "AcpWorkspaces", "branding", "naming", "images", "logs", "Chrome", "Browser", "WebView2", "Captures", "model", "models", "sandboxes", "worktrees", "proposals", "runtimes", "assets", "conversations" })
            Scan(Path.Combine(root, folder), folder);
        foreach (var name in new[] { "MCP.json", "mcp.json", "server.json", "server-link.json" })
        {
            var file = new FileInfo(Path.Combine(root, name)); if (file.Exists) Add(file, StorageCategory.Other);
        }
        return new(root, totals.Select(pair => new StorageUsageRow(pair.Key, pair.Value.Bytes, pair.Value.Files)).OrderByDescending(row => row.Bytes).ToArray(), skipped, DateTime.UtcNow);
    }
}
