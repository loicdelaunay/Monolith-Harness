using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace MonolithHarness.App;

// A small native spinner that does not depend on the optional Lottie renderer.
sealed class BusySpinner : Grid
{
    readonly Storyboard animation = new();
    bool running;
    readonly RotateTransform rotation = new() { CenterX = 8, CenterY = 8 };
    bool active;
    public bool IsActive
    {
        get => active;
        set { if (active == value) return; active = value; Refresh(); }
    }

    public BusySpinner()
    {
        Width = Height = 16;
        var figure = new PathFigure { StartPoint = new Point(8, 1), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment { Point = new Point(1, 8), Size = new Size(7, 7), IsLargeArc = true, SweepDirection = SweepDirection.Clockwise });
        var geometry = new PathGeometry(); geometry.Figures.Add(figure);
        Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = FluentDesign.Resource("AccentFillColorDefaultBrush"), StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = rotation });
        var turn = new DoubleAnimation { From = 0, To = 360, Duration = new Duration(TimeSpan.FromSeconds(1)), RepeatBehavior = RepeatBehavior.Forever, EnableDependentAnimation = true };
        Storyboard.SetTarget(turn, rotation); Storyboard.SetTargetProperty(turn, "Angle"); animation.Children.Add(turn);
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => { animation.Stop(); running = false; };
    }

    void Refresh()
    {
        if (active && IsLoaded) { if (!running) { animation.Begin(); running = true; } }
        else if (running) { animation.Stop(); running = false; }
    }
}
