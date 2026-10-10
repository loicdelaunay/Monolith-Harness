using System.Text;
using System.Text.RegularExpressions;

namespace MonolithHarness.Core;

/// <summary>Protect complete response documents from Markdown's blank-line HTML splitting.</summary>
internal static class ReplyHtmlGrouping
{
    static readonly Regex fence = new(@"^[ \t]{0,3}(?<marker>`{3,}|~{3,})(?<info>[^\r\n]*)$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    static readonly Regex start = new(@"\A[ \t]*(?:<!doctype\s+html(?=[\s>])|<html(?=[\s>]))", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    // Quoted attributes, comments and raw-text elements must not invent a closing HTML tag.
    static readonly Regex tag = new("<!--[\\s\\S]*?-->|<!\\[CDATA\\[[\\s\\S]*?\\]\\]>|<(?<end>/)?(?<name>[a-z][a-z0-9:-]*)(?=[\\s/>])(?:[^>\\\"']|\\\"[^\\\"]*\\\"|'[^']*')*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    static readonly HashSet<string> rawText = new(StringComparer.OrdinalIgnoreCase) { "script", "style", "textarea", "title", "xmp", "iframe", "noembed", "noframes" };

    internal static string Prepare(string markdown)
    {
        if (markdown.Length > 2_000_000) return markdown;
        StringBuilder? output = null;
        int lineStart = 0, copied = 0, fenceLength = 0;
        char fenceChar = '\0';
        while (lineStart < markdown.Length)
        {
            var newline = markdown.IndexOf('\n', lineStart);
            var lineEnd = newline < 0 ? markdown.Length : newline;
            var line = markdown[lineStart..lineEnd].TrimEnd('\r');
            var marker = fence.Match(line);
            if (marker.Success)
            {
                var run = marker.Groups["marker"].Value;
                if (fenceLength == 0) { fenceChar = run[0]; fenceLength = run.Length; }
                else if (run[0] == fenceChar && run.Length >= fenceLength && string.IsNullOrWhiteSpace(marker.Groups["info"].Value)) fenceLength = 0;
            }
            else if (fenceLength == 0)
            {
                var bodyStart = lineStart;
                var envelope = line.Trim().Equals("<![CDATA[", StringComparison.Ordinal);
                if (envelope) { bodyStart = lineEnd; while (bodyStart < markdown.Length && char.IsWhiteSpace(markdown[bodyStart])) bodyStart++; }
                var bodyNewline = markdown.IndexOf('\n', bodyStart);
                var firstLine = markdown[bodyStart..(bodyNewline < 0 ? markdown.Length : bodyNewline)];
                if (start.IsMatch(firstLine))
                {
                    var bodyEnd = End(markdown, bodyStart, out _);
                    var consumed = bodyEnd;
                    if (envelope)
                    {
                        var tail = bodyEnd; while (tail < markdown.Length && char.IsWhiteSpace(markdown[tail])) tail++;
                        if (markdown.AsSpan(tail).StartsWith("]]>", StringComparison.Ordinal)) consumed = tail + 3;
                    }
                    var html = markdown[bodyStart..bodyEnd];
                    var markerText = new string('~', Math.Max(3, LongestTildeRun(html) + 1));
                    output ??= new StringBuilder(markdown.Length + 32);
                    output.Append(markdown, copied, lineStart - copied);
                    output.Append("\n\n").Append(markerText).Append(html.Length <= ReplyWebDocument.MaxCharacters ? "html\n" : "text\n");
                    output.Append(html).Append('\n').Append(markerText).Append("\n\n");
                    copied = consumed; lineStart = consumed;
                    // The rest of a line after </html> is ordinary Markdown, not part of the app.
                    continue;
                }
            }
            lineStart = newline < 0 ? markdown.Length : newline + 1;
        }
        if (output == null) return markdown;
        output.Append(markdown, copied, markdown.Length - copied);
        return output.ToString();
    }

    internal static bool IsCompleteDocument(string source)
    {
        End(source, 0, out var complete); return complete;
    }

    static int End(string source, int offset, out bool complete)
    {
        complete = false;
        while (offset < source.Length)
        {
            var token = tag.Match(source, offset);
            if (!token.Success) break;
            offset = token.Index + token.Length;
            var name = token.Groups["name"].Value;
            var closing = token.Groups["end"].Success;
            if (closing && name.Equals("html", StringComparison.OrdinalIgnoreCase)) { complete = true; return offset; }
            if (!closing && rawText.Contains(name))
            {
                var close = "</" + name;
                var search = offset;
                while (true)
                {
                    var end = source.IndexOf(close, search, StringComparison.OrdinalIgnoreCase);
                    if (end < 0) return source.Length;
                    var after = end + close.Length;
                    if (after < source.Length && (char.IsWhiteSpace(source[after]) || source[after] == '>'))
                    {
                        var terminator = source.IndexOf('>', after);
                        if (terminator < 0) return source.Length;
                        offset = terminator + 1; break;
                    }
                    search = after;
                }
            }
        }
        // Streamed or imperfect responses remain one source/preview, rather than many fragments.
        return source.Length;
    }
    static int LongestTildeRun(string source)
    {
        int longest = 0, current = 0;
        foreach (var character in source) { current = character == '~' ? current + 1 : 0; longest = Math.Max(longest, current); }
        return longest;
    }
}
