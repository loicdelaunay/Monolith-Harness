using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using MonolithHarness.Core.Remote;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

/// <summary>
/// With a server configured, the sidebar has two views: Local (the projects of this device) and Cloud (the projects that live on the
/// server and the conversations shared with this account, public ones, or joined with a code). Everything in the Cloud view arrives
/// by itself through the synchronization.
/// </summary>
public sealed partial class MainWindow
{
    readonly Dictionary<Guid, Window> sharedWindows = [];

    /// <summary>The Cloud view is showing (only possible when a server is configured).</summary>
    bool CloudView => ServerLink is { Configured: true, Enabled: true, CloudView: true };

    /// <summary>A project belongs to the view being shown: cloud projects in Cloud, everything else in Local (all projects without a server; with the server mode off only the Local view exists).</summary>
    bool InSidebarView(int projectId) => !ServerLink.Configured || IsCloudProject(projectId) == CloudView;

    FrameworkElement BuildViewToggle()
    {
        var cloud = ServerLink.CloudView;
        var grid = new Grid { ColumnSpacing = 4, Margin = new(8, 2, 8, 10) };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        ToggleButton Segment(string glyph, string text, bool selected, bool target)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(FluentDesign.Icon(glyph, 13));
            content.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var button = new ToggleButton { Content = content, IsChecked = selected, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new(8, 6, 8, 6) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
            button.Click += (_, _) => SetSidebarView(target);
            return button;
        }
        var local = Segment("", WorkflowText("Local", "Local"), !cloud, false);
        var remote = Segment("", "Cloud", cloud, true);
        Grid.SetColumn(remote, 1);
        grid.Children.Add(local); grid.Children.Add(remote);
        return grid;
    }

    void SetSidebarView(bool cloud)
    {
        Server.Store.Update(l => l.CloudView = cloud);
        InvalidateServerLink();
        RebuildProjectNavigation();
    }

    // ---- Cloud view ----

    /// <summary>The part of the Cloud view that follows the projects: shared conversations, and what to do when not signed in.</summary>
    void AddCloudSections(StackPanel navigation)
    {
        if (!ServerLink.SignedIn)
        {
            var notice = new StackPanel { Spacing = 8, Margin = new(8, 10, 8, 0) };
            notice.Children.Add(Label(WorkflowText("Connectez-vous au serveur pour voir vos projets cloud et les conversations partagées.", "Sign in to the server to see your cloud projects and shared conversations."), 12));
            var signIn = Action(WorkflowText("Se connecter…", "Sign in…"), OpenServerSettingsAsync);
            signIn.HorizontalAlignment = HorizontalAlignment.Left; signIn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            notice.Children.Add(signIn);
            navigation.Children.Add(notice);
            return;
        }
        if (!(projects.ItemsSource as IEnumerable<Project> ?? []).Any(x => IsCloudProject(x.Id)))
        {
            var empty = Label(WorkflowText("Aucun projet cloud pour le moment. Créez-en un avec le bouton + : il apparaît ici, sur vos autres appareils et chez les membres que vous invitez.", "No cloud project yet. Create one with the + button: it shows up here, on your other devices and for the members you invite."), 12);
            empty.Foreground = FluentDesign.Secondary; empty.Margin = new(8, 10, 8, 0);
            navigation.Children.Add(empty);
        }
        var heading = new Grid { Margin = new(8, 22, 0, 6) };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = Label(WorkflowText("Conversations partagées", "Shared conversations"), 12); label.Foreground = FluentDesign.Secondary; label.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(label);
        var join = Action("", JoinSharedConversationAsync);
        FluentDesign.IconButton(join, "", WorkflowText("Rejoindre avec un code", "Join with a code"), false);
        join.Width = join.Height = 30; join.Padding = new(0); join.BorderThickness = new(0); join.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Grid.SetColumn(join, 1); heading.Children.Add(join);
        navigation.Children.Add(heading);
        var mine = Sync.SharedWithMe;
        var open = Sync.PublicShares;
        foreach (var share in mine) navigation.Children.Add(BuildSharedRow(share, joined: true));
        if (open.Count > 0)
        {
            var publicLabel = Label(WorkflowText("Publiques", "Public"), 11); publicLabel.Foreground = FluentDesign.Secondary; publicLabel.Margin = new(8, 10, 0, 4);
            navigation.Children.Add(publicLabel);
            foreach (var share in open) navigation.Children.Add(BuildSharedRow(share, joined: false));
        }
        if (mine.Count == 0 && open.Count == 0)
        {
            var none = Label(WorkflowText("Personne ne partage de conversation avec vous. Entrez un code de partage avec le bouton à droite du titre.", "Nobody shares a conversation with you. Enter a sharing code with the button next to the title."), 12);
            none.Foreground = FluentDesign.Secondary; none.Margin = new(8, 0, 8, 0);
            navigation.Children.Add(none);
        }
    }

    FrameworkElement BuildSharedRow(ServerShare share, bool joined)
    {
        var open = Action("", () => OpenSharedConversationAsync(share));
        open.HorizontalAlignment = HorizontalAlignment.Stretch; open.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        open.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); open.BorderThickness = new(0); open.Padding = new(8, 6, 8, 6);
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var dot = new Border { Width = 8, Height = 8, CornerRadius = new(4), Background = share.Live ? StatusGreen : StatusGrey, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(dot, share.Live ? WorkflowText("En direct", "Live") : WorkflowText("Appareil de l'hôte hors ligne", "Host device offline"));
        row.Children.Add(dot);
        var text = new StackPanel { Spacing = 0 };
        text.Children.Add(new TextBlock { Text = share.Title, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = FluentDesign.Primary });
        text.Children.Add(new TextBlock { Text = share.HostEmail, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = FluentDesign.Secondary });
        Grid.SetColumn(text, 1); row.Children.Add(text);
        open.Content = row; open.Tag = null;
        ToolTipService.SetToolTip(open, $"{share.Title} · {share.HostEmail}");
        if (joined)
            open.RightTapped += (_, e) =>
            {
                e.Handled = true;
                var menu = new MenuFlyout();
                var leave = new MenuFlyoutItem { Text = WorkflowText("Quitter la conversation", "Leave the conversation") };
                leave.Click += async (_, _) => await Guard(async () => { await Server.Api.LeaveShareAsync(share.Id); await Sync.SyncNowAsync(); });
                menu.Items.Add(leave); menu.ShowAt(open);
            };
        return open;
    }

    /// <summary>"+" in the Cloud view: a project that lives on the server (its requests run there).</summary>
    async Task NewCloudProjectAsync()
    {
        if (!ServerLink.SignedIn) { await OpenServerSettingsAsync(); return; }
        var name = await AskName(WorkflowText("Nouveau projet cloud", "New cloud project"), "");
        if (name == null) return;
        Project created;
        try { created = await ServerSync.CreateProjectAsync(db, Server, name); }
        catch (ServerException ex) { ShowStatus(ex.Message, StatusKind.Error); return; }
        if (!await db.Chats.AnyAsync(x => x.ProjectId == created.Id)) db.Chats.Add(new Chat { ProjectId = created.Id, Title = T("Nouvelle conversation") });
        await db.SaveChangesAsync();
        InvalidateServerLink();
        var list = await db.Projects.OrderBy(x => x.Id).ToListAsync();
        loading = true; projects.ItemsSource = list; projects.SelectedItem = list.First(x => x.Id == created.Id); loading = false;
        expandedSidebarProjects.Add(created.Id); collapsedSidebarProjects.Remove(created.Id);
        await SelectProject();
        _ = Sync.SyncNowAsync();
    }

    // ---- Joining and following a shared conversation ----

    /// <summary>The sharing code a host gave: enter it to follow the conversation.</summary>
    async Task JoinSharedConversationAsync()
    {
        var code = new TextBox { PlaceholderText = "XXXXX-XXXXX", MaxLength = 20, FontFamily = new FontFamily("Consolas"), FontSize = 18, MinWidth = 300, AcceptsReturn = false };
        var message = Label("", 12); message.Foreground = StatusRed;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Label(WorkflowText("Entrez le code de partage que l'hôte vous a transmis pour suivre sa conversation en direct. Vous pourrez la lire et la commenter.", "Enter the sharing code the host gave you to follow their conversation live. You can read it and comment on it."), 13));
        panel.Children.Add(code); panel.Children.Add(message);
        var dialog = new ContentDialog
        {
            XamlRoot = root.XamlRoot, Title = WorkflowText("Rejoindre avec un code", "Join with a code"), Content = panel,
            PrimaryButtonText = WorkflowText("Rejoindre", "Join"), CloseButtonText = T("Annuler"), DefaultButton = ContentDialogButton.Primary
        };
        ServerShare? joined = null;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            if (string.IsNullOrWhiteSpace(code.Text)) return;
            var deferral = args.GetDeferral();
            try
            {
                joined = await Server.Api.JoinShareAsync(code.Text.Trim());
                dialog.Hide();
            }
            catch (ServerException ex)
            {
                message.Text = ex.Code switch
                {
                    "invalid_share_code" => WorkflowText("Ce code est invalide ou n'est plus actif.", "This code is invalid or no longer active."),
                    "too_many_attempts" or "http_429" => WorkflowText("Trop d'essais : patientez quelques minutes.", "Too many attempts: wait a few minutes."),
                    "too_many_viewers" => WorkflowText("Cette conversation a atteint son nombre maximal de spectateurs.", "This conversation reached its maximum number of viewers."),
                    _ => ex.Message
                };
            }
            finally { deferral.Complete(); }
        };
        await ShowDialogAsync(dialog);
        if (joined == null) return;
        _ = Sync.SyncNowAsync();
        await OpenSharedConversationAsync(joined);
    }

    /// <summary>History actions (fork, resume, search anchors) are bound to the local database: never offered on a conversation that is not in it.</summary>
    bool suppressHistoryActions;

    /// <summary>
    /// A conversation shared by somebody else, followed live in its own window and drawn like the host draws it: user bubbles with the
    /// sender's avatar, reasoning, tool cards, timings, the text as it is written (about twice a second) and the tools that are running.
    /// A field lets the viewer comment. Nothing is stored on this device; the server keeps no history either.
    /// </summary>
    async Task OpenSharedConversationAsync(ServerShare share)
    {
        if (sharedWindows.TryGetValue(share.Id, out var existing)) { existing.Activate(); return; }
        ShareViewer viewer;
        try { viewer = await ShareViewer.OpenAsync(Server, share); }
        catch (ServerException ex) { ShowStatus(ex.Message, StatusKind.Error); return; }
        var window = new Window { Title = DisplayApplicationName + " · " + share.Title };
        sharedWindows[share.Id] = window;

        var title = Label(share.Title, 20);
        var host = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        host.Children.Add(BuildAvatar(new ServerAuthor(Guid.Empty, share.HostEmail, ""), 20));
        var hostLabel = Label(WorkflowText("Partagée par ", "Shared by ") + share.HostEmail, 12); hostLabel.Foreground = FluentDesign.Secondary; hostLabel.VerticalAlignment = VerticalAlignment.Center;
        host.Children.Add(hostLabel);
        var state = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var stateDot = new Border { Width = 9, Height = 9, CornerRadius = new(5), VerticalAlignment = VerticalAlignment.Center };
        var viewersText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = FluentDesign.Secondary };
        var stateRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        stateRow.Children.Add(stateDot); stateRow.Children.Add(state);
        stateRow.Children.Add(FluentDesign.Icon("", 12)); stateRow.Children.Add(viewersText);
        var banner = Label("", 13); banner.Foreground = StatusAmber; banner.Visibility = Visibility.Collapsed;
        var header = new StackPanel { Spacing = 4, Padding = new(24, 18, 24, 8) };
        header.Children.Add(title); header.Children.Add(host); header.Children.Add(stateRow); header.Children.Add(banner);

        var thread = new StackPanel { Spacing = 10, Padding = new(24, 8, 24, 8), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Stretch };
        var comments = new StackPanel { Spacing = 6, Padding = new(24, 4, 24, 0), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Stretch };
        var scroller = new ScrollViewer { Content = new StackPanel { Children = { thread, comments } }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var empty = Label("", 13); empty.Foreground = FluentDesign.Secondary; empty.Margin = new(24, 8, 24, 0);
        thread.Children.Add(empty);

        var input = new TextBox { PlaceholderText = WorkflowText("Votre commentaire (visible par tous et par l'hôte)", "Your comment (visible to everybody and to the host)"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = ShareCommentLimit, MinHeight = 44, MaxHeight = 120 };
        var feedback = Label("", 12); feedback.Foreground = StatusRed; feedback.VerticalAlignment = VerticalAlignment.Center;
        var send = new Button { Content = WorkflowText("Envoyer", "Send"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var leave = new HyperlinkButton { Content = WorkflowText("Quitter la conversation", "Leave the conversation"), Visibility = share.Joined ? Visibility.Visible : Visibility.Collapsed };
        var actions = new Grid { ColumnSpacing = 12 };
        actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        actions.Children.Add(leave);
        Grid.SetColumn(feedback, 1); actions.Children.Add(feedback);
        Grid.SetColumn(send, 2); actions.Children.Add(send);
        var footer = new StackPanel { Spacing = 8, Padding = new(24, 8, 24, 18) };
        footer.Children.Add(input); footer.Children.Add(actions);

        var layout = new Grid { RequestedTheme = root.RequestedTheme, Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush") };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.Children.Add(header);
        Grid.SetRow(scroller, 1); layout.Children.Add(scroller);
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        FluentDesign.WindowChrome(window);
        ApplyBrandingIcon(window);
        window.Content = layout; window.AppWindow.Resize(new() { Width = 980, Height = 860 });
        ObserveTextZoom(layout);

        // The final messages are drawn together whenever they change (a message completing replaces what was live); between two such
        // changes only the text being written is updated in place, and a tool that starts gets its card once.
        var shown = new List<(string Key, int Content, int Reasoning, string State, double Seconds, int Calls)>();
        var placeholders = new HashSet<string>();
        AssistantMessageUi? liveBubble = null;
        string? liveKey = null;
        var shownComments = 0;
        void Paint()
        {
            var items = viewer.Messages;
            var finals = items.Where(x => !x.Streaming).ToList();
            var signature = finals.Select(x => (x.Key, x.Content.Length, x.Reasoning.Length, x.State, x.Seconds, x.ToolCalls?.Count ?? 0)).ToList();
            if (!shown.SequenceEqual(signature))
            {
                ClearMessagePanel(thread); thread.Children.Add(empty);
                liveBubble = null; liveKey = null; placeholders.Clear();
                var transcript = finals.Select((message, i) => message.ToMessage(0, i)).ToList();
                suppressHistoryActions = true;
                try { RenderHistory(transcript, thread, null); }
                finally { suppressHistoryActions = false; }
                shown.Clear(); shown.AddRange(signature);
            }
            foreach (var tool in viewer.RunningTools)
                if (placeholders.Add(tool.Id)) AddToolMessage(tool.Name, tool.Arguments, target: thread);
            if (items.LastOrDefault(x => x.Streaming) is { } writing)
            {
                if (liveBubble == null || liveKey != writing.Key) { liveBubble = AddAssistantMessage("", target: thread, sourceProject: null); liveKey = writing.Key; }
                if (writing.Reasoning.Length > 0) liveBubble.UpdateThinking(writing.Reasoning, isComplete: writing.Content.Length > 0, streaming: true);
                liveBubble.UpdateContent(writing.Content, streaming: true);
            }
            empty.Text = items.Count == 0
                ? viewer.Online ? WorkflowText("En attente du contenu de l'hôte…", "Waiting for the host's content…")
                    : WorkflowText("Le contenu n'est disponible que lorsque l'appareil de l'hôte est connecté : le serveur n'en garde aucun historique.", "Content is only available while the host's device is connected: the server keeps no history.")
                : "";
            empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var list = viewer.Comments;
            for (; shownComments < list.Count; shownComments++)
            {
                var remark = Label($"💬 {list[shownComments].Author} · {list[shownComments].TimeUtc.ToLocalTime():t}\n{list[shownComments].Text}", 12);
                remark.Foreground = FluentDesign.Secondary; remark.IsTextSelectionEnabled = true;
                comments.Children.Add(remark);
            }
            title.Text = viewer.Title;
            window.Title = DisplayApplicationName + " · " + viewer.Title;
            stateDot.Background = viewer.ClosedReason != null ? StatusRed : viewer.Online ? StatusGreen : StatusGrey;
            state.Text = viewer.ClosedReason ?? (viewer.Online ? WorkflowText("En direct", "Live") : WorkflowText("Appareil de l'hôte hors ligne", "Host device offline"));
            state.Foreground = FluentDesign.Secondary;
            viewersText.Text = viewer.Viewers == 1 ? WorkflowText("1 spectateur", "1 viewer") : WorkflowText($"{viewer.Viewers} spectateurs", $"{viewer.Viewers} viewers");
            banner.Visibility = viewer.ClosedReason != null ? Visibility.Visible : Visibility.Collapsed;
            banner.Text = viewer.ClosedReason ?? "";
            send.IsEnabled = input.IsEnabled = viewer.ClosedReason == null;
            if (items.Count > 0 && scroller.VerticalOffset >= scroller.ScrollableHeight - 160) scroller.ChangeView(null, scroller.ScrollableHeight + 4000, null);
        }
        Action changed = () => DispatcherQueue.TryEnqueue(Paint);
        viewer.Changed += changed;
        send.Click += async (_, _) =>
        {
            var text = input.Text.Trim();
            if (text.Length == 0) return;
            feedback.Text = "";
            try { await viewer.CommentAsync(text); input.Text = ""; }
            catch (ServerException ex) { feedback.Text = ex.Code == "host_offline" ? WorkflowText("L'appareil de l'hôte est hors ligne.", "The host device is offline.") : ex.Message; }
        };
        leave.Click += async (_, _) =>
        {
            try { await Server.Api.LeaveShareAsync(share.Id); _ = Sync.SyncNowAsync(); window.Close(); }
            catch (ServerException ex) { feedback.Text = ex.Message; }
        };
        window.Closed += (_, _) => { viewer.Changed -= changed; viewer.Dispose(); sharedWindows.Remove(share.Id); };
        window.Activate();
        Paint();
    }

    const int ShareCommentLimit = 2000;
}
