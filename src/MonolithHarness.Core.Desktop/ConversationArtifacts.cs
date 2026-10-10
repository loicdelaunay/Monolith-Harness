namespace MonolithHarness.Core;

/// <summary>Disposable tool results only; separate from source folders and conversation documents.</summary>
public static class ConversationArtifacts
{
    internal static readonly SemaphoreSlim Gate = new(1, 1);
    static string Root(string? database) => Path.Combine(PortableStorage.Folder("temp",
        Path.GetDirectoryName(Path.GetFullPath(database ?? HarnessDb.DatabasePath))!), "tool-artifacts");
    public static string DirectoryPath(int chatId, string? database = null) => Path.Combine(Root(database), "chat-" + chatId, "web-http");

    public static async Task<int> CleanAsync(string database, FeatureSettings settings, IEnumerable<int> activeChatIds,
        DateTime nowUtc, CancellationToken ct = default)
    {
        if (!settings.AutoCleanArtifacts) return 0;
        if (settings.ArtifactRetentionDays is < 1 or > 3650) throw new ArgumentException("Invalid artifact retention period.");
        var excluded = activeChatIds.ToHashSet();
        var root = Path.GetFullPath(Root(database));
        if (!Directory.Exists(root)) return 0;
        var cutoff = nowUtc.AddDays(-settings.ArtifactRetentionDays);
        await Gate.WaitAsync(ct);
        try
        {
            return await Task.Run(() =>
            {
                int removed = 0;
                SandboxWorkspace.AssertNoLinks(root);
                foreach (var conversation in Directory.EnumerateDirectories(root))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(conversation);
                    if (name.StartsWith("chat-", StringComparison.Ordinal) && int.TryParse(name[5..], out var chatId))
                    { if (excluded.Contains(chatId)) continue; }
                    else if (!name.StartsWith("run-", StringComparison.Ordinal) || !Guid.TryParseExact(name[4..], "N", out _)) continue;
                    try
                    {
                        var pages = Path.Combine(conversation, "web-http");
                        SandboxWorkspace.AssertNoLinks(pages);
                        if (!Directory.Exists(pages)) continue;
                        foreach (var directory in Directory.EnumerateDirectories(pages))
                        {
                            ct.ThrowIfCancellationRequested();
                            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
                            try
                            {
                                // Validate the absolute target and every entry before deleting known cache files.
                                var target = Path.GetFullPath(directory);
                                if (!target.StartsWith(root + Path.DirectorySeparatorChar, PlatformSupport.PathComparison)) continue;
                                SandboxWorkspace.AssertNoLinks(target);
                                if (Directory.GetLastWriteTimeUtc(target) > cutoff || Directory.EnumerateDirectories(target).Any()) continue;
                                var files = Directory.GetFiles(target);
                                if (files.Any(f => Path.GetFileName(f) is not ("response.json" or "raw.txt" or "content.txt"))) continue;
                                foreach (var file in files) SandboxWorkspace.AssertNoLinks(file);
                                foreach (var file in files) File.Delete(file);
                                Directory.Delete(target); // Never recursively delete an arbitrary directory.
                                removed++;
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                if (removed > 0) AppLog.Write(AppLogLevel.Information, "artifacts.cleaned: " + removed);
                return removed;
            }, ct);
        }
        finally { Gate.Release(); }
    }
}
