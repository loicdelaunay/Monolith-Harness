using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using System.Runtime.CompilerServices;
using Windows.UI.Text;

namespace MonolithHarness.App;

/// <summary>Native inline badges keep the chat's existing selection and link hit testing.</summary>
static class FileCitationBadges
{
    sealed class State { public string Text = ""; public TextHighlighter? Highlight; }
    static readonly ConditionalWeakTable<Hyperlink, FileCitation> citations = new();
    static readonly ConditionalWeakTable<TextBlock, State> states = new();
    internal static Hyperlink Create(FileCitation citation, Func<string, Task>? openFile)
    {
        var link = new Hyperlink { Foreground = FluentDesign.Primary, UnderlineStyle = UnderlineStyle.None };
        citations.Add(link, citation);
        ChatPathMenus.Register(link, citation.Target);
        link.Inlines.Add(new Run { Text = "\u00A0" });
        link.Inlines.Add(new Run { Text = FileIconCatalog.Glyph(citation.Extension), FontSize = 16,
            FontFamily = new FontFamily("ms-appx:///Assets/Fonts/MaterialFileIcons.ttf#Monolith Material Files") });
        link.Inlines.Add(new Run { Text = "\u00A0" + citation.Name.Replace(' ', '\u00A0'), FontWeight = FontWeights.Medium,
            FontFamily = new FontFamily("Cascadia Code, Consolas") });
        var lines = citation.LineLabel(UiText.Language == "fr");
        if (lines.Length > 0) link.Inlines.Add(new Run { Text = "\u00A0" + lines.Replace(' ', '\u00A0'), Foreground = FluentDesign.Secondary, FontSize = 12 });
        link.Inlines.Add(new Run { Text = "\u00A0" });
        if (openFile != null) link.Click += async (_, _) =>
        {
            if (!ChatPathMenus.IsSecondaryPress(link)) await openFile(citation.Target);
        };
        return link;
    }
    internal static void Apply(UIElement element)
    {
        if (element is TextBlock text)
        {
            var state = states.GetOrCreateValue(text);
            var ranges = new List<TextRange>(); int offset = 0;
            void Visit(Inline inline)
            {
                var beginning = offset;
                if (inline is Run run) offset += run.Text.Length;
                else if (inline is LineBreak) offset++;
                else if (inline is Span span) foreach (var child in span.Inlines) Visit(child);
                if (inline is Hyperlink link && citations.TryGetValue(link, out _) && offset > beginning)
                    ranges.Add(new TextRange { StartIndex = beginning, Length = offset - beginning });
            }
            foreach (var inline in text.Inlines) Visit(inline);
            var signature = string.Join(';', ranges.Select(x => $"{x.StartIndex}:{x.Length}"));
            if (state.Text == signature) return;
            if (state.Highlight != null) text.TextHighlighters.Remove(state.Highlight);
            state.Highlight = null; state.Text = signature;
            if (ranges.Count == 0) return;
            var highlight = new TextHighlighter { Background = FluentDesign.Resource("ControlHoverBrush"), Foreground = FluentDesign.Primary };
            foreach (var range in ranges) highlight.Ranges.Add(range);
            // Search highlights are added afterwards and remain visible above these badges.
            text.TextHighlighters.Insert(0, highlight); state.Highlight = highlight;
        }
        else if (element is Panel panel) foreach (var child in panel.Children) Apply(child);
        else if (element is Border { Child: { } child }) Apply(child);
        else if (element is ScrollViewer { Content: UIElement content }) Apply(content);
    }
}
