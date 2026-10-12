using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using MonolithHarness.Core.Remote;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Smoke test of the server integration against a running server (MONOLITHHARNESS_SERVER_URL / _EMAIL / _PASSWORD):
    /// settings page, shared providers, a cloud project whose request runs on the server, live sharing, sign-out.
    /// </summary>
    async Task SmokeServerIntegration(string output)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed.Add(name); }
        static async Task Wait(Func<bool> condition, int seconds, string what)
        {
            for (var attempt = 0; attempt < seconds * 20; attempt++) { if (condition()) return; await Task.Delay(50); }
            throw new TimeoutException(what);
        }
        static IEnumerable<FrameworkElement> Descendants(DependencyObject? parent)
        {
            if (parent == null) yield break;
            if (parent is FrameworkElement element) yield return element;
            // A hidden window may not have applied control templates: also follow the content assigned to buttons.
            if (parent is ContentControl { Content: DependencyObject content })
                foreach (var child in Descendants(content)) yield return child;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
        static Task ForwardSharedMessage(ShareBridge bridge, Guid share, Message message, ServerAuthor author) => bridge.ForwardAsync(share, "message", new { chatId = message.ChatId, message = ShareContent.Of(message, author) });
        string Setting(string name) => Environment.GetEnvironmentVariable("MONOLITHHARNESS_SERVER_" + name) ?? throw new InvalidOperationException("Missing MONOLITHHARNESS_SERVER_" + name);
        UiText.Language = state.Language = "fr";
        var features = FeatureSettings.Read(state.FeaturesJson); features.GuiUpdateMode = AutomaticUpdateMode.Disabled;
        state.FeaturesJson = features.Json(); ConfigureAutomaticUpdates();

        // Without a server the application is local and the settings page builds.
        Check(!ServerLink.Configured && !ServerLink.SignedIn, "A fresh profile has no server link and stays local.");
        RefreshServerChip();
        Check(serverChipText?.Text == "Serveur : aucun" && serverChipBadge?.Visibility == Visibility.Collapsed, "Without a server the chip says so, without a badge.");
        RebuildProjectNavigation();
        Check(!Descendants(projectNavigation).OfType<ToggleButton>().Any(), "Without a server there is no Local/Cloud toggle.");
        var toolsMenu = Descendants(root).OfType<DropDownButton>().Single(b => b.Content?.ToString() == "Outils");
        Check(toolsMenu.Flyout is MenuFlyout tools && tools.Items.OfType<MenuFlyoutItem>().Any(x => x.Text == "Tâches planifiées"), "Scheduled tasks moved into the Tools menu.");
        var settingsPage = BuildServerSettings(this);
        Check(settingsPage.Children.Count > 5, "The Server settings page builds without a server.");
        var intervalPicker = Descendants(settingsPage).OfType<ComboBox>().First(x => x.Items.Count == ServerSyncService.IntervalChoices.Length);
        Check(intervalPicker.SelectedIndex == 2 && intervalPicker.Items.OfType<string>().SequenceEqual(["Jamais", "1 minute", "5 minutes (par défaut)", "1 heure", "1 jour"]), "The resynchronization picker offers Never / 1 min / 5 min (default) / 1 h / 1 day.");

        // Connection, account, shared providers.
        await Server.ConnectAsync(Setting("URL"), false);
        await Server.LoginAsync(Setting("EMAIL"), Setting("PASSWORD"), "GUI smoke");
        InvalidateServerLink();
        var synced = await ServerSync.SyncProvidersAsync(db, Server);
        await RefreshAfterServerChangeAsync();
        ServerLinkChanged();
        await Wait(() => Sync.Snapshot.State == LinkState.Online && Sync.Snapshot.LastSyncUtc != null, 30, "The automatic synchronization did not complete.");
        RefreshServerChip();
        Check(serverChipText?.Text == "Serveur · connecté", "Once synchronized the chip shows the connection.");
        Check(ServerLink.SignedIn && synced.Count >= 1 && db.Providers.Local.Any(x => x.Name.EndsWith(ServerSync.ServerProviderSuffix)), "Signing in synchronizes the shared providers as local providers.");
        Check(!File.ReadAllText(ServerLinkStore.PathFor(HarnessDb.DatabasePath)).Contains("mhs_"), "The session token is protected on disk.");
        var shared = db.Providers.Local.First(x => x.Name.EndsWith(ServerSync.ServerProviderSuffix));

        // A cloud project: the request runs on the server and the answer comes back in the conversation.
        var created = await ServerSync.CreateProjectAsync(db, Server, "GUI smoke cloud");
        await db.SaveChangesAsync();
        await RefreshAfterServerChangeAsync();
        var list = await db.Projects.OrderBy(x => x.Id).ToListAsync();
        projects.ItemsSource = list; projects.SelectedItem = list.First(x => x.Id == created.Id);
        await SelectProject();
        Check(project?.Id == created.Id && IsCloudProject(created.Id) && chat != null && IsCloudChat(chat), "The cloud project is listed and selected as a cloud project.");
        provider = shared; providers.SelectedItem = shared; state.ProviderId = shared.Id; UpdateProvider();
        composer.Text = "Bonjour depuis le GUI";
        var cloudChat = chat!.Id;
        await SendAsync();
        await Wait(() => !conversationRuns.ContainsKey(cloudChat), 120, "The cloud request did not finish.");
        var stored = await ReadStoreAsync(store => store.Messages.AsNoTracking().Where(x => x.ChatId == cloudChat).OrderBy(x => x.Id).ToList());
        Check(stored.Any(x => x.Role == "user" && x.Content == "Bonjour depuis le GUI") && stored.Any(x => x.Role == "assistant" && x.Content.Length > 0 && x.State == "complete"),
            "The request ran on the server: its answer is in the local conversation (mirror of the server).");
        Check(stored.Count(x => x.Role == "user") == 1 && stored.All(x => ServerSync.CloudId(x) != null), "The conversation mirror has no duplicate and every message is linked to the server copy.");
        var serverMessages = await Server.Api.MessagesAsync(Guid.Parse(ServerLink.Chats[cloudChat].ServerProjectId), ServerLink.Chats[cloudChat].ServerChatId);
        Check(serverMessages.Any(x => x.Role == "assistant" && x.Content == stored.Last(m => m.Role == "assistant").Content), "The server holds the same conversation.");

        // Who wrote what: user bubbles of a shared project carry the sender's avatar.
        root.UpdateLayout(); await Task.Delay(300);
        Check(Descendants(messages).Any(x => x.Tag as string == "author"), "A user bubble in a cloud conversation shows who sent it.");
        Check(serverMessages.Any(x => x.Role == "user" && x.AuthorEmail == Setting("EMAIL")), "The server recorded the sender of the message.");

        // A rename made here reaches the server (and so everybody else).
        var renamedTitle = "Renommée dans le GUI " + Guid.NewGuid().ToString("N")[..4];
        var serverProject = Guid.Parse(ServerLink.Chats[cloudChat].ServerProjectId);
        chat!.Title = renamedTitle; await db.SaveChangesAsync(); OnChatTitleChanged(cloudChat, renamedTitle);
        var pushed = false;
        for (var waited = 0; waited < 30000 && !pushed; waited += 300) { pushed = (await Server.Api.ChatsAsync(serverProject)).Any(x => x.Title == renamedTitle); if (!pushed) await Task.Delay(300); }
        Check(pushed, "Renaming a cloud conversation here renames it on the server.");

        // Another member's request in this conversation is followed live.
        var otherLink = new ServerLinkStore(Path.Combine(output, "other-link.json"));
        await using var other = new ServerConnection(otherLink, KeyVault.Encrypt, KeyVault.Decrypt);
        await other.ConnectAsync(Setting("URL"), false);
        await other.LoginAsync(Setting("EMAIL2"), Setting("PASSWORD2"), "other member");
        await Server.Api.SetMembersAsync(serverProject, [(Setting("EMAIL2"), "run")]);
        var otherProvider = (await other.Api.ProvidersAsync()).First();
        var followed = new List<RemoteProgress>();
        Sync.RemoteRunProgress += step => { lock (followed) followed.Add(step); return Task.CompletedTask; };
        await Wait(() => Sync.Snapshot.Projects >= 1, 10, "The project is not synchronized.");
        await Task.Delay(1500);
        await other.Api.SendRequestAsync(serverProject, ServerLink.Chats[cloudChat].ServerChatId, "Message de l'autre membre", otherProvider.Id, otherProvider.Models[0].Id);
        await Wait(() => { lock (followed) return followed.Any(x => x.Finished); }, 90, "The other member's request was not followed to its end.");
        await Task.Delay(800);
        root.UpdateLayout();
        lock (followed) Check(followed.Any(x => x.State == "running" && !x.Finished) && followed.Last().Finished, "A request sent by another member is followed while it runs.");
        Check(followBubble == null && followedChats.Count == 0, "Once it is over no live bubble is left.");
        Check(Descendants(messages).OfType<TextBlock>().Count(t => t.Text.Contains("Message de l'autre membre")) >= 1, "The other member's message and the answer appear without reloading the conversation.");
        Check(Descendants(messages).Count(x => x.Tag as string == "author") >= 2, "Each user bubble shows its own sender.");
        await Capture(root, Path.Combine(output, "server-cloud-authors.png"));

        // The sidebar offers Local and Cloud once a server is configured; cloud projects follow the server by themselves.
        RebuildProjectNavigation();
        root.UpdateLayout(); await Task.Delay(300);
        await Capture(root, Path.Combine(output, "server-local-view.png"));
        var segments = Descendants(projectNavigation).OfType<ToggleButton>().ToList();
        Check(segments.Count == 2 && segments[0].IsChecked == true && segments[1].IsChecked == false, "A Local/Cloud toggle shows above the projects, Local first.");
        Check(!Descendants(projectNavigation).OfType<TextBlock>().Any(t => t.Text == created.Name), "The Local view hides cloud projects.");
        SetSidebarView(true);
        Check(CloudView && Descendants(projectNavigation).OfType<ToggleButton>().ElementAt(1).IsChecked == true && Descendants(projectNavigation).OfType<TextBlock>().Any(t => t.Text == created.Name), "The Cloud view lists the cloud projects.");
        var autoName = "GUI auto " + Guid.NewGuid().ToString("N")[..6];
        var autoProject = await Server.Api.CreateProjectAsync(autoName);
        await Wait(() => (projects.ItemsSource as IEnumerable<Project>)?.Any(x => x.Name == autoName) == true, 30, "The new cloud project did not appear by itself.");
        Check(Descendants(projectNavigation).OfType<TextBlock>().Any(t => t.Text == autoName), "A project created elsewhere appears in the Cloud view without any action.");
        await Server.Api.DeleteProjectAsync(autoProject.Id);
        await Wait(() => (projects.ItemsSource as IEnumerable<Project>)?.All(x => x.Name != autoName) == true, 30, "The deleted cloud project did not disappear by itself.");
        Check(!Descendants(projectNavigation).OfType<TextBlock>().Any(t => t.Text == autoName), "...and it disappears when it is deleted on the server.");
        Check(Descendants(projectNavigation).OfType<TextBlock>().Any(t => t.Text.StartsWith("Conversations partagées")), "The Cloud view has a Shared conversations section.");
        Sync.ReportIssue("smoke", SyncLevel.Warning, "Test", "Problème de test");
        RefreshServerChip();
        root.UpdateLayout(); await Task.Delay(300);
        await Capture(root, Path.Combine(output, "server-cloud-view.png"));
        using (var closing = new CancellationTokenSource())
        {
            var shown = ShowServerStatusAsync(closing.Token);
            await Task.Delay(900);
            await Capture(root, Path.Combine(output, "server-status-dialog.png"));
            closing.Cancel();
            try { await shown; } catch (OperationCanceledException) { }
        }
        Check(serverChipBadge?.Visibility == Visibility.Visible && Sync.Snapshot.Worst == SyncLevel.Warning, "A problem shows as a badge on the chip.");
        Sync.ClearIssue("smoke");
        RefreshServerChip();
        Check(serverChipBadge?.Visibility == Visibility.Collapsed || Sync.Snapshot.Worst == SyncLevel.Info, "...which goes away with the problem.");
        SetSidebarView(false);

        // A local conversation shared live.
        var local = list.First(x => !IsCloudProject(x.Id));
        var localChat = await db.Chats.Where(x => x.ProjectId == local.Id).FirstAsync();
        var shareId = await StartShareAsync(localChat);
        Check((await Server.Api.SharesAsync()).Any(x => x.Id == shareId && x.IsHost && x.Live), "A local conversation is shared live through the bridge.");
        var hosted = await Server.Api.ShareAsync(shareId);
        Check(hosted.Code is { Length: 11 } && !hosted.IsPublic, "A shared conversation has a code to hand out and is private by default.");
        var joinedByCode = await Server.Api.JoinShareAsync(hosted.Code!.ToLowerInvariant());
        Check(joinedByCode.Id == shareId, "The code is accepted (case and separators do not matter).");

        // The host sees how many people follow, live.
        var joinedOther = await other.Api.JoinShareAsync(hosted.Code!);
        using var watching = await ShareViewer.OpenAsync(other, joinedOther);
        await Wait(() => Bridge.Viewers(shareId) == 1, 20, "The host was not told about its viewer.");
        var shownChat = chat; chat = localChat; RefreshSharingChip();
        Check(sharingChip!.Visibility == Visibility.Visible && sharingChipText!.Text == "1 spectateur", "A chip at the top of the shared conversation shows the number of viewers.");
        root.UpdateLayout(); await Task.Delay(300);
        await Capture(root, Path.Combine(output, "server-host-chip.png"));
        await OnShareViewersAsync(localChat.Id, 3); await Task.Delay(100);
        Check(sharingChipText!.Text == "3 spectateurs", "...and follows the count as it changes.");
        await OnShareViewersAsync(localChat.Id, 1);
        chat = shownChat; RefreshSharingChip();
        Check(sharingChip.Visibility == Visibility.Collapsed, "The chip is only shown on a shared conversation.");

        // A rename here reaches the viewers.
        localChat.Title = "Titre partagé " + Guid.NewGuid().ToString("N")[..4]; await db.SaveChangesAsync();
        OnChatTitleChanged(localChat.Id, localChat.Title);
        await Wait(() => watching.Title == localChat.Title, 20, "The viewer did not get the new title.");
        Check(watching.Title == localChat.Title, "Renaming a shared conversation renames it for its viewers.");

        // What the host sends is what the viewer draws: sender, reasoning, tool call and its result, timings.
        var sender = ServerSync.CurrentAuthor(ServerLink)!;
        await OpenSharedConversationAsync(hosted);
        Check(sharedWindows.ContainsKey(shareId), "A shared conversation opens in its own window.");
        await ForwardSharedMessage(Bridge, shareId, new Message { Id = 9001, ChatId = localChat.Id, Role = "user", Content = "Lis le fichier notes.txt", State = "complete" }, sender);
        await ForwardSharedMessage(Bridge, shareId, new Message { Id = 9002, ChatId = localChat.Id, Role = "assistant", Content = "", State = "complete", Seconds = 1.2,
            WireJson = "{\"reasoning_content\":\"Je dois d'abord lire le fichier\",\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"lire_fichier\",\"arguments\":\"{\\\"path\\\":\\\"notes.txt\\\"}\"}}]}" }, sender);
        await ForwardSharedMessage(Bridge, shareId, new Message { Id = 9003, ChatId = localChat.Id, Role = "tool", Content = "lire_fichier\nContenu de notes.txt", State = "complete", Seconds = 0.1, WireJson = "{\"tool_call_id\":\"c1\"}" }, sender);
        await ForwardSharedMessage(Bridge, shareId, new Message { Id = 9004, ChatId = localChat.Id, Role = "assistant", Content = "Le fichier contient des notes.", State = "complete", Seconds = 2.0 }, sender);
        await Bridge.ForwardAsync(shareId, "stream", new { chatId = localChat.Id, messageId = 9005, text = "Réponse en cours d'écriture", reasoning = "Je réfléchis encore" });
        var window = sharedWindows[shareId];
        var layout = (FrameworkElement)window.Content;
        await Wait(() => Descendants(layout).OfType<TextBlock>().Any(t => t.Text.Contains("Réponse en cours d'écriture")), 20, "The text being written did not show in the viewer.");
        root.UpdateLayout(); layout.UpdateLayout(); await Task.Delay(500);
        File.WriteAllLines(Path.Combine(output, "viewer-tree.txt"), Descendants(layout).Select(x => x.GetType().Name + " tag=" + x.Tag + " " + (x as TextBlock)?.Text));
        Check(Descendants(layout).Any(x => x.Tag as string == "author"), "The viewer's user bubble shows the sender's avatar.");
        Check(Descendants(layout).OfType<TextBlock>().Any(t => t.Text.Contains("lire_fichier")), "The tool call and its result are drawn as tool cards, like on the host.");
        Check(Descendants(layout).OfType<TextBlock>().Any(t => t.Text.Contains("Je dois d'abord lire le fichier")) && Descendants(layout).OfType<TextBlock>().Any(t => t.Text.Contains("Le fichier contient")), "Reasoning and the final answer are shown too.");
        Check(Descendants(layout).OfType<TextBlock>().Any(t => t.Text.Contains("1 spectateur")), "The viewer window shows how many people follow.");
        await Capture(layout, Path.Combine(output, "server-viewer-window.png"));
        window.Close();
        await Task.Delay(300);
        Check(!sharedWindows.ContainsKey(shareId), "Closing the window stops following.");
        await StopShareAsync(localChat);
        Check((await Server.Api.SharesAsync()).All(x => x.Id != shareId || x.RevokedUtc != null) && !sharedChats.ContainsKey(localChat.Id), "Stopping the sharing revokes it on the server.");

        // Sign-out removes the shared providers, projects stay linked for a later sign-in.
        ServerManagedProviders();
        await Server.LogoutAsync();
        await ServerSync.RemoveProvidersAsync(db, Server);
        await RefreshAfterServerChangeAsync();
        ServerLinkChanged();
        await Wait(() => Sync.Snapshot.State == LinkState.SignedOut, 15, "The state did not become signed out.");
        RefreshServerChip();
        Check(serverChipText?.Text == "Serveur · non connecté", "After signing out the chip says so.");
        Check(!ServerLink.SignedIn && !db.Providers.Local.Any(x => x.Name.EndsWith(ServerSync.ServerProviderSuffix) || x.Name.EndsWith(ServerSync.CloudProviderSuffix)), "Signing out removes the shared providers.");
        Check(IsCloudProject(created.Id), "Cloud projects stay linked after signing out.");
        File.WriteAllLines(Path.Combine(output, "smoke-ok.txt"), passed);
    }
}
