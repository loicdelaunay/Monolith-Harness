using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly Grid composerModelSummary = new() { ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock composerThinkingIcon = Label("🧠", 11), composerThinkingMarker = Label("A", 10);
    readonly ContextUsageRing composerContextRing = new();
    readonly TextBlock composerContextText = Label("0 %", 10), composerSpeedText = Label("⚡ — tok/s", 10);
    readonly StackPanel composerMetrics = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel composerContextIndicator = new() { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel composerSpeedIndicator = new() { VerticalAlignment = VerticalAlignment.Center };
    Action? resizeComposerFooter;
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
        RefreshComposerModelSummary();
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
    void RefreshComposerModelSummary()
    {
        var current = ActiveRun;
        var name = current?.Provider.Model ?? provider?.Model ?? (modelSelector.SelectedItem as ModelChoice)?.Model;
        selectedModelName.Text = string.IsNullOrWhiteSpace(name) ? WorkflowText("Choisir un modèle", "Choose a model") : name;
        var thinking = (current?.Options.ThinkingLevel ?? ConversationModes.EffectiveThinking(chat, state.ThinkingLevel, state.ChatThinkingLevel)).ToLowerInvariant();
        var index = thinking switch { "low" => 1, "medium" => 2, "high" => 3, "none" => 4, _ => 0 };
        selectedThinking.Text = ThinkingLevelLabels()[index];
        composerThinkingMarker.Text = index switch { 1 => "1", 2 => "2", 3 => "3", 4 => "—", _ => "A" };
        composerThinkingMarker.Foreground = index == 4 ? FluentDesign.Secondary : FluentDesign.Resource("AccentTextFillColorPrimaryBrush");
        var thinkingLabel = WorkflowText("Réflexion : ", "Thinking: ") + selectedThinking.Text;
        ToolTipService.SetToolTip(composerThinkingIcon, thinkingLabel);
        ToolTipService.SetToolTip(composerThinkingMarker, thinkingLabel);
        var source = current?.Provider.Name ?? provider?.Name ?? "";
        var description = selectedModelName.Text + (source.Length == 0 ? "" : "\n" + source) + "\n" + thinkingLabel;
        var config = FeatureSettings.Read(state.FeaturesJson);
        if (config.QuickModelLevelsEnabled)
        {
            var match = config.QuickModelLevels.FindIndex(level => QuickModelShortcuts.Matches(level, provider, state, chat));
            if (match >= 0) description += "\n" + QuickLevelCaption(config.QuickModelLevels[match], match);
        }
        ToolTipService.SetToolTip(modelOptionsButton, description);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(modelOptionsButton,
            WorkflowText("Choisir le modèle et sa réflexion", "Choose model and thinking") + " · " + description);
        composerContextIndicator.Visibility = config.ShowComposerContext ? Visibility.Visible : Visibility.Collapsed;
        composerSpeedIndicator.Visibility = config.ShowComposerSpeed ? Visibility.Visible : Visibility.Collapsed;
        composerMetrics.Visibility = config.ShowComposerContext || config.ShowComposerSpeed ? Visibility.Visible : Visibility.Collapsed;
        RefreshComposerMetrics(); resizeComposerFooter?.Invoke();
    }
    void RefreshComposerMetrics()
    {
        composerContextRing.Update(contextBar.Value / 100d);
        composerContextText.Text = contextPercentText.Text;
        composerSpeedText.Text = speedValueText.Text;
        composerContextText.Foreground = FluentDesign.Resource("AccentTextFillColorPrimaryBrush");
        composerSpeedText.Foreground = FluentDesign.Secondary;
        var description = WorkflowText("Contexte", "Context") + " : " + contextPercentText.Text + " · " + contextValueText.Text + " · " + speedValueText.Text;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(composerMetrics, description);
    }
    Grid BuildComposerModelSummary()
    {
        composerModelSummary.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        composerModelSummary.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        composerModelSummary.Children.Add(modelOptionsButton);
        // Center each inline text independently; stretched text boxes otherwise sit above the controls' center.
        foreach (var text in new[] { composerThinkingIcon, composerThinkingMarker, composerContextText, composerSpeedText })
        {
            text.VerticalAlignment = VerticalAlignment.Center;
            text.TextWrapping = TextWrapping.NoWrap;
        }
        composerContextIndicator.Children.Add(composerContextRing); composerContextIndicator.Children.Add(composerContextText);
        composerSpeedIndicator.Children.Add(composerSpeedText);
        composerMetrics.Children.Add(composerContextIndicator); composerMetrics.Children.Add(composerSpeedIndicator);
        Grid.SetColumn(composerMetrics, 1); composerModelSummary.Children.Add(composerMetrics);
        AttachContextPopover(composerContextIndicator); AttachSpeedPopover(composerSpeedIndicator); RefreshSpeedPopover();
        contextBar.RegisterPropertyChangedCallback(RangeBase.ValueProperty, (_, _) => RefreshComposerMetrics());
        foreach (var text in new[] { contextPercentText, contextValueText, speedValueText })
            text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => RefreshComposerMetrics());
        RefreshComposerModelSummary();
        return composerModelSummary;
    }
    UIElement BuildComposerSurface()
    {
        InitializeComposerModelSelection();
        assetsScroll.Content = assetsBar;
        var panel = new StackPanel { Spacing = 0 };
        assetsScroll.Margin = new(8, 4, 8, 4);
        panel.Children.Add(assetsScroll);
        panel.Children.Add(BuildComposer());
        return FluentDesign.Surface(panel, 0);
    }
}
