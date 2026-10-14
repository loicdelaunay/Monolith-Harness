using CSharpMath.SkiaSharp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MonolithHarness.Core;
using SkiaSharp;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace MonolithHarness.App;

/// <summary>Bounded, offline math image; no WebView or script execution.</summary>
sealed class NativeMathView : ContentControl
{
    static readonly SemaphoreSlim renderGate = new(1, 1);
    sealed record MathBitmap(byte[] Data, int Width, int Height);
    static readonly Dictionary<(string Formula, float Font, uint Color, bool Display), MathBitmap> cache = [];
    static readonly Queue<(string Formula, float Font, uint Color, bool Display)> cacheOrder = new();
    static long cacheBytes;
    readonly string formula;
    readonly bool display;
    readonly Func<FrameworkElement>? sourceView;
    CancellationTokenSource? pending;
    int revision;
    public NativeMathView(string formula, bool display, Func<FrameworkElement>? sourceView = null)
    {
        this.formula = MathMarkdown.Formula(formula); this.display = display; this.sourceView = sourceView;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        Margin = display ? new(0, 6, 0, 8) : new(2, 0, 2, 0);
        Content = Source();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, "LaTeX: " + this.formula);
        ToolTipService.SetToolTip(this, this.formula);
        var menu = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text = UiText.Resolve("Copier la formule LaTeX", "Copy LaTeX formula") };
        copy.Click += (_, _) => { var data = new DataPackage(); data.SetText(this.formula); Clipboard.SetContent(data); };
        menu.Items.Add(copy); ContextFlyout = menu;
        Loaded += async (_, _) => await RenderAsync();
        Unloaded += (_, _) => { revision++; pending?.Cancel(); };
        ActualThemeChanged += async (_, _) => { if (IsLoaded) await RenderAsync(); };
    }
    FrameworkElement Source() => new TextBlock { Text = formula, Foreground = FluentDesign.Primary,
        FontFamily = new FontFamily("Cascadia Code, Consolas"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = ChatDensity.FontSize };
    async Task RenderAsync()
    {
        int request = ++revision; pending?.Cancel();
        using var lifetime = new CancellationTokenSource(); pending = lifetime;
        var color = (FluentDesign.Primary as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.White;
        float font = (float)(display ? Math.Max(18, ChatDensity.FontSize + 2) : ChatDensity.FontSize);
        try
        {
            var rendered = await Task.Run(async () =>
            {
                await renderGate.WaitAsync(lifetime.Token);
                try
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    var key = (formula, font, (uint)new SKColor(color.R, color.G, color.B, color.A), display);
                    if (cache.TryGetValue(key, out var cached)) return cached;
                    if (formula.Length is 0 or > 6000) throw new ArgumentException("Formula length limit.");
                    int depth = 0;
                    foreach (char character in formula) { if (character == '{' && ++depth > 64) throw new ArgumentException("Formula nesting limit."); if (character == '}') depth--; }
                    var painter = new MathPainter { LaTeX = formula, FontSize = font, TextColor = new SKColor(color.R, color.G, color.B, color.A),
                        LineStyle = display ? CSharpMath.Atom.LineStyle.Display : CSharpMath.Atom.LineStyle.Text, DisplayErrorInline = false };
                    if (painter.ErrorMessage != null) throw new ArgumentException("Unsupported or incomplete LaTeX formula.");
                    var bounds = painter.Measure(); const int pad = 4, scale = 2;
                    if (!float.IsFinite(bounds.Width) || !float.IsFinite(bounds.Height) || bounds.Width > 2000 || bounds.Height > 700) throw new ArgumentException("Formula size limit.");
                    int width = Math.Max(1, (int)Math.Ceiling(bounds.Width) + pad * 2), height = Math.Max(1, (int)Math.Ceiling(bounds.Height) + pad * 2);
                    using var surface = SKSurface.Create(new SKImageInfo(width * scale, height * scale)) ?? throw new InvalidOperationException("Math image surface unavailable.");
                    surface.Canvas.Clear(SKColors.Transparent); surface.Canvas.Scale(scale);
                    painter.Draw(surface.Canvas, pad, pad - bounds.Y);
                    using var snapshot = surface.Snapshot(); using var data = snapshot.Encode(SKEncodedImageFormat.Png, 100);
                    var result = new MathBitmap(data.ToArray(), width, height);
                    while (cacheOrder.Count > 0 && (cache.Count >= 128 || cacheBytes + result.Data.Length > 8 * 1024 * 1024))
                    { var oldest = cacheOrder.Dequeue(); if (cache.Remove(oldest, out var removed)) cacheBytes -= removed.Data.Length; }
                    if (result.Data.Length <= 8 * 1024 * 1024) { cache[key] = result; cacheOrder.Enqueue(key); cacheBytes += result.Data.Length; }
                    return result;
                }
                finally { renderGate.Release(); }
            }, lifetime.Token);
            if (request != revision || lifetime.IsCancellationRequested) return;
            using var stream = new MemoryStream(rendered.Data); using var random = stream.AsRandomAccessStream();
            var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(random);
            if (request != revision || lifetime.IsCancellationRequested) return;
            var image = new Image { Source = bitmap, Width = rendered.Width, Height = rendered.Height, Stretch = Stretch.Uniform };
            Content = display ? new ScrollViewer { Content = image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled } : image;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (request != revision) return;
            var fallback = new StackPanel { Spacing = 4 };
            if (display) fallback.Children.Add(new TextBlock { Text = UiText.Resolve("Formule incomplète ou non prise en charge : source LaTeX affichée.", "Incomplete or unsupported formula: LaTeX source shown."), FontSize = 11, Foreground = FluentDesign.Secondary, TextWrapping = TextWrapping.Wrap });
            fallback.Children.Add(sourceView?.Invoke() ?? Source()); Content = fallback;
            AppLog.Write(AppLogLevel.Debug, "markdown.math_source_fallback", ex);
        }
        finally { if (ReferenceEquals(pending, lifetime)) pending = null; }
    }
}
