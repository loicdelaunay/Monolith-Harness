using System.Text;

namespace MonolithHarness.Core;

public enum ProposalLineKind { Context, Added, Removed }
public sealed record ProposalDiffLine(ProposalLineKind Kind, string Text, int? BeforeLine, int? AfterLine, bool HasLineEnding);
public sealed record ProposalHunk(int Id, int BeforeStart, int BeforeCount, int AfterStart, int AfterCount, IReadOnlyList<ProposalDiffLine> Lines)
{
    public int Added => Lines.Count(line => line.Kind == ProposalLineKind.Added);
    public int Removed => Lines.Count(line => line.Kind == ProposalLineKind.Removed);
}
public sealed record ProposalDecision(string Path, int HunkId, bool Accepted);

/// <summary>A bounded line diff. Each hunk can be reviewed independently without rewriting rejected ranges.</summary>
public sealed class ProposalDiff
{
    readonly string[] before, after;
    public FileProposal File { get; }
    public IReadOnlyList<ProposalHunk> Hunks { get; }
    public int Added => Hunks.Sum(hunk => hunk.Added);
    public int Removed => Hunks.Sum(hunk => hunk.Removed);

    ProposalDiff(FileProposal file, string[] before, string[] after, IReadOnlyList<ProposalHunk> hunks)
    { File = file; this.before = before; this.after = after; Hunks = hunks; }

    public static ProposalDiff Create(FileProposal file)
    {
        var original = file.Original == null ? "" : SourceText.TryDecode(file.Original, out var text)
            ? text.Content : throw new IOException("Fichier texte invalide / Invalid text file.");
        var before = SplitLines(original); var after = SplitLines(file.Content);
        var operations = Compare(before, after);
        var oldPositions = new int[operations.Count + 1]; var newPositions = new int[operations.Count + 1];
        for (int i = 0; i < operations.Count; i++)
        {
            oldPositions[i + 1] = oldPositions[i] + (operations[i] == ProposalLineKind.Added ? 0 : 1);
            newPositions[i + 1] = newPositions[i] + (operations[i] == ProposalLineKind.Removed ? 0 : 1);
        }
        var hunks = new List<ProposalHunk>();
        for (int cursor = 0; cursor < operations.Count;)
        {
            while (cursor < operations.Count && operations[cursor] == ProposalLineKind.Context) cursor++;
            if (cursor == operations.Count) break;
            var start = cursor; var end = cursor + 1;
            // Merge changes sharing their three context lines; separate distant changes.
            for (int next = cursor + 1; next < operations.Count; next++)
            {
                if (operations[next] == ProposalLineKind.Context) continue;
                if (next - end > 6) break;
                end = next + 1;
            }
            var rows = new List<ProposalDiffLine>();
            for (int i = Math.Max(0, start - 3); i < Math.Min(operations.Count, end + 3); i++)
            {
                var kind = operations[i];
                var value = kind == ProposalLineKind.Added ? after[newPositions[i]] : before[oldPositions[i]];
                rows.Add(new(kind, value.TrimEnd('\r', '\n'), kind == ProposalLineKind.Added ? null : oldPositions[i] + 1,
                    kind == ProposalLineKind.Removed ? null : newPositions[i] + 1, value.EndsWith('\n')));
            }
            hunks.Add(new(hunks.Count, oldPositions[start], oldPositions[end] - oldPositions[start],
                newPositions[start], newPositions[end] - newPositions[start], rows));
            cursor = end;
        }
        // Creation of an empty file is still an explicit modification requiring a decision.
        if (file.Original == null && hunks.Count == 0) hunks.Add(new(0, 0, 0, 0, 0, []));
        return new(file, before, after, hunks);
    }

    public string Compose(IReadOnlySet<int> accepted)
    {
        if (accepted.Any(id => Hunks.All(hunk => hunk.Id != id))) throw new ArgumentException("Bloc inconnu / Unknown hunk.");
        var result = new StringBuilder(); var cursor = 0;
        foreach (var hunk in Hunks)
        {
            for (; cursor < hunk.BeforeStart; cursor++) result.Append(before[cursor]);
            if (accepted.Contains(hunk.Id))
                for (int i = hunk.AfterStart; i < hunk.AfterStart + hunk.AfterCount; i++) result.Append(after[i]);
            else
                for (int i = hunk.BeforeStart; i < hunk.BeforeStart + hunk.BeforeCount; i++) result.Append(before[i]);
            cursor = hunk.BeforeStart + hunk.BeforeCount;
        }
        for (; cursor < before.Length; cursor++) result.Append(before[cursor]);
        return result.ToString();
    }

    static string[] SplitLines(string content)
    {
        var lines = new List<string>(); var start = 0;
        for (int i = 0; i < content.Length; i++)
            if (content[i] == '\n') { lines.Add(content[start..(i + 1)]); start = i + 1; }
        if (start < content.Length) lines.Add(content[start..]);
        return lines.ToArray();
    }

    static List<ProposalLineKind> Compare(string[] before, string[] after)
    {
        int prefix = 0, suffix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        var result = Enumerable.Repeat(ProposalLineKind.Context, prefix).ToList();
        int oldCount = before.Length - prefix - suffix, newCount = after.Length - prefix - suffix;
        if (oldCount == 0) result.AddRange(Enumerable.Repeat(ProposalLineKind.Added, newCount));
        else if (newCount == 0) result.AddRange(Enumerable.Repeat(ProposalLineKind.Removed, oldCount));
        else
        {
            var middle = Myers(before, after, prefix, oldCount, newCount);
            // Bound work and memory for adversarial/repetitive input. The fallback remains exact.
            result.AddRange(middle ?? Enumerable.Repeat(ProposalLineKind.Removed, oldCount)
                .Concat(Enumerable.Repeat(ProposalLineKind.Added, newCount)));
        }
        result.AddRange(Enumerable.Repeat(ProposalLineKind.Context, suffix));
        return result;
    }

    static List<ProposalLineKind>? Myers(string[] before, string[] after, int offset, int oldCount, int newCount)
    {
        var trace = new List<int[]>(); int cells = 0, work = 0;
        static int At(int[] row, int distance, int diagonal) => diagonal < -distance || diagonal > distance ? -1 : row[diagonal + distance];
        for (int distance = 0; distance <= oldCount + newCount; distance++)
        {
            cells += 2 * distance + 1;
            if (cells > 1_000_000) return null;
            var row = new int[2 * distance + 1]; var previous = distance == 0 ? [] : trace[^1];
            for (int diagonal = -distance; diagonal <= distance; diagonal += 2)
            {
                if (++work > 4_000_000) return null;
                int x = distance == 0 ? 0 : diagonal == -distance || (diagonal != distance && At(previous, distance - 1, diagonal - 1) < At(previous, distance - 1, diagonal + 1))
                    ? At(previous, distance - 1, diagonal + 1) : At(previous, distance - 1, diagonal - 1) + 1;
                int y = x - diagonal;
                while (x < oldCount && y < newCount && before[offset + x] == after[offset + y])
                { x++; y++; if (++work > 4_000_000) return null; }
                row[diagonal + distance] = x;
                if (x < oldCount || y < newCount) continue;
                trace.Add(row);
                var edits = new List<ProposalLineKind>(); x = oldCount; y = newCount;
                for (int d = distance; d > 0; d--)
                {
                    int k = x - y; var prior = trace[d - 1];
                    int priorK = k == -d || (k != d && At(prior, d - 1, k - 1) < At(prior, d - 1, k + 1)) ? k + 1 : k - 1;
                    int priorX = At(prior, d - 1, priorK), priorY = priorX - priorK;
                    while (x > priorX && y > priorY) { edits.Add(ProposalLineKind.Context); x--; y--; }
                    if (x == priorX) { edits.Add(ProposalLineKind.Added); y--; }
                    else { edits.Add(ProposalLineKind.Removed); x--; }
                }
                while (x > 0 && y > 0) { edits.Add(ProposalLineKind.Context); x--; y--; }
                edits.Reverse(); return edits;
            }
            trace.Add(row);
        }
        return null;
    }
}
