using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using System.Text.Json.Nodes;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly Dictionary<string,SubagentRecord> subagentViews=[];
    string? selectedSubagent;
    StackPanel? childPanel;
    readonly Border parentConversationBar = new() { Visibility = Visibility.Collapsed };
    readonly TextBlock parentConversationChildName = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxLines = 2, Foreground = FluentDesign.Primary };
    Button? parentConversationButton;
    FrameworkElement BuildParentConversationBar()
    {
        parentConversationButton = Action(WorkflowText("← Conversation parente", "← Parent conversation"), async () =>
        {
            selectedSubagent = null; childPanel = null; parentConversationBar.Visibility = Visibility.Collapsed;
            await SelectChat();
        });
        parentConversationButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        parentConversationButton.Content = new TextBlock { Text = WorkflowText("← Conversation parente", "← Parent conversation"), TextWrapping = TextWrapping.Wrap };
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.Children.Add(parentConversationButton); Grid.SetColumn(parentConversationChildName, 1); header.Children.Add(parentConversationChildName);
        parentConversationChildName.VerticalAlignment = VerticalAlignment.Center;
        parentConversationBar.Child = header; parentConversationBar.Background = FluentDesign.Card;
        parentConversationBar.BorderBrush = FluentDesign.Stroke; parentConversationBar.BorderThickness = new(0, 0, 0, 1);
        parentConversationBar.Padding = new(12, 8, 12, 8);
        return parentConversationBar;
    }
    void UpdateSubagent(ConversationRun run, SubagentRecord child)
    {
        subagentViews[child.Id]=child;
        if(!run.Messages.Children.OfType<Button>().Any(x=>Equals(x.Tag,child.Id)))run.Messages.Children.Add(ChildBubble(child));
        foreach(var button in run.Messages.Children.OfType<Button>().Where(x=>Equals(x.Tag,child.Id)))button.Content=ChildCard(child, false);
        RefreshSubagentSidebar();
        if(selectedSubagent==child.Id)RenderSubagent(child);
        else if(IsVisible(run))ScrollToBottom();
    }
    string ChildStatus(SubagentRecord child) => child.Status switch
    {
        "running" => WorkflowText("En cours", "Running"), "completed" => WorkflowText("Terminé", "Completed"),
        "failed" => WorkflowText("Échec", "Failed"), "limited" => WorkflowText("Limite atteinte", "Limit reached"),
        "cancelled" => WorkflowText("Annulé", "Cancelled"), _ => WorkflowText("Interrompu", "Interrupted")
    };
    string ChildActivity(SubagentRecord child)
    {
        var activity = System.Text.RegularExpressions.Regex.Replace(child.Activity, @"^Étape \d+/\d+ · ", "");
        foreach (var pair in new[] { ("Réflexion / Thinking", "Réflexion", "Thinking"), ("Démarrage / Starting", "Démarrage", "Starting"), ("Réponse reçue / Response received", "Réponse reçue", "Response received"), ("Outil / Tool", "Outil", "Tool"),
            ("Outil terminé / Tool completed", "Outil terminé", "Tool completed"), ("Compactage du contexte / Compacting context", "Compactage du contexte", "Compacting context"), ("Plan des tâches mis à jour / Task plan updated", "Plan des tâches mis à jour", "Task plan updated") })
            activity = activity.Replace(pair.Item1, WorkflowText(pair.Item2, pair.Item3));
        return child.Status == "running" ? activity : ChildStatus(child);
    }
    string ChildProgressLabel(SubagentRecord child)
    {
        var progress = child.Progress;
        if (!progress.HasPlan) return child.Status == "running" ? WorkflowText("Plan en préparation…", "Preparing task plan…") : WorkflowText("Plan non communiqué", "Task plan not provided");
        return $"{progress.Completed}/{progress.Total} " + WorkflowText("tâches terminées", "tasks completed") + $" · {progress.Percentage:F0} %" +
            (progress.Cancelled > 0 ? $" · {progress.Cancelled} " + WorkflowText("annulée(s)", "cancelled") : "");
    }
    FrameworkElement ChildProgressBar(SubagentRecord child, bool compact)
    {
        var progress = child.Progress;
        var panel = new StackPanel { Spacing = 4 };
        var label = new TextBlock { Text = ChildProgressLabel(child), FontSize = compact ? 10 : 12, Foreground = FluentDesign.Secondary, TextWrapping = TextWrapping.Wrap };
        var bar = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, progress.Total), Value = progress.Completed,
            IsIndeterminate = !progress.HasPlan && child.Status == "running", Height = compact ? 3 : 5 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bar, label.Text);
        ToolTipService.SetToolTip(bar, label.Text); panel.Children.Add(label); panel.Children.Add(bar);
        return panel;
    }
    FrameworkElement ChildCard(SubagentRecord child, bool compact)
    {
        var content = new Grid { ColumnSpacing = 8, RowSpacing = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var color = child.Status == "failed" ? Brush(255, 145, 145) : child.Status == "completed" ? Brush(110, 220, 150) : Brush(130, 180, 255);
        var icon = new FontIcon { Glyph = child.Status == "completed" ? "\uE73E" : child.Status == "failed" ? "\uE783" : "\uE8D7", FontSize = 13, Foreground = color, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(icon);
        var name = new TextBlock { Text = child.Name, FontSize = compact ? 12 : 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = FluentDesign.Primary };
        Grid.SetColumn(name, 1); content.Children.Add(name);
        var progress = child.Progress;
        var badge = new Border { Background = FluentDesign.Card, CornerRadius = new(4), Padding = new(5, 2, 5, 2), Child = new TextBlock { Text = compact ? (progress.HasPlan ? $"{progress.Completed}/{progress.Total}" : "…") : ChildStatus(child), FontSize = 10, Foreground = color } };
        Grid.SetColumn(badge, 2); content.Children.Add(badge);
        var activity = new TextBlock { Text = ChildActivity(child) + (progress.CurrentTask.Length > 0 ? " · " + progress.CurrentTask : ""), FontSize = 11, Foreground = FluentDesign.Secondary, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(activity, 1); Grid.SetColumn(activity, 1); Grid.SetColumnSpan(activity, 2); content.Children.Add(activity);
        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var progressBar = ChildProgressBar(child, compact); Grid.SetRow(progressBar, 2); Grid.SetColumn(progressBar, 1); Grid.SetColumnSpan(progressBar, 2); content.Children.Add(progressBar);
        if (!compact)
        {
            content.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var task = new TextBlock { Text = child.Task, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, Foreground = FluentDesign.Secondary, Margin = new(0, 4, 0, 0) };
            Grid.SetRow(task, 3); Grid.SetColumn(task, 1); Grid.SetColumnSpan(task, 2); content.Children.Add(task);
        }
        ToolTipService.SetToolTip(content, child.Name + "\n" + ChildProgressLabel(child) + "\n" + activity.Text + "\n" + child.Task);
        return content;
    }
    Button ChildBubble(SubagentRecord child)
    {
        var button=new Button {Tag=child.Id,Content=ChildCard(child, false),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(14),CornerRadius=new(10),BorderBrush=FluentDesign.Stroke,Background=FluentDesign.Card};
        button.Click+=(_,e)=> {OpenSubagent(child.Id);};return button;
    }
    void OpenSubagent(string id)
    {
        if(!subagentViews.TryGetValue(id,out var child))return;
        if (conversationLoading)
        {
            conversationLoad?.Cancel(); conversationLoad = null;
            ++conversationLoadRevision; conversationLoading = false; conversationReady = false;
        }
        selectedSubagent=id;childPanel=CreateMessagePanel();scroll.Content=childPanel;followChatTail=true;
        parentConversationBar.Visibility = Visibility.Visible;
        pinnedTasks.Visibility=Visibility.Collapsed;
        RenderSubagent(child);RefreshGenerationControls();
    }
    void RenderSubagent(SubagentRecord child)
    {
        if(childPanel==null)return;
        childPanel.Children.Clear();
        parentConversationChildName.Text = child.Name + " · " + ChildStatus(child);
        if (parentConversationButton?.Content is TextBlock back) back.Text = WorkflowText("← Conversation parente", "← Parent conversation");
        childPanel.Children.Add(Label(child.Name+" · "+ChildStatus(child),22));childPanel.Children.Add(Label(ChildActivity(child),13));
        childPanel.Children.Add(ChildProgressBar(child, false));
        var tasks = SubagentTasks.Read(child.TranscriptJson);
        if (tasks.Count > 0)
        {
            var plan = new StackPanel { Spacing = 8 };
            plan.Children.Add(Label(WorkflowText("Plan de cet agent", "This agent's plan"), 17));
            foreach (var task in tasks)
            {
                var marker = task.Status switch { "completed" => "✓", "in_progress" => "◉", "cancelled" => "—", _ => "○" };
                var status = task.Status switch { "completed" => WorkflowText("Terminée", "Completed"), "in_progress" => WorkflowText("En cours", "In progress"), "cancelled" => WorkflowText("Annulée", "Cancelled"), _ => WorkflowText("À faire", "Pending") };
                plan.Children.Add(Label(marker + " " + task.Content + " · " + status, 13));
            }
            childPanel.Children.Add(FluentDesign.Surface(plan, 12));
        }
        var content=new StackPanel {Spacing=12};childPanel.Children.Add(content);
        var transcript="## " + WorkflowText("Tâche", "Task") + "\n"+child.Task+"\n\n";
        foreach(var message in JsonNode.Parse(child.TranscriptJson)?.AsArray() ?? [])
        {
            if (message?["role"]?.GetValue<string>() == "tasks") continue;
            transcript+="\n### "+(message?["role"]?.GetValue<string>()??"")+"\n"+(message?["content"]?.GetValue<string>()??"")+"\n";
            if(message?["tool_calls"]!=null)transcript+="\n```json\n"+message["tool_calls"]!.ToJsonString()+"\n```\n";
        }
        var messageProject = chat == null || project == null ? project : ProjectResources.Effective(chat, project);
        MarkdownRenderer.RenderTo(content,transcript,path=>OpenChatFileAsync(path,messageProject),path=>CreateChatFileMenu(path,messageProject));
        ScrollToBottom();
    }
    async Task LoadSubagents(int chatId)
    {
        var revision = conversationLoadRevision;
        var children=await ReadStoreAsync(context => context.Subagents.AsNoTracking().Where(x=>x.ChatId==chatId).OrderBy(x=>x.CreatedUtc).ToList());
        if(chat?.Id!=chatId || revision != conversationLoadRevision)return;
        foreach(var child in children)
        {
            if(child.Status=="running" && !conversationRuns.ContainsKey(chatId))child.Status="interrupted";
            subagentViews[child.Id]=child;
            if(!messages.Children.OfType<Button>().Any(x=>Equals(x.Tag,child.Id)))messages.Children.Add(ChildBubble(child));
        }
        RefreshSubagentSidebar();
    }
    void RefreshSubagentSidebar()
    {
        static StackPanel? Find(DependencyObject node)
        {
            if(node is StackPanel p && Equals(p.Tag,"subagents"))return p;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)if(Find(VisualTreeHelper.GetChild(node,i)) is {} child)return child;
            return null;
        }
        foreach(var list in new[]{chats,archivedChats})
        foreach(var row in list.Items.OfType<Chat>())
        {
            if(list.ContainerFromItem(row) is not ListViewItem container || Find(container) is not {} panel)continue;
            panel.Children.Clear();
            foreach(var child in subagentViews.Values.Where(x=>x.ChatId==row.Id && x.Status=="running"))
            {
                var button=new Button {Content=ChildCard(child, true),Margin=new Thickness(0),FontSize=11,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(8),CornerRadius=new(6),BorderThickness=new(0),Background=selectedSubagent==child.Id ? FluentDesign.Card : new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, child.Name + ", " + ChildActivity(child));
                button.Click+=async(_,e)=>{if(chat?.Id!=row.Id){var previousLoading=loading;loading=true;list.SelectedItem=row;loading=previousLoading;await SelectChat();}OpenSubagent(child.Id);};panel.Children.Add(button);
            }
            if (panel.Parent is Border group) group.Visibility = panel.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
