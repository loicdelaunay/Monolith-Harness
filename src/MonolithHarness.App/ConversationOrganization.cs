using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly HashSet<int> namingChats = [];
    readonly BusySpinner namingSpinner = new() { Visibility = Visibility.Collapsed, Margin = new(0, 0, 8, 0) };
    async void FavoriteClick(object sender, RoutedEventArgs e)
    {
        if (GetChatFromOriginalSource(sender) is { } item) await Guard(() => ToggleFavoriteAsync(item));
    }
    async Task ToggleFavoriteAsync(Chat item)
    {
        var value = !item.IsFavorite;
        await using var store = new HarnessDb();
        await store.Chats.Where(c => c.Id == item.Id).ExecuteUpdateAsync(set => set.SetProperty(c => c.IsFavorite, value));
        item.IsFavorite = value;
        if (db.Entry(item).State != EntityState.Detached) db.Entry(item).Property(c => c.IsFavorite).OriginalValue = value;
        ApplyChatSearch(chat?.Id);
    }
    async Task AutoNameAsync(int id, bool automatic = false)
    {
        if (automatic && !FeatureSettings.Read(state.FeaturesJson).AutoNameConversations) return;
        if (!namingChats.Add(id)) return;
        RefreshNamingState();
        try
        {
            var name = await ConversationNaming.RenameAsync(HarnessDb.DatabasePath, id, http, (key, _) => Task.FromResult(KeyVault.Decrypt(key)), automatic, CancellationToken.None);
            if (name == null) return;
            foreach (var summary in navigationChats.Where(c => c.Id == id)) summary.Title = name;
            var item = db.Chats.Local.FirstOrDefault(c => c.Id == id) ?? allProjectChats.FirstOrDefault(c => c.Id == id);
            if (item != null) { item.Title = name; if (db.Entry(item).State != EntityState.Detached) db.Entry(item).Property(c => c.Title).OriginalValue = name; }
            if (conversationRuns.TryGetValue(id, out var run)) { run.Chat.Title = name; run.Db.Entry(run.Chat).Property(c => c.Title).OriginalValue = name; }
            if (chat?.Id == id) { chat.Title = name; title.Text = name; }
            ApplyChatSearch(chat?.Id);
            for (int i = 0; i < chatNotices.Count; i++) if (chatNotices[i].ChatId == id) chatNotices[i] = chatNotices[i] with { Title = name };
        }
        catch (Exception ex)
        {
            AppLog.Write(AppLogLevel.Warning, "conversation.naming_failed", ex, id);
            if (!automatic) throw;
        }
        finally { namingChats.Remove(id); RefreshNamingState(); }
    }
    void RefreshNamingState()
    {
        bool active = chat != null && namingChats.Contains(chat.Id);
        namingSpinner.IsActive = active;
        namingSpinner.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (chat != null) title.Text = active ? WorkflowText("Nommage…", "Naming…") : chat.Title;
        RefreshConversationProgress();
    }
}
