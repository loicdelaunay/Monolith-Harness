using System.Collections.Concurrent;
using System.Text.Json;

namespace MonolithHarness.Core;

public static partial class PortableStorage
{
    static readonly string[] OwnedFolders = ["skills", "temp", "exports", "terminal-profiles", "fonts",
        "OpenCodeWorkspaces", "AcpWorkspaces", "branding", "naming", "images", "scripts", "logs", "Chrome", "Browser",
        "WebView2", "Captures", "model", "models", "sandboxes", "worktrees", "runtimes/python"];
    static readonly ConcurrentDictionary<string, (DateTime Stamp, Dictionary<string, string> Paths)> layouts = new(PlatformSupport.PathComparer);
    static string LayoutFile(string root) => Path.Combine(root, "workspace", ".storage-layout.json");
    static string Normalize(string path) => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
    static bool Within(string path, string root) => PlatformSupport.PathComparer.Equals(path, root)
        || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, PlatformSupport.PathComparison);
    static bool Owned(string relative) => OwnedFolders.Any(x => Within(Normalize(relative), Normalize(x)))
        || Within(Normalize(relative), "assets") || Within(Normalize(relative), "conversations");

    /// <summary>Application resources remain beside the selected database, under one workspace directory.</summary>
    public static string Folder(string relative, string? root = null)
    {
        root = Path.GetFullPath(root ?? Root); relative = Normalize(relative);
        if (!Owned(relative) || Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Any(x => x is "." or ".."))
            throw new ArgumentException("Unknown portable resource folder.", nameof(relative));
        var source = Path.Combine(root, relative);
        var paths = ReadLayout(root);
        foreach (var item in paths.OrderByDescending(x => x.Key.Length))
        {
            var previous = Path.Combine(root, Normalize(item.Key));
            if (!Within(source, previous)) continue;
            if (Directory.Exists(previous)) return source; // An interrupted/locked move remains usable until retry.
            return Path.GetFullPath(Path.GetRelativePath(previous, source), Path.Combine(root, Normalize(item.Value)));
        }
        return Path.Combine(root, "workspace", relative);
    }

    /// <summary>Resolve saved legacy paths without changing external files or historical message text.</summary>
    public static string ResolvePath(string path, string? root = null)
    {
        root = Path.GetFullPath(root ?? Root);
        var full = Path.GetFullPath(Normalize(path), root);
        if (!Within(full, root) || Within(full, Path.Combine(root, "workspace"))) return full;
        var relative = Path.GetRelativePath(root, full);
        if (!Owned(relative)) return full;
        var mapped = Folder(relative, root);
        // Preserve installed resources and files that were not part of a completed migration.
        return Path.Exists(full) ? full : mapped;
    }

    public static string ResolveArgument(string value, string? root = null)
    {
        var split = value.StartsWith("--", StringComparison.Ordinal) ? value.IndexOf('=') : -1;
        var path = split >= 0 ? value[(split + 1)..] : value;
        if (!Path.IsPathFullyQualified(Normalize(path))) return value;
        try { return (split >= 0 ? value[..(split + 1)] : "") + ResolvePath(path, root); }
        catch (ArgumentException) { return value; }
    }

    static Dictionary<string, string> ReadLayout(string root)
    {
        var file = LayoutFile(root); var stamp = File.GetLastWriteTimeUtc(file);
        if (layouts.TryGetValue(root, out var cached) && cached.Stamp == stamp) return cached.Paths;
        SandboxWorkspace.AssertNoLinks(file);
        var paths = File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? [] : [];
        foreach (var item in paths)
        {
            if (!Owned(item.Key) || Path.IsPathRooted(Normalize(item.Key)) || Normalize(item.Key).Split(Path.DirectorySeparatorChar).Any(x => x is "." or "..")
                || !Within(Path.GetFullPath(Normalize(item.Value), root), Path.Combine(root, "workspace")))
                throw new IOException("Invalid portable workspace migration manifest.");
        }
        layouts[root] = (stamp, paths);
        return paths;
    }

    public static void MigrateLayout(string? root = null)
    {
        root = Path.GetFullPath(root ?? Root);
        var workspace = Path.Combine(root, "workspace"); SandboxWorkspace.AssertNoLinks(workspace);
        Directory.CreateDirectory(workspace);
        var lockPath = Path.Combine(workspace, ".storage-layout.lock"); SandboxWorkspace.AssertNoLinks(lockPath);
        using var guard = Lock(lockPath);
        layouts.TryRemove(root, out _);
        var paths = new Dictionary<string, string>(ReadLayout(root), PlatformSupport.PathComparer);
        void Save()
        {
            var file = LayoutFile(root); var staging = file + ".write-" + Guid.NewGuid().ToString("N");
            try { File.WriteAllText(staging, JsonSerializer.Serialize(paths)); File.Move(staging, file, true); }
            finally { if (File.Exists(staging)) File.Delete(staging); }
            layouts[root] = (File.GetLastWriteTimeUtc(file), new(paths, PlatformSupport.PathComparer));
        }
        void Move(string relative)
        {
            var source = Path.Combine(root, Normalize(relative));
            if (!Directory.Exists(source)) return;
            SandboxWorkspace.AssertNoLinks(source);
            var target = paths.TryGetValue(relative, out var pending) ? Path.GetFullPath(Normalize(pending), root) : Path.Combine(workspace, Normalize(relative));
            if (Directory.Exists(target) || File.Exists(target))
                target = Path.Combine(workspace, "legacy-imports", Guid.NewGuid().ToString("N"), Normalize(relative));
            paths[relative] = Path.GetRelativePath(root, target); Save(); // Journal before the atomic directory rename.
            SandboxWorkspace.AssertNoLinks(target); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try { Directory.Move(source, target); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        }
        foreach (var name in OwnedFolders)
        {
            // Development builds can have installed font/native resources in the executable directory.
            if (name == "fonts" && PlatformSupport.PathComparer.Equals(root.TrimEnd(Path.DirectorySeparatorChar), AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))
                && Directory.Exists(Path.Combine(root, "fonts")) && Directory.EnumerateFiles(Path.Combine(root, "fonts"), "*.ttf").Any()) continue;
            Move(name);
        }
        var assets = Path.Combine(root, "assets");
        if (Directory.Exists(assets))
            foreach (var folder in Directory.EnumerateDirectories(assets))
                if (Path.GetFileName(folder).StartsWith("chat-", StringComparison.Ordinal)
                    && int.TryParse(Path.GetFileName(folder)[5..], out var id) && id > 0) Move("assets/" + Path.GetFileName(folder));
        // A repeated launcher publication may supply the bundled skill template again. Keep user skills visible.
        if (paths.TryGetValue("skills", out var skillPath) && !PlatformSupport.PathComparer.Equals(Normalize(skillPath), Normalize("workspace/skills")))
        {
            var imported = Path.GetFullPath(Normalize(skillPath), root); var canonical = Path.Combine(workspace, "skills");
            if (Directory.Exists(imported) && !Directory.Exists(Path.Combine(root, "skills")))
            {
                Directory.CreateDirectory(canonical);
                foreach (var entry in Directory.EnumerateFileSystemEntries(imported))
                {
                    var relative = "skills/" + Path.GetFileName(entry); var target = Path.Combine(canonical, Path.GetFileName(entry));
                    if (Path.Exists(target)) { paths[relative] = Path.GetRelativePath(root, entry); Save(); continue; }
                    paths[relative] = Path.GetRelativePath(root, target); Save();
                    if (Directory.Exists(entry)) Directory.Move(entry, target); else File.Move(entry, target);
                }
                paths["skills"] = "workspace/skills"; Save();
            }
        }
        foreach (var name in new[] { "assets", "runtimes" })
        {
            var folder = Path.Combine(root, name);
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        static FileStream Lock(string path)
        {
            for (var attempt = 0; ; attempt++)
            {
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (attempt < 50) { Thread.Sleep(50); }
            }
        }
    }
}
