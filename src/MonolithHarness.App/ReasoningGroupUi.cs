using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    sealed class ReasoningGroupUi
    {
        public required StackPanel Host { get; init; }
        public required Border Card { get; init; }
        public required StackPanel Details { get; init; }
        public required TextBlock Summary { get; init; }
        public bool Expanded { get; private set; }
        bool preference;
        string latest = "";
        public void Refresh(bool value)
        {
            if (preference == value) return;
            preference = value; SetExpanded(value);
        }
        public void SetExpanded(bool value)
        {
            Expanded = value; Details.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Summary.Text = (value ? "▼ " : "▶ ") + latest;
        }
        public void Update(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            latest = ModelActivity.ReasoningLine(text);
            if (latest.Length == 0) latest = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (latest.Length > 240) latest = latest[..240] + "…";
            Card.Visibility = Visibility.Visible; SetExpanded(Expanded);
        }
    }
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<StackPanel, ReasoningGroupUi> activityGroups = new();
    readonly List<WeakReference<ReasoningGroupUi>> activityGroupViews = [];
    ReasoningGroupUi ActivityGroup(StackPanel host)
    {
        host = MessageHost(host);
        if (activityGroups.TryGetValue(host, out var existing) && host.Children.Contains(existing.Card)) return existing;
        activityGroups.Remove(host);
        var details = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        var summary = Label("", 13); summary.TextTrimming = TextTrimming.CharacterEllipsis; summary.MaxLines = 1;
        var toggle = new Button { Content = summary, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0) };
        var content = new StackPanel { Spacing = 8 }; content.Children.Add(toggle); content.Children.Add(details);
        var card = FluentDesign.MessageSurface(content, "assistant"); card.Visibility = Visibility.Collapsed;
        var group = new ReasoningGroupUi { Host = host, Card = card, Details = details, Summary = summary };
        group.Refresh(state.ShowReasoningDetails);
        toggle.Click += (_, _) => group.SetExpanded(!group.Expanded);
        ToolTipService.SetToolTip(toggle, WorkflowText("Étapes de réflexion et outils", "Reasoning steps and tools"));
        host.Children.Add(card); activityGroups.Add(host, group);
        activityGroupViews.RemoveAll(x => !x.TryGetTarget(out _)); activityGroupViews.Add(new(group));
        return group;
    }
    static void MoveToActivity(AssistantMessageUi ui)
    {
        if (ui.Group is not { } group || ui.Container is not { } card || group.Details.Children.Contains(card)) return;
        group.Host.Children.Remove(card); group.Details.Children.Add(card);
        group.Update(ui.CurrentText);
    }
    static bool IntermediateMessage(List<Message> history, int index)
    {
        try { if (System.Text.Json.Nodes.JsonNode.Parse(history[index].WireJson)?["tool_calls"] is System.Text.Json.Nodes.JsonArray { Count: > 0 }) return true; } catch { }
        foreach (var next in history.Skip(index + 1))
        {
            if (next.Role == "user") break;
            if (next.Role == "assistant") return true;
        }
        return false;
    }
}
