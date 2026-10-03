using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace MonolithHarness.App;

// A determinate ring fits inside the existing collapsed header without increasing its height.
sealed class ContextUsageRing : Grid
{
    readonly ArcSegment arc = new() { Size = new Size(7, 7), SweepDirection = SweepDirection.Clockwise };
    readonly Microsoft.UI.Xaml.Shapes.Path used;
    readonly Ellipse full;

    public ContextUsageRing()
    {
        Width = Height = 18; VerticalAlignment = VerticalAlignment.Center;
        Children.Add(new Ellipse { Width = 14, Height = 14, StrokeThickness = 2,
            Stroke = FluentDesign.Resource("ControlStrokeColorDefaultBrush") });
        var figure = new PathFigure { StartPoint = new Point(9, 2), IsClosed = false, IsFilled = false };
        figure.Segments.Add(arc);
        var geometry = new PathGeometry(); geometry.Figures.Add(figure);
        used = new() { Data = geometry, StrokeThickness = 2, Stroke = FluentDesign.Resource("AccentFillColorDefaultBrush"), Visibility = Visibility.Collapsed };
        full = new() { Width = 14, Height = 14, StrokeThickness = 2, Stroke = used.Stroke, Visibility = Visibility.Collapsed };
        Children.Add(used); Children.Add(full);
        IsHitTestVisible = false;
    }
    public void Update(double fraction)
    {
        var part = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        used.Visibility = part > 0 && part < 1 ? Visibility.Visible : Visibility.Collapsed;
        full.Visibility = part >= 1 ? Visibility.Visible : Visibility.Collapsed;
        if (part > 0 && part < 1)
        {
            var angle = 2 * Math.PI * part;
            arc.Point = new Point(9 + 7 * Math.Sin(angle), 9 - 7 * Math.Cos(angle));
            arc.IsLargeArc = part > .5;
        }
    }
}
