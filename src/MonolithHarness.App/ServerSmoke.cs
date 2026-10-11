using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
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
        string Setting(string name) => Environment.GetEnvironmentVariable("MONOLITHHARNESS_SERVER_" + name) ?? throw new InvalidOperationException("Missing MONOLITHHARNESS_SERVER_" + name);
        UiText.Language = state.Language = "fr";
        var features = FeatureSettings.Read(state.FeaturesJson); features.GuiUpdateMode = AutomaticUpdateMode.Disabled;
        state.FeaturesJson = features.Json(); ConfigureAutomaticUpdates();

        // Without a server the application is local and the settings page builds.
        Check(!ServerLink.Configured && !ServerLink.SignedIn, "A fresh profile has no server link and stays local.");
        var settingsPage = BuildServerSettings(this);
        Check(settingsPage.Children.Count > 5, "The Server settings page builds without a server.");

        // Connection, account, shared providers.
        await Server.ConnectAsync(Setting("URL"), false);
        await Server.LoginAsync(Setting("EMAIL"), Setting("PASSWORD"), "GUI smoke");
        InvalidateServerLink();
        var synced = await ServerSync.SyncProvidersAsync(db, Server);
        await RefreshAfterServerChangeAsync();
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

        // A local conversation shared live.
        var local = list.First(x => !IsCloudProject(x.Id));
        var localChat = await db.Chats.Where(x => x.ProjectId == local.Id).FirstAsync();
        var shareId = await StartShareAsync(localChat);
        Check((await Server.Api.SharesAsync()).Any(x => x.Id == shareId && x.IsHost && x.Live), "A local conversation is shared live through the bridge.");
        await StopShareAsync(localChat);
        Check((await Server.Api.SharesAsync()).All(x => x.Id != shareId || x.RevokedUtc != null) && !sharedChats.ContainsKey(localChat.Id), "Stopping the sharing revokes it on the server.");

        // Sign-out removes the shared providers, projects stay linked for a later sign-in.
        ServerManagedProviders();
        await Server.LogoutAsync();
        await ServerSync.RemoveProvidersAsync(db, Server);
        await RefreshAfterServerChangeAsync();
        Check(!ServerLink.SignedIn && !db.Providers.Local.Any(x => x.Name.EndsWith(ServerSync.ServerProviderSuffix) || x.Name.EndsWith(ServerSync.CloudProviderSuffix)), "Signing out removes the shared providers.");
        Check(IsCloudProject(created.Id), "Cloud projects stay linked after signing out.");
        File.WriteAllLines(Path.Combine(output, "smoke-ok.txt"), passed);
    }
}
