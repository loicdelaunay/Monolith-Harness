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
    /// independently of the window's Save button. The password is never stored; only an encrypted session token is.
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
        var sync = new Button { Content = WorkflowText("Synchroniser les providers", "Sync providers"), HorizontalAlignment = HorizontalAlignment.Right };
        var providerStatus = Label("", 13);
        var current = new PasswordBox { PlaceholderText = WorkflowText("Mot de passe actuel", "Current password") };
        var next = new PasswordBox { PlaceholderText = WorkflowText("Nouveau mot de passe (8 caractères minimum)", "New password (8 characters minimum)") };
        var change = new Button { Content = WorkflowText("Changer le mot de passe", "Change password"), HorizontalAlignment = HorizontalAlignment.Right };
        var passwordStatus = Label("", 13);
        var projectList = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 220 };
        var refreshProjects = new Button { Content = WorkflowText("Actualiser", "Refresh") };
        var openProject = new Button { Content = WorkflowText("Ouvrir en mode cloud", "Open in cloud mode"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var newProject = new TextBox { PlaceholderText = WorkflowText("Nom du nouveau projet cloud", "New cloud project name"), MinWidth = 260 };
        var createProject = new Button { Content = WorkflowText("Créer", "Create") };
        var projectStatus = Label("", 13);
        var forget = new Button { Content = WorkflowText("Oublier ce serveur", "Forget this server"), HorizontalAlignment = HorizontalAlignment.Right };
        var remoteProjects = new List<ServerProject>();

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
        var providerArea = new StackPanel { Spacing = 8 };
        providerArea.Children.Add(Label(WorkflowText("Providers partagés", "Shared providers"), 16));
        providerArea.Children.Add(Label(WorkflowText("Le catalogue du serveur apparaît comme des fournisseurs « · serveur » (utilisables aussi en local) ou « · cloud » (projets cloud seulement). Seul un jeton de session est conservé ici, jamais la clé du provider.",
            "The server catalog appears as providers “· server” (usable locally too) or “· cloud” (cloud projects only). Only a session token is kept here, never the provider's key."), 12));
        providerArea.Children.Add(sync); providerArea.Children.Add(providerStatus);
        signedInArea.Children.Add(providerArea);
        var cloud = new StackPanel { Spacing = 8 };
        cloud.Children.Add(Label(WorkflowText("Projets cloud", "Cloud projects"), 16));
        cloud.Children.Add(projectList);
        var cloudActions = Row(refreshProjects, openProject); cloudActions.HorizontalAlignment = HorizontalAlignment.Right;
        cloud.Children.Add(cloudActions);
        cloud.Children.Add(Row(newProject, createProject)); cloud.Children.Add(projectStatus);
        signedInArea.Children.Add(cloud);
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
            openProject.IsEnabled = projectList.SelectedItem != null;
            accountStatus.Text = signed ? WorkflowText("Connecté en tant que ", "Signed in as ") + l.Email : "";
            status.Text = l.Configured ? $"{l.ServerName} {l.ServerVersion} · {l.Url}" : "";
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
        async Task LoadProjects()
        {
            remoteProjects = await Server.Api.ProjectsAsync();
            var linked = Server.Link.Projects.Values.Select(x => x.ServerProjectId).ToHashSet();
            projectList.ItemsSource = remoteProjects.Select(x => $"{x.Name} — {x.OwnerEmail} · {x.Access}{(linked.Contains(x.Id.ToString()) ? " · ☁" : "")}").ToList();
            projectStatus.Text = remoteProjects.Count == 0 ? WorkflowText("Aucun projet cloud. Créez-en un.", "No cloud project. Create one.") : "";
        }

        connect.Click += async (_, _) => await Guarded(async () =>
        {
            var info = await Server.ConnectAsync(url.Text, allowHttp.IsOn);
            InvalidateServerLink();
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
            await LoadProjects();
        }, accountStatus);
        signOut.Click += async (_, _) => await Guarded(async () =>
        {
            if (conversationRuns.Count > 0) throw new InvalidOperationException(WorkflowText("Terminez les conversations en cours.", "Finish running conversations."));
            foreach (var chatId in sharedChats.Keys.ToList()) if (sharedChats.Remove(chatId, out var share)) await Bridge.Detach(share);
            ServerManagedProviders();
            await Server.LogoutAsync();
            await ServerSync.RemoveProvidersAsync(db, Server);
            await RefreshAfterServerChangeAsync();
            projectList.ItemsSource = null;
            accountStatus.Text = WorkflowText("Déconnecté.", "Signed out.");
        }, accountStatus);
        sync.Click += async (_, _) => await Guarded(async () =>
        {
            ServerManagedProviders();
            var synced = await ServerSync.SyncProvidersAsync(db, Server);
            await RefreshAfterServerChangeAsync();
            providerStatus.Text = WorkflowText($"{synced.Count} provider(s) synchronisé(s).", $"{synced.Count} provider(s) synced.");
        }, providerStatus);
        change.Click += async (_, _) => await Guarded(async () =>
        {
            await Server.Api.ChangePasswordAsync(current.Password, next.Password);
            current.Password = next.Password = "";
            passwordStatus.Text = WorkflowText("Mot de passe modifié (vos autres connexions ont été fermées).", "Password changed (your other sessions were closed).");
        }, passwordStatus);
        refreshProjects.Click += async (_, _) => await Guarded(LoadProjects, projectStatus);
        projectList.SelectionChanged += (_, _) => openProject.IsEnabled = projectList.SelectedIndex >= 0;
        openProject.Click += async (_, _) => await Guarded(async () =>
        {
            if (projectList.SelectedIndex < 0 || projectList.SelectedIndex >= remoteProjects.Count) return;
            var bound = await ServerSync.BindProjectAsync(db, Server, remoteProjects[projectList.SelectedIndex]);
            await db.SaveChangesAsync();
            await RefreshAfterServerChangeAsync();
            await LoadProjects();
            projectStatus.Text = WorkflowText($"« {bound.Name} » est dans la liste des projets: ses requêtes s'exécutent sur le serveur.", $"“{bound.Name}” is in the project list: its requests run on the server.");
        }, projectStatus);
        createProject.Click += async (_, _) => await Guarded(async () =>
        {
            if (string.IsNullOrWhiteSpace(newProject.Text)) return;
            var created = await ServerSync.CreateProjectAsync(db, Server, newProject.Text.Trim());
            await db.SaveChangesAsync();
            newProject.Text = "";
            await RefreshAfterServerChangeAsync();
            await LoadProjects();
            projectStatus.Text = WorkflowText($"Projet cloud « {created.Name} » créé.", $"Cloud project “{created.Name}” created.");
        }, projectStatus);
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
            Server.Store.Update(l => { l.Url = ""; l.Email = ""; l.ProtectedToken = ""; l.TokenExpiresUtc = null; l.ServerName = ""; l.ServerVersion = ""; l.Providers.Clear(); l.Projects.Clear(); l.Chats.Clear(); l.Shares.Clear(); });
            await RefreshAfterServerChangeAsync();
            url.Text = ""; email.Text = ""; projectList.ItemsSource = null;
            status.Text = WorkflowText("Serveur oublié: l'application est entièrement locale.", "Server forgotten: the application is fully local.");
        }, status);

        Refresh();
        if (link.SignedIn) _ = Guarded(LoadProjects, projectStatus);
        return panel;
    }
}
