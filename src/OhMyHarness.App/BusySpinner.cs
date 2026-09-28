using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace OhMyHarness.App;

// A small native spinner that does not depend on the optional Lottie renderer.
sealed class BusySpinner : Grid
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly RotateTransform rotation = new() { CenterX = 8, CenterY = 8 };
    bool active;
    public bool IsActive
    {
        get => active;
        set { active = value; Refresh(); }
    }

    public BusySpinner()
    {
        Width = Height = 16;
        var figure = new PathFigure { StartPoint = new Point(8, 1), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment { Point = new Point(1, 8), Size = new Size(7, 7), IsLargeArc = true, SweepDirection = SweepDirection.Clockwise });
        var geometry = new PathGeometry(); geometry.Figures.Add(figure);
        Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = FluentDesign.Resource("AccentFillColorDefaultBrush"), StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = rotation });
        timer.Tick += (_, _) => rotation.Angle = (rotation.Angle + 18) % 360;
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => timer.Stop();
    }

    void Refresh()
    {
        if (active && IsLoaded) timer.Start(); else timer.Stop();
    }
}
