using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly TextBlock goalLabel = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = FluentDesign.Secondary, Visibility = Visibility.Collapsed };
    readonly DispatcherTimer modelActivityTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    string goalSettingsSnapshot = "", visibleGoal = "";
    int? goalChatSnapshot;
    void InitializeModelActivity()
    {
        modelActivityTimer.Tick += (_, _) => RefreshModelActivity(); modelActivityTimer.Start();
        Closed += (_, _) => modelActivityTimer.Stop();
    }
    void ModelProgress(ConversationRun run, GenerationUpdate update)
    {
        if (update.Text != run.ProgressText || update.Reasoning != run.ProgressReasoning || update.OutputTokens != run.ProgressTokens)
        {
            run.LastModelProgress = DateTimeOffset.UtcNow;
            run.ProgressText = update.Text; run.ProgressReasoning = update.Reasoning; run.ProgressTokens = update.OutputTokens;
        }
        RefreshModelActivity();
    }
    void RefreshModelActivity()
    {
        var run = selectedSubagent == null ? ActiveRun : null;
        if (run?.WaitingForModel == true)
        {
            var idle = ModelActivity.IdleSeconds(run.LastModelProgress, DateTimeOffset.UtcNow);
            var line = ModelActivity.ReasoningLine(run.ProgressReasoning);
            var activity = WorkflowText("🧠 Le modèle réfléchit", "🧠 Model thinking") +
                (idle > 30 ? " · " + WorkflowText($"Pas de réponse depuis {idle} secondes", $"No response for {idle} seconds") : line.Length > 0 ? " · " + line : "…");
            if (idle > 30 && run.StatusMode == StatusKind.Activity && !IsTransientOverlayVisible)
                status.Text = activity;
        }
        if (goalSettingsSnapshot != state.FeaturesJson || goalChatSnapshot != chat?.Id)
        {
            goalSettingsSnapshot = state.FeaturesJson; goalChatSnapshot = chat?.Id;
            var config = FeatureSettings.Read(state.FeaturesJson);
            visibleGoal = chat != null ? config.ChatGoals.GetValueOrDefault(chat.Id, "") : "";
        }
        var goal = visibleGoal;
        goalLabel.Text = WorkflowText("Objectif : ", "Goal: ") + goal;
        goalLabel.Visibility = goal.Length > 0 && selectedSubagent == null ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(goalLabel, goal);
    }
}
