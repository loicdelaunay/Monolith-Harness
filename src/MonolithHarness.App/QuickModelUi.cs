using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly Button quickSimpleMode = new() { Content = "SIMPLE", FontSize = 11, MinHeight = 28, Padding = new(10, 4, 10, 4) };
    readonly Button quickAdvancedMode = new() { Content = "AVANCÉ", FontSize = 11, MinHeight = 28, Padding = new(10, 4, 10, 4) };
    readonly Border quickModeTabs = new() { CornerRadius = new(8), Padding = new(2), Background = FluentDesign.Card, Visibility = Visibility.Collapsed };
    readonly StackPanel quickLevelPanel = new() { Spacing = 10, Visibility = Visibility.Collapsed };
    readonly Slider quickLevelSlider = new() { Minimum = 1, Maximum = 1, StepFrequency = 1, TickFrequency = 1, IsThumbToolTipEnabled = false };
    readonly TextBlock quickLevelTitle = Label("", 15), quickLevelDetails = Label("", 12);
    readonly Button quickApplyLevel = new() { MinHeight = 32, HorizontalAlignment = HorizontalAlignment.Right, Padding = new(12, 4, 12, 4) };
    StackPanel? modelAdvancedSection;
    Grid? thinkingAdvancedSection;
    Button? modelDoneButton;
    TextBlock? modelOptionsHeading;
    bool syncingQuickControls, applyingQuickLevel, changingQuickMode, modelOptionsIsOpen;
    string? previewQuickLevelId;

    string[] ThinkingLevelLabels() => ["🧠 Auto", "🧠 " + WorkflowText("Faible", "Low"), "🧠 " + WorkflowText("Moyen", "Medium"),
        "🧠 " + WorkflowText("Élevé", "High"), "🧠 " + WorkflowText("Désactivé", "Disabled")];

    StackPanel BuildQuickModelHeader(TextBlock heading)
    {
        modelOptionsHeading = heading;
        quickModeTabs.Child = Row(quickSimpleMode, quickAdvancedMode);
        quickSimpleMode.Click += async (_, _) => await Guard(() => SetQuickSelectionModeAsync("simple"));
        quickAdvancedMode.Click += async (_, _) => await Guard(() => SetQuickSelectionModeAsync("advanced"));
        var header = new StackPanel { Spacing = 8 }; header.Children.Add(quickModeTabs); header.Children.Add(heading); return header;
    }

    StackPanel BuildQuickLevelPicker()
    {
        quickLevelPanel.Children.Add(quickLevelTitle);
        quickLevelPanel.Children.Add(quickLevelSlider);
        quickLevelPanel.Children.Add(quickLevelDetails);
        quickApplyLevel.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        quickLevelSlider.ValueChanged += (_, _) =>
        {
            if (syncingQuickControls) return;
            var levels = FeatureSettings.Read(state.FeaturesJson).QuickModelLevels;
            var index = (int)Math.Round(quickLevelSlider.Value) - 1;
            if (index >= 0 && index < levels.Count) previewQuickLevelId = levels[index].Id;
            RefreshQuickLevelPreview();
        };
        quickApplyLevel.Click += async (_, _) => await Guard(() => ApplyQuickLevelAsync());
        return quickLevelPanel;
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
        quickModeTabs.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        quickSimpleMode.IsEnabled = quickAdvancedMode.IsEnabled = !applyingQuickLevel && !changingQuickMode;
        quickApplyLevel.Visibility = simple ? Visibility.Visible : Visibility.Collapsed;
        modelOptionsButton.IsEnabled = modelSelector.IsEnabled = !applyingQuickLevel && ActiveRun == null;
        quickAdvancedMode.Content = WorkflowText("AVANCÉ", "ADVANCED");
        quickSimpleMode.Style = simple ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
        quickAdvancedMode.Style = !simple ? (Style)Application.Current.Resources["AccentButtonStyle"] : null;
        quickLevelPanel.Visibility = simple ? Visibility.Visible : Visibility.Collapsed;
        if (modelAdvancedSection != null) modelAdvancedSection.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        if (thinkingAdvancedSection != null) thinkingAdvancedSection.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        modelContextEditor.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        if (modelDoneButton != null) modelDoneButton.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
        if (modelOptionsHeading != null) modelOptionsHeading.Text = simple ? WorkflowText("Réglage rapide", "Quick settings")
            : WorkflowText("Modèle et réflexion", "Model and thinking");
        RefreshQuickLevelPreview(); RefreshComposerQuickLevels(); RefreshComposerModelSummary();
    }

    string QuickLevelCaption(QuickModelLevel level, int index) => WorkflowText("Niveau ", "Level ") + (index + 1)
        + (level.Name.Length == 0 ? "" : " · " + level.Name);

    void RefreshQuickLevelPreview()
    {
        var config = FeatureSettings.Read(state.FeaturesJson); var levels = config.QuickModelLevels;
        if (levels.Count == 0) { quickApplyLevel.IsEnabled = false; return; }
        var match = levels.FindIndex(level => QuickModelShortcuts.Matches(level, provider, state, chat));
        var index = modelOptionsIsOpen ? levels.FindIndex(l => l.Id == previewQuickLevelId) : match;
        if (index < 0) index = match >= 0 ? match : 0;
        var level = levels[index]; previewQuickLevelId = level.Id;
        syncingQuickControls = true;
        try { quickLevelSlider.Maximum = levels.Count; quickLevelSlider.Value = index + 1; quickLevelSlider.IsEnabled = levels.Count > 1 && !applyingQuickLevel; }
        finally { syncingQuickControls = false; }
        quickLevelTitle.Text = QuickLevelCaption(level, index);
        var source = db.Providers.Local.FirstOrDefault(p => p.Id == level.ProviderId);
        var available = source != null && db.Entry(source).State != Microsoft.EntityFrameworkCore.EntityState.Deleted && QuickModelShortcuts.Available(level, source);
        quickLevelDetails.Text = level.Model + "\n" + (source?.Name ?? WorkflowText("Fournisseur indisponible", "Provider unavailable")) + " · "
            + (level.InteractionMode == "chat" ? "Chat" : "Agent") + " · " + ThinkingLevelLabels()[Array.IndexOf(QuickModelShortcuts.ThinkingLevels, level.ThinkingLevel)];
        if (!available) quickLevelDetails.Text += "\n" + WorkflowText("Ce modèle n’est plus activé. Corrigez ce niveau dans Réglages → Raccourcis.", "This model is no longer enabled. Update this level in Settings → Shortcuts.");
        quickApplyLevel.Content = WorkflowText("Appliquer ce niveau", "Apply this level");
        quickApplyLevel.IsEnabled = available && chat != null && selectedSubagent == null && ActiveRun == null && conversationReady
            && !conversationLoading && !databaseMaintenanceBusy && !conversationRetentionBusy && !applyingQuickLevel;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(quickLevelSlider, WorkflowText("Niveau de modèle", "Model level"));
    }

    async Task ApplyQuickLevelAsync(string? requestedId = null)
    {
        if (applyingQuickLevel || chat == null || selectedSubagent != null || ActiveRun != null || !conversationReady || conversationLoading || databaseMaintenanceBusy || conversationRetentionBusy) return;
        var config = FeatureSettings.Read(state.FeaturesJson);
        var level = config.QuickModelLevels.FirstOrDefault(l => l.Id == (requestedId ?? previewQuickLevelId));
        if (!config.QuickModelLevelsEnabled || level == null) return;
        var target = db.Providers.Local.FirstOrDefault(p => p.Id == level.ProviderId && db.Entry(p).State != Microsoft.EntityFrameworkCore.EntityState.Deleted);
        if (target == null) throw new InvalidOperationException(WorkflowText("Le fournisseur de ce niveau est indisponible.", "This level’s provider is unavailable."));
        var owner = chat; var oldProvider = provider; var oldProviderId = state.ProviderId;
        var oldModel = target.Model; var oldMode = owner.InteractionMode; var oldThinking = state.ThinkingLevel; var oldChatThinking = state.ChatThinkingLevel;
        applyingQuickLevel = true; RefreshGenerationControls(); RefreshQuickModelUi();
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
            modelOptionsFlyout?.Hide();
            ShowStatus(WorkflowText("Niveau appliqué : ", "Level applied: ") + QuickLevelCaption(level, config.QuickModelLevels.IndexOf(level)), StatusKind.Notice);
        }
        finally { applyingQuickLevel = false; RefreshGenerationControls(); RefreshModelOptions(); }
    }
}
