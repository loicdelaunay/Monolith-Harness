using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    void ConfigureUserBubble(Border card, StackPanel host)
    {
        card.HorizontalAlignment = HorizontalAlignment.Right;
        void Resize()
        {
            var width = host.ActualWidth > 0 ? host.ActualWidth : scroll.ActualWidth;
            var scale = TextZoom.ForWindow(root) / 100d;
            card.MaxWidth = width > 0 ? Math.Max(1, Math.Min(820 * scale, width * (width < 420 * scale ? .92 : .78))) : 820 * scale;
        }
        void Changed(object sender, SizeChangedEventArgs e) => Resize();
        card.Loaded += (_, _) => { host.SizeChanged -= Changed; host.SizeChanged += Changed; Resize(); };
        card.Unloaded += (_, _) => host.SizeChanged -= Changed;
        Resize();
    }
}
