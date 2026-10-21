using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly Border globalTokenSpeed = new() { Visibility = Visibility.Collapsed, Padding = new(8, 8, 8, 8), BorderThickness = new(0, 0, 0, 1) };
    readonly TextBlock globalTokenSpeedValue = Label("0,0 tok/s", 12);
    readonly TextBlock globalTokenSpeedChats = Label("· 0 chats", 11);
    bool globalTokenSpeedEnabled;

    UIElement BuildGlobalTokenSpeed()
    {
        var panel = new Grid { ColumnSpacing = 8, MinHeight = 24 };
        panel.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var icon = FluentDesign.Icon("\uE945", 16); icon.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(icon);
        var caption = Label(WorkflowText("Débit global", "Token speed"), 12);
        caption.TextWrapping = TextWrapping.NoWrap; caption.TextTrimming = TextTrimming.CharacterEllipsis;
        caption.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(caption, 1); panel.Children.Add(caption);
        globalTokenSpeedValue.VerticalAlignment = globalTokenSpeedChats.VerticalAlignment = VerticalAlignment.Center;
        globalTokenSpeedValue.TextWrapping = globalTokenSpeedChats.TextWrapping = TextWrapping.NoWrap;
        globalTokenSpeedChats.Foreground = FluentDesign.Secondary;
        var values = Row(globalTokenSpeedValue, globalTokenSpeedChats); values.Spacing = 4;
        values.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(values, 2); panel.Children.Add(values);
        globalTokenSpeed.Background = FluentDesign.Resource("TransparentBrush");
        globalTokenSpeed.BorderBrush = FluentDesign.Stroke;
        globalTokenSpeed.Child = panel;
        ToolTipService.SetToolTip(globalTokenSpeed, WorkflowText("Somme des débits des modèles des conversations en cours, sur les deux dernières secondes. Les valeurs sans compteur fournisseur sont estimées.", "Combined speed of models in active conversations over the last two seconds. Values without provider token counters are estimated."));
        return globalTokenSpeed;
    }

    void ConfigureGlobalTokenSpeed(bool enabled)
    {
        globalTokenSpeedEnabled = enabled;
        SetVisualValue(globalTokenSpeed, UIElement.VisibilityProperty, enabled ? Visibility.Visible : Visibility.Collapsed);
        RefreshGlobalTokenSpeed();
        RefreshSpeedTimer();
    }

    void RefreshGlobalTokenSpeed()
    {
        RefreshSpeedTimer();
        if (!globalTokenSpeedEnabled || !VisualUpdatesAllowed) return;
        var live = TokenConsumption.LiveRates(db.FilePath);
        var known = live.Select(value => value.ChatId).ToHashSet();
        var fallback = conversationRuns.Values.Where(run => run.WaitingForModel && run.Tracker != null && !run.Cancellation.IsCancellationRequested && !known.Contains(run.Chat.Id)).ToArray();
        var rate = live.Sum(value => value.TokensPerSecond) + fallback.Sum(run => run.RecentSpeed.Read()?.Rate ?? 0);
        var count = known.Union(fallback.Select(run => run.Chat.Id)).Count();
        SetTextIfChanged(globalTokenSpeedValue, $"{rate:F1} tok/s");
        SetTextIfChanged(globalTokenSpeedChats, WorkflowText($"· {count} chats", $"· {count} chats"));
        SetVisualValue(globalTokenSpeed, Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, WorkflowText($"Débit global : {rate:F1} tokens par seconde, {count} conversations", $"Combined speed: {rate:F1} tokens per second, {count} conversations"));
    }
}
