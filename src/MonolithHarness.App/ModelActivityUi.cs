using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

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
    void ContextRequestProgressed(ConversationRun run, ContextRequestProgress progress)
    {
        if (progress.Stage == ContextRequestStage.Preparing) run.ContextRequestTimer.Restart();
        run.ContextRequest = progress;
        SetRunStatus(run, T("Le modèle réfléchit…"));
    }
    void ModelProgress(ConversationRun run, GenerationUpdate update)
    {
        if (update.HasModelOutput || update.Text.Length > 0 || update.Reasoning.Length > 0 || update.OutputTokens > 0)
        { run.ContextRequest = null; run.ContextRequestTimer.Stop(); }
        if (update.HasModelOutput || update.Text != run.ProgressText || update.Reasoning != run.ProgressReasoning || update.OutputTokens != run.ProgressTokens)
        {
            run.LastModelProgress = DateTimeOffset.UtcNow;
            run.ProgressText = update.Text; run.ProgressReasoning = update.Reasoning; run.ProgressTokens = update.OutputTokens;
        }
        RefreshModelActivity();
    }
    void RefreshModelActivity()
    {
        foreach (var conversation in conversationRuns.Values)
            if (conversation.CurrentTool is { } running)
                running.Message.UpdateProgress(running.Timer.Elapsed.TotalSeconds, running.Phase);
        var run = selectedSubagent == null ? ActiveRun : null;
        if (run?.CurrentTool is { } tool && run.StatusMode == StatusKind.Activity && !IsTransientOverlayVisible)
            status.Text = ToolActivityText(tool);
        string? details = null;
        if (run?.WaitingForModel == true && run.StatusMode == StatusKind.Activity && !IsTransientOverlayVisible)
        {
            if (run.ContextRequest is { } preload)
            {
                var elapsed = run.ContextRequestTimer.Elapsed.TotalSeconds;
                status.Text = preload.Caption(state.Language, elapsed);
                details = preload.Details(state.Language, run.Provider.ContextLimit, elapsed);
            }
            else
            {
                var idle = ModelActivity.IdleSeconds(run.LastModelProgress, DateTimeOffset.UtcNow);
                var responding = run.ProgressText.Length > 0;
                var activity = responding ? WorkflowText("Réponse en cours", "Response in progress") : WorkflowText("🧠 Le modèle réfléchit", "🧠 Model thinking");
                status.Text = responding
                    ? activity + (idle > 30 ? " · " + WorkflowText($"Pas de réponse depuis {idle} secondes", $"No response for {idle} seconds") : "…")
                    : activity;
            }
        }
        ToolTipService.SetToolTip(statusChipView ?? (FrameworkElement)status, details);
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
