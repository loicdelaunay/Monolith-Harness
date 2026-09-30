using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly StackPanel projectNavigation = new() { Spacing = 4 };
    readonly ScrollViewer sidebarScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    readonly Grid conversationArea = new();
    readonly HashSet<int> collapsedSidebarProjects = [];

    Grid BuildConversationNavigation()
    {
        var panel = new Grid { Padding = new(14, 18, 14, 14), RowSpacing = 8, Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush") };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            panel.RowDefinitions.Add(new RowDefinition { Height = height });
        var brand = new Grid { ColumnSpacing = 10, Margin = new(8, 0, 8, 14) };
        brand.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        brand.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        brand.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        brand.Children.Add(brandLogo); Grid.SetColumn(brandName, 1); brand.Children.Add(brandName);
        var version = BuildVersionChip();
        Grid.SetColumn(version, 2); brand.Children.Add(version);
        panel.Children.Add(brand);
        chatSearch.PlaceholderText = UiText.T("Rechercher une conversation…"); chatSearch.FontSize = 12;
        chatSearch.Visibility = Visibility.Collapsed;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chatSearch, UiText.T("Rechercher une conversation"));
        Grid.SetRow(chatSearch, 1); panel.Children.Add(chatSearch);
        sidebarScroll.Content = projectNavigation; Grid.SetRow(sidebarScroll, 2); panel.Children.Add(sidebarScroll);
        conversationArea.RowDefinitions.Add(new() { Height = GridLength.Auto });
        conversationArea.RowDefinitions.Add(new() { Height = GridLength.Auto });
        foreach (var list in new[] { chats, archivedChats })
        {
            list.MinHeight = 0; list.MaxHeight = 360;
            ScrollViewer.SetVerticalScrollMode(list, ScrollMode.Enabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        }
        conversationArea.Children.Add(chats);
        archiveExpander.Header = archiveHeading; archiveExpander.Content = archivedChats;
        archiveExpander.Expanding += (_, _) => { if (projects.SelectedItem is Project owner) expandedSidebarArchives.Add(owner.Id); };
        archiveExpander.Collapsed += (_, _) => { if (projects.SelectedItem is Project owner) expandedSidebarArchives.Remove(owner.Id); };
        archiveExpander.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        archiveExpander.BorderThickness = new(0);
        archiveExpander.Margin = new(0, 4, 0, 0);
        Grid.SetRow(archiveExpander, 1); conversationArea.Children.Add(archiveExpander);
        sidebarScroll.SizeChanged += (_, _) =>
        {
            chats.MaxHeight = Math.Max(120, sidebarScroll.ActualHeight * .65);
            archivedChats.MaxHeight = Math.Max(80, Math.Min(200, sidebarScroll.ActualHeight * .35));
        };
        chatEmpty.Foreground = FluentDesign.Secondary; chatEmpty.Margin = new(8);
        chatEmpty.Visibility = Visibility.Collapsed; conversationArea.Children.Add(chatEmpty);
        return panel;
    }

    static DataTemplate CompactConversationTemplate() => (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
            <Border Tag="conversation-card" Background="Transparent" BorderThickness="0" CornerRadius="5" Padding="8,4">
                <StackPanel Spacing="4">
                    <Grid ColumnSpacing="6" MinHeight="20">
                        <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                        <StackPanel Orientation="Horizontal" Tag="conversation-naming" Visibility="Collapsed" Spacing="6">
                            <Border Tag="conversation-naming-spinner" Width="16" Height="16" VerticalAlignment="Center"/>
                            <TextBlock Tag="conversation-naming-label" FontSize="13" VerticalAlignment="Center"/>
                        </StackPanel>
                        <TextBlock Tag="conversation-title" Text="{Binding Title}" TextTrimming="CharacterEllipsis" FontSize="14" VerticalAlignment="Center"/>
                        <Button Grid.Column="1" Tag="conversation-favorite" Width="22" Height="22" MinWidth="22" MinHeight="22" Padding="0" Opacity="0" IsHitTestVisible="False" IsTabStop="False" Background="Transparent" BorderThickness="0"/>
                        <FontIcon Grid.Column="2" Tag="conversation-notice" Glyph="&#xEA8F;" FontSize="13" Visibility="Collapsed"/>
                        <TextBlock Grid.Column="3" Tag="conversation-age" FontSize="11" VerticalAlignment="Center" Foreground="{ThemeResource TextFillColorSecondaryBrush}"/>
                    </Grid>
                    <ProgressBar Tag="conversation-progress" Height="2" IsIndeterminate="True" Visibility="Collapsed"/>
                    <Border Tag="conversation-subagents" Margin="0,2,0,0" Padding="7,0,0,0" BorderThickness="1,0,0,0" BorderBrush="{ThemeResource ControlStrokeColorDefaultBrush}" Visibility="Collapsed">
                        <StackPanel Tag="subagents" Spacing="3"/>
                    </Border>
                </StackPanel>
            </Border>
        </DataTemplate>
        """);

    void RebuildProjectNavigation()
    {
        if (conversationArea.Parent is Border previous) previous.Child = null;
        projectNavigation.Children.Clear(); independentChatRows.Clear(); projectBadgeHosts.Clear();
        var items = (projects.ItemsSource as IEnumerable<Project> ?? []).ToList();
        var summaries = navigationChats.Select(CurrentSidebarChat).ToList();
        var pinned = summaries.Where(x => x.IsPinned && MatchesSidebarSearch(x)).OrderBy(x => x.Id).ToList();
        if (pinned.Count > 0)
        {
            var pinnedHeading = Label(WorkflowText("Épinglées", "Pinned"), 12); pinnedHeading.Foreground = FluentDesign.Secondary; pinnedHeading.Margin = new(8, 4, 0, 6);
            projectNavigation.Children.Add(pinnedHeading);
            foreach (var item in pinned) projectNavigation.Children.Add(BuildIndependentChatRow(item, true));
        }
        var heading = new Grid { Margin = new(8, pinned.Count > 0 ? 16 : 4, 0, 6) };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++) heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = Label(WorkflowText("Projets", "Projects"), 12); label.Foreground = FluentDesign.Secondary;
        label.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(label);
        var bell = BuildNotificationBell(); Grid.SetColumn(bell, 1); heading.Children.Add(bell);
        var search = Action("", () =>
        {
            chatSearch.Visibility = chatSearch.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (chatSearch.Visibility == Visibility.Visible) chatSearch.Focus(FocusState.Programmatic);
            else chatSearch.Text = "";
            return Task.CompletedTask;
        });
        FluentDesign.IconButton(search, "\uE721", WorkflowText("Filtrer les conversations", "Filter conversations"), false);
        search.Width = search.Height = 30; search.Padding = new(0); search.BorderThickness = new(0); search.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Grid.SetColumn(search, 2); heading.Children.Add(search);
        var addProject = Action("", NewProject); FluentDesign.IconButton(addProject, "\uE8F4", WorkflowText("Nouveau projet", "New project"), false);
        addProject.Width = addProject.Height = 30; addProject.Padding = new(0); addProject.BorderThickness = new(0); addProject.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Grid.SetColumn(addProject, 3); heading.Children.Add(addProject);
        projectNavigation.Children.Add(heading);
        foreach (var owner in items.Where(x => !x.IsInbox)) AddGroup(owner, false);
        var inbox = items.FirstOrDefault(x => x.IsInbox);
        if (inbox != null) AddGroup(inbox, true);

        void AddGroup(Project owner, bool standalone)
        {
            bool selected = (projects.SelectedItem as Project)?.Id == owner.Id;
            bool expanded = expandedSidebarProjects.Contains(owner.Id) && !collapsedSidebarProjects.Contains(owner.Id);
            var header = new Grid { ColumnSpacing = 2, Margin = new(0, standalone ? 18 : 0, 0, 0) };
            header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var open = Action("", () =>
            {
                if (expanded) { expandedSidebarProjects.Remove(owner.Id); collapsedSidebarProjects.Add(owner.Id); }
                else { expandedSidebarProjects.Add(owner.Id); collapsedSidebarProjects.Remove(owner.Id); }
                RebuildProjectNavigation(); return Task.CompletedTask;
            });
            open.Padding = new(8, 7, 4, 7); open.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            open.BorderThickness = new(0); open.HorizontalAlignment = HorizontalAlignment.Stretch; open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            var body = new Grid { ColumnSpacing = 8 };
            body.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); body.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var icon = FluentDesign.Icon(standalone ? "\uE8F2" : ProjectGlyph(owner.Icon, expanded), 15); icon.Foreground = ProjectBrush(owner.Color); body.Children.Add(icon);
            var name = new TextBlock { Text = standalone ? WorkflowText("Conversations", "Conversations") : owner.Name, FontSize = standalone ? 12 : 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Foreground = selected ? FluentDesign.Primary : FluentDesign.Secondary };
            Grid.SetColumn(name, 1); body.Children.Add(name);
            var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            projectBadgeHosts[owner.Id] = badges; Grid.SetColumn(badges, 2); body.Children.Add(badges); open.Content = body;
            ToolTipService.SetToolTip(open, owner.Name); header.Children.Add(open);
            if (!standalone) open.RightTapped += (_, e) =>
            {
                e.Handled = true;
                var menu = new MenuFlyout(); var manage = new MenuFlyoutItem { Text = UiText.T("Gérer") };
                manage.Click += async (_, _) => await Guard(async () => { if (!selected) { loading = true; projects.SelectedItem = owner; loading = false; await SelectProject(); } await ManageProject(); });
                menu.Items.Add(manage); menu.ShowAt(open);
            };
            var add = Action("", async () =>
            {
                var created = new Chat { ProjectId = owner.Id, Title = UiText.T("Nouvelle conversation") };
                db.Chats.Add(created); await db.SaveChangesAsync();
                loading = true; projects.SelectedItem = owner; state.ChatId = created.Id; loading = false;
                collapsedSidebarProjects.Remove(owner.Id); await SelectProject();
            });
            FluentDesign.IconButton(add, "\uE710", UiText.T("Nouvelle conversation"), false);
            add.Width = add.Height = 30; add.Padding = new(0); add.BorderThickness = new(0); add.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            add.Opacity = standalone ? 1 : 0;
            if (!standalone)
            {
                header.PointerEntered += (_, _) => add.Opacity = 1;
                header.PointerExited += (_, _) => add.Opacity = 0;
                add.GotFocus += (_, _) => add.Opacity = 1;
                add.LostFocus += (_, _) => add.Opacity = 0;
            }
            Grid.SetColumn(add, 1); header.Children.Add(add); projectNavigation.Children.Add(header);
            if (!expanded) return;
            var host = new Border { Margin = new(standalone ? 0 : 16, 0, 0, 8) };
            if (selected) host.Child = conversationArea;
            else
            {
                var rows = new StackPanel { Spacing = 4 };
                var matching = summaries.Where(x => x.ProjectId == owner.Id && MatchesSidebarSearch(x)).ToList();
                foreach (var item in matching.Where(x => !x.IsArchived).OrderByDescending(x => x.IsFavorite).ThenByDescending(x => x.UpdatedUtc).ThenByDescending(x => x.Id))
                    rows.Children.Add(BuildIndependentChatRow(item, false));
                var archived = matching.Where(x => x.IsArchived).ToList();
                if (archived.Count > 0)
                {
                    var archivedRows = new StackPanel { Spacing = 4 };
                    foreach (var item in archived) archivedRows.Children.Add(BuildIndependentChatRow(item, false));
                    var archive = new Expander { Header = WorkflowText($"Archive ({archived.Count})", $"Archive ({archived.Count})"), IsExpanded = expandedSidebarArchives.Contains(owner.Id), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Content = new ScrollViewer { Content = archivedRows, MaxHeight = 200, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
                    archive.Expanding += (_, _) => expandedSidebarArchives.Add(owner.Id);
                    archive.Collapsed += (_, _) => expandedSidebarArchives.Remove(owner.Id);
                    rows.Children.Add(archive);
                }
                if (matching.Count == 0) rows.Children.Add(Label(WorkflowText("Aucune conversation", "No conversations"), 12));
                host.Child = rows;
            }
            projectNavigation.Children.Add(host);
        }
        RefreshConversationProgress();
    }

    string ConversationAge(Chat item)
    {
        if (item.UpdatedUtc == null) return "";
        var age = DateTime.UtcNow - DateTime.SpecifyKind(item.UpdatedUtc.Value, DateTimeKind.Utc);
        if (age.TotalMinutes < 1) return WorkflowText("maint.", "now");
        if (age.TotalHours < 1) return $"{Math.Max(1, (int)age.TotalMinutes)}m";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours}h";
        return $"{(int)age.TotalDays}" + WorkflowText("j", "d");
    }
}
