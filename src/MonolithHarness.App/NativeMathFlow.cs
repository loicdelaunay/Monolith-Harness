using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace MonolithHarness.App;

/// <summary>Small native inline flow for Uno, whose RichTextBlock does not host inline UI.</summary>
sealed class NativeMathFlow : Panel
{
    public const string BreakTag = "math-line-break";
    public double MinimumLineHeight { get; init; } = 20;
    sealed record Line(int Start, int End, double Y, double Height);
    readonly List<Line> lines = [];
    static bool Break(UIElement child) => child is FrameworkElement element && Equals(element.Tag, BreakTag);
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width) : 1120;
        lines.Clear(); double x = 0, y = 0, height = MinimumLineHeight, maxWidth = 0; int start = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i]; child.Measure(new(width, double.PositiveInfinity));
            if (Break(child)) { lines.Add(new(start, i, y, height)); maxWidth = Math.Max(maxWidth, x); y += height; x = 0; height = MinimumLineHeight; start = i + 1; continue; }
            if (x > 0 && x + child.DesiredSize.Width > width)
            { lines.Add(new(start, i, y, height)); maxWidth = Math.Max(maxWidth, x); y += height; x = 0; height = MinimumLineHeight; start = i; }
            x += child.DesiredSize.Width; height = Math.Max(height, child.DesiredSize.Height);
        }
        if (start < Children.Count) { lines.Add(new(start, Children.Count, y, height)); y += height; maxWidth = Math.Max(maxWidth, x); }
        return new(Math.Min(width, maxWidth), y);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var line in lines)
        {
            double x = 0;
            for (int i = line.Start; i < line.End; i++)
            {
                var child = Children[i]; var size = child.DesiredSize;
                child.Arrange(new Rect(x, line.Y + Math.Max(0, (line.Height - size.Height) / 2), Math.Min(finalSize.Width, size.Width), size.Height)); x += size.Width;
            }
        }
        foreach (var child in Children.Where(Break)) child.Arrange(new Rect(0, 0, 0, 0));
        return finalSize;
    }
}
