using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly ToggleButton composerSelectionModeToggle = new() { Width = 26, Height = 30, MinWidth = 0, MinHeight = 0,
        Padding = new(0), BorderThickness = new(0), CornerRadius = new(8), VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    readonly StackPanel quickLevelPanel = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    readonly TextBlock quickLevelTitle = Label("", 14), quickLevelDetails = Label("", 11);
    readonly Button quickLevelSelect = new() { Padding = new(0), MinHeight = 0, MinWidth = 0, BorderThickness = new(0),
        HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left,
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    Grid? modelOptionsPanel;
    bool compactModelPicker;
    StackPanel? modelAdvancedSection;
    Grid? thinkingAdvancedSection;
    Button? modelDoneButton;
    TextBlock? modelOptionsHeading;
    bool applyingQuickLevel, changingQuickMode, modelOptionsIsOpen;
    string? previewQuickLevelId;

    string[] ThinkingLevelLabels() => ["🧠 Auto", "🧠 " + WorkflowText("Faible", "Low"), "🧠 " + WorkflowText("Moyen", "Medium"),
        "🧠 " + WorkflowText("Élevé", "High"), "🧠 " + WorkflowText("Désactivé", "Disabled")];

    StackPanel BuildQuickModelHeader(TextBlock heading)
    {
        modelOptionsHeading = heading;
        composerSelectionModeToggle.Click += async (_, _) =>
        {
            await Guard(async () =>
            {
                await SetQuickSelectionModeAsync(composerSelectionModeToggle.IsChecked == true ? "simple" : "advanced");
                if (!modelOptionsIsOpen && modelOptionsButton.IsEnabled) modelOptionsFlyout?.ShowAt(modelOptionsButton);
            });
            RefreshQuickModelUi();
        };
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.Children.Add(composerSelectionModeToggle);
        var description = new StackPanel { Spacing = 2 };
        quickLevelSelect.Content = quickLevelTitle; quickLevelSelect.Click += (_, _) => QueueComposerLevel();
        description.Children.Add(heading); description.Children.Add(quickLevelSelect); description.Children.Add(quickLevelDetails);
        Grid.SetColumn(description, 1); row.Children.Add(description);
        var header = new StackPanel(); header.Children.Add(row); return header;
    }

    StackPanel BuildQuickLevelPicker()
    {
        quickLevelTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        quickLevelTitle.Foreground = FluentDesign.Resource("AccentTextFillColorPrimaryBrush");
        quickLevelDetails.Foreground = FluentDesign.Secondary;
        quickLevelDetails.MaxLines = 2; quickLevelDetails.TextTrimming = TextTrimming.CharacterEllipsis;
        quickLevelPanel.Margin = new(0, 8, 0, 0);
        quickLevelPanel.Children.Add(BuildComposerQuickLevels());
        return quickLevelPanel;
    }

    void ResizeModelPicker()
    {
        if (modelOptionsPanel == null) return;
        var scale = TextZoom.ForWindow(root) / 100d;
        modelOptionsPanel.Width = Math.Max(120, Math.Min((compactModelPicker ? 260 : 350) * scale, root.ActualWidth - 48));
        modelOptionsPanel.RowSpacing = compactModelPicker ? 0 : 10;
    }

    async Task SetQuickSelectionModeAsync(string mode)
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        if (applyingQuickLevel || changingQuickMode || !config.QuickModelLevelsEnabled || config.QuickModelSelectionMode == mode) return;
        var previous = state.FeaturesJson;
        config.QuickModelSelectionMode = mode; state.FeaturesJson = config.Json();
        changingQuickMode = true; RefreshQuickModelUi();
        try { await db.SaveChangesAsync(); previewQuickLevelId = null; }
        catch { state.FeaturesJson = previous; throw; }
        finally { changingQuickMode = false; RefreshModelOptions(); }
    }

    void RefreshQuickModelUi()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        var enabled = config.QuickModelLevelsEnabled && config.QuickModelLevels.Count > 0;
        var simple = enabled && config.QuickModelSelectionMode == "simple";
        compactModelPicker = simple;
        composerSelectionModeToggle.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        composerSelectionModeToggle.IsChecked = simple;
        composerSelectionModeToggle.IsEnabled = enabled && !applyingQuickLevel && !changingQuickMode && ActiveRun == null;
        var modeIcon = FluentDesign.Icon("\uE712", 14);
        modeIcon.Foreground = simple ? FluentDesign.Resource("TextOnAccentFillColorPrimaryBrush") : FluentDesign.Secondary;
        composerSelectionModeToggle.Content = modeIcon;
        var switchLabel = WorkflowText(simple ? "Passer à la sélection détaillée" : "Passer aux niveaux rapides", simple ? "Switch to detailed selection" : "Switch to quick levels");
        ToolTipService.SetToolTip(composerSelectionModeToggle, switchLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(composerSelectionModeToggle, switchLabel);
        modelOptionsButton.IsEnabled = modelSelector.IsEnabled = !applyingQuickLevel && ActiveRun == null;
        quickLevelPanel.Visibility = simple ? Visibility.Visible : Visibility.Collapsed;
        quickLevelSelect.Visibility = quickLevelDetails.Visibility = simple ? Visibility.Visible : Visibility.Collapsed;
        if (modelAdvancedSection != null) modelAdvancedSection.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        if (thinkingAdvancedSection != null) thinkingAdvancedSection.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        modelContextEditor.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        if (modelDoneButton != null) modelDoneButton.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        if (modelOptionsHeading != null)
        {
            modelOptionsHeading.Text = WorkflowText("Modèle et réflexion", "Model and thinking");
            modelOptionsHeading.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        }
        ResizeModelPicker(); RefreshComposerQuickLevels(); RefreshComposerModelSummary();
    }

    string QuickLevelCaption(QuickModelLevel level, int index) => WorkflowText("Niveau ", "Level ") + (index + 1)
        + (level.Name.Length == 0 ? "" : " · " + level.Name);

    void RefreshQuickLevelPreview()
    {
        var config = FeatureSettings.Read(state.FeaturesJson); var levels = config.QuickModelLevels;
        if (levels.Count == 0) return;
        var match = levels.FindIndex(level => QuickModelShortcuts.Matches(level, provider, state, chat));
        var index = modelOptionsIsOpen ? levels.FindIndex(l => l.Id == previewQuickLevelId) : match;
        if (index < 0) index = match;
        if (index < 0)
        {
            quickLevelTitle.Text = WorkflowText("Personnalisé", "Custom");
            quickLevelDetails.Text = provider?.Model ?? WorkflowText("Choisir un modèle", "Choose a model");
            ToolTipService.SetToolTip(quickLevelDetails, quickLevelDetails.Text);
            var firstLevel = WorkflowText("Sélectionner le premier niveau configuré", "Select the first configured level") + " · " + QuickLevelCaption(levels[0], 0);
            ToolTipService.SetToolTip(quickLevelSelect, firstLevel);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(quickLevelSelect, firstLevel);
            return;
        }
        var level = levels[index];
        quickLevelTitle.Text = level.Name.Length > 0 ? level.Name : QuickLevelCaption(level, index);
        var detail = level.Model + " · " + (level.InteractionMode == "chat" ? "Chat" : "Agent") + " · "
            + ThinkingLevelLabels()[Array.IndexOf(QuickModelShortcuts.ThinkingLevels, level.ThinkingLevel)];
        quickLevelDetails.Text = detail;
        if (!ComposerLevelAvailable(level)) quickLevelDetails.Text += " · " + WorkflowText("Indisponible", "Unavailable");
        ToolTipService.SetToolTip(quickLevelSelect, WorkflowText("Sélectionner ce niveau", "Select this level") + " · " + QuickLevelCaption(level, index));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(quickLevelSelect, WorkflowText("Sélectionner ce niveau", "Select this level") + " · " + QuickLevelCaption(level, index));
        ToolTipService.SetToolTip(quickLevelDetails, detail);
    }

    async Task ApplyQuickLevelAsync(string? requestedId = null, bool keepPickerOpen = false)
    {
        if (applyingQuickLevel || chat == null || selectedSubagent != null || ActiveRun != null || !conversationReady || conversationLoading || databaseMaintenanceBusy || conversationRetentionBusy) return;
        var config = FeatureSettings.Read(state.FeaturesJson);
        var level = config.QuickModelLevels.FirstOrDefault(l => l.Id == (requestedId ?? previewQuickLevelId));
        if (!config.QuickModelLevelsEnabled || level == null) return;
        var target = db.Providers.Local.FirstOrDefault(p => p.Id == level.ProviderId && db.Entry(p).State != Microsoft.EntityFrameworkCore.EntityState.Deleted);
        if (target == null) throw new InvalidOperationException(WorkflowText("Le fournisseur de ce niveau est indisponible.", "This level’s provider is unavailable."));
        var owner = chat; var oldProvider = provider; var oldProviderId = state.ProviderId;
        var oldModel = target.Model; var oldMode = owner.InteractionMode; var oldThinking = state.ThinkingLevel; var oldChatThinking = state.ChatThinkingLevel;
        previewQuickLevelId = level.Id; applyingQuickLevel = true; RefreshGenerationControls(); RefreshQuickModelUi();
        try
        {
            QuickModelShortcuts.Apply(level, target, state, owner);
            try { await db.SaveChangesAsync(); }
            catch
            {
                ModelContexts.Select(target, oldModel); owner.InteractionMode = oldMode; state.ProviderId = oldProviderId;
                state.ThinkingLevel = oldThinking; state.ChatThinkingLevel = oldChatThinking; provider = oldProvider; throw;
            }
            provider = target;
            var wasLoading = loading; loading = true;
            try { providers.SelectedItem = target; } finally { loading = wasLoading; }
            PopulateModelSelector(); UpdateProvider(); PopulateThinkingSelector(); RefreshConversationMode();
            if (chat?.Id == owner.Id) { await RefreshPinnedTasksAsync(); UpdateSourceLabel(); UpdateFloatingAssets(); }
            if (!keepPickerOpen) modelOptionsFlyout?.Hide();
            ShowStatus(WorkflowText("Niveau appliqué : ", "Level applied: ") + QuickLevelCaption(level, config.QuickModelLevels.IndexOf(level)), StatusKind.Notice);
        }
        catch { previewQuickLevelId = null; throw; }
        finally { applyingQuickLevel = false; RefreshGenerationControls(); RefreshModelOptions(); }
    }
}
