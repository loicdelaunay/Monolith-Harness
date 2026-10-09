using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MonolithHarness.Core;
using Windows.Storage.Streams;

namespace MonolithHarness.App;

sealed class ComposerResourceTile : UserControl
{
    public Button PreviewButton { get; }
    public Button RemoveButton { get; }
    public Image Thumbnail { get; } = new() { Stretch = Stretch.UniformToFill };
    readonly FontIcon placeholder;
    readonly Border frame;
    readonly Grid content;
    readonly TextBlock caption;
    Func<CancellationToken, Task<byte[]>>? load;
    CancellationTokenSource? request;
    bool pointerInside;

    public ComposerResourceTile(string name, string location, bool folder, Action open, Action? remove)
    {
        Tag = location;
        frame = new Border { CornerRadius = new(8), BorderThickness = new(1), BorderBrush = FluentDesign.Stroke, Background = FluentDesign.Card };
        content = new Grid();
        placeholder = FluentDesign.Icon(folder ? "\uE8B7" : "\uE8A5", 30);
        placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.Margin = new(0, 0, 0, 16);
        content.Children.Add(placeholder); content.Children.Add(Thumbnail);
        caption = new TextBlock { Text = name, FontSize = 10, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = FluentDesign.Primary };
        content.Children.Add(new Border { Child = caption, Background = FluentDesign.Card, CornerRadius = new(4), Padding = new(4, 3, 4, 3),
            Margin = new(3), VerticalAlignment = VerticalAlignment.Bottom });
        PreviewButton = new() { Content = content, Padding = new(0), BorderThickness = new(0), CornerRadius = new(8),
            MinHeight = 0, MinWidth = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        ToolTipService.SetToolTip(PreviewButton, location);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PreviewButton, name);
        PreviewButton.Click += (_, _) => open();
        RemoveButton = new() { Width = 24, MinWidth = 0, MinHeight = 24, Padding = new(0),
            Content = FluentDesign.Icon("\uE711", 10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new(3), CornerRadius = new(12), Background = FluentDesign.Card, Opacity = 0, IsHitTestVisible = false, IsEnabled = remove != null };
        RemoveButton.Click += (_, _) => remove?.Invoke();
        var grid = new Grid(); grid.Children.Add(PreviewButton); if (remove != null) grid.Children.Add(RemoveButton); frame.Child = grid; Content = frame;
        PointerEntered += (_, _) => { pointerInside = true; SetRemoveVisible(true); };
        PointerExited += (_, e) =>
        {
            var point = e.GetCurrentPoint(this).Position;
            if (point.X >= 0 && point.Y >= 0 && point.X <= ActualWidth && point.Y <= ActualHeight) return;
            pointerInside = false; SetRemoveVisible(false);
        };
        GotFocus += (_, _) => SetRemoveVisible(true);
        LostFocus += (_, _) => { if (!pointerInside) SetRemoveVisible(false); };
        SetPreviewSize(96);
        Loaded += async (_, _) => await LoadAsync();
        Unloaded += (_, _) => request?.Cancel();
    }
    internal void SetPreviewSize(double size)
    {
        size = Math.Clamp(size, 48, 96);
        // Let the frame determine height: text zoom must not inflate only one side of the tile.
        Width = size; frame.Width = frame.Height = size;
        content.Width = content.Height = Math.Max(0, size - 2);
        placeholder.FontSize = Math.Clamp(size * .3, 14, 30);
        placeholder.Margin = new(0, 0, 0, caption.FontSize + 14);
        RemoveButton.Width = RemoveButton.MinHeight = size < 64 ? 20 : 24;
    }
    internal void SetRemoveVisible(bool visible) { RemoveButton.Opacity = visible ? 1 : 0; RemoveButton.IsHitTestVisible = visible && RemoveButton.IsEnabled; }
    public void LoadPreview(Func<CancellationToken, Task<byte[]>> loader) => load = loader;
    async Task LoadAsync()
    {
        if (load == null || Thumbnail.Source != null) return;
        request?.Cancel(); var lifetime = request = new(); var ct = lifetime.Token;
        try
        {
            var data = await load(ct); ct.ThrowIfCancellationRequested();
            var bitmap = new BitmapImage { DecodePixelWidth = 192 };
            using var stream = new MemoryStream(data); using var random = stream.AsRandomAccessStream();
            await bitmap.SetSourceAsync(random); ct.ThrowIfCancellationRequested();
            Thumbnail.Source = bitmap; placeholder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "composer.thumbnail", ex); }
        finally { if (ReferenceEquals(request, lifetime)) request = null; lifetime.Dispose(); }
    }
}
