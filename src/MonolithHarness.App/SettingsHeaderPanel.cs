using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace MonolithHarness.App;

// Keep the title and actions together, wrapping whole controls when space is limited.
sealed class SettingsHeaderPanel : Panel
{
    readonly List<(UIElement Element, Rect Bounds)> layout = [];
    public double Spacing { get; set; } = 12;
    public bool RightAlignLast { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        layout.Clear();
        var width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : double.PositiveInfinity;
        double x = 0, y = 0, rowHeight = 0, requiredWidth = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (x > 0 && x + child.DesiredSize.Width > width)
            {
                y += rowHeight + Spacing; x = 0; rowHeight = 0;
            }
            if (child.DesiredSize.Width > width) child.Measure(new Size(width, double.PositiveInfinity));
            var size = child.DesiredSize;
            layout.Add((child, new Rect(x, y, Math.Min(width, size.Width), size.Height)));
            requiredWidth = Math.Max(requiredWidth, x + Math.Min(width, size.Width));
            x += Math.Min(width, size.Width) + Spacing; rowHeight = Math.Max(rowHeight, size.Height);
        }
        return new Size(requiredWidth, layout.Count == 0 ? 0 : y + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (element, measured) in layout)
        {
            var bounds = measured;
            if (RightAlignLast && layout.Count > 1 && element == layout[^1].Element && measured.Y == layout[0].Bounds.Y)
                bounds.X = Math.Max(bounds.X, finalSize.Width - bounds.Width);
            var rowHeight = layout.Where(x => x.Bounds.Y == measured.Y).Max(x => x.Bounds.Height);
            bounds.Y += (rowHeight - bounds.Height) / 2;
            element.Arrange(bounds);
        }
        return finalSize;
    }
}
