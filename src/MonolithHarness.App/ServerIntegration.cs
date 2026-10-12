using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using MonolithHarness.Core.Remote;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

/// <summary>
/// The optional link to a Monolith Harness Server: shared providers appear as ordinary providers, a project can live on the server
/// (cloud mode: its requests run there) and a local conversation can be shared live. Nothing here runs without a configured server.
/// </summary>
public sealed partial class MainWindow
{
    ServerConnection? serverConnection;
    ServerLinkData? serverLinkCache;
    ShareBridge? shareBridge;
    readonly HashSet<int> rememberedServerProviders = [];
    readonly Dictionary<int, Guid> sharedChats = [];

    ServerConnection Server => serverConnection ??= new ServerConnection(new ServerLinkStore(ServerLinkStore.PathFor(HarnessDb.DatabasePath)), KeyVault.Encrypt, KeyVault.Decrypt);
    ShareBridge Bridge => shareBridge ??= new ShareBridge(Server);
    /// <summary>The persisted link, read once and refreshed after each change made here.</summary>
    ServerLinkData ServerLink => serverLinkCache ??= Server.Link;
    void InvalidateServerLink() => serverLinkCache = null;

    bool IsCloudProject(int projectId) => ServerLink.Projects.ContainsKey(projectId);
    bool IsCloudChat(Chat? target) => target != null && ServerLink.Projects.ContainsKey(target.ProjectId);

    /// <summary>
    /// Providers managed by the server (including those removed during this session): the provider editor must neither delete them nor
    /// write a stale copy of their token back.
    /// </summary>
    IReadOnlySet<int> ServerManagedProviders()
    {
        foreach (var id in ServerLink.Providers.Values) rememberedServerProviders.Add(id);
        return rememberedServerProviders;
    }

    Task<T> RunOnUiAsync<T>(Func<Task<T>> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try { completion.SetResult(await action()); }
            catch (Exception ex) { completion.SetException(ex); }
        })) completion.SetException(new InvalidOperationException("The window is closing."));
        return completion.Task;
    }

    Task RunOnUiAsync(Func<Task> action) => RunOnUiAsync(async () => { await action(); return true; });

    /// <summary>Provider lists, selectors and the project sidebar after the server changed the local database.</summary>
    async Task RefreshAfterServerChangeAsync()
    {
        InvalidateServerLink();
        var providerList = (await db.Providers.OrderBy(x => x.Id).ToListAsync()).Where(ProviderModels.CanChat).ToList();
        provider = providerList.FirstOrDefault(x => x.Id == state.ProviderId) ?? providerList.FirstOrDefault();
        if (provider != null) state.ProviderId = provider.Id;
        await db.SaveChangesAsync();
        loading = true; providers.ItemsSource = providerList; providers.SelectedItem = provider; loading = false;
        UpdateProvider(); PopulateModelSelector(); RebuildProjectNavigation();
    }

    // ---- Cloud conversations ----

    /// <summary>The server copy of a cloud conversation is the reference: bring the local one up to date (offline, the local copy stays).</summary>
    async Task PullCloudChatAsync(Chat target, CancellationToken ct)
    {
        if (!IsCloudChat(target) || !ServerLink.SignedIn || !ServerLink.Chats.ContainsKey(target.Id) || conversationRuns.ContainsKey(target.Id)) return;
        try
        {
            await using var store = new HarnessDb();
            var stored = await store.Chats.SingleAsync(x => x.Id == target.Id, ct);
            await ServerSync.PullChatAsync(store, Server, stored, ct);
        }
        catch (Exception ex) when (ex is ServerException or HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        { AppLog.Write(AppLogLevel.Information, "server.pull_skipped", ex); }
    }

    /// <summary>Replaces the local engine for one request: the server runs it and this window shows its progress.</summary>
    async Task SendCloudAsync(ConversationRun run)
    {
        var chat = run.Chat; var db = run.Db; var ct = run.Cancellation.Token;
        if (!Server.SignedIn) throw new InvalidOperationException(WorkflowText("Connectez-vous au serveur dans Réglages › Serveur.", "Sign in to the server in Settings › Server."));
        var target = ServerSync.ServerProviderOf(ServerLink, run.Provider)
            ?? throw new InvalidOperationException(WorkflowText("Un projet cloud exige un fournisseur du serveur (« · serveur » ou « · cloud »).", "A cloud project needs a server provider (“· server” or “· cloud”)."));
        if (run.Images.Count > 0) throw new InvalidOperationException(WorkflowText("Les images ne sont pas prises en charge dans un projet cloud.", "Images are not supported in a cloud project."));
        var hadHistory = await db.Messages.AnyAsync(x => x.ChatId == chat.Id && x.State == "complete", ct);
        var user = new Message { ChatId = chat.Id, Content = run.Prompt, Attachments = run.Images };
        await ConversationInbox.SubmitAsync(run, user, ct);
        MarkRunSubmitted(run);
        await RefreshInboxAsync();
        if (!hadHistory) ClearMessagePanel(run.Messages);
        AddHistoryActions(user, AddMessage("user", user.Content, user.Attachments, run.Messages, run.Project, ServerSync.CurrentAuthor(ServerLink)));
        ScrollRunToBottom(run);
        ShareMessage(chat, user);
        var sink = new CloudUiSink(this, run);
        try
        {
            var (project, serverChat) = await ServerSync.EnsureChatAsync(db, Server, chat, ct);
            SetRunStatus(run, WorkflowText("Envoi au serveur…", "Sending to the server…"));
            var result = await new CloudRunSession(Server, sink).RunAsync(project, serverChat, run.Prompt, target.ServerProvider, target.Model, ct);
            await sink.FinishAsync();
            if (result.State == "failed")
            {
                run.Failed = true;
                SetRunStatus(run, T("Erreur : ") + (string.IsNullOrWhiteSpace(result.Error) ? WorkflowText("échec de la requête cloud", "cloud request failed") : result.Error.Split('\n')[0]), StatusKind.Error);
            }
            else if (result.State == "cancelled" || ct.IsCancellationRequested)
            {
                run.Failed = true;
                SetRunStatus(run, T("Génération arrêtée. Réponse partielle conservée."), StatusKind.Notice);
            }
            else SetRunStatus(run, T("Réponse terminée · historique enregistré."), StatusKind.Notice);
        }
        catch (ServerException ex) { throw new InvalidOperationException(ex.Message, ex); }
    }

    /// <summary>Paints the progress of a cloud run in the conversation. Callbacks come from the network thread and are marshalled to the UI.</summary>
    sealed class CloudUiSink(MainWindow owner, ConversationRun run) : ICloudSink
    {
        AssistantMessageUi? streaming;
        int streamingCloudId;
        readonly System.Diagnostics.Stopwatch streamingClock = new();

        public Task StateAsync(ServerRun state) => owner.RunOnUiAsync(() =>
        {
            owner.SetRunStatus(run, state.State switch
            {
                "queued" => state.QueuePosition is { } rank ? owner.WorkflowText($"En attente sur le serveur (rang {rank})", $"Queued on the server (position {rank})") : owner.WorkflowText("En attente sur le serveur", "Queued on the server"),
                "running" => owner.WorkflowText("Exécution sur le serveur…", "Running on the server…"),
                "awaiting_approval" => owner.WorkflowText("Approbation requise", "Approval required"),
                _ => run.Status
            });
            return Task.CompletedTask;
        });

        public Task StreamAsync(JsonElement data) => owner.RunOnUiAsync(() =>
        {
            if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("messageId", out var id) || !id.TryGetInt32(out var cloudId)) return Task.CompletedTask;
            string Text(string name) => data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            if (streaming == null || streamingCloudId != cloudId)
            {
                streaming = owner.AddAssistantMessage("", target: run.Messages, sourceProject: run.Project);
                streamingCloudId = cloudId;
                streamingClock.Restart(); run.Tracker = new GenerationSpeedTracker();
            }
            var reasoning = Text("reasoning"); var text = Text("text");
            owner.ObserveComposerSpeed(run, new GenerationUpdate(text, reasoning, null, null, streamingClock.Elapsed.TotalSeconds));
            if (reasoning.Length > 0) streaming.UpdateThinking(reasoning, isComplete: text.Length > 0, streaming: true);
            streaming.UpdateContent(text, streaming: true);
            owner.SetRunStatus(run, owner.WorkflowText("Le modèle réfléchit…", "The model is thinking…"));
            owner.ScrollRunToBottom(run);
            owner.ShareStream(run.Chat, cloudId, text, reasoning);
            return Task.CompletedTask;
        });

        public Task MessageAsync(JsonElement data) => owner.RunOnUiAsync(MergeAsync);

        public Task FinishAsync() => owner.RunOnUiAsync(MergeAsync);

        /// <summary>Merges the server conversation into the local one and repaints it: tool cards, reasoning and durations come out as in a local chat.</summary>
        async Task MergeAsync()
        {
            var changed = await ServerSync.PullChatAsync(run.Db, owner.Server, run.Chat);
            if (changed.Count == 0) return;
            var all = await run.Db.Messages.Where(x => x.ChatId == run.Chat.Id).OrderBy(x => x.Id).ToListAsync();
            owner.ClearMessagePanel(run.Messages);
            owner.RenderHistory(all, run.Messages, run.Project);
            streaming = null;
            run.Tracker = null; streamingClock.Stop(); owner.RefreshGlobalTokenSpeed();
            owner.ScrollRunToBottom(run);
            foreach (var message in changed.Where(x => x.State == "complete")) owner.ShareMessage(run.Chat, message);
        }

        public Task StatusAsync(string text) => owner.RunOnUiAsync(() => { owner.SetRunStatus(run, text); return Task.CompletedTask; });

        public Task<string> PermissionAsync(ServerApproval approval, CancellationToken ct) => owner.RunOnUiAsync(() => owner.CloudPermissionAsync(run, approval, ct));

        public Task<CloudAnswer> QuestionAsync(ServerApproval approval, CancellationToken ct) => owner.RunOnUiAsync(async () =>
        {
            owner.NotifyChat(run, true);
            try
            {
                var questions = WorkflowTools.ParseQuestions(JsonNode.Parse(approval.Questions?.GetRawText() ?? "[]") as JsonArray ?? []);
                var answer = await owner.CreateWorkflow(run).AskAsync(questions, ct);
                return new CloudAnswer(answer.Cancelled, answer.Answers.Select(x => x.ToList()).ToList());
            }
            catch (OperationCanceledException) { return new CloudAnswer(true, []); }
            finally { owner.AcknowledgeChatNotice(run.Chat.Id, actionResolved: true); }
        });
    }

    /// <summary>The server asks its user (this one) to approve a tool action of a cloud run.</summary>
    async Task<string> CloudPermissionAsync(ConversationRun run, ServerApproval approval, CancellationToken ct)
    {
        NotifyChat(run, true);
        try
        {
            UIElement content;
            CommandApproval? command = null;
            if (approval.Command.StartsWith('{'))
                try { command = JsonSerializer.Deserialize<CommandApproval>(approval.Command); } catch (JsonException) { }
            if (command != null) content = CommandApprovalContent(approval.Title, command.Display, command, null, ct);
            else
            {
                var details = new StackPanel { Spacing = 8 };
                details.Children.Add(Label(approval.Title, 15));
                details.Children.Add(new ScrollViewer
                {
                    MaxHeight = 360, Content = new TextBlock { Text = approval.Details.Length > 0 ? approval.Details : approval.Command, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12, Foreground = FluentDesign.Primary }
                });
                content = details;
            }
            var dialog = new ContentDialog
            {
                XamlRoot = root.XamlRoot, Title = WorkflowText("Autorisation demandée par le serveur", "Authorization requested by the server"), Content = content,
                PrimaryButtonText = T("Autoriser une fois"), SecondaryButtonText = approval.OneTimeOnly ? "" : T("Toujours autoriser"), CloseButtonText = T("Refuser"), DefaultButton = ContentDialogButton.Close
            };
            using var registration = ct.Register(() => DispatcherQueue.TryEnqueue(dialog.Hide));
            var response = await ShowDialogAsync(dialog, ct);
            ct.ThrowIfCancellationRequested();
            return response switch { ContentDialogResult.Primary => "allow", ContentDialogResult.Secondary when !approval.OneTimeOnly => "always", _ => "deny" };
        }
        finally { AcknowledgeChatNotice(run.Chat.Id, actionResolved: true); }
    }

    // ---- Local shared conversations ----

    void ShareMessage(Chat target, Message message)
    {
        if (!sharedChats.TryGetValue(target.Id, out var share)) return;
        _ = ForwardSafely(share, "message", new { chatId = target.Id, message = ShareContent.Of(message, ServerSync.CurrentAuthor(ServerLink)) });
    }

    /// <summary>A tool starts: viewers see its card running until the result arrives.</summary>
    void ShareToolStarted(Chat target, string callId, string name, string arguments)
    {
        if (!sharedChats.TryGetValue(target.Id, out var share)) return;
        _ = ForwardSafely(share, "tool", new { chatId = target.Id, id = callId, name, arguments = arguments.Length > 4000 ? arguments[..4000] + "…" : arguments });
    }

    void ShareStream(Chat target, int messageId, string text, string reasoning)
    {
        if (!sharedChats.TryGetValue(target.Id, out var share)) return;
        _ = ForwardSafely(share, "stream", new { chatId = target.Id, messageId, text, reasoning });
    }

    async Task ForwardSafely(Guid share, string name, object data)
    {
        try { await Bridge.ForwardAsync(share, name, data); }
        catch (Exception ex) when (ex is ServerException or InvalidOperationException) { AppLog.Write(AppLogLevel.Information, "server.share_forward_skipped", ex); }
    }

    async Task<object> ShareSnapshotAsync(int chatId)
    {
        var stored = await ReadStoreAsync(store => (Chat: store.Chats.AsNoTracking().Single(x => x.Id == chatId),
            Messages: store.Messages.AsNoTracking().Where(x => x.ChatId == chatId).OrderBy(x => x.Id).ToList()));
        return ShareContent.Snapshot(stored.Chat.Title, stored.Messages, ServerSync.CurrentAuthor(ServerLink));
    }

    Task OnShareCommentAsync(int chatId, ShareComment comment) =>
        RunOnUiAsync(() => { ShowStatus($"💬 {comment.Author} : {comment.Text}", StatusKind.Notice, chatId); return Task.CompletedTask; });

    async Task<Guid> StartShareAsync(Chat target, bool isPublic = false)
    {
        var share = await Bridge.StartAsync(target.Title, target.Id.ToString(), () => ShareSnapshotAsync(target.Id), comment => OnShareCommentAsync(target.Id, comment), isPublic: isPublic, onViewers: count => OnShareViewersAsync(target.Id, count));
        sharedChats[target.Id] = share.Id;
        Server.Store.Update(l => l.Shares[target.Id] = share.Id.ToString());
        InvalidateServerLink();
        return share.Id;
    }

    async Task StopShareAsync(Chat target)
    {
        if (!sharedChats.Remove(target.Id, out var share)) return;
        Server.Store.Update(l => l.Shares.Remove(target.Id));
        InvalidateServerLink();
        await Bridge.StopAsync(share);
    }

    /// <summary>After a restart, conversations that were being shared attach their bridge again, if the sharing still exists.</summary>
    async Task ResumeSharesAsync()
    {
        try
        {
            var link = Server.Link;
            if (!link.SignedIn || link.Shares.Count == 0) return;
            var live = (await Server.Api.SharesAsync()).Where(x => x.IsHost && x.RevokedUtc == null).Select(x => x.Id).ToHashSet();
            foreach (var (chatId, id) in link.Shares.ToList())
            {
                if (!Guid.TryParse(id, out var share) || !live.Contains(share)) { Server.Store.Update(l => l.Shares.Remove(chatId)); continue; }
                sharedChats[chatId] = share;
                var chat = chatId;
                await Bridge.AttachAsync(share, () => ShareSnapshotAsync(chat), comment => OnShareCommentAsync(chat, comment), count => OnShareViewersAsync(chat, count));
            }
            InvalidateServerLink();
        }
        catch (Exception ex) when (ex is ServerException or InvalidOperationException) { AppLog.Write(AppLogLevel.Information, "server.share_resume_skipped", ex); }
    }

    /// <summary>The dialog of a conversation's sharing: start (public or not), the code to hand out, invite a viewer, stop.</summary>
    async Task ManageShareAsync(Chat target)
    {
        if (!ServerLink.SignedIn) { ShowStatus(WorkflowText("Connectez-vous au serveur dans Réglages › Serveur.", "Sign in to the server in Settings › Server."), StatusKind.Notice); return; }
        if (IsCloudChat(target)) { ShowStatus(WorkflowText("Une conversation cloud est déjà sur le serveur.", "A cloud conversation is already on the server."), StatusKind.Notice); return; }
        var shared = sharedChats.TryGetValue(target.Id, out var shareId);
        ServerShare? details = null;
        if (shared)
            try { details = await Server.Api.ShareAsync(shareId); }
            catch (ServerException ex) { ShowStatus(ex.Message, StatusKind.Error); return; }
        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(Label(shared
            ? WorkflowText("Cette conversation est suivie en direct. Les outils restent sur cet appareil; les spectateurs lisent et commentent, rien de plus. Le serveur ne garde aucun historique quand cet appareil se déconnecte.",
                "This conversation is followed live. Tools stay on this device; viewers can read and comment, nothing more. The server keeps no history when this device disconnects.")
            : WorkflowText("Les personnes autorisées pourront suivre cette conversation en direct. La conversation et ses outils restent sur cet appareil. Un code de partage sera généré : transmettez-le à qui doit la suivre.",
                "Authorized people will be able to follow this conversation live. The conversation and its tools stay on this device. A sharing code will be generated: hand it to whoever should follow it."), 13));
        var message = Label("", 12);
        var isPublic = new ToggleSwitch
        {
            Header = WorkflowText("Visible par tous les comptes du serveur", "Visible to every account on the server"), IsOn = details?.IsPublic ?? false,
            OnContent = WorkflowText("Publique : listée dans l'onglet Cloud de chacun", "Public: listed in everybody's Cloud view"), OffContent = WorkflowText("Privée : code ou invitation requis", "Private: code or invitation required")
        };
        if (shared)
            isPublic.Toggled += async (_, _) =>
            {
                try { await Server.Api.SetSharePublicAsync(shareId, isPublic.IsOn); message.Text = isPublic.IsOn ? WorkflowText("Conversation publique.", "Conversation is public.") : WorkflowText("Conversation privée.", "Conversation is private."); }
                catch (ServerException ex) { message.Text = ex.Message; }
            };
        panel.Children.Add(isPublic);
        if (shared)
        {
            var code = new TextBlock { Text = details?.Code ?? WorkflowText("(code désactivé)", "(code turned off)"), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 26, IsTextSelectionEnabled = true, Foreground = FluentDesign.Primary };
            var copy = new Button { Content = WorkflowText("Copier le code", "Copy code"), IsEnabled = details?.Code != null };
            var renew = new Button { Content = WorkflowText("Nouveau code", "New code") };
            var turnOff = new HyperlinkButton { Content = WorkflowText("Désactiver le code", "Turn the code off"), Padding = new(0) };
            copy.Click += (_, _) =>
            {
                if (code.Text.Length == 0 || details?.Code == null) return;
                var data = new Windows.ApplicationModel.DataTransfer.DataPackage(); data.SetText(details.Code);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(data);
                message.Text = WorkflowText("Code copié.", "Code copied.");
            };
            renew.Click += async (_, _) =>
            {
                try { details = await Server.Api.NewShareCodeAsync(shareId); code.Text = details.Code ?? ""; copy.IsEnabled = true; message.Text = WorkflowText("Nouveau code généré : l'ancien ne fonctionne plus.", "New code generated: the old one no longer works."); }
                catch (ServerException ex) { message.Text = ex.Message; }
            };
            turnOff.Click += async (_, _) =>
            {
                try { details = await Server.Api.DisableShareCodeAsync(shareId); code.Text = WorkflowText("(code désactivé)", "(code turned off)"); copy.IsEnabled = false; message.Text = WorkflowText("Code désactivé : seules les invitations (et la liste publique) donnent accès.", "Code turned off: only invitations (and the public list) give access."); }
                catch (ServerException ex) { message.Text = ex.Message; }
            };
            var codeBox = new StackPanel { Spacing = 6 };
            codeBox.Children.Add(Label(WorkflowText("Code de partage", "Sharing code"), 12));
            codeBox.Children.Add(code);
            var codeActions = Row(copy, renew); codeBox.Children.Add(codeActions); codeBox.Children.Add(turnOff);
            panel.Children.Add(codeBox);
            var email = new TextBox { Header = WorkflowText("Inviter un compte du serveur (adresse e-mail)", "Invite a server account (e-mail address)") };
            var invite = new Button { Content = WorkflowText("Inviter", "Invite"), HorizontalAlignment = HorizontalAlignment.Right };
            invite.Click += async (_, _) =>
            {
                try
                {
                    await Server.Api.InviteAsync(shareId, email.Text.Trim());
                    message.Text = WorkflowText("Invitation envoyée : ", "Invitation sent: ") + email.Text.Trim(); email.Text = "";
                }
                catch (ServerException ex) { message.Text = ex.Message; }
            };
            panel.Children.Add(email); panel.Children.Add(invite);
        }
        panel.Children.Add(message);
        var dialog = new ContentDialog
        {
            XamlRoot = root.XamlRoot, Title = WorkflowText("Partager en direct", "Share live"), Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            PrimaryButtonText = shared ? WorkflowText("Arrêter le partage", "Stop sharing") : WorkflowText("Démarrer le partage", "Start sharing"), CloseButtonText = T("Fermer"), DefaultButton = ContentDialogButton.Primary
        };
        if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
        try
        {
            if (shared) await StopShareAsync(target); else await StartShareAsync(target, isPublic.IsOn);
            ShowStatus(shared ? WorkflowText("Partage arrêté.", "Sharing stopped.") : WorkflowText("Partage en direct démarré.", "Live sharing started."), StatusKind.Notice);
            RefreshSharingIndicator();
        }
        catch (ServerException ex) { ShowStatus(ex.Message, StatusKind.Error); return; }
        // Just started: show the code to hand out right away.
        if (!shared) await ManageShareAsync(target);
    }

    /// <summary>The conversation title carries a marker while it is shared or lives on the server.</summary>
    void RefreshSharingIndicator()
    {
        if (chat == null) return;
        var marker = sharedChats.ContainsKey(chat.Id) ? "⇄ " : IsCloudChat(chat) ? "☁ " : "";
        title.Text = marker + chat.Title;
        RefreshSharingChip();
        ToolTipService.SetToolTip(title, marker.Length == 0 ? chat.Title : marker + chat.Title + " · " + (marker == "☁ " ? WorkflowText("exécution sur le serveur", "runs on the server") : WorkflowText("partagée en direct", "shared live")));
    }
}
