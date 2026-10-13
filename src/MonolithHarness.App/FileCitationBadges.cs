using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using System.Runtime.CompilerServices;
using Windows.UI.Text;
using System.Reflection;
using Windows.Foundation;

namespace MonolithHarness.App;

/// <summary>Native inline badges keep the chat's existing selection and link hit testing.</summary>
static class FileCitationBadges
{
    sealed record CitationRange(int Start, int Length, Hyperlink Link);
    sealed class State
    {
        public string Text = "";
        public TextHighlighter? Highlight;
        public Canvas? Canvas;
        public List<CitationRange> Ranges = [];
        public List<(int Start, Run Run)> Runs = [];
        public List<(Rect Rect, Border Frame, Hyperlink Link)> Frames = [];
#if HAS_UNO
        public object? Parsed;
        public MethodInfo? RectangleMethod;
#endif
        public bool Dirty = true, Attached;
        public Hyperlink? Hover;
    }
    static readonly ConditionalWeakTable<Hyperlink, FileCitation> citations = new();
    static readonly ConditionalWeakTable<TextBlock, State> states = new();
    internal static TextBlock? TextOf(UIElement element) => element as TextBlock ?? (element as Grid)?.Children.OfType<TextBlock>().FirstOrDefault();
    internal static Grid Surface(TextBlock text)
    {
        var state = states.GetOrCreateValue(text);
        state.Canvas = new Canvas { IsHitTestVisible = false };
        var surface = new Grid(); surface.Children.Add(state.Canvas); surface.Children.Add(text);
        return surface;
    }

    static Rect CharacterRect(TextBlock text, State state, int index)
    {
#if HAS_UNO
        // Geometry from the pinned Uno 6.7 renderer keeps normal hyperlinks and selection intact.
        var parsed = state.Parsed;
        if (parsed == null) return default;
        return state.RectangleMethod?.Invoke(parsed, [index]) is Rect rectangle ? rectangle : default;
#else
        var run = state.Runs.LastOrDefault(item => item.Start <= index && item.Start + item.Run.Text.Length > index);
        return run.Run?.ContentStart.GetPositionAtOffset(index - run.Start, LogicalDirection.Forward)?.GetCharacterRect(LogicalDirection.Forward) ?? default;
#endif
    }
#if HAS_UNO
    static readonly PropertyInfo? parsedText = typeof(TextBlock).GetProperty("ParsedText", BindingFlags.Instance | BindingFlags.NonPublic);
#endif
    static void PaintFrames(TextBlock text, State state)
    {
        if (state.Canvas == null || state.Ranges.Count == 0 || text.ActualWidth <= 0) return;
        if (!state.Dirty) return;
#if HAS_UNO
        var parsed = parsedText?.GetValue(text);
        state.Parsed = parsed;
        state.RectangleMethod = parsed?.GetType().GetMethod("GetRectForIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
#endif
        state.Dirty = false; state.Canvas.Children.Clear(); state.Frames.Clear();
        try
        {
            foreach (var range in state.Ranges)
            {
                var rows = new List<Rect>();
                var first = CharacterRect(text, state, range.Start);
                var last = CharacterRect(text, state, range.Start + range.Length - 1);
                if (first.Height > 0 && last.Height > 0 && Math.Abs(first.Y - last.Y) < 1)
                    rows.Add(new(Math.Min(first.X, last.X), first.Y, Math.Max(first.Right, last.Right) - Math.Min(first.X, last.X), Math.Max(first.Height, last.Height)));
                else for (int index = range.Start; index < range.Start + range.Length; index++)
                {
                    var rectangle = CharacterRect(text, state, index);
                    if (rectangle.Height <= 0) continue;
                    var row = rows.FindIndex(item => Math.Abs(item.Y - rectangle.Y) < 1);
                    if (row < 0) rows.Add(rectangle);
                    else { var current = rows[row]; var left = Math.Min(current.X, rectangle.X); rows[row] = new(left, current.Y, Math.Max(current.Right, rectangle.Right) - left, Math.Max(current.Height, rectangle.Height)); }
                }
                foreach (var row in rows)
                {
                    var bounds = new Rect(Math.Max(0, row.X - 1), row.Y + 2, Math.Max(1, row.Width + 2), Math.Max(8, row.Height - 4));
                    var frame = new Border { Width = bounds.Width, Height = bounds.Height, CornerRadius = new(5), BorderThickness = new(1), BorderBrush = FluentDesign.Stroke, Background = FluentDesign.Card };
                    Canvas.SetLeft(frame, bounds.X); Canvas.SetTop(frame, bounds.Y); state.Canvas.Children.Add(frame); state.Frames.Add((bounds, frame, range.Link));
                }
            }
            if (state.Frames.Count > 0 && state.Highlight != null) { text.TextHighlighters.Remove(state.Highlight); state.Highlight = null; }
            SetHover(state, state.Hover);
        }
        catch (Exception ex) when (ex is TargetInvocationException or MemberAccessException or ArgumentException or NotImplementedException)
        { state.Canvas.Children.Clear(); state.Frames.Clear(); }
    }
    static void SetHover(State state, Hyperlink? link)
    {
        state.Hover = link;
        foreach (var frame in state.Frames)
        {
            frame.Frame.BorderBrush = ReferenceEquals(frame.Link, link) ? FluentDesign.Resource("AccentTextFillColorPrimaryBrush") : FluentDesign.Stroke;
            frame.Frame.Background = ReferenceEquals(frame.Link, link) ? FluentDesign.Resource("ControlHoverBrush") : FluentDesign.Card;
        }
    }
    internal static async Task VerifyAsync(Span placeholder, FileCitation citation, Func<string, Task<string?>>? resolve,
        Func<string, Task>? openFile, Action? changed)
    {
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var path = await FileCitationVerification.ResolveAsync(citation, resolve).ConfigureAwait(false);
        if (path == null || dispatcher == null) return;
        dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            placeholder.Inlines.Clear(); placeholder.Inlines.Add(Create(citation with { Path = path }, openFile));
            changed?.Invoke();
        });
    }
    internal static Hyperlink Create(FileCitation citation, Func<string, Task>? openFile)
    {
        var link = new Hyperlink { Foreground = FluentDesign.Primary, UnderlineStyle = UnderlineStyle.None };
        citations.Add(link, citation);
        ChatPathMenus.Register(link, citation.Target);
        link.Inlines.Add(new Run { Text = "\u00A0" });
        link.Inlines.Add(new Run { Text = FileIconCatalog.Glyph(citation.Extension), FontSize = ChatDensity.FontSize,
            FontFamily = new FontFamily("ms-appx:///Assets/Fonts/MaterialFileIcons.ttf#Monolith Material Files") });
        link.Inlines.Add(new Run { Text = "\u00A0" + citation.Name.Replace(' ', '\u00A0'), FontWeight = FontWeights.Medium });
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
            var ranges = new List<TextRange>(); var links = new List<CitationRange>(); var runs = new List<(int Start, Run Run)>(); int offset = 0;
            void Visit(Inline inline)
            {
                var beginning = offset;
                if (inline is Run run) { runs.Add((offset, run)); offset += run.Text.Length; }
                else if (inline is LineBreak) offset++;
                else if (inline is Span span) foreach (var child in span.Inlines) Visit(child);
                if (inline is Hyperlink link && citations.TryGetValue(link, out _) && offset > beginning)
                {
                    ranges.Add(new TextRange { StartIndex = beginning, Length = offset - beginning });
                    links.Add(new(beginning, offset - beginning, link));
                }
            }
            foreach (var inline in text.Inlines) Visit(inline);
            var signature = string.Join(';', links.Select(x => $"{x.Start}:{x.Length}:{RuntimeHelpers.GetHashCode(x.Link)}"));
            if (state.Text == signature) return;
            if (state.Highlight != null) text.TextHighlighters.Remove(state.Highlight);
            state.Highlight = null; state.Text = signature;
            state.Ranges = links; state.Runs = runs; state.Dirty = true;
            if (state.Canvas != null) state.Canvas.Children.Clear();
            if (ranges.Count == 0) return;
            if (state.Canvas == null)
            {
                // Paragraphs containing media and table cells also need the same native frame.
                var parent = VisualTreeHelper.GetParent(text);
                if (parent is Panel owner && owner.Children.IndexOf(text) is var position && position >= 0)
                { owner.Children.RemoveAt(position); owner.Children.Insert(position, Surface(text)); }
                else if (parent is Border ownerBorder && ReferenceEquals(ownerBorder.Child, text))
                { ownerBorder.Child = null; ownerBorder.Child = Surface(text); }
            }
            if (state.Canvas != null && !state.Attached)
            {
                state.Attached = true;
                text.LayoutUpdated += (_, _) => PaintFrames(text, state);
                text.SizeChanged += (_, _) => { state.Dirty = true; PaintFrames(text, state); };
                text.PointerMoved += (_, args) =>
                {
                    var point = args.GetCurrentPoint(text).Position;
                    var hovered = state.Frames.FirstOrDefault(frame => frame.Rect.Contains(point)).Link;
                    if (!ReferenceEquals(hovered, state.Hover)) SetHover(state, hovered);
                };
                text.PointerExited += (_, _) => SetHover(state, null);
            }
            var highlight = new TextHighlighter { Background = FluentDesign.Resource("ControlHoverBrush"), Foreground = FluentDesign.Primary };
            foreach (var range in ranges) highlight.Ranges.Add(range);
            // Search highlights are added afterwards and remain visible above these badges.
            text.TextHighlighters.Insert(0, highlight); state.Highlight = highlight;
        }
        else if (element is Panel panel) foreach (var child in panel.Children.ToArray()) Apply(child);
        else if (element is Border { Child: { } child }) Apply(child);
        else if (element is ScrollViewer { Content: UIElement content }) Apply(content);
    }
}
