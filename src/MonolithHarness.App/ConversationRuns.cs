using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    // Each generation owns its database context, captured configuration and message view.
    // UI selection is never used to route a response that is already running.
    sealed class ConversationRun(Chat chat, Project project, Provider provider, AppState options,
        string prompt, IEnumerable<Attachment> images, IEnumerable<Provider>? availableProviders=null) : ConversationSession(chat, project, provider, options, prompt, images, availableProviders:availableProviders)
    {
        public required StackPanel Messages { get; init; }
        public GenerationSpeedTracker? Tracker { get; set; }
        public GenerationUpdate? Update { get; set; }
        public int? InputEstimate { get; set; }
        public (double Tokens, bool Estimated)? Context { get; set; }
        public string Status { get; set; } = "";
        public StatusKind StatusMode { get; set; } = StatusKind.Activity;
        public DateTimeOffset? StatusExpiresAt { get; set; }
        public bool Submitted { get; set; }
        public bool Failed { get; set; }
        public bool IsScheduled { get; init; }
        public RunningTool? CurrentTool { get; set; }
        public ContextRequestProgress? ContextRequest { get; set; }
        public System.Diagnostics.Stopwatch ContextRequestTimer { get; } = new();
        public bool WaitingForModel { get; set; }
        public DateTimeOffset LastModelProgress { get; set; } = DateTimeOffset.UtcNow;
        public string ProgressText { get; set; } = "";
        public string ProgressReasoning { get; set; } = "";
        public int? ProgressTokens { get; set; }
        public int RetryRevision { get; set; }
        public bool RetryFeedbackActive { get; set; }
    }

    readonly Dictionary<int, ConversationRun> conversationRuns = [];
    readonly Dictionary<int, (string Text, List<Attachment> Images)> conversationDrafts = [];
    readonly Dictionary<int, StatusEntry> conversationStatuses = [];
    readonly Dictionary<int, List<Message>> conversationHistory = [];
    readonly SemaphoreSlim toolQueue = new(1, 1);
    ConversationRun? ActiveRun => chat != null ? conversationRuns.GetValueOrDefault(chat.Id) : null;
    string RunSkills(ConversationRun run) => ConversationModes.EffectiveSkills(run.Chat,
        run.IsScheduled ? run.Options.EnabledSkills : state.EnabledSkills, run.Provider.IsExternalAgent);
    string ToolSkills => automaticToolRun.Value is { } run ? RunSkills(run) : state.EnabledSkills;
    static StackPanel CreateMessagePanel() => ChatDensity.Track(new StackPanel { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Stretch }, "messages");
    bool IsVisible(ConversationRun run) => selectedSubagent == null && chat?.Id == run.Chat.Id;
    IEnumerable<Message> VisibleHistory() => ActiveRun is { } active ? active.Db.Messages.Local :
        chat != null ? conversationHistory.GetValueOrDefault(chat.Id) ?? [] : [];

    void SaveConversationDraft()
    {
        if (chat != null) conversationDrafts[chat.Id] = (composer.Text, [.. pendingImages]);
    }

    void RestoreConversationDraft()
    {
        pendingImages.Clear(); composer.Text = "";
        if (chat != null && conversationDrafts.TryGetValue(chat.Id, out var draft))
        {
            composer.Text = draft.Text; pendingImages.AddRange(draft.Images);
        }
        UpdateAttachments();
    }

    void RefreshGenerationControls()
    {
        RefreshConversationMode();
        // Keep navigation, settings and drafts usable; only sending to this running chat is blocked.
        send.IsEnabled = selectedSubagent == null && chat != null && conversationReady && !conversationLoading && !conversationRetentionBusy && !databaseMaintenanceBusy;
        stop.IsEnabled = ActiveRun != null;
        composer.IsEnabled = selectedSubagent == null;
        RefreshConversationProgress();
        RefreshModelActivity();
    }

    static TElement? FindConversationElement<TElement>(DependencyObject parent, string tag) where TElement : FrameworkElement
    {
        if (parent is TElement found && Equals(found.Tag, tag)) return found;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindConversationElement<TElement>(VisualTreeHelper.GetChild(parent, i), tag) is { } child) return child;
        return default;
    }

    void RefreshConversationCard(ListViewItem container)
    {
        if (FindConversationElement<Border>(container, "conversation-card") is not { } card) return;
        var hover = ReferenceEquals(hoveredConversationContainer, container);
        var selected = container.Content is Chat selectedChat &&
            (selectedChat.IsArchived ? archivedChats : chats).SelectedItems.Contains(selectedChat);
        card.Background = hover || selected ? FluentDesign.Resource("ConversationHoverFillBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        card.BorderBrush = selected ? FluentDesign.Resource("AccentFillColorDefaultBrush") :
            hover ? FluentDesign.Resource("ConversationHoverStrokeBrush") : FluentDesign.Stroke;
        if (FindConversationElement<Button>(container, "conversation-favorite") is { } favorite && container.Content is Chat item)
        {
            favorite.Opacity = item.IsFavorite || hover ? 1 : 0;
            favorite.Visibility = Visibility.Visible;
            favorite.IsHitTestVisible = item.IsFavorite || hover;
            favorite.IsTabStop = item.IsFavorite || hover;
            favorite.Content = new FontIcon { Glyph = item.IsFavorite ? "\uE735" : "\uE734", FontSize = 14 };
            ToolTipService.SetToolTip(favorite, WorkflowText(item.IsFavorite ? "Retirer des favoris" : "Ajouter aux favoris", item.IsFavorite ? "Remove favorite" : "Add favorite"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(favorite, WorkflowText("Favori", "Favorite"));
            favorite.Click -= FavoriteClick; favorite.Click += FavoriteClick;
        }
    }

    void RefreshConversationProgress()
    {
        RefreshIndependentChatRows();
        RefreshSubagentSidebar();
        foreach (var list in new[] { chats, archivedChats })
        foreach (var item in list.Items.OfType<Chat>())
        {
            if (list.ContainerFromItem(item) is not ListViewItem container) continue;
            RefreshConversationCard(container);
            if (FindConversationElement<Border>(container, "conversation-selection") is { } selection)
                selection.Visibility = list.SelectedItems.Contains(item) ? Visibility.Visible : Visibility.Collapsed;
            if (FindConversationElement<ProgressBar>(container, "conversation-progress") is { } bar)
            {
                bar.Visibility = conversationRuns.ContainsKey(item.Id) ? Visibility.Visible : Visibility.Collapsed;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bar, T("Le modèle réfléchit…") + " " + item.Title);
            }
            if (FindConversationElement<TextBlock>(container, "conversation-title") is { } label)
            {
                label.Text = item.Title;
                label.Visibility = namingChats.Contains(item.Id) ? Visibility.Collapsed : Visibility.Visible;
                ToolTipService.SetToolTip(label, item.Title);
            }
            if (FindConversationElement<StackPanel>(container, "conversation-naming") is { } naming)
                naming.Visibility = namingChats.Contains(item.Id) ? Visibility.Visible : Visibility.Collapsed;
            if (FindConversationElement<Border>(container, "conversation-naming-spinner") is { } spinnerHost)
            {
                if (spinnerHost.Child is not BusySpinner) spinnerHost.Child = new BusySpinner();
                ((BusySpinner)spinnerHost.Child).IsActive = namingChats.Contains(item.Id);
            }
            if (FindConversationElement<TextBlock>(container, "conversation-naming-label") is { } namingLabel)
                namingLabel.Text = WorkflowText("Nommage…", "Naming…");
            if (FindConversationElement<TextBlock>(container, "conversation-age") is { } age)
            {
                age.Text = ConversationAge(item);
                ToolTipService.SetToolTip(age, item.UpdatedUtc?.ToLocalTime().ToString("g") ?? "");
            }
            if (FindConversationElement<FontIcon>(container, "conversation-notice") is { } notice)
                notice.Visibility = FeatureSettings.Read(state.FeaturesJson).NotificationBell && chatNotices.Any(x => x.ChatId == item.Id) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    void SetRunStatus(ConversationRun run, string text, StatusKind kind = StatusKind.Activity)
    {
        CancelModelRetryFeedback(run);
        var wasWaiting = run.WaitingForModel;
        run.WaitingForModel = text == T("Le modèle réfléchit…") || text == T("OpenCode réfléchit…");
        if (run.WaitingForModel && !wasWaiting)
        { run.LastModelProgress = DateTimeOffset.UtcNow; run.ProgressText = run.ProgressReasoning = ""; run.ProgressTokens = null; }
        if (!run.WaitingForModel) run.ContextRequest = null;
        if (run.WaitingForModel && run.ContextRequest is { } preload) text = preload.Caption(state.Language, run.ContextRequestTimer.Elapsed.TotalSeconds);
        run.Status = text;
        run.StatusMode = kind;
        run.StatusExpiresAt = StatusExpiry(text, kind);
        conversationStatuses[run.Chat.Id] = new(text, kind, run.StatusExpiresAt);
        if (IsVisible(run) && !(kind == StatusKind.Activity && IsTransientOverlayVisible))
            ShowStatus(text, kind, run.Chat.Id, run.StatusExpiresAt);
        RefreshModelActivity();
    }

    void ShowContextUsage(ConversationRun run, double tokens, bool estimated = false)
    {
        run.Context = (tokens, estimated);
        if (IsVisible(run)) ShowContextUsage(tokens, estimated, run.Provider.ContextLimit);
    }

    void UpdateMetrics(ConversationRun run, GenerationUpdate update, int? fallbackInputTokens = null)
    {
        run.Update = update; run.InputEstimate = fallbackInputTokens; run.Context = null;
        if (IsVisible(run)) UpdateMetrics(update, fallbackInputTokens, run.Provider.ContextLimit);
    }

    void RestoreRunMetrics(ConversationRun run)
    {
        ShowStatus(run.Status, run.StatusMode, run.Chat.Id, run.StatusExpiresAt);
        if (run.Update != null) UpdateMetrics(run.Update, run.InputEstimate, run.Provider.ContextLimit);
        if (run.Context is { } context) ShowContextUsage(context.Tokens, context.Estimated, run.Provider.ContextLimit);
        RefreshSpeedPopover(run.Tracker);
        RefreshModelActivity();
    }

    void ScrollRunToBottom(ConversationRun run)
    {
        if (IsVisible(run)) ScrollToBottom();
    }

    void MarkRunSubmitted(ConversationRun run)
    {
        run.Submitted = true;
        run.Chat.UpdatedUtc = DateTime.UtcNow;
        // Update only the originating list entry, never the newly selected conversation.
        var entry = db.Chats.Local.FirstOrDefault(x => x.Id == run.Chat.Id);
        if (entry != null)
        {
            entry.Title = run.Chat.Title;
            entry.UpdatedUtc = run.Chat.UpdatedUtc;
            db.Entry(entry).Property(x => x.UpdatedUtc).OriginalValue = run.Chat.UpdatedUtc;
            db.Entry(entry).Property(x => x.Title).OriginalValue = run.Chat.Title;
        }
        if (IsVisible(run)) title.Text = run.Chat.Title;
        RefreshConversationProgress();
        if (ConversationNaming.At(FeatureSettings.Read(run.Options.FeaturesJson), "first-message")) _ = AutoNameAsync(run.Chat.Id, true);
    }

    async Task SendAsync()
    {
        if (databaseMaintenanceBusy || conversationRetentionBusy) return;
        if (await ExecuteSlashCommandAsync()) return;
        if (!conversationReady || conversationLoading || chat == null || provider == null || project == null) return;
        if(!ProviderModels.Visible(provider).Contains(provider.Model)) { ShowStatus(WorkflowText("Cochez un modèle dans les réglages des fournisseurs.","Select a model in provider settings."), StatusKind.Error); return; }
        if (string.IsNullOrWhiteSpace(composer.Text) && pendingImages.Count == 0) return;
        if(ActiveRun is { } active)
        {
            var text=composer.Text.Trim();var images=pendingImages.ToList();var id=chat.Id;var providerId=provider.Id;var mode="queued";
            composer.Text="";pendingImages.Clear();UpdateAttachments();SaveConversationDraft();
            try{await ConversationInbox.AddAsync(HarnessDb.DatabasePath,id,providerId,text,images,mode);}
            catch{var draft=conversationDrafts.GetValueOrDefault(id);conversationDrafts[id]=(text+"\n"+draft.Text,[..images,..draft.Images??[]]);if(chat?.Id==id)RestoreConversationDraft();throw;}
            await RefreshInboxAsync();
            if(!conversationRuns.ContainsKey(id) && !active.Failed && !active.Cancellation.IsCancellationRequested)await RunNextQueuedAsync(id,active.Messages);
            return;
        }
        var run = new ConversationRun(chat, project, provider, state, composer.Text.Trim(), pendingImages,db.Providers.Local) { Messages = messages };
        var secret = KeyVault.Decrypt(run.Provider.ProtectedKey);
        if ((!run.Provider.IsExternalAgent && !run.Provider.IsLocal && secret.Length == 0) || string.IsNullOrWhiteSpace(run.Provider.Model))
        { run.Dispose();await Settings(); return; }
        conversationRuns.Add(run.Chat.Id, run); // Reserve before the first await, preventing duplicate sends.
        composer.Text = ""; pendingImages.Clear(); UpdateAttachments();
        SaveConversationDraft();await ExecuteRunAsync(run);
    }
    async Task ExecuteRunAsync(ConversationRun run)
    {
        AppLog.Write(AppLogLevel.Information, "generation.started", chatId: run.Chat.Id);
        permissionProject.Value = run.Project;
        automaticToolRun.Value = run;
        bool success=false;
        MarkProjectChatStarted(run);
        RefreshGenerationControls();
        SetRunStatus(run, T("Le modèle réfléchit…"));
        try
        {
            var secret=KeyVault.Decrypt(run.Provider.ProtectedKey);
            await run.PrepareSandboxAsync(run.Cancellation.Token);
            if (run.Provider.IsOpenCode) await SendOpenCodeAsync(run, secret);
            else await SendCoreAsync(run, secret);
            success=!run.Failed && !run.Cancellation.IsCancellationRequested;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex is OperationCanceledException ? AppLogLevel.Information : AppLogLevel.Error, "generation.failed", ex, run.Chat.Id);
            run.Failed = true;
            SetRunStatus(run, ex is OperationCanceledException
                ? T("Génération arrêtée. Réponse partielle conservée.") : T("Erreur : ") + ex.Message,
                ex is OperationCanceledException ? StatusKind.Notice : StatusKind.Error);
            if (!run.Submitted && run.PendingInputId==0)
            {
                if (IsVisible(run)) SaveConversationDraft();
                var draft = conversationDrafts.GetValueOrDefault(run.Chat.Id);
                conversationDrafts[run.Chat.Id] = (run.Prompt + (string.IsNullOrEmpty(draft.Text) ? "" : "\n" + draft.Text),
                    [.. run.Images, .. draft.Images ?? []]);
                if (IsVisible(run)) RestoreConversationDraft();
            }
        }
        finally
        {
            try { await run.Db.SaveChangesAsync(); }
            catch (Exception ex) { run.Failed = true; success = false; SetRunStatus(run, T("Erreur : ") + ex.Message, StatusKind.Error); }
            if (success && run.StatusMode == StatusKind.Activity)
                SetRunStatus(run, T("Réponse terminée · historique enregistré."), StatusKind.Notice);
            if (run.Submitted) conversationHistory[run.Chat.Id] = run.Db.Messages.Local.ToList();
            if (run.Sandbox != null) await terminals.StopChatAsync(run.Chat.Id, true);
            conversationRuns.Remove(run.Chat.Id);
            MarkProjectChatEnded(run, success);
            run.Dispose();
            RefreshGenerationControls();
            if (IsVisible(run)) RestoreRunMetrics(run);
            await RefreshInboxAsync();
        }
        if (success)
        {
            NotifyChat(run, false);
            if (ConversationNaming.At(FeatureSettings.Read(run.Options.FeaturesJson), "first-response")) await AutoNameAsync(run.Chat.Id, true);
            AppLog.Write(AppLogLevel.Information, "generation.completed", chatId: run.Chat.Id);
            await RunNextQueuedAsync(run.Chat.Id,run.Messages);
        }
    }

    async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, CancellationToken ct = default)
    {
        await approvalQueue.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            using var registration = ct.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
            var pending = automaticToolRun.Value;
            var waitingTool = pending?.CurrentTool;
            var priorPhase = waitingTool?.Phase;
            if (waitingTool != null)
            {
                waitingTool.Phase = WorkflowText("Action requise", "Action required");
                waitingTool.Message.UpdateProgress(waitingTool.Timer.Elapsed.TotalSeconds, waitingTool.Phase);
            }
            if (pending != null && conversationRuns.ContainsKey(pending.Chat.Id)) NotifyChat(pending, true);
            try { return await dialog.ShowAsync(); }
            finally
            {
                if (waitingTool != null)
                {
                    waitingTool.Phase = priorPhase;
                    waitingTool.Message.UpdateProgress(waitingTool.Timer.Elapsed.TotalSeconds, priorPhase);
                }
                if (pending != null) AcknowledgeChatNotice(pending.Chat.Id, actionResolved: true);
            }
        }
        finally { approvalQueue.Release(); }
    }
}
