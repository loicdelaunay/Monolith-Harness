using System.Text;

namespace MonolithHarness.Core;

public static class MathMarkdown
{
    public static bool IsMathFence(string? language) => language?.Trim().Split(' ', '\t')[0].ToLowerInvariant() is "math" or "latex" or "tex" or "katex";
    public static string Formula(string source)
    {
        source = source.Trim();
        foreach (var pair in new[] { ("$$", "$$"), (@"\[", @"\]"), (@"\(", @"\)"), ("$", "$") })
            if (source.Length >= pair.Item1.Length + pair.Item2.Length && source.StartsWith(pair.Item1, StringComparison.Ordinal) && source.EndsWith(pair.Item2, StringComparison.Ordinal))
                return source[pair.Item1.Length..^pair.Item2.Length].Trim();
        return source;
    }
    // Markdig uses dollar delimiters. Normalize the common LaTeX alternatives only outside code.
    public static string Prepare(string markdown)
    {
        if (!markdown.Contains(@"\(", StringComparison.Ordinal) && !markdown.Contains(@"\[", StringComparison.Ordinal)) return markdown;
        var output = new StringBuilder(markdown.Length); char fence = '\0'; int fenceSize = 0;
        foreach (var line in markdown.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                char marker = trimmed[0]; int count = trimmed.TakeWhile(c => c == marker).Count();
                if (fence == '\0') { fence = marker; fenceSize = count; }
                else if (marker == fence && count >= fenceSize && trimmed[count..].Trim().Length == 0) fence = '\0';
                output.Append(line).Append('\n'); continue;
            }
            if (fence != '\0' || line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith('\t')) { output.Append(line).Append('\n'); continue; }
            var mathLine = line.Trim();
            if (mathLine.Length >= 4 && mathLine.StartsWith(@"\[", StringComparison.Ordinal) && mathLine.EndsWith(@"\]", StringComparison.Ordinal))
            { output.Append("$$\n").Append(mathLine[2..^2].Trim()).Append("\n$$\n"); continue; }
            if (line.Trim() is @"\[" or @"\]") { output.Append(line.Replace(line.Trim(), "$$", StringComparison.Ordinal)).Append('\n'); continue; }
            int codeTicks = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '`')
                {
                    int count = 1; while (i + count < line.Length && line[i + count] == '`') count++;
                    if (codeTicks == 0) codeTicks = count; else if (codeTicks == count) codeTicks = 0;
                    output.Append('`', count); i += count - 1; continue;
                }
                if (codeTicks == 0 && line[i] == '\\' && i + 1 < line.Length && line[i + 1] == '(' && (i == 0 || line[i - 1] != '\\'))
                {
                    int end = line.IndexOf(@"\)", i + 2, StringComparison.Ordinal);
                    if (end > i + 2) { output.Append('$').Append(line.AsSpan(i + 2, end - i - 2)).Append('$'); i = end + 1; continue; }
                }
                output.Append(line[i]);
            }
            output.Append('\n');
        }
        if (output.Length > 0) output.Length--; return output.ToString();
    }
}
