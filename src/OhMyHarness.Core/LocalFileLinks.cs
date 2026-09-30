using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text.RegularExpressions;

namespace OhMyHarness.Core;

public static class LocalFileLinks
{
    public const string Prefix = "omh-file:";
    static readonly Regex Paths = new(@"(?<![\w:/\\])(?:[A-Za-z]:[/\\]|\.{1,2}[/\\]|/)?(?:[\w.@-]+[/\\])*[\w@-][\w.@-]*\.(?:html?|pdf|md|txt|json|csv|cs|csproj|slnx?|ts|tsx|js|jsx|py|css|xaml|xml|yaml|yml|sql|svg|png|jpe?g|webp|gif|ico|docx?|xlsx?|pptx?|odt|ods|rtf|zip|7z|rar|tar|gz|exe|dll|bat|cmd|ps1|sh|ini|toml|lock|config|ohm|log|mp[34]|wav|ogg)(?![\w.])", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    static readonly Regex ExplicitPaths = new("(?<![\\w:/\\\\])(?:[A-Za-z]:[/\\\\]|\\\\\\\\|\\.{1,2}[/\\\\]|~/|/(?=[\\w.])|[\\w.@-]+[/\\\\])[^\\s<>\"'|?*()\\[\\]{};,]*", RegexOptions.None, TimeSpan.FromMilliseconds(100));
    static readonly Regex QuotedPaths = new("[\"“'](?<path>(?:[A-Za-z]:[/\\\\]|\\\\\\\\|\\.{1,2}[/\\\\]|~/|/|[\\w.@-]+[/\\\\])[^\"“”'\\r\\n<>]+)[\"”']", RegexOptions.None, TimeSpan.FromMilliseconds(100));
    static readonly Regex Location = new(@"(?::\d+(?::\d+)?|#L\d+(?:-L?\d+)?)$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    public readonly record struct PathMatch(int Index, int Length, string Path);

    public static string WithoutLocation(string path) => Location.Replace(path, "");

    static bool LooksLikePath(string value) => !value.Contains("://", StringComparison.Ordinal) && !value.Contains('\n') &&
        (Regex.IsMatch(value, @"^(?:[A-Za-z]:[/\\]|\\\\|\.{1,2}[/\\]|~/|/)") ||
         value is "." or ".." || Regex.IsMatch(value, @"^[\w.@-]+[/\\]") ||
         Paths.Match(value) is { Success: true, Index: 0 } match && match.Length == WithoutLocation(value).Length ||
         value is "README" or "LICENSE" or "AGENTS.md");

    static PathMatch ExtendExistingAbsolutePath(string text, Match match, string value)
    {
        var original = new PathMatch(match.Index, value.Length, WithoutLocation(value));
        if (!Path.IsPathFullyQualified(value) || value.StartsWith(@"\\") || match.Index + match.Length >= text.Length || !char.IsWhiteSpace(text[match.Index + match.Length])) return original;
        var end = Math.Min(text.Length, match.Index + 512);
        var delimiter = text.IndexOfAny(['\r', '\n', '<', '>', '"', '\'', '|', '?', '*', ')', ']', '}', ';'], match.Index);
        if (delimiter >= 0) end = Math.Min(end, delimiter);
        // Plain prose has no delimiter around paths with spaces. Only extend to a
        // real local item, with bounded work; quoted paths need no filesystem lookup.
        int attempts = 0;
        for (var index = end; index > match.Index + match.Length && attempts < 24; index--)
        {
            if (index != end && !char.IsWhiteSpace(text[index])) continue;
            var candidate = text[match.Index..index].TrimEnd().TrimEnd('.', ',', ':', '!', '?');
            var path = WithoutLocation(candidate);
            attempts++;
            if (File.Exists(path) || Directory.Exists(path)) return new(match.Index, candidate.Length, path);
        }
        return original;
    }

    public static IReadOnlyList<PathMatch> Find(string text, bool wholePath = false)
    {
        var trimmed = text.Trim();
        if (wholePath && trimmed.Length > 0 && LooksLikePath(trimmed))
            return [new(text.IndexOf(trimmed, StringComparison.Ordinal), trimmed.Length, WithoutLocation(trimmed))];
        var found = new List<PathMatch>();
        foreach (Match match in QuotedPaths.Matches(text))
        {
            var group = match.Groups["path"];
            found.Add(new(group.Index, group.Length, WithoutLocation(group.Value)));
        }
        foreach (var regex in new[] { ExplicitPaths, Paths })
        foreach (Match match in regex.Matches(text))
        {
            if (found.Any(x => match.Index < x.Index + x.Length && match.Index + match.Length > x.Index)) continue;
            var tokenStart = match.Index == 0 ? -1 : text.LastIndexOfAny([' ', '\t', '\n', '"', '\''], match.Index - 1);
            var preceding = text[(tokenStart + 1)..match.Index];
            if (preceding.Contains("://", StringComparison.Ordinal) || preceding.Contains('@')) continue;
            var value = match.Value.TrimEnd(',', ';', ':', '!', '?');
            if (value.EndsWith('.') && !value.EndsWith("/.") && !value.EndsWith("\\.") && !value.EndsWith("/..") && !value.EndsWith("\\..")) value = value.TrimEnd('.');
            if (value.Length > 0) found.Add(ExtendExistingAbsolutePath(text, match, value));
        }
        return found.OrderBy(x => x.Index).ToArray();
    }
    public static string? PathFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith(Prefix, StringComparison.Ordinal)) return Uri.UnescapeDataString(url[Prefix.Length..]);
        if (Regex.IsMatch(url, @"^[A-Za-z]:[/\\]") || url.StartsWith(@"\\") || url.StartsWith('/') && !url.StartsWith("//")) return WithoutLocation(Uri.UnescapeDataString(url));
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.IsFile ? WithoutLocation(uri.LocalPath) : null;
        if (url.StartsWith('#') || url.StartsWith("//")) return null;
        var decoded = Uri.UnescapeDataString(url);
        return LooksLikePath(decoded) ? WithoutLocation(decoded) : null;
    }
    public static void Decorate(MarkdownDocument document)
    {
        void Inlines(ContainerInline container)
        {
            for (var child = container.FirstChild; child is not null;)
            {
                var next = child.NextSibling;
                if (child is LinkInline link)
                {
                    if (!link.IsImage && PathFromUrl(link.Url) is string path) link.Url = Prefix + Uri.EscapeDataString(path);
                }
                else if (child is ContainerInline nested) Inlines(nested);
                else if (child is LiteralInline or CodeInline)
                {
                    var text = child is LiteralInline literal ? literal.Content.ToString() : ((CodeInline)child).Content;
                    var matches = Find(text, child is CodeInline); int offset = 0;
                    foreach (var match in matches)
                    {
                        if (match.Index > offset) child.InsertBefore(child is CodeInline ? new CodeInline(text[offset..match.Index]) : new LiteralInline(text[offset..match.Index]));
                        var fileLink = new LinkInline(Prefix + Uri.EscapeDataString(match.Path), "");
                        var label = text.Substring(match.Index, match.Length);
                        fileLink.AppendChild(child is CodeInline ? new CodeInline(label) : new LiteralInline(label));
                        child.InsertBefore(fileLink); offset = match.Index + match.Length;
                    }
                    if (offset > 0) { if (offset < text.Length) child.InsertBefore(child is CodeInline ? new CodeInline(text[offset..]) : new LiteralInline(text[offset..])); child.Remove(); }
                }
                child = next;
            }
        }
        void Blocks(ContainerBlock blocks)
        {
            foreach (var block in blocks)
            {
                if (block is LeafBlock { Inline: { } inline }) Inlines(inline);
                if (block is ContainerBlock nested) Blocks(nested);
            }
        }
        Blocks(document);
    }
}
