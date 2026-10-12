using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly Border globalTokenSpeed = new() { Visibility = Visibility.Collapsed, CornerRadius = new(8), Padding = new(10, 7, 10, 7) };
    readonly TextBlock globalTokenSpeedValue = Label("0,0 tok/s", 14);
    readonly TextBlock globalTokenSpeedChats = Label("", 11);
    bool globalTokenSpeedEnabled;

    UIElement BuildGlobalTokenSpeed()
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(Label(WorkflowText("Débit global", "Combined token speed"), 11));
        panel.Children.Add(Row(FluentDesign.Icon("\uE945", 14), globalTokenSpeedValue, globalTokenSpeedChats));
        globalTokenSpeed.Child = panel;
        ToolTipService.SetToolTip(globalTokenSpeed, WorkflowText("Somme des débits des modèles des conversations en cours, sur les deux dernières secondes. Les valeurs sans compteur fournisseur sont estimées.", "Combined speed of models in active conversations over the last two seconds. Values without provider token counters are estimated."));
        return globalTokenSpeed;
    }

    void ConfigureGlobalTokenSpeed(bool enabled)
    {
        globalTokenSpeedEnabled = enabled;
        globalTokenSpeed.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        globalTokenSpeed.Background = FluentDesign.Card;
        RefreshGlobalTokenSpeed();
        if (enabled) { EnsureComposerSpeedTimer(); composerSpeedTimer.Start(); }
    }

    void RefreshGlobalTokenSpeed()
    {
        if (!globalTokenSpeedEnabled) return;
        var live = TokenConsumption.LiveRates(db.FilePath);
        var known = live.Select(value => value.ChatId).ToHashSet();
        var fallback = conversationRuns.Values.Where(run => run.Tracker != null && !run.Cancellation.IsCancellationRequested && !known.Contains(run.Chat.Id)).ToArray();
        var rate = live.Sum(value => value.TokensPerSecond) + fallback.Sum(run => run.RecentSpeed.Read()?.Rate ?? 0);
        var count = known.Union(fallback.Select(run => run.Chat.Id)).Count();
        globalTokenSpeedValue.Text = $"{rate:F1} tok/s";
        globalTokenSpeedChats.Text = WorkflowText($"· {count} chats", $"· {count} chats");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(globalTokenSpeed, WorkflowText($"Débit global : {rate:F1} tokens par seconde, {count} conversations", $"Combined speed: {rate:F1} tokens per second, {count} conversations"));
    }
}
