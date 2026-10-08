using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    List<Chat> navigationChats = [];
    readonly HashSet<int> expandedSidebarProjects = [];
    readonly HashSet<int> expandedSidebarArchives = [];
    readonly Dictionary<int, StackPanel> projectBadgeHosts = [];
    readonly Dictionary<int, int> activityProjects = [];
    readonly Dictionary<int, int> pendingChatAttention = [];
    readonly HashSet<int> completedUnreadChats = [];
    readonly HashSet<int> failedUnreadChats = [];
    sealed record SidebarRow(Chat Chat, Button Button, TextBlock Title, TextBlock Age, TextBlock Status, StackPanel Naming, BusySpinner Spinner, ProgressBar Progress);
    readonly List<SidebarRow> independentChatRows = [];
    readonly StackPanel attentionNavigation = new() { Spacing = 4, Visibility = Visibility.Collapsed };
    readonly List<SidebarRow> attentionSidebarRows = [];
    string attentionSidebarSignature = "";

    void RefreshAttentionSection()
    {
        var settings = FeatureSettings.Read(state.FeaturesJson);
        var ids = pendingChatAttention.Keys.Concat(failedUnreadChats).ToHashSet();
        var items = settings.ShowAttentionSection ? navigationChats.Select(CurrentSidebarChat).Where(x => ids.Contains(x.Id) && MatchesSidebarSearch(x)).OrderByDescending(x => x.UpdatedUtc).ThenByDescending(x => x.Id).ToList() : [];
        var signature = settings.ShowAttentionSection + "|" + state.Language + "|" + string.Join(",", items.Select(x => x.Id));
        if (attentionSidebarSignature == signature) return;
        attentionSidebarSignature = signature;
        foreach (var row in attentionSidebarRows) independentChatRows.Remove(row);
        attentionSidebarRows.Clear(); attentionNavigation.Children.Clear();
        attentionNavigation.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (items.Count == 0) return;
        var heading = Label(WorkflowText($"Attention requise ({items.Count})", $"Needs attention ({items.Count})"), 12);
        heading.Foreground = FluentDesign.Secondary; heading.Margin = new(8, 4, 0, 6);
        attentionNavigation.Children.Add(heading);
        foreach (var item in items)
        {
            var button = BuildIndependentChatRow(item, true);
            attentionSidebarRows.Add(independentChatRows[^1]);
            attentionNavigation.Children.Add(button);
        }
        attentionNavigation.Margin = new(0, 0, 0, 12);
    }

    void PruneSidebarActivity()
    {
        var present = navigationChats.Select(x => x.Id).Concat(conversationRuns.Keys).ToHashSet();
        foreach (var id in activityProjects.Keys.Where(id => !present.Contains(id)).ToArray())
        {
            activityProjects.Remove(id); pendingChatAttention.Remove(id); completedUnreadChats.Remove(id); failedUnreadChats.Remove(id);
        }
        chatNotices.RemoveAll(x => !present.Contains(x.ChatId)); RefreshNotificationBell();
    }

    Chat CurrentSidebarChat(Chat row) => db.Chats.Local.FirstOrDefault(x => x.Id == row.Id) ?? row;
    bool MatchesSidebarSearch(Chat row) => chatSearch.Text.Trim() is not { Length: > 0 } query ||
        System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(row.Title, query,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) >= 0;

    async Task TogglePinnedChatAsync(Chat target)
    {
        bool pinned = !CurrentSidebarChat(target).IsPinned;
        await using var store = new HarnessDb();
        if (await store.Chats.Where(x => x.Id == target.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsPinned, pinned)) == 0) return;
        target.IsPinned = pinned;
        foreach (var item in navigationChats.Where(x => x.Id == target.Id)) item.IsPinned = pinned;
        if (db.Chats.Local.FirstOrDefault(x => x.Id == target.Id) is { } tracked)
        {
            tracked.IsPinned = pinned; db.Entry(tracked).Property(x => x.IsPinned).OriginalValue = pinned;
        }
        RebuildProjectNavigation();
    }

    Button BuildIndependentChatRow(Chat item, bool pinned)
    {
        var button = Action("", () => OpenConversationByIdAsync(item.Id));
        button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.Padding = new(8, 4, 8, 4); button.BorderThickness = new(0); button.CornerRadius = new(5);
        var body = new StackPanel { Spacing = 3 };
        var line = new Grid { ColumnSpacing = 6, MinHeight = 22 };
        line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new() { Width = new(20) }); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = new TextBlock { Text = item.Title, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(title);
        var spinner = new BusySpinner();
        var naming = Row(spinner, Label(WorkflowText("Nommage…", "Naming…"), 13)); naming.Visibility = Visibility.Collapsed; line.Children.Add(naming);
        var status = Label("", 12); status.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(status, 1); line.Children.Add(status);
        var age = Label(ConversationAge(item), 11); age.Foreground = FluentDesign.Secondary; age.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(age, 2); line.Children.Add(age);
        var progress = new ProgressBar { Height = 2, IsIndeterminate = false, Opacity = 0 };
        body.Children.Add(line); body.Children.Add(progress); button.Content = body;
        independentChatRows.Add(new(item, button, title, age, status, naming, spinner, progress));
        var projectName = (projects.ItemsSource as IEnumerable<Project>)?.FirstOrDefault(x => x.Id == item.ProjectId)?.Name ?? "";
        ToolTipService.SetToolTip(button, (pinned ? projectName + "\n" : "") + item.Title);
        button.RightTapped += (_, e) =>
        {
            e.Handled = true;
            var menu = new MenuFlyout();
            void Add(string label, Func<Chat, Task> action)
            {
                var entry = new MenuFlyoutItem { Text = label };
                entry.Click += async (_, _) => await Guard(async () => {
                    if (await db.Chats.FindAsync(item.Id) is { } tracked) await action(tracked);
                });
                menu.Items.Add(entry);
            }
            var current = CurrentSidebarChat(item);
            Add(WorkflowText(current.IsPinned ? "Désépingler" : "Épingler", current.IsPinned ? "Unpin" : "Pin"), TogglePinnedChatAsync);
            Add(UiText.T("Renommer"), async target => { await OpenConversationByIdAsync(target.Id); await RenameChatAsync(target); });
            Add(WorkflowText("Nommer avec l’IA", "Name with AI"), target => AutoNameAsync(target.Id));
            Add(WorkflowText(current.IsFavorite ? "Retirer des favoris" : "Ajouter aux favoris", current.IsFavorite ? "Remove favorite" : "Add favorite"), ToggleFavoriteAsync);
            Add(WorkflowText(current.IsArchived ? "Restaurer" : "Archiver", current.IsArchived ? "Restore" : "Archive"), target => ArchiveChatsAsync([target], !target.IsArchived));
            if (!conversationRuns.ContainsKey(item.Id)) Add(WorkflowText("Supprimer…", "Delete…"), DeleteChatAsync);
            menu.ShowAt(button, e.GetPosition(button));
        };
        EnableSidebarDrag(button, false, () => item.Id);
        return button;
    }

    void RefreshIndependentChatRows()
    {
        RefreshAttentionSection();
        foreach (var row in independentChatRows)
        {
            var item = CurrentSidebarChat(row.Chat);
            bool naming = namingChats.Contains(item.Id), running = conversationRuns.ContainsKey(item.Id);
            row.Title.Text = item.Title; row.Title.Visibility = naming ? Visibility.Collapsed : Visibility.Visible;
            row.Naming.Visibility = naming ? Visibility.Visible : Visibility.Collapsed; row.Spinner.IsActive = naming;
            row.Age.Text = ConversationAge(item);
            row.Button.Background = item.Id == chat?.Id ? FluentDesign.Resource("ConversationHoverFillBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            row.Progress.IsIndeterminate = running; row.Progress.Opacity = running ? 1 : 0;
            bool attention = pendingChatAttention.ContainsKey(item.Id) || failedUnreadChats.Contains(item.Id);
            row.Status.Text = attention ? "!" : completedUnreadChats.Contains(item.Id) ? "✓" : running ? "…" : "";
            row.Status.Foreground = FluentDesign.Primary;
            ToolTipService.SetToolTip(row.Status, attention ? WorkflowText("Attention requise", "Needs attention") : running ? WorkflowText("En cours", "Running") : WorkflowText("Réponse terminée non lue", "Unread completed reply"));
        }
        RefreshProjectBadges();
    }

    void MarkProjectChatStarted(ConversationRun run)
    {
        activityProjects[run.Chat.Id] = run.Chat.ProjectId;
        completedUnreadChats.Remove(run.Chat.Id); failedUnreadChats.Remove(run.Chat.Id); pendingChatAttention.Remove(run.Chat.Id);
        chatNotices.RemoveAll(x => x.ChatId == run.Chat.Id); RefreshNotificationBell();
    }
    void MarkProjectChatNotification(ConversationRun run, bool actionRequired)
    {
        activityProjects[run.Chat.Id] = run.Chat.ProjectId;
        if (actionRequired) pendingChatAttention[run.Chat.Id] = pendingChatAttention.GetValueOrDefault(run.Chat.Id) + 1;
        else { pendingChatAttention.Remove(run.Chat.Id); failedUnreadChats.Remove(run.Chat.Id); completedUnreadChats.Add(run.Chat.Id); }
        RefreshIndependentChatRows();
    }
    void ReadProjectChatActivity(int chatId, bool actionResolved)
    {
        completedUnreadChats.Remove(chatId); failedUnreadChats.Remove(chatId);
        if (actionResolved && pendingChatAttention.TryGetValue(chatId, out var count))
        {
            if (count <= 1) pendingChatAttention.Remove(chatId); else pendingChatAttention[chatId] = count - 1;
        }
    }
    void ClearUnreadProjectActivity() { completedUnreadChats.Clear(); failedUnreadChats.Clear(); RefreshProjectBadges(); }
    void MarkProjectChatEnded(ConversationRun run, bool success)
    {
        pendingChatAttention.Remove(run.Chat.Id);
        if (!success && !run.Cancellation.IsCancellationRequested) failedUnreadChats.Add(run.Chat.Id);
    }
    void RefreshProjectBadges()
    {
        var settings = FeatureSettings.Read(state.FeaturesJson);
        var theme = AppearanceThemes.Get(settings.Theme, settings.CustomThemes);
        foreach (var (projectId, host) in projectBadgeHosts)
        {
            int running = conversationRuns.Values.Count(x => x.Chat.ProjectId == projectId && !pendingChatAttention.ContainsKey(x.Chat.Id));
            int done = completedUnreadChats.Count(id => activityProjects.GetValueOrDefault(id) == projectId);
            int attention = pendingChatAttention.Keys.Concat(failedUnreadChats).Distinct().Count(id => activityProjects.GetValueOrDefault(id) == projectId);
            string signature = $"{running}|{done}|{attention}|{theme.Id}|{theme.Surface}|{theme.Accent}|{theme.Dark}|{state.Language}";
            if (Equals(host.Tag, signature)) continue;
            host.Tag = signature; host.Children.Clear();
            void Badge(int count, string glyph, string caption, string color)
            {
                if (count == 0) return;
                var background = ThemeContrast.Blend(theme.Surface, color, theme.Dark ? .23 : .13);
                var content = new TextBlock { Text = glyph + " " + count, FontSize = 10, Foreground = new SolidColorBrush(ParseSidebarColor(ThemeContrast.Readable(color, background))) };
                var badge = new Border { Child = content, Padding = new(5, 2, 5, 2), CornerRadius = new(8), Background = new SolidColorBrush(ParseSidebarColor(background)) };
                ToolTipService.SetToolTip(badge, caption + " : " + count);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(badge, caption + " : " + count);
                host.Children.Add(badge);
            }
            Badge(running, "…", WorkflowText("En cours", "Running"), theme.Accent);
            Badge(done, "✓", WorkflowText("Réponses terminées non lues", "Unread completed replies"), "#34D399");
            Badge(attention, "!", WorkflowText("Attention requise", "Needs attention"), "#FBBF24");
        }
    }
}
