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
        public required ScrollViewer Scroller { get; init; }
        public required TextBlock Summary { get; init; }
        public bool Expanded { get; private set; }
        public Button? Toggle { get; init; }
        ReasoningDetailLevel? preference;
        string latest = "";
        public void Refresh(bool value) => Refresh(value ? ReasoningDetailLevel.Detailed : ReasoningDetailLevel.Reduced);
        public void Refresh(ReasoningDetailLevel value)
        {
            if (preference == value) return;
            preference = value;
            if (Toggle != null) Toggle.IsEnabled = value != ReasoningDetailLevel.None;
            SetExpanded(value == ReasoningDetailLevel.Detailed);
        }
        public void SetExpanded(bool value)
        {
            value &= preference != ReasoningDetailLevel.None;
            Expanded = value; Details.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Scroller.Visibility = Details.Visibility;
            Summary.Text = preference == ReasoningDetailLevel.None ? UiText.Resolve("Raisonnement…", "Reasoning…") : (value ? "▼ " : "▶ ") + latest;
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
    ReasoningDetailLevel? reasoningDisplayOverride;
    ReasoningDetailLevel CurrentReasoningDisplay => reasoningDisplayOverride ?? (state.ShowReasoningDetails ? ReasoningDetailLevel.Detailed : ReasoningDetailLevel.Reduced);
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<StackPanel, ReasoningGroupUi> activityGroups = new();
    readonly List<WeakReference<ReasoningGroupUi>> activityGroupViews = [];
    bool reasoningResizeObserved;
    ReasoningGroupUi ActivityGroup(StackPanel host)
    {
        host = MessageHost(host);
        if (activityGroups.TryGetValue(host, out var existing) && host.Children.Contains(existing.Card)) return existing;
        activityGroups.Remove(host);
        var details = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        var summary = Label("", 13); summary.TextTrimming = TextTrimming.CharacterEllipsis; summary.MaxLines = 1;
        var toggle = new Button { Content = summary, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0) };
        var scroller = new ScrollViewer { Content = details, MaxHeight = 360, Visibility = Visibility.Collapsed,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled };
        void ResizeDetails() => scroller.MaxHeight = Math.Clamp(root.ActualHeight * .4, 140, 360);
        scroller.Loaded += (_, _) => ResizeDetails();
        if (!reasoningResizeObserved)
        {
            reasoningResizeObserved = true;
            root.SizeChanged += (_, _) =>
            {
                activityGroupViews.RemoveAll(reference => !reference.TryGetTarget(out _));
                foreach (var reference in activityGroupViews)
                    if (reference.TryGetTarget(out var view)) view.Scroller.MaxHeight = Math.Clamp(root.ActualHeight * .4, 140, 360);
            };
        }
        var content = new StackPanel { Spacing = 8 }; content.Children.Add(toggle); content.Children.Add(scroller);
        var card = FluentDesign.MessageSurface(content, "assistant"); card.Visibility = Visibility.Collapsed;
        var group = new ReasoningGroupUi { Host = host, Card = card, Details = details, Scroller = scroller, Summary = summary, Toggle = toggle };
        _ = new ContentTailFollower(scroller);
        group.Refresh(CurrentReasoningDisplay);
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
    static bool ContainsActionSuggestion(System.Text.Json.Nodes.JsonArray calls) => calls.Any(call =>
        string.Equals(call?["function"]?["name"]?.ToString(), ActionSuggestions.Tool, StringComparison.Ordinal));

    static bool IntermediateMessage(List<Message> history, int index)
    {
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(history[index].WireJson)?["tool_calls"] is System.Text.Json.Nodes.JsonArray { Count: > 0 } calls)
                return !ContainsActionSuggestion(calls);
        }
        catch { }
        foreach (var next in history.Skip(index + 1))
        {
            if (next.Role == "user") break;
            // A persisted proposal can also follow text from providers that omit tool metadata.
            if (ActionSuggestions.Read(next) != null) return false;
            if (next.Role == "assistant") return true;
        }
        return false;
    }
}
