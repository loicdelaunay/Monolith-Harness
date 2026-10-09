using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public sealed record FileProposal(string Path, string ResolvedPath, byte[]? Original, string Content);
public sealed record ProposalBatch(string Revision, List<FileProposal> Files, List<ProposalDecision>? Decisions = null);
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
        var diff = ProposalDiff.Create(file);
        var result = new StringBuilder("--- " + file.Path + (file.Original == null ? " (nouveau / new)" : "") + "\n+++ " + file.Path + "\n");
        if (diff.Hunks.Count == 0) return result.Append("Aucune différence textuelle / No text differences.").ToString();
        foreach (var hunk in diff.Hunks)
        {
            var leading = hunk.Lines.TakeWhile(line => line.Kind == ProposalLineKind.Context).Count();
            var trailing = hunk.Lines.Reverse().TakeWhile(line => line.Kind == ProposalLineKind.Context).Count();
            var oldCount = hunk.BeforeCount + leading + trailing; var newCount = hunk.AfterCount + leading + trailing;
            var oldStart = hunk.BeforeStart - leading + (oldCount == 0 ? 0 : 1); var newStart = hunk.AfterStart - leading + (newCount == 0 ? 0 : 1);
            result.AppendLine($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@");
            foreach (var line in hunk.Lines)
            {
                result.AppendLine((line.Kind == ProposalLineKind.Added ? "+ " : line.Kind == ProposalLineKind.Removed ? "- " : "  ") + line.Text);
                if (!line.HasLineEnding) result.AppendLine("\\ No newline at end of file");
            }
        }
        return result.ToString();
    }

    static Dictionary<string, ProposalDiff> ReviewDiffs(ProposalBatch batch) => batch.Files.ToDictionary(file => file.Path, ProposalDiff.Create, StringComparer.Ordinal);
    static void ValidateDecisions(IReadOnlyDictionary<string, ProposalDiff> diffs, IReadOnlyCollection<ProposalDecision> decisions, bool complete)
    {
        var seen = new HashSet<(string Path, int Hunk)>();
        foreach (var decision in decisions)
            if (!diffs.TryGetValue(decision.Path, out var diff) || diff.Hunks.All(hunk => hunk.Id != decision.HunkId) || !seen.Add((decision.Path, decision.HunkId)))
                throw new ArgumentException("Décision de revue invalide / Invalid review decision.");
        if (complete && seen.Count != diffs.Values.Sum(diff => diff.Hunks.Count))
            throw new InvalidOperationException("Décidez chaque modification avant de terminer / Decide every change before finishing.");
    }
    static async Task<ProposalBatch> CurrentBatch(string database, int chatId, string revision, CancellationToken ct)
    {
        var batch = await ReadAsync(database, chatId, ct) ?? throw new IOException("Aucune proposition / No proposal.");
        if (batch.Revision != revision) throw new IOException("La proposition a changé, rouvrez la revue / Proposal changed, reopen review.");
        return batch;
    }
    public static async Task SaveReviewAsync(string database, int chatId, string revision, IReadOnlyCollection<ProposalDecision> decisions, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var batch = await CurrentBatch(database, chatId, revision, ct);
            var diffs = await Task.Run(() => ReviewDiffs(batch), ct);
            ValidateDecisions(diffs, decisions, complete: false);
            // Saving a review changes neither project files nor the proposal revision.
            await SaveBatch(database, chatId, batch with { Decisions = decisions.ToList() }, ct);
        }
        finally { gate.Release(); }
    }
    public static async Task CompleteReviewAsync(string database, int chatId, string revision, IReadOnlyCollection<ProposalDecision> decisions, SourceAccess access, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var batch = await CurrentBatch(database, chatId, revision, ct);
            var diffs = await Task.Run(() => ReviewDiffs(batch), ct);
            ValidateDecisions(diffs, decisions, complete: true);
            var files = new List<FileProposal>();
            foreach (var diff in diffs.Values)
            {
                var accepted = decisions.Where(decision => decision.Path == diff.File.Path && decision.Accepted).Select(decision => decision.HunkId).ToHashSet();
                if (accepted.Count > 0) files.Add(diff.File with { Content = diff.Compose(accepted) });
            }
            // Rejected changes are removed only on Finish, together with successfully applied changes.
            await Commit(database, chatId, files, new(Guid.NewGuid().ToString("N"), []), access, ct);
        }
        finally { gate.Release(); }
    }
    public static async Task ApplyAsync(string database, int chatId, string revision, IEnumerable<string> paths, SourceAccess access, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var batch = await CurrentBatch(database, chatId, revision, ct);
            var selected = paths.ToHashSet(StringComparer.Ordinal); var files = batch.Files.Where(file => selected.Contains(file.Path)).ToList();
            if (files.Count == 0 || files.Count != selected.Count) throw new ArgumentException("Sélection invalide / Invalid selection.");
            var remaining = batch.Files.Except(files).ToList();
            var savedDecisions = batch.Decisions?.Where(decision => !selected.Contains(decision.Path)).ToList();
            await Commit(database, chatId, files, new(Guid.NewGuid().ToString("N"), remaining, savedDecisions), access, ct);
        }
        finally { gate.Release(); }
    }
    static async Task Commit(string database, int chatId, List<FileProposal> files, ProposalBatch remaining, SourceAccess access, CancellationToken ct)
    {
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
            await SaveBatch(database, chatId, remaining, ct);
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
}
