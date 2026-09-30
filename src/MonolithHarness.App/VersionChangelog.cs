using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using Windows.Foundation;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    Button? versionChip;
    bool changelogOpen;
    static Windows.UI.Color ParseSidebarColor(string hex) => Windows.UI.Color.FromArgb(255,
        Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
    Button BuildVersionChip()
    {
        versionChip = new Button { Content = "v" + GitHubUpdates.CurrentVersion, FontSize = 11, MinWidth = 66, MinHeight = 24,
            Padding = new(8, 4, 8, 4), CornerRadius = new(12), BorderThickness = new(1), HorizontalContentAlignment = HorizontalAlignment.Center };
        versionChip.Click += async (_, _) => await Guard(ShowChangelogAsync);
        versionChip.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(versionChip, WorkflowText("Voir le changelog", "View changelog"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(versionChip, WorkflowText("Version ", "Version ") + GitHubUpdates.CurrentVersion + WorkflowText(" · Voir le changelog", " · View changelog"));
        var settings = FeatureSettings.Read(state.FeaturesJson);
        RefreshVersionChip(AppearanceThemes.Get(settings.Theme, settings.CustomThemes));
        return versionChip;
    }
    void RefreshVersionChip(AppearanceTheme theme)
    {
        if (versionChip == null) return;
        string first = ThemeContrast.Blend(theme.Surface, theme.Accent, .15), last = first, foreground = theme.Text;
        for (double amount = .45; amount >= .14; amount -= .05)
        {
            last = ThemeContrast.Blend(theme.Surface, theme.Accent, amount);
            var samples = Enumerable.Range(0, 11).Select(i => ThemeContrast.Blend(first, last, i / 10d)).ToArray();
            double dark = samples.Min(x => ThemeContrast.Ratio("#0E0F12", x)), light = samples.Min(x => ThemeContrast.Ratio("#FFFFFF", x));
            foreground = dark >= light ? "#0E0F12" : "#FFFFFF";
            if (Math.Max(dark, light) >= 4.5) break;
        }
        versionChip.Background = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), GradientStops = {
            new GradientStop { Offset = 0, Color = ParseSidebarColor(first) }, new GradientStop { Offset = 1, Color = ParseSidebarColor(last) } } };
        versionChip.BorderBrush = new SolidColorBrush(ParseSidebarColor(ThemeContrast.Blend(theme.Surface, theme.Accent, .5)));
        versionChip.Foreground = new SolidColorBrush(ParseSidebarColor(foreground));
    }
    async Task ShowChangelogAsync()
    {
        if (changelogOpen) return;
        changelogOpen = true;
        try
        {
            using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("MonolithHarness.Changelog.md") ?? throw new IOException("Changelog indisponible / Changelog unavailable.");
            using var reader = new StreamReader(stream); var markdown = await reader.ReadToEndAsync();
            var content = new StackPanel { Spacing = 12 };
            MarkdownRenderer.RenderTo(content, markdown);
            var viewer = new ScrollViewer { Content = content, Width = Math.Max(320, Math.Min(860, root.ActualWidth - 110)), MaxHeight = Math.Max(240, Math.Min(660, root.ActualHeight - 180)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = WorkflowText("Historique des versions", "Changelog"), Content = viewer, PrimaryButtonText = WorkflowText("Fermer", "Close"), DefaultButton = ContentDialogButton.Primary };
            dialog.Resources["ContentDialogMaxWidth"] = viewer.Width + 60;
            ObserveTextZoom(viewer);
            // Local reading should not mark an active agent as waiting for an answer.
            await approvalQueue.WaitAsync();
            try { await dialog.ShowAsync(); }
            finally { approvalQueue.Release(); }
        }
        finally { changelogOpen = false; }
    }
}
