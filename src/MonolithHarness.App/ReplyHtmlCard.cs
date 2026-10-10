using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using MonolithHarness.Presentation;

namespace MonolithHarness.App;

internal sealed class ReplyHtmlCard : UserControl
{
    readonly ReplyWebDocument artifact;
    ReplyHtmlPreview? inlinePreview;
    Window? previewWindow;
    readonly Func<string, CancellationToken, Task<byte[]?>>? localImage;
    FrameworkElement Native() => ReplyHtmlView.Build(artifact, ChatDensity.FontSize,
        FluentDesign.Primary, FluentDesign.Card, FluentDesign.Stroke,
        FluentDesign.Resource("AccentFillColorDefaultBrush"), true, localImage);

    internal ReplyHtmlCard(ReplyWebDocument artifact, Func<string, CancellationToken, Task<byte[]?>>? localImage = null)
    {
        this.artifact = artifact; this.localImage = localImage;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = artifact.Title + " · HTML/CSS", Foreground = FluentDesign.Primary,
            FontSize = 16, TextWrapping = TextWrapping.Wrap });
        if (MarkdownRenderer.AllowRichWebPreview && MarkdownRenderer.PreviewHtmlInline && artifact.ReadyForInlinePreview)
        {
            var dynamicHeight = MarkdownRenderer.DynamicHtmlHeight;
            inlinePreview = new ReplyHtmlPreview(artifact, Native, dynamicHeight)
            { Height = 360, MinHeight = dynamicHeight ? 80 : 200, MaxHeight = dynamicHeight ? 2000 : double.PositiveInfinity };
            panel.Children.Add(inlinePreview);
        }
        else panel.Children.Add(new ScrollViewer { Content = Native(), MaxHeight = MarkdownRenderer.DynamicHtmlHeight ? 2000 : 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled });
        var open = new Button { Content = UiText.Resolve("Ouvrir l’aperçu", "Open preview"), HorizontalAlignment = HorizontalAlignment.Right };
        open.Click += (_, _) =>
        {
            try { Open(); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                panel.Children.Add(new TextBlock { Text = UiText.Resolve("Aperçu indisponible : ", "Preview unavailable: ") + ex.Message,
                    Foreground = FluentDesign.Secondary, TextWrapping = TextWrapping.Wrap });
            }
        };
        panel.Children.Add(open);
        Content = new Border { Child = panel, Padding = new(12), CornerRadius = new(12), BorderBrush = FluentDesign.Stroke,
            BorderThickness = new(1), Background = FluentDesign.Card };
    }

    void Open()
    {
        if (previewWindow != null) { previewWindow.Activate(); return; }
        var window = new Window { Title = artifact.Title }; previewWindow = window;
        var grid = new Grid { Background = FluentDesign.Card,
            RequestedTheme = FluentDesign.CurrentTheme.Dark ? ElementTheme.Dark : ElementTheme.Light, Padding = new(16) };
        grid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        ReplyHtmlPreview? externalPreview = null;
        if (MarkdownRenderer.AllowRichWebPreview)
        { externalPreview = new ReplyHtmlPreview(artifact, Native); grid.Children.Add(externalPreview); }
        else
        {
            grid.Children.Add(new ScrollViewer { Content = Native(), HorizontalScrollMode = ScrollMode.Disabled });
            var notice = new TextBlock
            {
                Text = UiText.Resolve("Rendu natif simplifié. Pour le rendu HTML/CSS complet, activez les aperçus WebView dans Paramètres / Chat.",
                    "Simplified native rendering. For full HTML/CSS rendering, enable WebView previews in Settings / Chat."),
                Foreground = FluentDesign.Secondary, TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0)
            };
            Grid.SetRow(notice, 1); grid.Children.Add(notice);
        }
        var close = new Button { Content = UiText.Resolve("Fermer", "Close"), HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new(0, 12, 0, 0) };
        close.Click += (_, _) => window.Close(); Grid.SetRow(close, 2); grid.Children.Add(close);
        window.Closed += (_, _) =>
        {
            externalPreview?.Close();
            if (ReferenceEquals(previewWindow, window)) previewWindow = null;
            // The native overlay may lose its viewport while another window owns focus.
            inlinePreview?.Recover();
        };
        window.Activated += (_, e) =>
        {
            if (e.WindowActivationState.ToString() == "Deactivated") inlinePreview?.Recover();
        };
        window.Content = grid; FluentDesign.WindowChrome(window);
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 1000, Height = 760 });
        window.Activate();
    }
}
