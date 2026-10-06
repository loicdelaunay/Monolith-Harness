using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    bool composerInfoExpanded = true;
    readonly Button infoToggle = new() { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 28, Padding = new(12,4,12,4), Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush") };
    readonly TextBlock infoHeading = Label("", 12);
    readonly ContextUsageRing collapsedContextRing = new();
    readonly TextBlock collapsedContextText = Label("0 %", 10);
    readonly TextBlock collapsedSpeedText = Label("⚡ — tok/s", 10);
    readonly StackPanel collapsedMetrics = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    void ApplyAppearance()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        MarkdownRenderer.ConfigureVisuals(config);
        ChatDensity.Configure(config.ChatMessageDensity);
        AppTypography.Configure(config);
        AppTypography.SetScope(composer, FontArea.User);
        TextZoom.Set(config.FontZoomPercent);
        ApplyTheme(config.Theme);
        ApplyBranding();
        composerInfoExpanded = config.ComposerInfoExpanded;
        UpdateInfoPanel();
    }
    void ApplyTheme(string themeId, IEnumerable<AppearanceTheme>? customThemes = null)
    {
        var theme = AppearanceThemes.Get(themeId, customThemes ?? FeatureSettings.Read(state.FeaturesJson).CustomThemes);
        FluentDesign.SetTheme(theme);
        MarkdownRenderer.RefreshDensity();
        RefreshVersionChip(theme);
        root.RequestedTheme = theme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        root.Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush");
        shell.PaneBackground = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush");
        ApplyBranding(theme.Dark);
        FluentDesign.WindowChrome(this);
        if (settingsWindow?.Content is FrameworkElement settingsRoot)
        { settingsRoot.RequestedTheme = root.RequestedTheme; FluentDesign.WindowChrome(settingsWindow); }
        if (tasksWindow?.Content is FrameworkElement tasksRoot)
        { tasksRoot.RequestedTheme = root.RequestedTheme; FluentDesign.WindowChrome(tasksWindow); }
        if (localModelsWindow != null)
        { localModelsWindow.Panel.RequestedTheme = root.RequestedTheme; FluentDesign.WindowChrome(localModelsWindow); }
    }
    void UpdateInfoPanel()
    {
        bool expanded = composerInfoExpanded;
        floatingInfoBar.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        RefreshInfoHeading();
        collapsedMetrics.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        RefreshCollapsedMetrics();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(infoToggle, WorkflowText("Afficher les informations du modèle", "Show model information"));
    }
    void RefreshInfoHeading()
    {
        infoHeading.Text = (composerInfoExpanded ? "⌄  " : "›  ") + WorkflowText("Plus d’information", "More information");
        ToolTipService.SetToolTip(infoHeading, ActiveRun?.Provider.Model ?? provider?.Model ?? "");
    }
    void RefreshCollapsedMetrics()
    {
        RefreshInfoHeading();
        collapsedContextRing.Update(contextBar.Value / 100d);
        collapsedContextText.Text = contextPercentText.Text;
        collapsedSpeedText.Text = speedValueText.Text;
        collapsedContextText.Foreground = FluentDesign.Resource("AccentTextFillColorPrimaryBrush");
        collapsedSpeedText.Foreground = FluentDesign.Secondary;
        var description = WorkflowText("Contexte", "Context") + " : " + contextPercentText.Text + " · " + contextValueText.Text + " · " + speedValueText.Text;
        ToolTipService.SetToolTip(infoToggle, description);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(collapsedMetrics, description);
    }
    UIElement BuildComposerSurface()
    {
        var panel = new StackPanel { Spacing = 0 };
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        infoHeading.TextWrapping = TextWrapping.NoWrap;
        infoHeading.TextTrimming = TextTrimming.CharacterEllipsis;
        infoHeading.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(infoHeading);
        collapsedMetrics.Children.Add(collapsedContextRing);
        collapsedMetrics.Children.Add(collapsedContextText);
        collapsedMetrics.Children.Add(collapsedSpeedText);
        Grid.SetColumn(collapsedMetrics, 1); header.Children.Add(collapsedMetrics);
        infoToggle.Content = header;
        contextBar.RegisterPropertyChangedCallback(RangeBase.ValueProperty, (_, _) => RefreshCollapsedMetrics());
        foreach (var text in new[] { contextPercentText, contextValueText, speedValueText })
            text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => RefreshCollapsedMetrics());
        var information = new StackPanel { Spacing = 0, Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush") };
        information.Children.Add(infoToggle);
        information.Children.Add(BuildFloatingInfoBar());
        floatingInfoBar.Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush");
        panel.Children.Add(information);
        floatingInfoBar.CornerRadius = new(0); floatingInfoBar.BorderThickness = new(0);
        panel.Children.Add(BuildComposer());
        infoToggle.Click += async (_, _) => await Guard(async () =>
        {
            composerInfoExpanded = !composerInfoExpanded;
            UpdateInfoPanel();
            var config = FeatureSettings.Read(state.FeaturesJson); config.ComposerInfoExpanded = composerInfoExpanded;
            state.FeaturesJson = config.Json(); await db.SaveChangesAsync();
        });
        var surface = FluentDesign.Surface(panel, 0);
        UpdateInfoPanel();
        return surface;
    }
}
