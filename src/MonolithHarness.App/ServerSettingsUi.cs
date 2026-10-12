using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using MonolithHarness.Core.Remote;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// Réglages › Serveur: the optional connection to a Monolith Harness Server. Each action applies immediately (like a connection test),
    /// independently of the window's Save button. The password is never stored; only an encrypted session token is. Providers, cloud
    /// projects and shared conversations are not picked here: they follow the server by themselves (see the Cloud view of the sidebar).
    /// </summary>
    StackPanel BuildServerSettings(Window owner)
    {
        var panel = new StackPanel { Spacing = 14 };
        var link = ServerLink;
        var url = new TextBox { Text = link.Url, PlaceholderText = "https://monolith.exemple:5800", MinWidth = 360, HorizontalAlignment = HorizontalAlignment.Stretch };
        var allowHttp = new ToggleSwitch { IsOn = link.AllowHttp };
        var connect = new Button { Content = WorkflowText("Tester et enregistrer", "Test and save"), Style = (Style)Application.Current.Resources["AccentButtonStyle"], HorizontalAlignment = HorizontalAlignment.Right };
        var status = Label("", 13);
        var email = new TextBox { Text = link.Email, PlaceholderText = "nom@exemple.com", MinWidth = 360, HorizontalAlignment = HorizontalAlignment.Stretch };
        var password = new PasswordBox { PlaceholderText = WorkflowText("Mot de passe", "Password"), MinWidth = 360, HorizontalAlignment = HorizontalAlignment.Stretch };
        var signIn = new Button { Content = T("Se connecter"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var signOut = new Button { Content = T("Se déconnecter") };
        var accountStatus = Label("", 13);
        var current = new PasswordBox { PlaceholderText = WorkflowText("Mot de passe actuel", "Current password") };
        var next = new PasswordBox { PlaceholderText = WorkflowText("Nouveau mot de passe (8 caractères minimum)", "New password (8 characters minimum)") };
        var change = new Button { Content = WorkflowText("Changer le mot de passe", "Change password"), HorizontalAlignment = HorizontalAlignment.Right };
        var passwordStatus = Label("", 13);
        var forget = new Button { Content = WorkflowText("Oublier ce serveur", "Forget this server"), HorizontalAlignment = HorizontalAlignment.Right };

        // Synchronization: how often a complete resync runs (changes themselves arrive live), and a button to run one now.
        var intervals = ServerSyncService.IntervalChoices;
        var interval = new ComboBox { ItemsSource = intervals.Select(IntervalLabel).ToList(), SelectedIndex = Array.IndexOf(intervals, link.SyncMinutes) is var known and >= 0 ? known : Array.IndexOf(intervals, ServerSyncService.DefaultMinutes), MinWidth = 220 };
        var syncNow = new Button { Content = WorkflowText("Synchroniser maintenant", "Sync now"), HorizontalAlignment = HorizontalAlignment.Right };
        var syncStatus = Label("", 13);
        var syncInfo = Label(WorkflowText(
            "Les providers partagés, les projets cloud et les conversations partagées apparaissent, changent et disparaissent tout seuls dès que le serveur les modifie : il n'y a rien à choisir ici. La resynchronisation complète est un filet de sécurité ; elle s'ajoute aux mises à jour en direct.",
            "Shared providers, cloud projects and shared conversations appear, change and disappear by themselves as soon as the server changes them: there is nothing to pick here. The full resynchronization is a safety net; it comes on top of the live updates."), 12);

        panel.Children.Add(Label(WorkflowText(
            "Connexion facultative à un serveur Monolith Harness: providers partagés dont les clés restent sur le serveur, projets cloud (le moteur et les outils s'exécutent sur le serveur) et partage en direct d'une conversation locale. Sans serveur, l'application reste entièrement locale.",
            "Optional connection to a Monolith Harness Server: shared providers whose keys stay on the server, cloud projects (the engine and tools run on the server) and live sharing of a local conversation. Without a server the application stays fully local."), 13));
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Adresse du serveur", "Server address"), WorkflowText("Utilisez HTTPS pour un serveur distant.", "Use HTTPS for a remote server."), url));
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Accepter HTTP non chiffré", "Accept unencrypted HTTP"),
            WorkflowText("Autorise une adresse http:// distante: le mot de passe et le jeton circulent alors en clair. Inutile pour localhost.", "Allows a remote http:// address: the password and token then travel unencrypted. Not needed for localhost."), allowHttp));
        panel.Children.Add(connect); panel.Children.Add(status);
        var account = new StackPanel { Spacing = 10 };
        account.Children.Add(Label(T("Compte"), 16));
        account.Children.Add(email); account.Children.Add(password);
        var accountActions = Row(signOut, signIn); accountActions.HorizontalAlignment = HorizontalAlignment.Right;
        account.Children.Add(accountActions); account.Children.Add(accountStatus);
        panel.Children.Add(account);
        var signedInArea = new StackPanel { Spacing = 14 };
        var syncArea = new StackPanel { Spacing = 8 };
        syncArea.Children.Add(Label(WorkflowText("Synchronisation", "Synchronization"), 16));
        syncArea.Children.Add(syncInfo);
        syncArea.Children.Add(FluentDesign.Setting(WorkflowText("Resynchronisation complète", "Full resynchronization"),
            WorkflowText("Par défaut toutes les 5 minutes. « Jamais » ne garde que les mises à jour en direct.", "Every 5 minutes by default. \"Never\" keeps only the live updates."), interval));
        syncArea.Children.Add(syncNow); syncArea.Children.Add(syncStatus);
        signedInArea.Children.Add(syncArea);
        var passwordArea = new StackPanel { Spacing = 8 };
        passwordArea.Children.Add(Label(T("Mot de passe"), 16));
        passwordArea.Children.Add(current); passwordArea.Children.Add(next); passwordArea.Children.Add(change); passwordArea.Children.Add(passwordStatus);
        signedInArea.Children.Add(passwordArea);
        panel.Children.Add(signedInArea);
        panel.Children.Add(forget);

        bool busy = false;
        void Refresh()
        {
            var l = Server.Link;
            var signed = l.SignedIn;
            signedInArea.Visibility = signed ? Visibility.Visible : Visibility.Collapsed;
            account.Visibility = l.Configured ? Visibility.Visible : Visibility.Collapsed;
            signIn.Visibility = signed ? Visibility.Collapsed : Visibility.Visible;
            signOut.Visibility = signed ? Visibility.Visible : Visibility.Collapsed;
            email.IsEnabled = password.IsEnabled = !signed;
            forget.Visibility = l.Configured ? Visibility.Visible : Visibility.Collapsed;
            accountStatus.Text = signed ? WorkflowText("Connecté en tant que ", "Signed in as ") + l.Email : "";
            status.Text = l.Configured ? $"{l.ServerName} {l.ServerVersion} · {l.Url}" : "";
            var snapshot = serverSync?.Snapshot;
            syncStatus.Text = snapshot == null ? "" : WorkflowText("Dernière synchronisation : ", "Last synchronization: ") + Ago(snapshot.LastSyncUtc)
                + WorkflowText($" · {snapshot.Providers} provider(s) · {snapshot.Projects} projet(s) cloud", $" · {snapshot.Providers} provider(s) · {snapshot.Projects} cloud project(s)");
        }
        async Task Guarded(Func<Task> action, TextBlock target)
        {
            if (busy) return;
            busy = true; target.Text = "…";
            try { await action(); }
            catch (ServerException ex) { target.Text = ex.Message; }
            catch (InvalidOperationException ex) { target.Text = ex.Message; }
            finally { busy = false; Refresh(); }
        }

        connect.Click += async (_, _) => await Guarded(async () =>
        {
            var info = await Server.ConnectAsync(url.Text, allowHttp.IsOn);
            ServerLinkChanged();
            status.Text = $"{info.Name} {info.Version} — " + WorkflowText("serveur joignable.", "server reachable.");
        }, status);
        signIn.Click += async (_, _) => await Guarded(async () =>
        {
            if (conversationRuns.Count > 0) throw new InvalidOperationException(WorkflowText("Terminez les conversations en cours.", "Finish running conversations."));
            var login = await Server.LoginAsync(email.Text.Trim(), password.Password, "Monolith " + Environment.MachineName);
            password.Password = "";
            InvalidateServerLink();
            var synced = await ServerSync.SyncProvidersAsync(db, Server);
            await RefreshAfterServerChangeAsync();
            accountStatus.Text = WorkflowText($"Connecté · {synced.Count} provider(s) du serveur", $"Signed in · {synced.Count} server provider(s)");
            if (login.PasswordChangeSuggested)
                passwordStatus.Text = WorkflowText("Votre mot de passe est temporaire: choisissez-en un nouveau ci-dessous.", "Your password is temporary: choose a new one below.");
            _ = ResumeSharesAsync();
            // Cloud projects and shared conversations come next, by themselves.
            ServerLinkChanged();
        }, accountStatus);
        signOut.Click += async (_, _) => await Guarded(async () =>
        {
            if (conversationRuns.Count > 0) throw new InvalidOperationException(WorkflowText("Terminez les conversations en cours.", "Finish running conversations."));
            foreach (var chatId in sharedChats.Keys.ToList()) if (sharedChats.Remove(chatId, out var share)) await Bridge.Detach(share);
            ServerManagedProviders();
            await Server.LogoutAsync();
            await ServerSync.RemoveProvidersAsync(db, Server);
            await RefreshAfterServerChangeAsync();
            ServerLinkChanged();
            accountStatus.Text = WorkflowText("Déconnecté.", "Signed out.");
        }, accountStatus);
        interval.SelectionChanged += (_, _) =>
        {
            if (interval.SelectedIndex < 0 || interval.SelectedIndex >= intervals.Length) return;
            if (intervals[interval.SelectedIndex] == Sync.Minutes) return;
            Sync.SetMinutes(intervals[interval.SelectedIndex]);
            Refresh();
        };
        syncNow.Click += async (_, _) => await Guarded(async () =>
        {
            ServerManagedProviders();
            await Sync.SyncNowAsync();
            var snapshot = Sync.Snapshot;
            syncStatus.Text = snapshot.Worst is { } level && snapshot.Issues.FirstOrDefault() is { } first
                ? $"{LevelName(level)} : {first.Message}"
                : WorkflowText("Synchronisation terminée.", "Synchronization complete.");
        }, syncStatus);
        change.Click += async (_, _) => await Guarded(async () =>
        {
            await Server.Api.ChangePasswordAsync(current.Password, next.Password);
            current.Password = next.Password = "";
            passwordStatus.Text = WorkflowText("Mot de passe modifié (vos autres connexions ont été fermées).", "Password changed (your other sessions were closed).");
        }, passwordStatus);
        forget.Click += async (_, _) => await Guarded(async () =>
        {
            if (conversationRuns.Count > 0) throw new InvalidOperationException(WorkflowText("Terminez les conversations en cours.", "Finish running conversations."));
            var confirm = new ContentDialog
            {
                XamlRoot = (owner.Content as FrameworkElement)?.XamlRoot ?? root.XamlRoot, Title = WorkflowText("Oublier ce serveur ?", "Forget this server?"),
                Content = Label(WorkflowText("Les providers du serveur sont retirés. Les projets cloud restent comme copies locales sans lien avec le serveur.", "Server providers are removed. Cloud projects stay as local copies with no link to the server."), 14),
                PrimaryButtonText = WorkflowText("Oublier", "Forget"), CloseButtonText = T("Annuler"), DefaultButton = ContentDialogButton.Close
            };
            if (await ShowDialogAsync(confirm) != ContentDialogResult.Primary) return;
            ServerManagedProviders();
            if (Server.SignedIn) { foreach (var chatId in sharedChats.Keys.ToList()) if (sharedChats.Remove(chatId, out var share)) await Bridge.Detach(share); await Server.LogoutAsync(); }
            await ServerSync.RemoveProvidersAsync(db, Server);
            Server.Store.Update(l => { l.Url = ""; l.Email = ""; l.ProtectedToken = ""; l.TokenExpiresUtc = null; l.ServerName = ""; l.ServerVersion = ""; l.Providers.Clear(); l.Projects.Clear(); l.Chats.Clear(); l.Shares.Clear(); l.CloudView = false; });
            await RefreshAfterServerChangeAsync();
            ServerLinkChanged();
            url.Text = ""; email.Text = "";
            status.Text = WorkflowText("Serveur oublié: l'application est entièrement locale.", "Server forgotten: the application is fully local.");
        }, status);

        Refresh();
        return panel;
    }
}
