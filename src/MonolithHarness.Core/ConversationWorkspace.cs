namespace MonolithHarness.Core;

/// <summary>Private portable file workspace for a saved conversation without a folder resource.</summary>
public static class ConversationWorkspace
{
    public static string DirectoryPath(int chatId, string? database = null)
    {
        if (chatId <= 0) throw new ArgumentException("A saved conversation is required.", nameof(chatId));
        var root = Path.GetDirectoryName(Path.GetFullPath(database ?? HarnessDb.DatabasePath))!;
        return Path.Combine(PortableStorage.Folder("conversations", root), "chat-" + chatId);
    }
    public static bool IsDirectory(string path, int chatId, string? database = null) => chatId > 0
        && PlatformSupport.PathComparer.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar), DirectoryPath(chatId, database));
    public static string Ensure(int chatId, string? database = null)
    {
        var path = DirectoryPath(chatId, database); SandboxWorkspace.AssertNoLinks(path);
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }
}
