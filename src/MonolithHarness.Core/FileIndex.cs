using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MonolithHarness.Core;

public sealed class FileIndexEntry
{
    public string Path { get; set; } = "";
    public string Kind { get; set; } = "file";
    public long Size { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Description { get; set; } = "";
    public string GeneratedDescription { get; set; } = "";
    public string DescriptionSource { get; set; } = "automatic";
    public bool DescriptionStale { get; set; }
}

public sealed class FileIndexDocument
{
    public string Format { get; set; } = FileIndex.FormatName;
    public int Version { get; set; } = 1;
    public DateTime UpdatedUtc { get; set; }
    public bool AutoUpdate { get; set; } = true;
    public List<FileIndexEntry> Entries { get; set; } = [];
}

public static class FileIndex
{
    public const string FileName = "index.ohm";
    public const string FormatName = "monolithharness.file-index";
    public const int MaximumEntries = 10000;
    const int MaximumBytes = 16 * 1024 * 1024;
    internal static readonly SemaphoreSlim Gate = new(1, 1);
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static async Task<FileIndexDocument?> LoadAsync(SourceAccess source, string folder, CancellationToken ct)
    {
        var path = source.Resolve(System.IO.Path.Combine(folder, FileName));
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > MaximumBytes) throw new IOException("index.ohm dépasse 16 Mio.");
        var document = JsonSerializer.Deserialize<FileIndexDocument>(await File.ReadAllTextAsync(path, ct), JsonOptions);
        if (document == null || (document.Format != FormatName && document.Format != LegacyCompatibility.FileIndexFormat) || document.Version != 1 || document.Entries == null || document.Entries.Count > MaximumEntries)
            throw new InvalidDataException("Format index.ohm inconnu : le fichier existant est conservé.");
        if (document.Entries.Any(x => x == null || string.IsNullOrWhiteSpace(x.Path) || System.IO.Path.IsPathRooted(x.Path) || x.Path.Split('/').Contains("..") ||
            x.Kind is not ("file" or "directory") || x.DescriptionSource is not ("automatic" or "model") || x.Description == null || x.Description.Length > 1500 || x.GeneratedDescription == null || x.Fingerprint == null) ||
            document.Entries.Select(x => x.Path).Distinct(PlatformSupport.PathComparer).Count() != document.Entries.Count)
            throw new InvalidDataException("Chemins invalides dans index.ohm.");
        return document;
    }

    internal static async Task SaveAsync(SourceAccess source, string folder, FileIndexDocument document, CancellationToken ct)
    {
        var path = source.Resolve(System.IO.Path.Combine(folder, FileName));
        document.Format = FormatName;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > MaximumBytes) throw new IOException("Index trop volumineux : choisissez un sous-dossier.");
        var temporary = source.Resolve(System.IO.Path.Combine(folder, ".omh-index-" + Guid.NewGuid().ToString("N")));
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            ct.ThrowIfCancellationRequested(); source.Resolve(path); source.Resolve(temporary);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static Task<FileIndexDocument> ScanAsync(SourceAccess source, string folder, FileIndexDocument? before, CancellationToken ct) => Task.Run(() =>
    {
        source.Resolve(folder); SandboxWorkspace.AssertNoLinks(folder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        var old = before?.Entries.ToDictionary(x => x.Path, PlatformSupport.PathComparer) ?? new(PlatformSupport.PathComparer);
        var entries = new List<FileIndexEntry>();
        var pending = new Stack<string>(); pending.Push(folder);
        while (pending.TryPop(out var path))
        {
            ct.ThrowIfCancellationRequested();
            if (entries.Count >= MaximumEntries) throw new IOException("Plus de 10 000 entrées : indexez un sous-dossier. L’index existant est conservé.");
            var relative = System.IO.Path.GetRelativePath(folder, path).Replace('\\', '/');
            source.Resolve(path);
            var directory = Directory.Exists(path);
            var entry = new FileIndexEntry { Path = relative, Kind = directory ? "directory" : "file" };
            entries.Add(entry);
            if (directory)
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(path).OrderDescending(PlatformSupport.PathComparer))
                {
                    var name = System.IO.Path.GetFileName(child);
                    if (name.Equals(FileName, StringComparison.OrdinalIgnoreCase) || name.StartsWith(".omh-index-", StringComparison.Ordinal)) continue;
                    try { source.Resolve(child); }
                    catch (UnauthorizedAccessException) { continue; }
                    pending.Push(child);
                }
            }
            else
            {
                var info = new FileInfo(path); entry.Size = info.Length; entry.ModifiedUtc = info.LastWriteTimeUtc;
                if (info.Length <= 128000)
                {
                    var bytes = File.ReadAllBytes(source.Resolve(path));
                    entry.Fingerprint = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    entry.GeneratedDescription = Describe(path, SourceText.TryDecode(bytes, out var decoded) ? decoded.Content : null, bytes.Length);
                }
                else
                {
                    entry.Fingerprint = Hash($"large:{entry.Size}:{entry.ModifiedUtc.Ticks}");
                    entry.GeneratedDescription = Describe(path, null, entry.Size) + " Contenu non analysé (>128 Ko).";
                }
            }
        }
        // Directory fingerprints incorporate descendants, so moved/deleted files invalidate descriptions too.
        var byParent = entries.Where(x => x.Path != ".").GroupBy(x => Parent(x.Path), PlatformSupport.PathComparer)
            .ToDictionary(x => x.Key, x => x.OrderBy(item => item.Path, PlatformSupport.PathComparer).ToList(), PlatformSupport.PathComparer);
        foreach (var entry in entries.OrderByDescending(x => x.Path.Count(c => c == '/')).ThenByDescending(x => x.Path.Length))
        {
            if (entry.Kind == "directory")
            {
                var children = byParent.GetValueOrDefault(entry.Path) ?? [];
                entry.Fingerprint = Hash(string.Join('\n', children.Select(x => x.Path + ":" + x.Fingerprint)));
                entry.GeneratedDescription = $"Dossier : {children.Count(x => x.Kind == "directory")} sous-dossier(s), {children.Count(x => x.Kind == "file")} fichier(s)." +
                    (children.Count == 0 ? " Vide." : " Contient : " + string.Join(", ", children.Take(12).Select(x => System.IO.Path.GetFileName(x.Path))) + (children.Count > 12 ? "…" : "."));
            }
            entry.GeneratedDescription = entry.GeneratedDescription[..Math.Min(entry.GeneratedDescription.Length, 1500)];
            entry.Description = entry.GeneratedDescription;
            if (old.TryGetValue(entry.Path, out var previous) && previous.Kind == entry.Kind && previous.DescriptionSource == "model")
            {
                entry.Description = previous.Description; entry.DescriptionSource = "model";
                entry.DescriptionStale = previous.DescriptionStale || previous.Fingerprint != entry.Fingerprint;
            }
        }
        return new FileIndexDocument { UpdatedUtc = DateTime.UtcNow, AutoUpdate = before?.AutoUpdate ?? true, Entries = entries.OrderBy(x => x.Path, PlatformSupport.PathComparer).ToList() };
    }, ct);

    internal static string Parent(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : ".";
    static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    static string Describe(string path, string? text, long size)
    {
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var type = extension switch {
            ".cs" => "Code C#", ".js" or ".mjs" or ".cjs" => "Code JavaScript", ".ts" or ".tsx" => "Code TypeScript", ".py" => "Script Python",
            ".md" or ".txt" or ".rst" => "Documentation", ".json" => "Données/configuration JSON", ".yml" or ".yaml" or ".toml" or ".ini" => "Configuration",
            ".csproj" or ".sln" or ".props" or ".targets" => "Projet/configuration .NET", ".html" or ".xaml" or ".vue" => "Interface", ".css" or ".scss" => "Styles",
            ".png" or ".jpg" or ".jpeg" or ".svg" or ".ico" or ".webp" => "Image", ".sh" or ".ps1" or ".bat" => "Script de commandes",
            _ => string.IsNullOrEmpty(extension) ? "Fichier" : "Fichier " + extension };
        if (text == null) return $"{type} · {size} octets.";
        var details = new List<string>();
        if (extension == ".json")
        {
            try { using var json = JsonDocument.Parse(text); if (json.RootElement.ValueKind == JsonValueKind.Object) details.Add("Clés : " + string.Join(", ", json.RootElement.EnumerateObject().Take(12).Select(x => x.Name))); }
            catch (JsonException) { }
        }
        if (extension is ".md" or ".txt" or ".rst")
        {
            var heading = text.Split('\n').FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim().TrimStart('#', ' ');
            if (!string.IsNullOrEmpty(heading)) details.Add(heading[..Math.Min(heading.Length, 240)]);
        }
        try
        {
            var symbols = Regex.Matches(text[..Math.Min(text.Length, 64000)], @"\b(?:class|interface|enum|record|struct|def|function)\s+([A-Za-z_$][\w$]*)", RegexOptions.None, TimeSpan.FromMilliseconds(100))
                .Select(x => x.Groups[1].Value).Distinct().Take(12).ToArray();
            if (symbols.Length > 0) details.Add("Déclarations : " + string.Join(", ", symbols));
        }
        catch (RegexMatchTimeoutException) { }
        return type + $" · {text.Count(c => c == '\n') + 1} ligne(s)." + (details.Count == 0 ? "" : " " + string.Join(". ", details));
    }
}
