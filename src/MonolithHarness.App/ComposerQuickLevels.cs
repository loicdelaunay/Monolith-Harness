using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly MaterialLevelSlider composerLevelSlider = new() { Minimum = 1, Maximum = 1, StepFrequency = 1, TickFrequency = 1,
        IsThumbToolTipEnabled = false, MinWidth = 0, MinHeight = 48, Height = 48,
        Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
    readonly DispatcherTimer composerLevelDelay = new() { Interval = TimeSpan.FromMilliseconds(180) };
    bool syncingComposerLevels, draggingComposerLevel;
    string? pendingComposerLevel;
    int pendingComposerChat;
    string pendingComposerFeatures = "";

    Slider BuildComposerQuickLevels()
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(composerLevelSlider, WorkflowText("Niveau d’IA", "AI level"));
        composerLevelSlider.ValueChanged += (_, _) =>
        {
            if (syncingComposerLevels) return;
            PreviewComposerLevel();
            if (!draggingComposerLevel) QueueComposerLevel();
        };
        composerLevelSlider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) =>
        { draggingComposerLevel = true; composerLevelDelay.Stop(); }), true);
        void ReleaseLevel()
        {
            if (!draggingComposerLevel) return;
            draggingComposerLevel = false; QueueComposerLevel();
        }
        composerLevelSlider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => ReleaseLevel()), true);
        composerLevelSlider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => ReleaseLevel()), true);
        composerLevelSlider.KeyDown += (_, e) =>
        {
            if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space) { QueueComposerLevel(); e.Handled = true; }
        };
        composerLevelSlider.Unloaded += (_, _) => draggingComposerLevel = false;
        Closed += (_, _) => { composerLevelDelay.Stop(); pendingComposerLevel = null; draggingComposerLevel = false; };
        composerLevelDelay.Tick += async (_, _) =>
        {
            composerLevelDelay.Stop(); var id = pendingComposerLevel; pendingComposerLevel = null;
            if (id == null || chat?.Id != pendingComposerChat || state.FeaturesJson != pendingComposerFeatures) { previewQuickLevelId = null; RefreshComposerQuickLevels(); return; }
            try { await Guard(() => ApplyQuickLevelAsync(id, keepPickerOpen: true)); }
            finally { RefreshComposerQuickLevels(); }
        };
        RefreshComposerQuickLevels(); return composerLevelSlider;
    }
    bool ComposerLevelAvailable(QuickModelLevel level) => db.Providers.Local.Any(p =>
        db.Entry(p).State != Microsoft.EntityFrameworkCore.EntityState.Deleted && QuickModelShortcuts.Available(level, p));
    QuickModelLevel? PreviewComposerLevel()
    {
        var levels = FeatureSettings.Read(state.FeaturesJson).QuickModelLevels;
        var index = (int)Math.Round(composerLevelSlider.Value) - 1;
        if (index < 0 || index >= levels.Count) return null;
        var level = levels[index]; previewQuickLevelId = level.Id;
        RefreshQuickLevelPreview();
        var description = QuickLevelCaption(level, index) + "\n" + level.Model + " · "
            + (level.InteractionMode == "chat" ? "Chat" : "Agent") + " · " + level.ThinkingLevel;
        if (!ComposerLevelAvailable(level)) description += "\n" + WorkflowText("Modèle indisponible", "Model unavailable");
        ToolTipService.SetToolTip(composerLevelSlider, description);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(composerLevelSlider, description);
        return level;
    }
    void QueueComposerLevel()
    {
        if (!quickLevelSelect.IsEnabled || PreviewComposerLevel() is not { } level) return;
        if (!ComposerLevelAvailable(level))
        {
            composerLevelDelay.Stop(); pendingComposerLevel = null; previewQuickLevelId = null;
            ShowStatus(WorkflowText("Ce niveau utilise un modèle indisponible. Modifiez-le dans Réglages / Raccourcis.",
                "This level uses an unavailable model. Update it in Settings / Shortcuts."), StatusKind.Notice);
            RefreshComposerQuickLevels(); return;
        }
        if (QuickModelShortcuts.Matches(level, provider, state, chat))
        { composerLevelDelay.Stop(); pendingComposerLevel = null; RefreshComposerQuickLevels(); return; }
        pendingComposerLevel = level.Id; pendingComposerChat = chat?.Id ?? 0; pendingComposerFeatures = state.FeaturesJson;
        composerLevelDelay.Stop(); composerLevelDelay.Start();
    }
    void RefreshComposerQuickLevels()
    {
        var settings = FeatureSettings.Read(state.FeaturesJson); var levels = settings.QuickModelLevels;
        var visible = settings.QuickModelLevelsEnabled && levels.Count > 0;
        composerLevelSlider.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        var enabled = visible && chat != null && selectedSubagent == null && ActiveRun == null && conversationReady && !conversationLoading
            && !databaseMaintenanceBusy && !conversationRetentionBusy && !applyingQuickLevel && levels.Any(ComposerLevelAvailable);
        composerLevelSlider.IsEnabled = enabled && levels.Count > 1; quickLevelSelect.IsEnabled = enabled;
        if (pendingComposerLevel != null && (chat?.Id != pendingComposerChat || state.FeaturesJson != pendingComposerFeatures))
        { composerLevelDelay.Stop(); pendingComposerLevel = null; previewQuickLevelId = null; }
        if (!enabled)
        { composerLevelDelay.Stop(); pendingComposerLevel = null; if (!applyingQuickLevel) previewQuickLevelId = null; }
        var index = modelOptionsIsOpen ? levels.FindIndex(level => level.Id == previewQuickLevelId) : -1;
        if (index < 0) index = levels.FindIndex(level => QuickModelShortcuts.Matches(level, provider, state, chat));
        syncingComposerLevels = true;
        try
        {
            composerLevelSlider.Maximum = Math.Max(1, levels.Count);
            if (!draggingComposerLevel && pendingComposerLevel == null) composerLevelSlider.Value = Math.Max(1, index + 1);
        }
        finally { syncingComposerLevels = false; }
        if (!draggingComposerLevel && pendingComposerLevel == null && index < 0)
        {
            var label = WorkflowText("Réglage personnalisé · Choisir rapidement un niveau d’IA", "Custom settings · Quickly choose an AI level");
            ToolTipService.SetToolTip(composerLevelSlider, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(composerLevelSlider, label);
        }
        else
        {
            if (index >= 0) previewQuickLevelId = levels[index].Id;
            var label = index >= 0 ? QuickLevelCaption(levels[index], index) + "\n" + levels[index].Model : WorkflowText("Niveau d’IA", "AI level");
            ToolTipService.SetToolTip(composerLevelSlider, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(composerLevelSlider, label);
        }
        RefreshQuickLevelPreview();
    }
}
