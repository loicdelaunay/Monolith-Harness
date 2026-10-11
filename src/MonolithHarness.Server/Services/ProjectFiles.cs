using System.Text.RegularExpressions;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

public sealed record FileEntry(string Name, string Path, bool IsDirectory, long Size, DateTime ModifiedUtc);

/// <summary>
/// File operations confined to one project folder: no path traversal, no links, no device names, size and disk quotas.
/// Every path is a relative, slash-separated project path.
/// </summary>
public sealed partial class ProjectFiles(ServerPaths paths, SettingsService settings)
{
    public const long MaxFileBytes = 64L * 1024 * 1024;

    [GeneratedRegex(@"^(con|prn|aux|nul|com[0-9]|lpt[0-9])(\..*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Reserved();

    public string Root(Guid project) => Path.GetFullPath(paths.ProjectFiles(project));

    /// <summary>Validates a relative path and returns its absolute location inside the project, or throws.</summary>
    public string Resolve(Guid project, string? relative)
    {
        var root = Root(project);
        relative = (relative ?? "").Replace('\\', '/').Trim('/');
        if (relative.Length == 0) return root;
        var segments = relative.Split('/');
        foreach (var segment in segments)
            if (segment is "" or "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.EndsWith(' ') || segment.EndsWith('.') || Reserved().IsMatch(segment))
                throw ApiException.BadRequest("invalid_path", "The path is not allowed.");
        var full = Path.GetFullPath(Path.Combine(root, string.Join(Path.DirectorySeparatorChar, segments)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, Core.PlatformSupport.PathComparison)) throw ApiException.BadRequest("invalid_path", "The path is not allowed.");
        // A link planted inside the project (by a tool, for example) must never lead outside it.
        var current = root;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if ((File.Exists(current) || Directory.Exists(current)) && new FileInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw ApiException.BadRequest("invalid_path", "Links are not allowed in a project.");
        }
        return full;
    }

    public List<FileEntry> List(Guid project, string? directory)
    {
        var path = Resolve(project, directory);
        if (!Directory.Exists(path)) throw ApiException.NotFound("Folder not found.");
        var root = Root(project);
        return new DirectoryInfo(path).EnumerateFileSystemInfos()
            .Where(x => !x.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .OrderByDescending(x => x is DirectoryInfo).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new FileEntry(x.Name, Path.GetRelativePath(root, x.FullName).Replace('\\', '/'), x is DirectoryInfo, x is FileInfo f ? f.Length : 0, x.LastWriteTimeUtc)).ToList();
    }

    public FileStream OpenRead(Guid project, string? file)
    {
        var path = Resolve(project, file);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, true) : throw ApiException.NotFound("File not found.");
    }

    public async Task WriteAsync(Guid project, string? file, Stream content, CancellationToken ct)
    {
        var path = Resolve(project, file);
        if (path == Root(project)) throw ApiException.BadRequest("invalid_path", "A file name is required.");
        if (Directory.Exists(path)) throw ApiException.Conflict("is_directory", "A folder already uses this name.");
        var quota = settings.Int(SettingKeys.ProjectDiskMb) * 1024L * 1024L;
        var replaced = File.Exists(path) ? new FileInfo(path).Length : 0;
        var available = quota - (Size(project) - replaced);
        if (available <= 0) throw new ApiException(413, "quota_exceeded", "The project disk quota is exhausted.");
        var limit = Math.Min(MaxFileBytes, available);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Resolve again after creating folders: the final location must still be free of links.
        path = Resolve(project, file);
        var temporary = path + ".upload-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
            {
                var chunk = new byte[64 * 1024];
                long total = 0;
                int read;
                while ((read = await content.ReadAsync(chunk, ct)) > 0)
                {
                    total += read;
                    if (total > limit) throw new ApiException(413, total > MaxFileBytes ? "file_too_large" : "quota_exceeded", total > MaxFileBytes ? "The file is too large." : "The project disk quota would be exceeded.");
                    await output.WriteAsync(chunk.AsMemory(0, read), ct);
                }
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void CreateDirectory(Guid project, string? directory)
    {
        var path = Resolve(project, directory);
        if (path == Root(project)) return;
        if (File.Exists(path)) throw ApiException.Conflict("is_file", "A file already uses this name.");
        Directory.CreateDirectory(path);
    }

    public void Delete(Guid project, string? target)
    {
        var path = Resolve(project, target);
        if (path == Root(project)) throw ApiException.BadRequest("invalid_path", "The project root cannot be deleted.");
        if (File.Exists(path)) File.Delete(path);
        else if (Directory.Exists(path)) Directory.Delete(path, true);
        else throw ApiException.NotFound("Not found.");
    }

    /// <summary>Total size of regular files; links are ignored so they can neither inflate nor hide usage.</summary>
    public long Size(Guid project)
    {
        var root = Root(project);
        if (!Directory.Exists(root)) return 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        return new DirectoryInfo(root).EnumerateFiles("*", options).Sum(x => x.Length);
    }
}
