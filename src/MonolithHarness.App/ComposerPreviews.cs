using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using MonolithHarness.Core;
using Windows.Storage.Streams;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly HashSet<int> hiddenWorkspacePreviews = [];
    readonly List<Window> imagePreviewWindows = [];
    bool imagePreviewLifetimeAttached;

    ComposerResourceTile CreateResourceTile(string name, string location, bool folder, Func<Task> open, Func<Task>? remove)
    {
        var tile = new ComposerResourceTile(name, location, folder, async () => await Guard(open),
            remove == null ? null : async () => await Guard(remove));
        var label = WorkflowText("Retirer ", "Remove ") + name;
        ToolTipService.SetToolTip(tile.RemoveButton, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile.RemoveButton, label);
        return tile;
    }
    static bool IsImagePreviewPath(string path) => File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif";
    static async Task<byte[]> ReadPreviewImageAsync(string path, CancellationToken ct)
    {
        return await Task.Run(async () =>
        {
            if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new IOException("Image exceeds the 20 MB preview limit.");
            return await File.ReadAllBytesAsync(path, ct);
        }, ct);
    }
    async Task OpenImageFileFullscreenAsync(string path) => await OpenImageFullscreenAsync(await ReadPreviewImageAsync(path, default), Path.GetFileName(path));
    async Task OpenImageFullscreenAsync(byte[] bytes, string name)
    {
        var bitmap = new BitmapImage { DecodePixelWidth = 4096 };
        using (var stream = new MemoryStream(bytes))
        using (var random = stream.AsRandomAccessStream()) await bitmap.SetSourceAsync(random);
        var viewer = new Window { Title = name };
        var surface = new Grid { Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush"), RequestedTheme = root.RequestedTheme };
        var image = new Image { Source = bitmap, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
        var scroller = new ScrollViewer { Content = image, ZoomMode = ZoomMode.Enabled, MinZoomFactor = 1, MaxZoomFactor = 8,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(24, 56, 24, 24) };
        image.HorizontalAlignment = HorizontalAlignment.Center; image.VerticalAlignment = VerticalAlignment.Center;
        scroller.SizeChanged += (_, e) => { image.Width = Math.Max(1, e.NewSize.Width); image.Height = Math.Max(1, e.NewSize.Height); };
        surface.Children.Add(scroller);
        var heading = Label(name, 13); heading.Margin = new(18, 16, 64, 0); heading.VerticalAlignment = VerticalAlignment.Top;
        heading.TextTrimming = TextTrimming.CharacterEllipsis; surface.Children.Add(heading);
        var close = new Button { Width = 36, Height = 36, Padding = new(0), Margin = new(12), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        FluentDesign.IconButton(close, "\uE711", WorkflowText("Fermer l’image · Échap", "Close image · Escape"), false);
        close.Click += (_, _) => viewer.Close(); surface.Children.Add(close);
        surface.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; viewer.Close(); } };
        viewer.Closed += (_, _) => imagePreviewWindows.Remove(viewer);
        if (!imagePreviewLifetimeAttached)
        { imagePreviewLifetimeAttached = true; Closed += (_, _) => { foreach (var window in imagePreviewWindows.ToList()) window.Close(); }; }
        viewer.Content = surface; imagePreviewWindows.Add(viewer);
        try
        {
            viewer.Activate();
            viewer.AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            close.Focus(FocusState.Programmatic);
        }
        catch { viewer.Close(); throw; }
    }
}
