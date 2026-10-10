using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace MonolithHarness.Core;

// Parses local clipboard content only. HtmlParser has no loader, scripts or navigation.
public static class FormattedHtml
{
    static readonly HashSet<string> allowed = new("DIV P SPAN B STRONG I EM U S STRIKE DEL SUB SUP BR HR A UL OL LI BLOCKQUOTE PRE CODE H1 H2 H3 H4 H5 H6 TABLE THEAD TBODY TFOOT TR TD TH CAPTION COLGROUP COL FONT IMG".Split(' '));
    static readonly HashSet<string> dropped = new("SCRIPT STYLE IFRAME FRAME OBJECT EMBED SVG MATH FORM INPUT BUTTON TEXTAREA SELECT LINK META BASE NOSCRIPT TEMPLATE".Split(' '));
    static readonly HashSet<string> blocks = new("DIV P LI BLOCKQUOTE PRE H1 H2 H3 H4 H5 H6 TR TABLE UL OL".Split(' '));
    static readonly HashSet<string> attributes = new("colspan rowspan start type dir align valign cellpadding cellspacing width height color face size bgcolor".Split(' '));
    static readonly HashSet<string> properties = new("color background-color font-family font-size font-weight font-style font-variant text-decoration text-decoration-line text-decoration-color text-align vertical-align line-height letter-spacing word-spacing white-space direction unicode-bidi margin margin-top margin-bottom margin-left margin-right padding padding-top padding-bottom padding-left padding-right border border-width border-style border-color border-collapse border-spacing border-top border-bottom border-left border-right width max-width height list-style-type text-indent".Split(' '));
    public static string Fragment(string value)
    {
        const string start = "<!--StartFragment-->", end = "<!--EndFragment-->";
        var begin = value.IndexOf(start, StringComparison.OrdinalIgnoreCase);
        var finish = value.IndexOf(end, StringComparison.OrdinalIgnoreCase);
        if (begin >= 0 && finish >= begin + start.Length) return value[(begin + start.Length)..finish];
        if (value.StartsWith("Version:", StringComparison.OrdinalIgnoreCase) && value.IndexOf('<') is var tag && tag >= 0) return value[tag..].TrimEnd('\0');
        return value;
    }
    public static FormattedText Parse(string html, int maxText = ModelUtilityPrompts.MaxTextLength)
    {
        if (html.Length > 3_000_000) throw new IOException("Collage trop volumineux / Paste is too large.");
        using var source = new HtmlParser().ParseDocument(Fragment(html));
        var template = new StringBuilder(); var runs = new List<FormattedTextRun>(); int count = 0, characters = 0, id = 0, structural = -1;
        void Boundary(string text)
        {
            if (runs.Count == 0 || runs[^1].Text.EndsWith('\n')) return;
            runs.Add(new(structural--, text, true)); characters += text.Length;
        }
        void Visit(INode node, int depth)
        {
            if (++count > 12000 || depth > 128) throw new IOException("Mise en forme trop complexe. Collez un extrait plus court / Formatting is too complex; paste a shorter excerpt.");
            if (node is IText text)
            {
                characters += text.Data.Length;
                if (characters > maxText) throw new IOException("Texte trop long / Text is too long.");
                runs.Add(new(id, text.Data)); template.Append("<!--omh-text-").Append(id++).Append("-->"); return;
            }
            if (node is not IElement element || dropped.Contains(element.TagName)) return;
            var tag = element.TagName; var kept = allowed.Contains(tag); var name = tag.ToLowerInvariant();
            if (blocks.Contains(tag)) Boundary("\n");
            if (kept)
            {
                template.Append('<').Append(name);
                foreach (var attribute in element.Attributes)
                {
                    var key = attribute.Name.ToLowerInvariant(); var value = attribute.Value;
                    if (attributes.Contains(key) && value.Length < 200 && !value.Contains('<') && !value.Contains('>')) AppendAttribute(key, value);
                }
                var style = element.GetAttribute("style");
                if (style is { Length: < 16000 })
                {
                    var clean = new List<string>();
                    foreach (var entry in style.Split(';'))
                    {
                        var colon = entry.IndexOf(':'); if (colon < 1) continue;
                        var key = entry[..colon].Trim().ToLowerInvariant(); var value = entry[(colon + 1)..].Trim();
                        if (properties.Contains(key) && value.Length < 500 && !Regex.IsMatch(value, @"url\s*\(|expression|@import|javascript:|behavior", RegexOptions.IgnoreCase)) clean.Add(key + ":" + value);
                    }
                    if (clean.Count > 0) AppendAttribute("style", string.Join(';', clean));
                }
                if (tag == "A" && element.GetAttribute("href") is { Length: < 4000 } href && Regex.IsMatch(href, @"^(https?:|mailto:|tel:)", RegexOptions.IgnoreCase)) AppendAttribute("href", href);
                if (tag == "IMG")
                {
                    if (element.GetAttribute("alt") is { } alt) AppendAttribute("alt", alt[..Math.Min(500, alt.Length)]);
                    if (element.GetAttribute("src") is { Length: < 2_000_000 } src && Regex.IsMatch(src, @"^(https?:|cid:|data:image/(png|jpeg|gif|webp);base64,)", RegexOptions.IgnoreCase)) AppendAttribute("src", src);
                }
                template.Append('>');
            }
            if (tag == "BR") { runs.Add(new(structural--, "\n", true)); characters++; }
            foreach (var child in node.ChildNodes) Visit(child, depth + 1);
            if (kept && tag is not ("BR" or "HR" or "IMG" or "COL")) template.Append("</").Append(name).Append('>');
            if (blocks.Contains(tag)) Boundary("\n");
            if (tag is "TD" or "TH") Boundary("\t");
        }
        void AppendAttribute(string name, string value) => template.Append(' ').Append(name).Append("=\"").Append(WebUtility.HtmlEncode(value)).Append('"');
        foreach (var node in source.Body!.ChildNodes) Visit(node, 0);
        while (runs.Count > 0 && runs[^1].Structural) runs.RemoveAt(runs.Count - 1);
        if (runs.Count == 0) return FormattedText.Empty;
        if (runs.Sum(r => r.Text.Length) > maxText) throw new IOException("Texte trop long / Text is too long.");
        return new(template.ToString(), runs);
    }
    public static FormattedText Edit(FormattedText original, string text)
    {
        if (original.Text == text) return original;
        var old = original.Text; int start = 0, suffix = 0;
        while (start < old.Length && start < text.Length && old[start] == text[start]) start++;
        if (start > 0 && start < old.Length && char.IsLowSurrogate(old[start])) start--;
        while (suffix < old.Length - start && suffix < text.Length - start && old[^(suffix + 1)] == text[^(suffix + 1)]) suffix++;
        if (suffix > 0 && char.IsLowSurrogate(old[old.Length - suffix])) suffix--;
        var removed = old.Substring(start, old.Length - start - suffix); var added = text.Substring(start, text.Length - start - suffix);
        return original.Apply([new TextCorrection(start, removed.Length, removed, added, "Native edit")]);
    }
}
