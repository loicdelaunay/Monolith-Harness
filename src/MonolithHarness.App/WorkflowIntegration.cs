using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using MonolithHarness.Core;
using System.Text.Json.Nodes;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly Border pinnedTasks = new() { Visibility = Visibility.Collapsed };
    readonly Dictionary<int, bool> taskExpansion = new();
    async Task RefreshPinnedTasksAsync()
    {
        var id = chat?.Id;
        var revision = conversationLoadRevision;
        var json = id == null ? null : await ReadStoreAsync(store => store.Messages.Where(x => x.ChatId == id && x.Role == "tasks").Select(x => x.Content).FirstOrDefault());
        if (chat?.Id == id && revision == conversationLoadRevision) ShowPinnedTasks(json);
    }
    void ShowPinnedTasks(string? json)
    {
        var items = string.IsNullOrWhiteSpace(json) ? new JsonArray() : JsonNode.Parse(json) as JsonArray ?? [];
        if (ChatInteraction || selectedSubagent != null || chat?.TodoDismissed == true || !items.Any(x => x?["status"]?.GetValue<string>() is "pending" or "in_progress"))
        { pinnedTasks.Child = null; pinnedTasks.Visibility = Visibility.Collapsed; return; }
        var panel = new StackPanel { Spacing = 4 };
        foreach (var item in items)
        {
            var status = item?["status"]?.GetValue<string>();
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var check = new CheckBox { IsChecked = status == "completed", IsHitTestVisible = false, IsTabStop = false, MinWidth = 22, MinHeight = 24 };
            row.Children.Add(check);
            var label = new TextBlock { Text = (status == "in_progress" ? "◉ " : status == "cancelled" ? "— " : "") + item?["content"]?.GetValue<string>(),
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = status == "in_progress" ? FluentDesign.Primary : FluentDesign.Secondary, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); row.Children.Add(label);
            panel.Children.Add(row);
        }
        var owner = chat!;
        var expanded = taskExpansion.GetValueOrDefault(owner.Id, true);
        var current = items.FirstOrDefault(x => x?["status"]?.GetValue<string>() == "in_progress") ?? items.First(x => x?["status"]?.GetValue<string>() == "pending");
        var index = items.IndexOf(current) + 1;
        var header = new Grid(); header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var toggle = Action((expanded ? "⌄  " : "›  ") + WorkflowText("Étape", "Step") + $" {index}/{items.Count} · " + current?["content"]?.GetValue<string>(), () => { taskExpansion[owner.Id] = !expanded; ShowPinnedTasks(json); return Task.CompletedTask; });
        toggle.HorizontalAlignment = HorizontalAlignment.Stretch; toggle.HorizontalContentAlignment = HorizontalAlignment.Left;
        header.Children.Add(toggle);
        var close = Action("×", async () => { owner.TodoDismissed = true; await db.SaveChangesAsync(); if (chat?.Id == owner.Id) ShowPinnedTasks(json); });
        ToolTipService.SetToolTip(close, WorkflowText("Masquer la liste · réouvrir depuis +", "Hide list · reopen from +")); Grid.SetColumn(close, 1); header.Children.Add(close);
        var content = new StackPanel { Spacing = 6 }; content.Children.Add(header);
        content.Children.Add(new ScrollViewer { Content = panel, MaxHeight = 140, Visibility = expanded ? Visibility.Visible : Visibility.Collapsed });
        pinnedTasks.Child = FluentDesign.Surface(content, 10);
        pinnedTasks.Visibility = Visibility.Visible;
    }
    string WorkflowText(string fr, string en) => UiText.Resolve(fr, en, state.Language);
    WorkflowTools CreateWorkflow(ConversationRun run) => new(
        (questions, ct) => AskWorkflowQuestionsAsync(run, questions, ct),
        async (items, ct) =>
        {
            await using var store = new HarnessDb();
            var message = await store.Messages.FirstOrDefaultAsync(x => x.ChatId == run.Chat.Id && x.Role == "tasks", ct);
            if (message == null) { message = new() { ChatId = run.Chat.Id, Role = "tasks", State = "ui" }; store.Messages.Add(message); }
            message.Content = items.ToJsonString(); await store.SaveChangesAsync(ct);
            RenderTasks(message.Content, run.Messages, run.Chat.Id);
        }, (proposal, ct) => SaveActionSuggestionAsync(run, proposal, ct));

    void RenderTasks(string json, StackPanel target, int? ownerId = null)
    {
        foreach (var old in target.Children.OfType<FrameworkElement>().Where(x => Equals(x.Tag, "workflow-tasks")).ToList()) target.Children.Remove(old);
        var items = JsonNode.Parse(json) as JsonArray ?? [];
        if ((ownerId ?? chat?.Id) == chat?.Id) ShowPinnedTasks(json);
        if (items.Count == 0) return;
        if (items.Any(x => x?["status"]?.GetValue<string>() is "pending" or "in_progress")) return;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = WorkflowText("Tâches", "Tasks") + $" · {items.Count(x => x?["status"]?.GetValue<string>() == "completed")}/{items.Count}", FontSize = 18 });
        foreach (var item in items)
        {
            var status = item?["status"]?.GetValue<string>();
            var label = status switch { "completed" => WorkflowText("✓ Terminée", "✓ Completed"), "in_progress" => WorkflowText("◉ En cours", "◉ In progress"), "cancelled" => WorkflowText("— Annulée", "— Cancelled"), _ => WorkflowText("○ À faire", "○ To do") };
            panel.Children.Add(new TextBlock { Text = label + " · " + item?["content"]?.GetValue<string>(), TextWrapping = TextWrapping.Wrap });
        }
        var card = new Expander { Header = WorkflowText("Étapes du travail", "Work steps"), Content = panel, IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch, Tag = "workflow-tasks" };
        target.Children.Insert(0, card);
    }
}
