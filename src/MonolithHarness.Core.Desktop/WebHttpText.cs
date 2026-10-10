using System.Net;
using System.Text;
using System.Text.Json;

namespace MonolithHarness.Core;

/// <summary>Bounded, non-executing HTML text extraction; the original body remains available.</summary>
static class WebHttpText
{
    public static string Extract(string raw, string media, Uri url)
    {
        if (media.Contains("html", StringComparison.OrdinalIgnoreCase)) return Html(raw, url);
        // Large/deep JSON remains exact raw text rather than expanding indentation in memory.
        if (raw.Length <= 128 * 1024 && media.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                var formatted = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
                return formatted.Length <= 4 * 1024 * 1024 ? formatted : raw;
            }
            catch (JsonException) { }
        }
        return raw;
    }
    static string Html(string html, Uri url)
    {
        var output = new StringBuilder(); string? link = null; int linkBudget = 1024 * 1024;
        void Break() { if (output.Length > 0 && output[^1] != '\n') output.Append('\n'); }
        void Text(string value)
        {
            foreach (var c in WebUtility.HtmlDecode(value))
            {
                if (!char.IsWhiteSpace(c)) output.Append(c);
                else if (output.Length > 0 && !char.IsWhiteSpace(output[^1])) output.Append(' ');
            }
        }
        for (int i = 0; i < html.Length;)
        {
            int tag = html.IndexOf('<', i);
            if (tag < 0) { Text(html[i..]); break; }
            Text(html[i..tag]);
            if (html.AsSpan(tag).StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = html.IndexOf("-->", tag + 4, StringComparison.Ordinal);
                i = end < 0 ? html.Length : end + 3; continue;
            }
            int cursor = tag + 1; bool closing = cursor < html.Length && html[cursor] == '/'; if (closing) cursor++;
            int first = cursor;
            while (cursor < html.Length && (char.IsAsciiLetterOrDigit(html[cursor]) || html[cursor] is '-' or ':')) cursor++;
            var name = html[first..cursor].ToLowerInvariant();
            if (name.Length == 0 && cursor < html.Length && html[cursor] is not ('!' or '?'))
            { output.Append('<'); i = tag + 1; continue; }
            int last = TagEnd(html, cursor);
            var attributes = html[cursor..last]; i = Math.Min(html.Length, last + 1);
            if (!closing && name is "script" or "style" or "noscript" or "svg" or "template" or "head")
            {
                int end = html.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                i = end < 0 ? html.Length : Math.Min(html.Length, TagEnd(html, end + name.Length + 2) + 1);
                continue;
            }
            if (name is "p" or "div" or "section" or "article" or "header" or "footer" or "nav" or "main" or "ul" or "ol" or "li" or "br" or "hr" or "tr" or "pre" or "blockquote" or "dt" or "dd"
                || name.Length == 2 && name[0] == 'h' && name[1] is >= '1' and <= '6') Break();
            if (!closing && name == "li") output.Append("• ");
            if (!closing && name.Length == 2 && name[0] == 'h' && name[1] is >= '1' and <= '6') output.Append(new string('#', name[1] - '0')).Append(' ');
            if (closing && name is "td" or "th") output.Append(" | ");
            if (!closing && name == "img") Text(Attribute(attributes, "alt"));
            if (!closing && name == "a")
            {
                var href = Attribute(attributes, "href");
                link = href.Length is > 0 and <= 2048 && Uri.TryCreate(url, href, out var destination) && destination.Scheme is "http" or "https" && destination.UserInfo.Length == 0
                    ? destination.AbsoluteUri : null;
            }
            if (closing && name == "a" && link != null)
            {
                if (link.Length + 3 <= linkBudget) { output.Append(" (").Append(link).Append(')'); linkBudget -= link.Length + 3; }
                link = null;
            }
        }
        return string.Join('\n', output.ToString().Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0));
    }
    static int TagEnd(string text, int start)
    {
        char quote = '\0';
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '\'' or '"') quote = c;
            else if (c == '>') return i;
        }
        return text.Length;
    }
    static string Attribute(string attributes, string wanted)
    {
        for (int i = 0; i < attributes.Length;)
        {
            while (i < attributes.Length && (char.IsWhiteSpace(attributes[i]) || attributes[i] == '/')) i++;
            int start = i;
            while (i < attributes.Length && !char.IsWhiteSpace(attributes[i]) && attributes[i] is not ('=' or '/')) i++;
            var name = attributes[start..i];
            while (i < attributes.Length && char.IsWhiteSpace(attributes[i])) i++;
            if (i >= attributes.Length || attributes[i] != '=') continue;
            i++; while (i < attributes.Length && char.IsWhiteSpace(attributes[i])) i++;
            char quote = i < attributes.Length && attributes[i] is '\'' or '"' ? attributes[i++] : '\0';
            start = i;
            while (i < attributes.Length && (quote == '\0' ? !char.IsWhiteSpace(attributes[i]) : attributes[i] != quote)) i++;
            var value = attributes[start..i]; if (quote != '\0' && i < attributes.Length) i++;
            if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase)) return WebUtility.HtmlDecode(value);
        }
        return "";
    }
}
