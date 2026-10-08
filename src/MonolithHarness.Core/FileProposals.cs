using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public sealed record FileProposal(string Path, string ResolvedPath, byte[]? Original, string Content);
public sealed record ProposalBatch(string Revision, List<FileProposal> Files);
public static class FileProposals
{
    public const string Tool = "propose_file_changes";
    static readonly SemaphoreSlim gate = new(1, 1);
    static string Store(string database, int chatId) => System.IO.Path.Combine(PortableStorage.Folder("proposals", System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(database))!),
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(database))))[..16] + "-" + chatId + ".json");
    public static void AddDefinition(JsonArray tools)
    {
        tools.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = Tool,
            ["description"] = "Prepare a persistent proposal for human review. No project file is modified. Read originals first, then provide complete new UTF-8 text for 1..20 files within attached sources. User reviews a diff and applies selected files later.",
            ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["files"] = new JsonObject {
                ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 20, ["items"] = new JsonObject { ["type"] = "object",
                    ["properties"] = new JsonObject { ["path"] = new JsonObject { ["type"] = "string" }, ["content"] = new JsonObject { ["type"] = "string" } },
                    ["required"] = new JsonArray("path", "content"), ["additionalProperties"] = false } } }, ["required"] = new JsonArray("files"), ["additionalProperties"] = false } } });
    }
    public static async Task<ProposalBatch?> ReadAsync(string database, int chatId, CancellationToken ct = default)
    {
        var path = Store(database, chatId);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 6_000_000) throw new IOException("Proposition trop volumineuse / Proposal too large.");
        return JsonSerializer.Deserialize<ProposalBatch>(await File.ReadAllTextAsync(path, ct));
    }
    static async Task SaveBatch(string database, int chatId, ProposalBatch batch, CancellationToken ct)
    {
        var file = Store(database, chatId); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(batch), ct); File.Move(temp, file, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static async Task<string> PrepareAsync(string database, int chatId, SourceAccess access, JsonObject args, CancellationToken ct)
    {
        if (args["files"] is not JsonArray { Count: > 0 and <= 20 } files) throw new ArgumentException("1..20 fichiers requis / 1..20 files required.");
        await gate.WaitAsync(ct);
        try
        {
            var batch = await ReadAsync(database, chatId, ct) ?? new(Guid.NewGuid().ToString("N"), []);
            var drafts = batch.Files.ToList();
            foreach (var item in files)
            {
                var path = item?["path"]?.GetValue<string>() ?? ""; var content = item?["content"]?.GetValue<string>() ?? "";
                if (content.Length > 128_000) throw new ArgumentException("Fichier proposé limité à 128 000 caractères / Proposal exceeds 128,000 characters.");
                var full = access.Resolve(path); if (Directory.Exists(full)) throw new IOException("Un fichier est requis / Expected a file.");
                byte[]? original = null;
                if (File.Exists(full))
                {
                    if (new FileInfo(full).Length > 128_000) throw new IOException("Fichier source trop volumineux / Source file too large.");
                    original = await File.ReadAllBytesAsync(full, ct);
                    if (!SourceText.TryDecode(original, out _)) throw new IOException("Propositions limitées aux fichiers texte / Only text files can be proposed.");
                }
                drafts.RemoveAll(d => PlatformSupport.PathComparer.Equals(d.ResolvedPath, full)); drafts.Add(new(path, full, original, content));
            }
            if (drafts.Count > 20 || drafts.Sum(f => f.Content.Length + (f.Original?.Length ?? 0)) > 2_000_000) throw new IOException("Lot de propositions trop volumineux / Proposal batch too large.");
            var saved = new ProposalBatch(Guid.NewGuid().ToString("N"), drafts); await SaveBatch(database, chatId, saved, ct);
            return JsonSerializer.Serialize(new { status = "awaiting_user_review", modified_project_files = 0, files = saved.Files.Select(f => f.Path), revision = saved.Revision });
        }
        finally { gate.Release(); }
    }
    public static string Diff(FileProposal file)
    {
        var original = file.Original == null ? "" : SourceText.TryDecode(file.Original, out var decoded) ? decoded.Content : "";
        var before = original.Length == 0 ? Array.Empty<string>() : original.Replace("\r\n", "\n").Split('\n');
        var after = file.Content.Length == 0 ? Array.Empty<string>() : file.Content.Replace("\r\n", "\n").Split('\n');
        int prefix = 0, suffix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        var result = new StringBuilder("--- " + file.Path + (file.Original == null ? " (nouveau / new)" : "") + "\n+++ " + file.Path + "\n");
        if (prefix == before.Length && prefix == after.Length) return result.Append("Aucune différence textuelle / No text differences.").ToString();
        var contextStart = Math.Max(0, prefix - 3); var contextEnd = Math.Min(suffix, 3);
        result.Append($"@@ -{contextStart + 1},{before.Length - suffix - contextStart + contextEnd} +{contextStart + 1},{after.Length - suffix - contextStart + contextEnd} @@\n");
        for (int i = contextStart; i < prefix; i++) result.AppendLine("  " + before[i]);
        for (int i = prefix; i < before.Length - suffix; i++) result.AppendLine("- " + before[i]);
        for (int i = prefix; i < after.Length - suffix; i++) result.AppendLine("+ " + after[i]);
        for (int i = after.Length - suffix; i < after.Length - suffix + contextEnd; i++) result.AppendLine("  " + after[i]);
        return result.ToString();
    }
    public static async Task ApplyAsync(string database, int chatId, string revision, IEnumerable<string> paths, SourceAccess access, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var batch = await ReadAsync(database, chatId, ct) ?? throw new IOException("Aucune proposition / No proposal.");
            if (batch.Revision != revision) throw new IOException("La proposition a changé, rouvrez la revue / Proposal changed, reopen review.");
            var selected = paths.ToHashSet(StringComparer.Ordinal); var files = batch.Files.Where(f => selected.Contains(f.Path)).ToList();
            if (files.Count == 0 || files.Count != selected.Count) throw new ArgumentException("Sélection invalide / Invalid selection.");
            async Task Validate(FileProposal file)
            {
                if (!PlatformSupport.PathComparer.Equals(access.Resolve(file.Path), file.ResolvedPath)) throw new IOException("Dossier source modifié / Source scope changed.");
                if (File.Exists(file.ResolvedPath) && new FileInfo(file.ResolvedPath).Length > 128_000) throw new IOException("Fichier modifié depuis la proposition : " + file.Path);
                var current = File.Exists(file.ResolvedPath) ? await File.ReadAllBytesAsync(file.ResolvedPath, ct) : null;
                if (current == null != (file.Original == null) || current != null && !current.AsSpan().SequenceEqual(file.Original))
                    throw new IOException("Fichier modifié depuis la proposition : " + file.Path + " / File changed since proposal.");
            }
            foreach (var file in files) await Validate(file);
            var written = new List<(FileProposal File, byte[] Bytes)>();
            try
            {
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested(); await Validate(file);
                    var bytes = file.Original != null && SourceText.TryDecode(file.Original, out var text) ? text.Encode(file.Content) : new UTF8Encoding(false).GetBytes(file.Content);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file.ResolvedPath)!);
                    var temporary = file.ResolvedPath + ".monolith-" + Guid.NewGuid().ToString("N") + ".tmp";
                    try { await File.WriteAllBytesAsync(temporary, bytes, ct); await Validate(file); File.Move(temporary, file.ResolvedPath, true); written.Add((file, bytes)); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                await SaveBatch(database, chatId, new(Guid.NewGuid().ToString("N"), batch.Files.Except(files).ToList()), ct);
            }
            catch
            {
                foreach (var entry in written.AsEnumerable().Reverse())
                {
                    if (!File.Exists(entry.File.ResolvedPath) || !(await File.ReadAllBytesAsync(entry.File.ResolvedPath)).AsSpan().SequenceEqual(entry.Bytes)) continue;
                    if (entry.File.Original == null) File.Delete(entry.File.ResolvedPath);
                    else await File.WriteAllBytesAsync(entry.File.ResolvedPath, entry.File.Original);
                }
                throw;
            }
        }
        finally { gate.Release(); }
    }
}
