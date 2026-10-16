using Microsoft.EntityFrameworkCore;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using MonolithHarness.Core.Remote;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

/// <summary>
/// Automatic synchronization with the optional server and how it shows: the chip of the sidebar (state of the connection plus a badge
/// when something is wrong) and the dialog behind it (connection information, problems, recent events).
/// </summary>
public sealed partial class MainWindow
{
    ServerSyncService? serverSync;
    Button? serverChip;
    TextBlock? serverChipText;
    Border? serverChipDot;
    ProgressRing? serverChipRing;
    Border? serverChipBadge;
    FontIcon? serverChipBadgeIcon;

    static readonly SolidColorBrush StatusGreen = new(ColorHelper.FromArgb(255, 46, 158, 91));
    static readonly SolidColorBrush StatusAmber = new(ColorHelper.FromArgb(255, 224, 160, 48));
    static readonly SolidColorBrush StatusRed = new(ColorHelper.FromArgb(255, 229, 72, 77));
    static readonly SolidColorBrush StatusBlue = new(ColorHelper.FromArgb(255, 76, 154, 255));
    static readonly SolidColorBrush StatusGrey = new(ColorHelper.FromArgb(255, 128, 128, 128));

    /// <summary>The synchronization service of this window; it only runs once a server is configured (see <see cref="StartServerSync"/>).</summary>
    ServerSyncService Sync => serverSync ??= CreateServerSync();

    ServerSyncService CreateServerSync()
    {
        var service = new ServerSyncService(Server, () => new HarnessDb(HarnessDb.DatabasePath)) { IsChatBusy = IsChatBusyFromAnyThread };
        service.StateChanged += _ => DispatcherQueue.TryEnqueue(() => { RefreshServerChip(); RefreshAnnouncement(); });
        service.Changed += changes => RunOnUiAsync(() => ApplyServerChangesAsync(changes));
        service.RemoteRunProgress += OnRemoteProgressAsync;
        service.RelayQueueChanged += waits => DispatcherQueue.TryEnqueue(() => ShowRelayQueue(waits));
        return service;
    }

    /// <summary>The synchronization runs off the UI thread but must not rewrite a conversation that is being used: ask the UI, and assume "busy" if it cannot answer.</summary>
    bool IsChatBusyFromAnyThread(int chatId)
    {
        if (DispatcherQueue.HasThreadAccess) return conversationRuns.ContainsKey(chatId);
        using var answered = new ManualResetEventSlim();
        var busy = true;
        if (!DispatcherQueue.TryEnqueue(() => { busy = conversationRuns.ContainsKey(chatId); answered.Set(); })) return true;
        return !answered.Wait(TimeSpan.FromSeconds(3)) || busy;
    }

    /// <summary>Starts the background synchronization when a server is configured (at startup, after connecting or signing in).</summary>
    void StartServerSync()
    {
        if (Server.Link.Configured) Sync.Start();
        RefreshServerChip();
        RefreshAnnouncement();
    }

    /// <summary>Starts over after the address or the account changed (sign-in, sign-out, forget).</summary>
    void ServerLinkChanged()
    {
        InvalidateServerLink();
        if (Server.Link.Configured) { Sync.LinkChanged(); Sync.Start(); }
        else serverSync?.LinkChanged();
        RefreshServerChip();
        RefreshAnnouncement();
        RebuildProjectNavigation();
    }

    // ---- What a synchronization changed ----

    /// <summary>
    /// The synchronization wrote to the database through its own context: this window's copies are reloaded, then only what is on
    /// screen is refreshed (the open conversation is left alone unless it changed or disappeared).
    /// </summary>
    async Task ApplyServerChangesAsync(SyncChanges changes)
    {
        try
        {
            InvalidateServerLink();
            foreach (var entry in db.ChangeTracker.Entries().Where(x => x.State == EntityState.Unchanged).ToList())
            {
                var touched = entry.Entity switch
                {
                    Provider => changes.Providers,
                    Project owner => changes.Projects.Contains(owner.Id) || changes.RemovedProjects.Contains(owner.Id),
                    Chat item => changes.Chats.Contains(item.Id) || changes.RemovedChats.Contains(item.Id) || changes.RemovedProjects.Contains(item.ProjectId),
                    _ => false
                };
                if (touched) await entry.ReloadAsync();
            }
            var openProject = state.ProjectId;
            var openChat = chat?.Id;
            var openChatGone = openChat is { } id && changes.RemovedChats.Contains(id);
            var openProjectGone = openProject is { } pid && changes.RemovedProjects.Contains(pid);
            var listChanged = allProjectChats.Any(x => changes.RemovedChats.Contains(x.Id)) || changes.Chats.Count > 0 && await db.Chats.AnyAsync(x => changes.Chats.Contains(x.Id) && x.ProjectId == openProject);

            var list = await db.Projects.OrderBy(x => x.Id).ToListAsync();
            loading = true;
            try { projects.ItemsSource = list; projects.SelectedItem = list.FirstOrDefault(x => x.Id == state.ProjectId) ?? list.FirstOrDefault(); }
            finally { loading = false; }
            if (changes.Providers) await RefreshAfterServerChangeAsync();
            if (openChatGone || openProjectGone || listChanged) { await SelectProject(); return; }
            navigationChats = await ReadStoreAsync(store => store.Chats.AsNoTracking().Select(x => new Chat {
                Id = x.Id, ProjectId = x.ProjectId, Title = x.Title, IsPinned = x.IsPinned, IsFavorite = x.IsFavorite, IsArchived = x.IsArchived, UpdatedUtc = x.UpdatedUtc
            }).ToList());
            RebuildProjectNavigation();
            // A conversation changed elsewhere (another device, another member): show it, unless it is being worked on here.
            if (openChat is { } current && changes.Messages.Contains(current) && !conversationRuns.ContainsKey(current)) await RefreshDisplayedChatAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write(AppLogLevel.Warning, "ui.server_refresh_failed", ex);
        }
    }

    // ---- The chip ----

    /// <summary>Takes the place of the former "Scheduled tasks" button: state of the link and a badge for problems.</summary>
    FrameworkElement BuildServerChip()
    {
        serverChipDot = new Border { Width = 18, Height = 18, Child = FluentDesign.Icon("\uE968", 16), VerticalAlignment = VerticalAlignment.Center };
        serverChipRing = new ProgressRing { Width = 12, Height = 12, IsActive = false, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        serverChipText = new TextBlock { FontSize = 12, Foreground = FluentDesign.Secondary, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        serverChipBadgeIcon = FluentDesign.Icon("", 11);
        serverChipBadgeIcon.Foreground = new SolidColorBrush(Colors.White);
        serverChipBadge = new Border { Width = 18, Height = 18, CornerRadius = new(9), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, Child = serverChipBadgeIcon };
        var indicator = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center };
        indicator.Children.Add(serverChipDot); indicator.Children.Add(serverChipRing);
        serverChipDot.HorizontalAlignment = HorizontalAlignment.Center;
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.Children.Add(indicator);
        var labels = new Grid { ColumnSpacing = 8 };
        labels.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        labels.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var title = Label(WorkflowText("Serveur", "Server"), 13); title.VerticalAlignment = VerticalAlignment.Center;
        labels.Children.Add(title); Grid.SetColumn(serverChipText, 1); labels.Children.Add(serverChipText);
        Grid.SetColumn(labels, 1); content.Children.Add(labels);
        Grid.SetColumn(serverChipBadge, 2); content.Children.Add(serverChipBadge);
        serverChip = Action("", ShowServerStatusAsync);
        serverChip.Content = content; serverChip.Tag = null;
        serverChip.FontSize = 13; serverChip.Padding = new(8, 8, 4, 8); serverChip.MinHeight = 42;
        serverChip.Background = FluentDesign.Resource("TransparentBrush"); serverChip.BorderThickness = new(0); serverChip.CornerRadius = new(6);
        serverChip.HorizontalAlignment = HorizontalAlignment.Stretch; serverChip.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        RefreshServerChip();
        return serverChip;
    }

    string LevelName(SyncLevel level) => level switch
    {
        SyncLevel.Error => WorkflowText("Erreur", "Error"),
        SyncLevel.Warning => WorkflowText("Avertissement", "Warning"),
        _ => "Info"
    };


    static SolidColorBrush LevelBrush(SyncLevel level) => level switch { SyncLevel.Error => StatusRed, SyncLevel.Warning => StatusAmber, _ => StatusBlue };

    static string LevelGlyph(SyncLevel level) => level switch { SyncLevel.Error => "", SyncLevel.Warning => "", _ => "" };

    (string Text, SolidColorBrush Color) ChipState(SyncSnapshot? snapshot)
    {
        var link = Server.Link;
        var current = snapshot?.State ?? (!link.Configured ? LinkState.NotConfigured : link.SignedIn ? LinkState.Connecting : LinkState.SignedOut);
        return current switch
        {
            LinkState.NotConfigured => (WorkflowText("Serveur : aucun", "Server: none"), StatusGrey),
            LinkState.SignedOut => (WorkflowText("Serveur · non connecté", "Server · signed out"), StatusGrey),
            LinkState.Connecting => (WorkflowText("Connexion au serveur…", "Connecting…"), StatusAmber),
            LinkState.Offline => (WorkflowText("Serveur · hors ligne", "Server · offline"), StatusAmber),
            LinkState.SessionLost => (WorkflowText("Serveur · session expirée", "Server · session expired"), StatusRed),
            LinkState.Paused => (WorkflowText("Serveur · désactivé", "Server · off"), StatusGrey),
            _ => (WorkflowText("Serveur · connecté", "Server · connected") + (snapshot?.Realtime == false ? "" : ""), StatusGreen)
        };
    }

    void RefreshServerChip()
    {
        if (serverChip == null || serverChipText == null || serverChipDot == null || serverChipBadge == null || serverChipBadgeIcon == null || serverChipRing == null) return;
        var snapshot = serverSync?.Snapshot;
        var (text, color) = ChipState(snapshot);
        var separator = text.IndexOf('·');
        var caption = separator >= 0 ? text[(separator + 1)..].Trim() : text switch
        {
            "Serveur : aucun" => "Non configuré", "Server: none" => "Not configured",
            "Connexion au serveur…" => "Connexion…", _ => text
        };
        serverChipText.Text = caption.Length == 0 ? caption : char.ToUpperInvariant(caption[0]) + caption[1..];
        if (serverChipDot.Child is FontIcon icon) icon.Foreground = color;
        var syncing = snapshot?.Syncing == true;
        serverChipRing.IsActive = syncing; serverChipRing.Visibility = syncing ? Visibility.Visible : Visibility.Collapsed;
        serverChipDot.Visibility = syncing ? Visibility.Collapsed : Visibility.Visible;
        var worst = snapshot?.Worst;
        serverChipBadge.Visibility = worst == null ? Visibility.Collapsed : Visibility.Visible;
        if (worst is { } level)
        {
            serverChipBadge.Background = LevelBrush(level);
            serverChipBadgeIcon.Glyph = LevelGlyph(level);
        }
        var detail = snapshot == null ? text : $"{text} · {snapshot.Issues.Count} {WorkflowText("point(s) à voir", "item(s) to review")}";
        ToolTipService.SetToolTip(serverChip, detail);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(serverChip, detail);
        RefreshServerModeToggle();
    }

    // ---- The dialog behind the chip ----

    string Ago(DateTime? utc)
    {
        if (utc == null) return WorkflowText("jamais", "never");
        var span = DateTime.UtcNow - utc.Value;
        if (span.TotalSeconds < 45) return WorkflowText("à l'instant", "just now");
        if (span.TotalMinutes < 60) return WorkflowText($"il y a {(int)span.TotalMinutes} min", $"{(int)span.TotalMinutes} min ago");
        if (span.TotalHours < 24) return WorkflowText($"il y a {(int)span.TotalHours} h", $"{(int)span.TotalHours} h ago");
        return utc.Value.ToLocalTime().ToString("g");
    }

    string IntervalLabel(int minutes) => minutes switch
    {
        0 => WorkflowText("Jamais", "Never"),
        1 => WorkflowText("1 minute", "1 minute"),
        5 => WorkflowText("5 minutes (par défaut)", "5 minutes (default)"),
        60 => WorkflowText("1 heure", "1 hour"),
        1440 => WorkflowText("1 jour", "1 day"),
        _ => minutes + " min"
    };

    /// <summary>Connection information and everything that went wrong, as plain sentences.</summary>
    Task ShowServerStatusAsync() => ShowServerStatusAsync(default);

    async Task ShowServerStatusAsync(CancellationToken ct)
    {
        var link = Server.Link;
        var body = new StackPanel { Spacing = 12, MinWidth = 440, MaxWidth = 620 };
        void Fill()
        {
            body.Children.Clear();
            var snapshot = serverSync?.Snapshot;
            var (state, color) = ChipState(snapshot);
            if (!Server.Link.Configured)
            {
                body.Children.Add(Label(WorkflowText("Aucun serveur n'est configuré : l'application est entièrement locale. Un serveur Monolith Harness apporte des providers partagés, des projets cloud et le partage de conversations.",
                    "No server is configured: the application is fully local. A Monolith Harness Server brings shared providers, cloud projects and conversation sharing."), 13));
                return;
            }
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            heading.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new(5), Background = color, VerticalAlignment = VerticalAlignment.Center });
            heading.Children.Add(Label(state, 16));
            body.Children.Add(heading);
            if (Server.Link.SignedIn)
            {
                var mode = new ToggleSwitch { IsOn = Server.Link.Enabled, OnContent = WorkflowText("Mode serveur activé", "Server mode on"), OffContent = WorkflowText("Mode serveur désactivé", "Server mode off") };
                mode.Toggled += async (_, _) => { if (mode.IsOn != Server.Link.Enabled) await Guard(() => SetServerModeAsync(mode.IsOn)); Fill(); };
                body.Children.Add(mode);
            }
            if (snapshot?.Announcement is { } note)
            {
                var message = new Border { Background = note.Level switch { "error" => AnnouncementError, "warning" => AnnouncementWarning, _ => AnnouncementInfo }, CornerRadius = new(6), Padding = new(12, 8, 12, 8) };
                message.Child = new TextBlock { Text = note.Text, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Colors.White), FontSize = 13, IsTextSelectionEnabled = true };
                body.Children.Add(message);
            }
            var info = new StackPanel { Spacing = 2 };
            var current = Server.Link;
            info.Children.Add(Label($"{(snapshot?.ServerName is { Length: > 0 } name ? name : current.ServerName)} {(snapshot?.ServerVersion is { Length: > 0 } version ? version : current.ServerVersion)}".Trim(), 13));
            info.Children.Add(Label(current.Url, 12));
            if (current.Email.Length > 0) info.Children.Add(Label(WorkflowText("Compte : ", "Account: ") + current.Email, 12));
            if (snapshot != null)
            {
                info.Children.Add(Label(WorkflowText("Temps réel : ", "Real time: ") + (snapshot.Realtime ? WorkflowText("actif", "active") : WorkflowText("inactif", "inactive")), 12));
                info.Children.Add(Label(WorkflowText("Dernière synchronisation : ", "Last synchronization: ") + Ago(snapshot.LastSyncUtc), 12));
                info.Children.Add(Label(WorkflowText("Resynchronisation complète : ", "Full resync: ") + IntervalLabel(snapshot.SyncMinutes)
                    + (snapshot.NextSyncUtc is { } next ? WorkflowText(" · prochaine ", " · next ") + next.ToLocalTime().ToString("t") : ""), 12));
                info.Children.Add(Label(WorkflowText($"{snapshot.Providers} provider(s) · {snapshot.Projects} projet(s) cloud · {snapshot.SharedWithMe} conversation(s) partagée(s) avec vous · {snapshot.PublicShares} publique(s)",
                    $"{snapshot.Providers} provider(s) · {snapshot.Projects} cloud project(s) · {snapshot.SharedWithMe} conversation(s) shared with you · {snapshot.PublicShares} public"), 12));
            }
            foreach (var line in info.Children.OfType<TextBlock>()) line.Foreground = FluentDesign.Secondary;
            ((TextBlock)info.Children[0]).Foreground = FluentDesign.Primary;
            body.Children.Add(info);

            body.Children.Add(Label(WorkflowText("Problèmes", "Problems"), 14));
            if (snapshot == null || snapshot.Issues.Count == 0)
                body.Children.Add(Label(WorkflowText("Aucun problème : tout est synchronisé.", "No problems: everything is in sync."), 12));
            else
                foreach (var issue in snapshot.Issues)
                {
                    var row = new Grid { ColumnSpacing = 10 };
                    row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                    var icon = FluentDesign.Icon(LevelGlyph(issue.Level), 14); icon.Foreground = LevelBrush(issue.Level); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new(0, 2, 0, 0);
                    row.Children.Add(icon);
                    var text = new StackPanel { Spacing = 1 };
                    text.Children.Add(Label($"{LevelName(issue.Level)} · {issue.Area}", 12));
                    text.Children.Add(Label(issue.Message, 12));
                    var since = Label(WorkflowText("depuis ", "since ") + Ago(issue.SinceUtc) + (issue.Count > 1 ? $" · ×{issue.Count}" : ""), 11);
                    since.Foreground = FluentDesign.Secondary; text.Children.Add(since);
                    Grid.SetColumn(text, 1); row.Children.Add(text);
                    body.Children.Add(row);
                }
            if (snapshot is { Log.Count: > 0 })
            {
                var log = new StackPanel { Spacing = 3 };
                foreach (var entry in snapshot.Log.Take(15))
                {
                    var line = Label($"{entry.TimeUtc.ToLocalTime():t} · {entry.Area} · {entry.Message}", 11);
                    line.Foreground = FluentDesign.Secondary; log.Children.Add(line);
                }
                body.Children.Add(new Expander { Header = WorkflowText("Événements récents", "Recent events"), Content = log, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            }
        }
        Fill();
        var configured = link.Configured;
        var clear = new HyperlinkButton { Content = WorkflowText("Effacer la liste des problèmes", "Clear the problem list"), Padding = new(0), FontSize = 12 };
        clear.Click += (_, _) => { serverSync?.ClearIssues(); Fill(); };
        if (configured) body.Children.Add(clear);
        var dialog = new ContentDialog
        {
            XamlRoot = root.XamlRoot, Title = WorkflowText("Connexion au serveur", "Server connection"), Content = new ScrollViewer { Content = body, MaxHeight = 520 },
            PrimaryButtonText = configured ? WorkflowText("Synchroniser maintenant", "Sync now") : WorkflowText("Configurer un serveur…", "Set up a server…"),
            SecondaryButtonText = configured ? WorkflowText("Réglages du serveur", "Server settings") : "",
            CloseButtonText = T("Fermer"), DefaultButton = ContentDialogButton.Primary
        };
        void Repaint(SyncSnapshot _) => DispatcherQueue.TryEnqueue(Fill);
        if (serverSync != null) serverSync.StateChanged += Repaint;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            if (!configured) return;
            args.Cancel = true;
            try { await Sync.SyncNowAsync(); }
            catch (Exception ex) when (ex is ServerException or InvalidOperationException or TaskCanceledException) { Sync.ReportIssue("manual", SyncLevel.Warning, WorkflowText("Synchronisation", "Synchronization"), ex.Message); }
            Fill();
        };
        try
        {
            var result = await ShowDialogAsync(dialog, ct);
            if (result == ContentDialogResult.Primary && !configured || result == ContentDialogResult.Secondary) await OpenServerSettingsAsync();
        }
        finally { if (serverSync != null) serverSync.StateChanged -= Repaint; }
    }
}
