namespace MonolithHarness.Core;

/// <summary>Finds where a streamed reply can be split so its completed blocks are parsed only once.</summary>
public static class MarkdownStreaming
{
    /// <summary>
    /// Returns the start of the last line, at or after <paramref name="from"/>, that follows a blank line and
    /// cannot continue the previous block (no indentation, list, quote, table, HTML or fence). Markdown before
    /// that offset parses to the same blocks alone as within the full text. <paramref name="from"/> must be 0
    /// or a value previously returned for a prefix of the same text.
    /// </summary>
    public static int StableBoundary(string markdown, int from = 0)
    {
        int stable = from, lineStart = from, fenceLength = 0; char fence = '\0';
        bool math = false, previousBlank = false;
        while (lineStart < markdown.Length)
        {
            var newline = markdown.IndexOf('\n', lineStart);
            var lineEnd = newline < 0 ? markdown.Length : newline;
            var line = markdown.AsSpan(lineStart, lineEnd - lineStart);
            var blank = line.IsWhiteSpace();
            if (fence == '\0' && !math && previousBlank && lineStart > from && SafeStart(line, complete: newline >= 0)) stable = lineStart;
            var content = line.TrimStart(' ');
            var indent = line.Length - content.Length;
            if (indent <= 3 && content.Length >= 3 && (content[0] == '`' || content[0] == '~'))
            {
                var marker = content[0]; var count = 0;
                while (count < content.Length && content[count] == marker) count++;
                if (count >= 3)
                {
                    if (fence == '\0' && !math) { fence = marker; fenceLength = count; }
                    else if (marker == fence && count >= fenceLength && content[count..].IsWhiteSpace()) fence = '\0';
                }
            }
            else if (fence == '\0' && indent <= 3 && content.StartsWith("$$")) math = !math;
            previousBlank = blank;
            if (newline < 0) break;
            lineStart = newline + 1;
        }
        return stable;
    }

    static bool SafeStart(ReadOnlySpan<char> line, bool complete)
    {
        if (line.IsEmpty || " \t\r-*+>|<`~$:[=_".Contains(line[0])) return false;
        if (!char.IsAsciiDigit(line[0])) return true;
        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits])) digits++;
        // An ordered list item can continue the list that precedes the blank line. A streamed
        // line made only of digits may still become such a marker ("2" then "2.").
        if (digits >= line.Length) return complete;
        return line[digits] is not ('.' or ')');
    }
}
