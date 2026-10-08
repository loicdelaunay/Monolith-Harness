using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace MonolithHarness.App;

internal static class NativeFormattedRenderer
{
    internal sealed record Style(bool Bold = false, bool Italic = false, bool Underline = false, bool Strike = false, bool Mono = false, double Size = 14);
    internal sealed record Piece(string Text, Style Style);
    internal static List<Piece> Prepare(string html)
    {
        using var document = new HtmlParser().ParseDocument(html); var pieces = new List<Piece>(); int nodes = 0;
        void Add(string text, Style style)
        {
            if (text.Length == 0) return;
            if (pieces.Count > 0 && pieces[^1].Style == style) pieces[^1] = pieces[^1] with { Text = pieces[^1].Text + text };
            else pieces.Add(new(text, style));
        }
        void Break(Style style)
        { if (pieces.Count > 0 && !pieces[^1].Text.EndsWith('\n')) Add("\n", style); }
        void Visit(INode node, Style style, int depth)
        {
            if (++nodes > 12000 || depth > 128) throw new IOException("Mise en forme trop complexe / Formatting is too complex.");
            if (node is IText text) { Add(text.Data, style); return; }
            if (node is not IElement e) return;
            var tag = e.TagName;
            if (tag is "SCRIPT" or "STYLE" or "IFRAME" or "OBJECT" or "EMBED") return;
            var css = e.GetAttribute("style") ?? "";
            style = style with {
                Bold = style.Bold || tag is "B" or "STRONG" or "TH" || Regex.IsMatch(css, @"font-weight\s*:\s*(bold|[6-9]00)", RegexOptions.IgnoreCase),
                Italic = style.Italic || tag is "I" or "EM" || Regex.IsMatch(css, @"font-style\s*:\s*italic", RegexOptions.IgnoreCase),
                Underline = style.Underline || tag is "U" or "A" || css.Contains("underline", StringComparison.OrdinalIgnoreCase),
                Strike = style.Strike || tag is "S" or "STRIKE" or "DEL" || css.Contains("line-through", StringComparison.OrdinalIgnoreCase),
                Mono = style.Mono || tag is "PRE" or "CODE" };
            if (tag.Length == 2 && tag[0] == 'H' && tag[1] is >= '1' and <= '6') style = style with { Bold = true, Size = 28 - (tag[1] - '1') * 2 };
            var size = Regex.Match(css, @"font-size\s*:\s*(\d+(?:\.\d+)?)(px|pt)", RegexOptions.IgnoreCase);
            if (size.Success && double.TryParse(size.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) style = style with { Size = Math.Clamp(value * (size.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase) ? 4d / 3 : 1), 10, 32) };
            var block = tag is "DIV" or "P" or "LI" or "BLOCKQUOTE" or "PRE" or "TR" or "TABLE" or "UL" or "OL" || tag.Length == 2 && tag[0] == 'H';
            if (block) Break(style);
            if (tag == "BR") Add("\n", style);
            else if (tag == "HR") { Break(style); Add("────────\n", style); }
            else if (tag == "IMG") Add("[" + UiText.Resolve("Image", "Image") + (e.GetAttribute("alt") is { Length: > 0 } alt ? " : " + alt : "") + "]", style);
            else
            {
                if (tag == "LI")
                {
                    var ordinal = e.ParentElement?.Children.ToList().IndexOf(e) + 1;
                    Add(e.ParentElement?.TagName == "OL" ? (ordinal ?? 1) + ". " : "• ", style);
                }
                foreach (var child in node.ChildNodes) Visit(child, style, depth + 1);
            }
            if (block) Break(style);
            if (tag is "TD" or "TH") Add("\t", style);
        }
        foreach (var node in document.Body!.ChildNodes) Visit(node, new(), 0);
        return pieces;
    }
    internal static FrameworkElement Build(List<Piece> pieces, string text)
    {
        // Bound the native visual tree for long, highly fragmented Office documents.
        if (pieces.Count > 3000) return new TextBlock { Text = text, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        var result = new TextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Primary, FontSize = 14 };
        foreach (var piece in pieces)
        {
            var run = new Run { Text = piece.Text, FontSize = piece.Style.Size, FontWeight = piece.Style.Bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                TextDecorations = (piece.Style.Underline ? TextDecorations.Underline : TextDecorations.None) | (piece.Style.Strike ? TextDecorations.Strikethrough : TextDecorations.None) };
            if (piece.Style.Mono) run.FontFamily = new FontFamily("Cascadia Code, Consolas, monospace");
            if (piece.Style.Italic) { var italic = new Italic(); italic.Inlines.Add(run); result.Inlines.Add(italic); }
            else result.Inlines.Add(run);
        }
        return result;
    }
}
