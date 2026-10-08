using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    const int HistoryPageSize = 24;
    sealed class HistoryPagination
    {
        public Func<Task> Load = null!;
        public bool Loading;
        public bool Armed = true;
    }
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<StackPanel, HistoryPagination> historyPagination = new();

    async Task LoadHistoryAtTopAsync()
    {
        if (scroll.Content is not StackPanel panel || !historyPagination.TryGetValue(panel, out var page)) return;
        if (scroll.VerticalOffset > 64) { page.Armed = true; return; }
        if (conversationLoading || followChatTail || page.Loading || !page.Armed) return;
        page.Loading = true;
        page.Armed = false;
        try { await page.Load(); }
        catch
        {
            if (ReferenceEquals(scroll.Content, panel))
                ShowStatus(WorkflowText("Historique indisponible. Remontez à nouveau pour réessayer.", "History unavailable. Scroll up again to retry."));
        }
        finally
        {
            page.Loading = false;
            if (ReferenceEquals(scroll.Content, panel) && scroll.VerticalOffset > 64) page.Armed = true;
        }
    }
    int projectLoadRevision;
    int conversationLoadRevision;
    bool conversationLoading;
    bool conversationReady;
    CancellationTokenSource? conversationLoad;

    // SQLite's async API still performs synchronous I/O. Each worker owns its context;
    // never move the window's tracked context onto a background thread.
    static Task<T> ReadStoreAsync<T>(Func<HarnessDb, T> read, CancellationToken ct = default) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        using var store = new HarnessDb();
        var result = read(store);
        ct.ThrowIfCancellationRequested();
        return result;
    }, ct);

    FrameworkElement LoadingPlaceholder(string caption)
    {
        var panel = new StackPanel { Spacing = 16, Margin = new(20) };
        panel.Children.Add(new TextBlock { Text = caption, Foreground = FluentDesign.Secondary });
        panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 3 });
        foreach (var width in new[] { 0.9, 0.65, 0.8 })
        {
            var rows = new StackPanel { Spacing = 10 };
            rows.Children.Add(new Border { Height = 12, Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Background = FluentDesign.Card, CornerRadius = new(6) });
            rows.Children.Add(new Border { Height = 40, Opacity = width, Background = FluentDesign.Card, CornerRadius = new(8) });
            panel.Children.Add(rows);
        }
        return panel;
    }

    async Task SelectProject()
    {
        selectedSubagent = null; childPanel = null; parentConversationBar.Visibility = Visibility.Collapsed;
        var revision = ++projectLoadRevision;
        var selected = projects.SelectedItem as Project;
        SaveConversationDraft();
        conversationLoad?.Cancel();
        conversationLoad = null;
        ++conversationLoadRevision;
        chat = null;
        project = selected;
        state.ProjectId = selected?.Id;
        conversationLoading = true;
        conversationReady = false;
        RefreshGenerationControls();
        scroll.Content = LoadingPlaceholder(WorkflowText("Chargement du projet…", "Loading project…"));
        // Old rows must not remain actionable while another project's list is loading.
        loading = true;
        try { allProjectChats = []; ApplyChatSearch(null); }
        finally { loading = false; }
        try
        {
            var list = selected == null ? [] : await ReadStoreAsync(store => store.Chats.AsNoTracking()
                .Where(x => x.ProjectId == selected.Id).OrderByDescending(x => x.Id).ToList());
            var sidebarRows = await ReadStoreAsync(store => store.Chats.AsNoTracking().Select(x => new Chat {
                Id = x.Id, ProjectId = x.ProjectId, Title = x.Title, IsPinned = x.IsPinned, IsFavorite = x.IsFavorite, IsArchived = x.IsArchived, UpdatedUtc = x.UpdatedUtc
            }).ToList());
            if (revision != projectLoadRevision) return;
            navigationChats = sidebarRows;
            PruneSidebarActivity();
            if (selected != null && !collapsedSidebarProjects.Contains(selected.Id)) expandedSidebarProjects.Add(selected.Id);
            // Keep edits to chat preferences on the UI-owned tracked entities.
            list = list.Select(item => db.Chats.Local.FirstOrDefault(x => x.Id == item.Id) ?? db.Chats.Attach(item).Entity).ToList();
            loading = true;
            try
            {
                chatSearch.Text = string.Empty;
                allProjectChats = list;
                archiveExpander.IsExpanded = selected != null && expandedSidebarArchives.Contains(selected.Id);
                ApplyChatSearch(state.ChatId, selectFirst: true);
                RebuildProjectNavigation();
            }
            finally { loading = false; }
            await SelectChat();
        }
        catch
        {
            if (revision != projectLoadRevision) return;
            conversationLoading = false;
            RefreshGenerationControls();
            scroll.Content = Label(WorkflowText("Impossible de charger le projet. Sélectionnez-le pour réessayer.", "Could not load the project. Select it to retry."));
            throw;
        }
    }

    async Task SelectChat()
    {
        if (chatFindBar.Visibility == Visibility.Visible) CloseChatFind();
        conversationLoad?.Cancel();
        using var cancellation = new CancellationTokenSource();
        conversationLoad = cancellation;
        var ct = cancellation.Token;
        var revision = ++conversationLoadRevision;
        selectedSubagent = null; childPanel = null;
        parentConversationBar.Visibility = Visibility.Collapsed;
        SaveConversationDraft();
        var selected = (chats.SelectedItem ?? archivedChats.SelectedItem) as Chat;
        chat = selected; state.ChatId = selected?.Id;
        if (selected != null) AcknowledgeChatNotice(selected.Id);
        project = projects.SelectedItem is Project owner && (selected == null || owner.Id == selected.ProjectId)
            ? selected == null ? owner : ProjectResources.Effective(selected, owner)
            : null;
        conversationLoading = true;
        conversationReady = false;
        pinnedTasks.Child = null; pinnedTasks.Visibility = Visibility.Collapsed;
        inboxPanel.Children.Clear();
        title.Text = selected?.Title ?? T("Créez une conversation");
        RefreshNamingState();
        ToolTipService.SetToolTip(title, title.Text);
        var displayedRun = ActiveRun;
        messages = displayedRun?.Messages ?? CreateMessagePanel();
        var panel = messages;
        var placeholder = LoadingPlaceholder(WorkflowText("Chargement de la conversation…", "Loading conversation…"));
        if (displayedRun == null) panel.Children.Add(placeholder);
        scroll.Content = panel;
        SetChatFollow(true); chatScrollInputUntil = 0; draggingChatScroll = manipulatingChatScroll = false;
        RestoreConversationDraft();
        ResetWorkspaceTools();
        RefreshGenerationControls();
        try
        {
            if (selected != null)
            {
                var storedProject = await ReadStoreAsync(store => store.Projects.AsNoTracking().Single(x => x.Id == selected.ProjectId), ct);
                ct.ThrowIfCancellationRequested();
                project = ProjectResources.Effective(selected, storedProject);
            }
            UpdateSourceLabel(); UpdateFloatingAssets();
            if (displayedRun is { } running)
            {
                RestoreRunMetrics(running);
                ScrollToBottom(force: true);
            }
            else
            {
                RestoreConversationStatus();
                var history = selected == null ? [] : await ReadStoreAsync(store =>
                {
                    var items = store.Messages.AsNoTracking().Where(x => x.ChatId == selected.Id).OrderBy(x => x.Id).ToList();
                    // Keep attachment metadata for context counts, but defer image blobs to
                    // the displayed page instead of reading every screenshot in the chat.
                    var attachments = store.Set<Attachment>().AsNoTracking().Where(x => store.Messages.Any(m => m.Id == x.MessageId && m.ChatId == selected.Id))
                        .Select(x => new Attachment { Id = x.Id, MessageId = x.MessageId, Name = x.Name, Mime = x.Mime }).ToList().ToLookup(x => x.MessageId);
                    foreach (var item in items) item.Attachments = attachments[item.Id].ToList();
                    return items;
                }, ct);
                ct.ThrowIfCancellationRequested();
                if (selected != null) conversationHistory[selected.Id] = history;
                var sourceProject = project;
                await RenderRecentHistoryAsync(history, panel, sourceProject, ct);
                ct.ThrowIfCancellationRequested();
                panel.Children.Remove(placeholder);
                var last = history.LastOrDefault(x => x.InputTokens.HasValue);
                if (last != null) UpdateMetrics(new(last.Content, "", last.InputTokens, last.OutputTokens, last.Seconds));
                RefreshSpeedPopover(); RefreshContextInfo();
                if (panel.Children.Count == 0)
                {
                    var welcome = new StackPanel { Spacing = 16, Margin = new(20, 42, 20, 24) };
                    welcome.Children.Add(Label(T("Un espace pour vos idées.\nDes outils pour aller plus loin."), 30));
                    welcome.Children.Add(Label(T("Discutez avec votre modèle, joignez une image ou explorez un dossier source. Chaque projet garde ses conversations et son contexte."), 15));
                    if (selected == null)
                    {
                        var startDiscussion = Action(WorkflowText("Lancer une nouvelle discussion", "Start a new conversation"), NewChat);
                        startDiscussion.HorizontalAlignment = HorizontalAlignment.Left; startDiscussion.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
                        welcome.Children.Add(startDiscussion);
                    }
                    panel.Children.Add(welcome);
                }
                ScrollToBottom();
            }
            if (selected != null && (await FileProposals.ReadAsync(db.FilePath, selected.Id, ct))?.Files.Count > 0) AddProposalReviewButton(panel);
            await Task.WhenAll(RefreshInboxAsync(), RefreshPinnedTasksAsync());
            ct.ThrowIfCancellationRequested();
            if (selected != null && displayedRun == null) await LoadSubagents(selected.Id);
            ct.ThrowIfCancellationRequested();
            // Persist selection before starting a potentially slow tool refresh.
            await db.SaveChangesAsync();
            ct.ThrowIfCancellationRequested();
            conversationReady = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch
        {
            if (revision != conversationLoadRevision) return;
            panel.Children.Remove(placeholder);
            panel.Children.Add(Action(WorkflowText("Chargement impossible · Réessayer", "Loading failed · Retry"), SelectChat));
            throw;
        }
        finally
        {
            if (revision == conversationLoadRevision)
            {
                conversationLoading = false;
                conversationLoad = null;
                RefreshGenerationControls();
                QueueMessageVirtualization();
            }
        }
        if (revision == conversationLoadRevision && browserVisible && toolTabs.SelectedIndex is 2 or 3 or 4)
            await ActivateToolAsync();
    }

    async Task RenderRecentHistoryAsync(List<Message> history, StackPanel panel, Project? sourceProject, CancellationToken ct)
    {
        if (history.LastOrDefault(x => x.Role == "tasks") is { } tasks) RenderTasks(tasks.Content, panel);
        await Virtualize(panel).InitializeAsync(history, sourceProject, ct);
    }

    static async Task LoadHistoryImagesAsync(List<Message> history, int start, int count, CancellationToken ct = default)
    {
        var page = history.Skip(start).Take(count).Where(x => x.Attachments.Any(a => a.Data.Length == 0)).ToList();
        if (page.Count == 0) return;
        var ids = page.Select(x => x.Id).ToArray();
        var images = await ReadStoreAsync(store => store.Set<Attachment>().AsNoTracking().Where(x => ids.Contains(x.MessageId)).ToList(), ct);
        var groups = images.ToLookup(x => x.MessageId);
        foreach (var item in page) item.Attachments = groups[item.Id].ToList();
    }
}
