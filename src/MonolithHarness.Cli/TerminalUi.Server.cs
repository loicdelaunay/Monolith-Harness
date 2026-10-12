using System.Text.Json.Nodes;
using MonolithHarness.Core;
using MonolithHarness.Core.Remote;

namespace MonolithHarness.Cli;

/// <summary>/server: optional connection to a Monolith Harness Server (cloud projects, shared providers, live sharing).</summary>
public sealed partial class TerminalUi
{
    static string Str(JsonNode? node, string name) => node?[name]?.ToString() ?? "";
    static bool Flag(JsonNode? node, string name) => node?[name]?.GetValue<bool>() == true;

    async Task Server(WorkspaceSnapshot snapshot, string argument)
    {
        var view = (await client.Call("server.status", ct: lifetime.Token))!;
        var status = view["status"]!;
        bool configured = Flag(status, "configured"), signedIn = Flag(status, "signedIn");
        var link = client.ServerLink;
        var choices = new List<Choice>();
        if (!configured) choices.Add(new("connect", L("Connecter un serveur", "Connect to a server"), L("Adresse https://hôte:port", "Address https://host:port")));
        else if (!signedIn)
        {
            choices.Add(new("login", L("Se connecter", "Sign in"), Str(status, "email")));
            choices.Add(new("connect", L("Changer d’adresse", "Change address")));
            choices.Add(new("forget", L("Oublier ce serveur", "Forget this server")));
        }
        else
        {
            var chat = CurrentChat;
            var shared = chat != null && link.Shares.ContainsKey(chat.Id);
            choices.Add(new("projects", L("Projets cloud", "Cloud projects"), L("Ouvrir ou créer · exécution sur le serveur", "Open or create · runs on the server")));
            if (chat != null && !link.Chats.ContainsKey(chat.Id) && !link.Projects.ContainsKey(chat.ProjectId))
                choices.Add(shared ? new("invite", L("Inviter un spectateur", "Invite a viewer")) : new("share", L("Partager cette conversation en direct", "Share this conversation live"), L("Les outils restent sur cet appareil", "Tools stay on this device")));
            if (shared) choices.Add(new("unshare", L("Arrêter le partage", "Stop sharing")));
            choices.Add(new("join", L("Rejoindre avec un code", "Join with a code"), L("Suivre une conversation partagée", "Follow a shared conversation")));
            choices.Add(new("sync", L("Synchronisation", "Synchronization"), L("État, problèmes, fréquence", "State, problems, frequency")));
            choices.Add(new("password", L("Changer le mot de passe", "Change password")));
            choices.Add(new("logout", L("Se déconnecter", "Sign out")));
            choices.Add(new("forget", L("Oublier ce serveur", "Forget this server")));
        }
        var body = !configured
            ? L("Aucun serveur : l’application reste entièrement locale.", "No server: the application stays fully local.")
            : $"{Str(status, "serverName")} {Str(status, "version")} · {Str(status, "url")}\n"
                + (signedIn ? Str(status, "email") : L("Non connecté", "Not signed in"))
                + (Flag(status, "reachable") ? "" : "\n! " + Str(status, "error"))
                + (!signedIn && Str(status, "errorCode") == "session_lost" ? "\n! " + Str(status, "error") : "");
        var action = argument.Length > 0 ? argument.Split(' ')[0].ToLowerInvariant() : await Prompt("Serveur / Server", body, choices);
        if (action == null || choices.All(x => x.Value != action)) return;
        switch (action)
        {
            case "connect": await ServerConnect(link); break;
            case "login": await ServerLogin(link); break;
            case "projects": await ServerProjects(); break;
            case "share": await ServerShare(); break;
            case "unshare":
                if (CurrentChat is { } stop) { await client.Call("server.share.stop", new { chatId = stop.Id }, lifetime.Token); await Refresh(); Post(() => notice = L("Partage arrêté", "Sharing stopped")); }
                break;
            case "invite":
            {
                var email = await Prompt(L("Inviter · adresse e-mail", "Invite · e-mail address"), L("Un compte du serveur. Les spectateurs lisent et commentent, rien de plus.", "A server account. Viewers can read and comment, nothing more."));
                if (string.IsNullOrWhiteSpace(email) || CurrentChat is not { } shareChat) return;
                await client.Call("server.share.invite", new { chatId = shareChat.Id, email = email.Trim() }, lifetime.Token);
                Post(() => notice = L("Invitation envoyée : ", "Invitation sent: ") + email.Trim());
                break;
            }
            case "sync": await ServerSyncMenu(); break;
            case "join": await ServerJoin(); break;
            case "password": await ServerPassword(); break;
            case "logout":
                await client.Call("server.logout", ct: lifetime.Token); await Refresh(); Post(() => notice = L("Déconnecté du serveur", "Signed out of the server"));
                break;
            case "forget":
                if (await Prompt(L("Oublier ce serveur ?", "Forget this server?"), L("Les providers du serveur sont retirés; les projets cloud restent comme copies locales sans lien.", "Server providers are removed; cloud projects stay as unlinked local copies."),
                    [new("no", "Annuler / Cancel"), new("yes", "Oublier / Forget")]) != "yes") return;
                await client.Call("server.disconnect", ct: lifetime.Token); await Refresh(); Post(() => notice = L("Serveur oublié", "Server forgotten"));
                break;
        }
    }

    async Task ServerConnect(ServerLinkData link)
    {
        var url = await Prompt(L("Serveur · adresse", "Server · address"), L("Adresse du serveur Monolith, par exemple https://monolith.exemple:5800", "Monolith server address, for example https://monolith.example:5800"), initial: link.Url.Length > 0 ? link.Url : "https://");
        if (string.IsNullOrWhiteSpace(url)) return;
        var allowHttp = link.AllowHttp;
        while (true)
        {
            try { await client.Call("server.connect", new { url = url.Trim(), allowHttp }, lifetime.Token); break; }
            catch (InvalidOperationException ex) when (ex.InnerException is ServerException { Code: "insecure_http" } && !allowHttp)
            {
                var accept = await Prompt(L("HTTP non chiffré", "Unencrypted HTTP"), L("Cette adresse n’utilise pas HTTPS: le mot de passe et le jeton circuleraient en clair sur le réseau. Accepter quand même ?", "This address does not use HTTPS: the password and token would travel unencrypted. Accept anyway?"),
                    [new("no", "Non, annuler / No, cancel"), new("yes", "Oui, accepter HTTP / Yes, accept HTTP")]);
                if (accept != "yes") return;
                allowHttp = true;
            }
        }
        await Refresh();
        Post(() => Command("/server login"));
    }

    async Task ServerLogin(ServerLinkData link)
    {
        var email = await Prompt(L("Serveur · e-mail", "Server · e-mail"), initial: link.Email);
        if (string.IsNullOrWhiteSpace(email)) return;
        var password = await Prompt(L("Serveur · mot de passe", "Server · password"), L("Saisie masquée; seul un jeton chiffré est conservé.", "Masked; only an encrypted token is kept."), secret: true);
        if (string.IsNullOrEmpty(password)) return;
        var result = (await client.Call("server.login", new { email = email.Trim(), password, label = "CLI " + Environment.MachineName }, lifetime.Token))!;
        await Refresh();
        var count = (await client.Call("snapshot", ct: lifetime.Token))?["providers"]?.AsArray().Count(x => Str(x, "name").EndsWith(" · serveur") || Str(x, "name").EndsWith(" · cloud")) ?? 0;
        Post(() => notice = L($"Connecté · {count} provider(s) du serveur", $"Signed in · {count} server provider(s)") + (Str(result, "syncError").Length > 0 ? " · ! " + Str(result, "syncError") : ""));
        if (Flag(result, "passwordChangeSuggested") &&
            await Prompt(L("Mot de passe temporaire", "Temporary password"), L("Ce mot de passe est initial ou fourni par un administrateur. Le changer maintenant ?", "This password is initial or issued by an administrator. Change it now?"),
                [new("later", "Plus tard / Later"), new("now", "Changer maintenant / Change now")]) == "now")
            await ServerPassword();
    }

    async Task ServerPassword()
    {
        var current = await Prompt(L("Mot de passe actuel", "Current password"), secret: true);
        if (string.IsNullOrEmpty(current)) return;
        var next = await Prompt(L("Nouveau mot de passe", "New password"), L("8 caractères minimum.", "8 characters minimum."), secret: true);
        if (string.IsNullOrEmpty(next)) return;
        var confirm = await Prompt(L("Confirmer le nouveau mot de passe", "Confirm the new password"), secret: true);
        if (confirm != next) { await Show(L("Mot de passe", "Password"), L("Les deux saisies diffèrent.", "The two entries differ.")); return; }
        await client.Call("server.password", new { current, @new = next }, lifetime.Token);
        Post(() => notice = L("Mot de passe modifié (les autres connexions sont fermées)", "Password changed (other sessions were closed)"));
    }

    async Task ServerProjects()
    {
        var projects = (await client.Call("server.projects", ct: lifetime.Token))!.AsArray();
        var choices = projects.Select(x => new Choice(Str(x, "id"), Str(x, "name"), $"{Str(x, "ownerEmail")} · {Str(x, "access")}")).Append(new("new", L("+ Nouveau projet cloud", "+ New cloud project"))).ToList();
        var value = await Prompt(L("Projets cloud", "Cloud projects"), choices: choices);
        if (value == null) return;
        JsonNode opened;
        if (value == "new")
        {
            var name = await Prompt(L("Nom du projet cloud", "Cloud project name"));
            if (string.IsNullOrWhiteSpace(name)) return;
            opened = (await client.Call("server.project.create", new { name = name.Trim() }, lifetime.Token))!;
        }
        else opened = (await client.Call("server.project.open", new { id = value }, lifetime.Token))!;
        int projectId = opened["id"]!.GetValue<int>();
        var fresh = await client.Snapshot(lifetime.Token);
        var chat = fresh.Chats.Where(c => c.ProjectId == projectId).OrderByDescending(c => c.UpdatedUtc).FirstOrDefault();
        int target = chat?.Id ?? await client.CreateChat(projectId, lifetime.Token);
        fresh = await client.Snapshot(lifetime.Token);
        var link = client.ServerLink;
        // Cloud runs use the server's providers: select one when the current provider is an ordinary local one.
        var serverProvider = fresh.Providers.FirstOrDefault(p => link.Providers.Values.Contains(p.Id) && p.Name.EndsWith(ServerSync.ServerProviderSuffix)) ?? fresh.Providers.FirstOrDefault(p => link.Providers.Values.Contains(p.Id));
        Post(() =>
        {
            workspace = fresh; serverLink = link;
            if (serverProvider != null && !link.Providers.Values.Contains(providerId)) providerId = serverProvider.Id;
            SwitchChat(target);
            notice = serverProvider == null ? L("Projet cloud ouvert · aucun provider du serveur : /server", "Cloud project open · no server provider: /server") : L("Projet cloud ouvert · les requêtes s’exécutent sur le serveur", "Cloud project open · requests run on the server");
        });
    }

    async Task ServerShare()
    {
        if (CurrentChat is not { } chat) return;
        var publicChoice = await Prompt(L("Visibilité", "Visibility"), L("Privée : seules les personnes ayant le code ou invitées la suivent. Publique : tous les comptes la voient dans leur liste.", "Private: only people with the code or invited follow it. Public: every account sees it in their list."),
            [new("private", L("Privée (code ou invitation)", "Private (code or invitation)")), new("public", L("Publique (listée pour tous)", "Public (listed for everybody)"))]);
        if (publicChoice == null) return;
        var started = (await client.Call("server.share.start", new { chatId = chat.Id, isPublic = publicChoice == "public" }, lifetime.Token))!;
        await Refresh();
        Post(() => notice = L("Partage en direct démarré · /server pour inviter", "Live sharing started · /server to invite"));
        await Show(L("Code de partage", "Sharing code"), Str(started, "code") + "\n\n" + L("Transmettez ce code : la personne le saisit dans Cloud › Rejoindre avec un code (GUI) ou `server join` (CLI). `/server` pour le renouveler ou inviter.", "Hand this code over: the person enters it in Cloud › Join with a code (GUI) or `server join` (CLI)."));
    }

    async Task ServerJoin()
    {
        var code = await Prompt(L("Code de partage", "Sharing code"), L("Le code transmis par l'hôte, par exemple K7QD4-M9X2R.", "The code the host gave you, for example K7QD4-M9X2R."));
        if (string.IsNullOrWhiteSpace(code)) return;
        JsonNode content;
        try { content = (await client.Call("server.share.join", new { code = code.Trim() }, lifetime.Token))!; }
        catch (InvalidOperationException ex) when (ex.InnerException is ServerException problem)
        { await Show(L("Rejoindre", "Join"), problem.Code == "invalid_share_code" ? L("Ce code est invalide ou n'est plus actif.", "This code is invalid or no longer active.") : problem.Message); return; }
        var lines = new List<string> { $"{Str(content, "title")} · {Str(content["share"], "hostEmail")}" + (Flag(content, "online") ? " · live" : " · " + L("hôte hors ligne", "host offline")), "" };
        foreach (var message in content["messages"]?.AsArray() ?? []) lines.Add($"[{Str(message, "role")}] {Str(message, "content")}\n");
        if (lines.Count == 2) lines.Add(L("Aucun contenu pour le moment : il n'est disponible que lorsque l'appareil de l'hôte est connecté.", "No content yet: it is only available while the host's device is connected."));
        await Show(L("Conversation partagée (lecture seule)", "Shared conversation (read-only)"), string.Join('\n', lines));
        await Refresh();
    }

    async Task ServerSyncMenu()
    {
        while (true)
        {
            var state = (await client.Call("server.sync.status", ct: lifetime.Token))!;
            var minutes = state["minutes"]?.GetValue<int>() ?? 5;
            var body = $"{Str(state, "state")}{(Flag(state, "realtime") ? " · " + L("temps réel actif", "real time on") : "")}\n"
                + L("Dernière synchronisation : ", "Last synchronization: ") + (state["lastSyncUtc"] is { } last ? DateTime.Parse(last.ToString()).ToLocalTime().ToString("g") : L("jamais", "never")) + "\n"
                + L("Resynchronisation complète : ", "Full resync: ") + (minutes == 0 ? L("jamais", "never") : minutes + " min") + "\n"
                + L($"{state["providers"]} provider(s) · {state["projects"]} projet(s) cloud · {state["sharedWithMe"]} partagée(s) avec vous", $"{state["providers"]} provider(s) · {state["projects"]} cloud project(s) · {state["sharedWithMe"]} shared with you");
            foreach (var issue in state["issues"]?.AsArray() ?? []) body += $"\n[{Str(issue, "level")}] {Str(issue, "area")} : {Str(issue, "message")}";
            var action = await Prompt(L("Synchronisation", "Synchronization"), body, [new("now", L("Synchroniser maintenant", "Sync now")), new("interval", L("Changer la fréquence", "Change the frequency")), new("clear", L("Effacer les problèmes", "Clear the problems")), new("back", L("Retour", "Back"))]);
            if (action is null or "back") return;
            if (action == "now") { await client.Call("server.sync.now", ct: lifetime.Token); await Refresh(); }
            else if (action == "clear") await client.Call("server.sync.clear", ct: lifetime.Token);
            else
            {
                var choice = await Prompt(L("Resynchronisation complète", "Full resynchronization"), L("Les changements du serveur arrivent toujours en direct ; ceci n'est qu'un filet de sécurité.", "Server changes always arrive live; this is only a safety net."),
                    [new("0", L("Jamais", "Never")), new("1", "1 min"), new("5", L("5 min (par défaut)", "5 min (default)")), new("60", L("1 heure", "1 hour")), new("1440", L("1 jour", "1 day"))]);
                if (choice != null) await client.Call("server.sync.interval", new { minutes = int.Parse(choice) }, lifetime.Token);
            }
        }
    }
}
