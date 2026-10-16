using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using MonolithHarness.Core;
using Windows.Foundation;
using UiPath = Microsoft.UI.Xaml.Shapes.Path;

namespace MonolithHarness.App;

/// <summary>A bounded decorative layer: no web view, frame timer, conversation data or hit testing.</summary>
internal sealed class ChatBackgroundView : Grid
{
    readonly Canvas canvas = new();
    readonly RectangleGeometry clip = new();
    readonly Storyboard motion = new();
    readonly DispatcherTimer resizeDelay = new() { Interval = TimeSpan.FromMilliseconds(140) };
    AppearanceTheme palette = AppearanceThemes.All[0];
    string effect = ChatBackgroundStyles.Disabled;
    string? customColor;
    bool loaded, suspended, running, disposed;

    public ChatBackgroundView()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        Clip = clip;
        Children.Add(canvas);
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Loaded += (_, _) => { loaded = true; Rebuild(); };
        Unloaded += (_, _) => { loaded = false; resizeDelay.Stop(); StopMotion(); };
        SizeChanged += (_, _) =>
        {
            clip.Rect = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));
            if (!loaded || effect == ChatBackgroundStyles.Disabled || disposed) return;
            StopMotion(); resizeDelay.Stop(); resizeDelay.Start();
        };
        resizeDelay.Tick += (_, _) => { resizeDelay.Stop(); Rebuild(); };
    }

    public void Configure(string? selection, AppearanceTheme theme, int opacityPercent = ChatBackgroundStyles.DefaultOpacityPercent, string? color = null)
    {
        if (disposed) return;
        var next = ChatBackgroundStyles.Normalize(selection);
        var nextColor = ChatBackgroundStyles.NormalizeColor(color);
        var sameColors = palette.Dark == theme.Dark && palette.Accent == theme.Accent && palette.Muted == theme.Muted && customColor == nextColor;
        palette = theme; customColor = nextColor;
        Opacity = ChatBackgroundStyles.NormalizeOpacity(opacityPercent) / 100d;
        if (effect == next && sameColors) { RefreshMotion(); return; }
        effect = next;
        resizeDelay.Stop(); StopMotion();
        if (effect == ChatBackgroundStyles.Disabled)
        {
            motion.Children.Clear(); canvas.Children.Clear();
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;
        Rebuild();
    }

    public void SetSuspended(bool value)
    {
        suspended = value;
        RefreshMotion();
    }

    void StopMotion()
    {
        if (!running) return;
        motion.Stop(); running = false;
    }

    void RefreshMotion()
    {
        if (disposed || !loaded || suspended || Opacity <= 0 || effect == ChatBackgroundStyles.Disabled ||
            motion.Children.Count == 0 || !SystemMotionEnabled())
        { StopMotion(); return; }
        if (!running) { motion.Begin(); running = true; }
    }

    void Rebuild()
    {
        if (disposed || !loaded || effect == ChatBackgroundStyles.Disabled || ActualWidth <= 0 || ActualHeight <= 0) return;
        StopMotion(); motion.Children.Clear(); canvas.Children.Clear();
        var width = ActualWidth; var height = ActualHeight;
        clip.Rect = new(0, 0, width, height);
        var random = new Random(1701);
        switch (effect)
        {
            case "code": Code(width, height, random); break;
            case "aurora": Aurora(width, height); break;
            case "constellation": Constellation(width, height, random); break;
            case "waves": Waves(width, height); break;
            case "particles": Particles(width, height, random); break;
            case "grid": LuminousGrid(width, height); break;
        }
        RefreshMotion();
    }

    SolidColorBrush Ink(string color) => new(Hex(color));
    static Windows.UI.Color Hex(string color) => Windows.UI.Color.FromArgb(255,
        Convert.ToByte(color.Substring(1, 2), 16), Convert.ToByte(color.Substring(3, 2), 16), Convert.ToByte(color.Substring(5, 2), 16));
    // Normalize the original scenes so the default layer opacity preserves their former intensity.
    double Strength => (palette.Dark ? 1 : .55) / (ChatBackgroundStyles.DefaultOpacityPercent / 100d);
    string PrimaryInk => customColor ?? palette.Accent;
    string SecondaryInk => customColor ?? palette.Muted;

    void Animate(DependencyObject target, string property, double from, double to, double seconds)
    {
        // Explicit return keyframes work on both WinUI and Uno (where AutoReverse is unavailable).
        var animation = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromSeconds(seconds * 2)),
            RepeatBehavior = RepeatBehavior.Forever, EnableDependentAnimation = true };
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { Value = from, KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero) });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { Value = to, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        animation.KeyFrames.Add(new EasingDoubleKeyFrame { Value = from, KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds * 2)),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property);
        motion.Children.Add(animation);
    }

    void Drift(UIElement element, double x, double y, double seconds)
    {
        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        if (x != 0) Animate(transform, "X", -x, x, seconds);
        if (y != 0) Animate(transform, "Y", -y, y, seconds * 1.23);
    }

    void Code(double width, double height, Random random)
    {
        const string alphabet = "0123456789abcdef{}<>/+-:;";
        var count = Math.Clamp((int)(width / 110), 4, 18);
        var rows = Math.Clamp((int)(height / 17) + 10, 14, 66);
        for (var column = 0; column < count; column++)
        {
            var lines = new StringBuilder();
            for (var row = 0; row < rows; row++)
            {
                var length = random.Next(3, 12);
                for (var letter = 0; letter < length; letter++)
                    lines.Append(random.Next(7) == 0 ? ' ' : alphabet[random.Next(alphabet.Length)]);
                lines.Append('\n');
            }
            var text = new TextBlock { Text = lines.ToString(), FontSize = 10, FontFamily = new FontFamily("Consolas"),
                CharacterSpacing = 170, LineHeight = 17, Foreground = Ink(column % 4 == 0 ? PrimaryInk : SecondaryInk),
                Opacity = .045 * Strength, TextWrapping = TextWrapping.NoWrap };
            AutomationProperties.SetAccessibilityView(text, AccessibilityView.Raw);
            Canvas.SetLeft(text, column * width / count + random.Next(3, 24));
            Canvas.SetTop(text, -70 - random.Next(0, 100));
            canvas.Children.Add(text);
            Drift(text, 0, 30 + random.Next(20), 30 + random.Next(25));
            Animate(text, "Opacity", .018 * Strength, (.045 + random.NextDouble() * .035) * Strength, 12 + random.Next(17));
        }
    }

    LinearGradientBrush Veil(string color)
    {
        var solid = Hex(color);
        return new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1), GradientStops = {
            new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(0, solid.R, solid.G, solid.B) },
            new GradientStop { Offset = .45, Color = solid },
            new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(0, solid.R, solid.G, solid.B) } } };
    }

    void Aurora(double width, double height)
    {
        for (var i = 0; i < 3; i++)
        {
            var y = height * (.24 + i * .24);
            var band = new PathFigure { StartPoint = new Point(-width * .25, y), IsClosed = true, IsFilled = true };
            band.Segments.Add(new BezierSegment { Point1 = new Point(width * .2, y - height * .38),
                Point2 = new Point(width * .58, y + height * .28), Point3 = new Point(width * 1.25, y - height * .14) });
            band.Segments.Add(new LineSegment { Point = new Point(width * 1.25, y + height * .2) });
            band.Segments.Add(new BezierSegment { Point1 = new Point(width * .62, y + height * .55),
                Point2 = new Point(width * .18, y - height * .12), Point3 = new Point(-width * .25, y + height * .28) });
            var geometry = new PathGeometry(); geometry.Figures.Add(band);
            var path = new UiPath { Data = geometry, Fill = Veil(i == 1 ? SecondaryInk : PrimaryInk), Opacity = .05 * Strength };
            canvas.Children.Add(path);
            Drift(path, width * .045, height * .025, 26 + i * 9);
            Animate(path, "Opacity", .025 * Strength, .085 * Strength, 18 + i * 8);
        }
    }

    void Constellation(double width, double height, Random random)
    {
        var field = new Canvas { Width = width, Height = height };
        var count = Math.Clamp((int)(width * height / 21000), 16, 56);
        var points = Enumerable.Range(0, count).Select(_ => new Point(random.NextDouble() * width, random.NextDouble() * height)).ToArray();
        var brush = Ink(PrimaryInk);
        for (var i = 0; i < points.Length; i++)
        {
            var point = points[i];
            var closest = points.Select((other, index) => (other, index, distance: Math.Pow(other.X - point.X, 2) + Math.Pow(other.Y - point.Y, 2)))
                .Where(item => item.index > i).OrderBy(item => item.distance).FirstOrDefault();
            if (closest.distance is > 0 and < 28000)
                field.Children.Add(new Line { X1 = point.X, Y1 = point.Y, X2 = closest.other.X, Y2 = closest.other.Y,
                    Stroke = brush, StrokeThickness = 1, Opacity = .045 * Strength });
            var size = 1.4 + random.NextDouble() * 1.8;
            var dot = new Ellipse { Width = size, Height = size, Fill = brush, Opacity = .12 * Strength };
            Canvas.SetLeft(dot, point.X); Canvas.SetTop(dot, point.Y); field.Children.Add(dot);
            if (i % 3 == 0) Animate(dot, "Opacity", .04 * Strength, .22 * Strength, 8 + random.Next(15));
        }
        canvas.Children.Add(field);
        Drift(field, 10, 7, 36);
    }

    void Waves(double width, double height)
    {
        for (var i = 0; i < 9; i++)
        {
            var y = height * .23 + i * Math.Max(24, height * .057);
            var figure = new PathFigure { StartPoint = new Point(-width * .2, y), IsFilled = false };
            figure.Segments.Add(new BezierSegment { Point1 = new Point(width * .15, y - height * .24),
                Point2 = new Point(width * .55, y + height * .32), Point3 = new Point(width * 1.2, y - height * .12) });
            var geometry = new PathGeometry(); geometry.Figures.Add(figure);
            var path = new UiPath { Data = geometry, Stroke = Ink(i % 3 == 0 ? PrimaryInk : SecondaryInk),
                StrokeThickness = 1, Opacity = .065 * Strength };
            canvas.Children.Add(path);
            Drift(path, 18, 6 + i * 2, 24 + i * 3);
            Animate(path, "Opacity", .025 * Strength, .095 * Strength, 16 + i * 2);
        }
    }

    void Particles(double width, double height, Random random)
    {
        var count = Math.Clamp((int)(width * height / 26000), 12, 42);
        for (var i = 0; i < count; i++)
        {
            var size = 2.5 + random.NextDouble() * 5;
            var particle = new Ellipse { Width = size, Height = size, Fill = Ink(i % 4 == 0 ? SecondaryInk : PrimaryInk),
                Opacity = .1 * Strength };
            Canvas.SetLeft(particle, random.NextDouble() * width);
            Canvas.SetTop(particle, random.NextDouble() * height);
            canvas.Children.Add(particle);
            Drift(particle, 12 + random.Next(24), 24 + random.Next(48), 18 + random.Next(28));
            Animate(particle, "Opacity", .025 * Strength, (.1 + random.NextDouble() * .1) * Strength, 7 + random.Next(14));
        }
    }

    void LuminousGrid(double width, double height)
    {
        var columns = Math.Clamp((int)(width / 70), 4, 28);
        var rows = Math.Clamp((int)(height / 70), 3, 20);
        var geometry = new PathGeometry();
        for (var i = 0; i < columns; i++)
        {
            var x = (i + .5) * width / columns;
            var line = new PathFigure { StartPoint = new Point(x, 0), IsFilled = false };
            line.Segments.Add(new LineSegment { Point = new Point(x, height) }); geometry.Figures.Add(line);
        }
        for (var i = 0; i < rows; i++)
        {
            var y = (i + .5) * height / rows;
            var line = new PathFigure { StartPoint = new Point(0, y), IsFilled = false };
            line.Segments.Add(new LineSegment { Point = new Point(width, y) }); geometry.Figures.Add(line);
        }
        var grid = new UiPath { Data = geometry, Stroke = Ink(SecondaryInk), StrokeThickness = .75, Opacity = .055 * Strength };
        canvas.Children.Add(grid);
        Animate(grid, "Opacity", .025 * Strength, .08 * Strength, 16);
        var bandHeight = Math.Clamp(height * .18, 60, 180);
        var sweep = new Rectangle { Width = width, Height = bandHeight, Fill = Veil(PrimaryInk), Opacity = .1 * Strength };
        Canvas.SetTop(sweep, (height - bandHeight) / 2); canvas.Children.Add(sweep);
        Drift(sweep, 0, (height + bandHeight) / 2, 18);
        Animate(sweep, "Opacity", .035 * Strength, .14 * Strength, 12);
    }

    static bool SystemMotionEnabled()
    {
        if (!OperatingSystem.IsWindows()) return true;
        try { return !NativeMotion.SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled; }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return true; }
    }

    static class NativeMotion
    {
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
    }

    public void Shutdown()
    {
        if (disposed) return;
        disposed = true;
        resizeDelay.Stop(); StopMotion(); motion.Children.Clear(); canvas.Children.Clear();
    }
}
