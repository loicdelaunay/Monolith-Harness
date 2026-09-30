using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace OhMyHarness.App;

// Native shapes keep the chart independent of a web renderer and follow theme changes.
sealed class ContextUsageChart : Grid
{
    readonly ArcSegment arc = new() { Size = new Size(47, 47), SweepDirection = SweepDirection.Clockwise };
    readonly Microsoft.UI.Xaml.Shapes.Path usedArc;
    readonly Ellipse fullRing;
    readonly TextBlock percentage = new() { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = FluentDesign.Primary, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock caption = new() { FontSize = 11, Foreground = FluentDesign.Secondary,
        HorizontalAlignment = HorizontalAlignment.Center };

    public ContextUsageChart()
    {
        Width = Height = 108;
        var drawing = new Grid { Width = 108, Height = 108 };
        drawing.Children.Add(new Ellipse { Width = 94, Height = 94, StrokeThickness = 8,
            Stroke = FluentDesign.Resource("ControlStrokeColorDefaultBrush") });
        var figure = new PathFigure { StartPoint = new Point(54, 7), IsClosed = false, IsFilled = false };
        figure.Segments.Add(arc);
        var geometry = new PathGeometry(); geometry.Figures.Add(figure);
        usedArc = new() { Data = geometry, StrokeThickness = 8, Visibility = Visibility.Collapsed };
        fullRing = new() { Width = 94, Height = 94, StrokeThickness = 8, Visibility = Visibility.Collapsed };
        drawing.Children.Add(usedArc); drawing.Children.Add(fullRing);
        Children.Add(new Viewbox { Child = drawing });
        var center = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        center.Children.Add(percentage); center.Children.Add(caption); Children.Add(center);
    }

    public void Update(double fraction, string percent, string label, string description, double scale)
    {
        Width = Height = 108 * scale;
        percentage.Text = percent; caption.Text = label;
        AutomationProperties.SetName(this, description);
        var stroke = FluentDesign.Resource(fraction >= OhMyHarness.Core.ContextWindow.CompactThreshold
            ? "ToolMessageErrorStrokeBrush" : "AccentFillColorDefaultBrush");
        usedArc.Stroke = fullRing.Stroke = stroke;
        var part = Math.Clamp(fraction, 0, 1);
        usedArc.Visibility = part > 0 && part < 1 ? Visibility.Visible : Visibility.Collapsed;
        fullRing.Visibility = part >= 1 ? Visibility.Visible : Visibility.Collapsed;
        if (part > 0 && part < 1)
        {
            var angle = 2 * Math.PI * part;
            arc.Point = new Point(54 + 47 * Math.Sin(angle), 54 - 47 * Math.Cos(angle));
            arc.IsLargeArc = part > .5;
        }
    }
}
