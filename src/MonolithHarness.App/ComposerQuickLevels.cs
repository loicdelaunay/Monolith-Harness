using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly ComboBox composerQuickLevels = new() { Width = 150, Height = 38, MinWidth = 0, MinHeight = 0,
        FontSize = 11, Padding = new(8, 3, 8, 3), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
    bool syncingComposerLevels;
    string composerLevelsKey = "";
    readonly List<ComboBoxItem> composerLevelChoices = [];

    ComboBox BuildComposerQuickLevels()
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(composerQuickLevels, WorkflowText("Niveau d’IA", "AI level"));
        composerQuickLevels.SelectionChanged += async (_, _) =>
        {
            if (syncingComposerLevels || composerQuickLevels.SelectedItem is not ComboBoxItem { Tag: string id } || id.Length == 0) return;
            try { await Guard(() => ApplyQuickLevelAsync(id)); } finally { RefreshComposerQuickLevels(); }
        };
        RefreshComposerQuickLevels(); return composerQuickLevels;
    }
    void RefreshComposerQuickLevels()
    {
        var settings = FeatureSettings.Read(state.FeaturesJson); var levels = settings.QuickModelLevels;
        composerQuickLevels.Visibility = settings.QuickModelLevelsEnabled && levels.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        var matching = levels.FirstOrDefault(l => QuickModelShortcuts.Matches(l, provider, state, chat));
        bool Available(QuickModelLevel level) => db.Providers.Local.Any(p => db.Entry(p).State != Microsoft.EntityFrameworkCore.EntityState.Deleted && QuickModelShortcuts.Available(level, p));
        var key = UiText.Language + "|" + state.FeaturesJson + "|" + (matching == null) + "|" + string.Join(',', levels.Select(Available));
        syncingComposerLevels = true;
        try
        {
            if (composerLevelsKey != key)
            {
                composerLevelsKey = key; composerLevelChoices.Clear();
                if (matching == null) composerLevelChoices.Add(new() { Content = WorkflowText("Personnalisé", "Custom"), Tag = "", IsEnabled = false });
                for (int i = 0; i < levels.Count; i++)
                {
                    var level = levels[i];
                    var item = new ComboBoxItem { Content = QuickLevelCaption(level, i), Tag = level.Id, IsEnabled = Available(level) };
                    ToolTipService.SetToolTip(item, level.Model + " · " + (level.InteractionMode == "chat" ? "Chat" : "Agent") + " · " + level.ThinkingLevel
                        + (item.IsEnabled ? "" : " · " + WorkflowText("Modèle indisponible", "Model unavailable")));
                    composerLevelChoices.Add(item);
                }
                composerQuickLevels.ItemsSource = composerLevelChoices.ToList();
            }
            composerQuickLevels.SelectedItem = composerLevelChoices.FirstOrDefault(item => (string)item.Tag == (matching?.Id ?? ""));
        }
        finally { syncingComposerLevels = false; }
        composerQuickLevels.IsEnabled = chat != null && selectedSubagent == null && ActiveRun == null && conversationReady && !conversationLoading
            && !databaseMaintenanceBusy && !conversationRetentionBusy && !applyingQuickLevel && levels.Any(Available);
        var tooltip = matching == null ? WorkflowText("Choisir rapidement un niveau d’IA", "Quickly choose an AI level")
            : matching.Model + " · " + (matching.InteractionMode == "chat" ? "Chat" : "Agent") + " · " + matching.ThinkingLevel;
        ToolTipService.SetToolTip(composerQuickLevels, tooltip);
    }
}
