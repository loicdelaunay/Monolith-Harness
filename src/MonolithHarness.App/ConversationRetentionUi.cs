using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly DispatcherTimer conversationRetentionTimer = new() { Interval = TimeSpan.FromHours(1) };
    bool conversationRetentionBusy;
    void InitializeConversationRetention()
    {
        conversationRetentionTimer.Tick += async (_, _) => await Guard(MaintainConversationsAsync);
        Closed += (_, _) => conversationRetentionTimer.Stop();
    }
    (FrameworkElement Panel, Action<FeatureSettings> Save, Func<bool> Validate) BuildConversationRetentionSettings()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Label(WorkflowText("Entretien des conversations", "Conversation maintenance"), 16));
        var archive = new ToggleSwitch { IsOn = config.AutoArchiveConversations };
        var deletion = new ToggleSwitch { IsOn = config.AutoDeleteConversations };
        var archiveDays = new NumberBox { Header = WorkflowText("Jours d’inactivité avant archivage", "Inactive days before archiving"), Minimum = 1, Maximum = 3650, Value = config.AutoArchiveDays, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var deleteDays = new NumberBox { Header = WorkflowText("Jours d’inactivité au total avant suppression", "Total inactive days before deletion"), Minimum = 1, Maximum = 3650, Value = config.AutoDeleteDays, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Archiver automatiquement", "Automatically archive"), WorkflowText("Déplace les discussions inactives dans Archive.", "Move inactive conversations to Archive."), archive));
        panel.Children.Add(archiveDays);
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Supprimer automatiquement les archives", "Automatically delete archived conversations"), WorkflowText("Suppression définitive des conversations archivées, de leurs messages, images et mémoires propres. 45 jours signifie 45 jours depuis la dernière activité, et non 45 jours après l’archivage.", "Permanently delete archived conversations, their messages, images and conversation memories. 45 days means 45 days since the last activity, not 45 days after archiving."), deletion));
        panel.Children.Add(deleteDays);
        panel.Children.Add(Label(WorkflowText("Vérification au démarrage puis toutes les heures pendant que l’application est ouverte. Les discussions affichées, épinglées, favorites, en cours ou en attente d’action/envoi sont conservées. Les deux automatismes sont désactivés par défaut.", "Checked at startup and hourly while the app is open. Displayed, pinned, favorite, running conversations and those waiting for action or queued messages are preserved. Both automations are off by default."), 12));
        var error = Label("", 12); error.Foreground = FluentDesign.Resource("ToolMessageErrorStrokeBrush"); panel.Children.Add(error);
        void Refresh() { archiveDays.IsEnabled = archive.IsOn; deleteDays.IsEnabled = deletion.IsOn; }
        archive.Toggled += (_, _) => Refresh(); deletion.Toggled += (_, _) => Refresh(); Refresh();
        bool Validate()
        {
            bool valid = double.IsFinite(archiveDays.Value) && double.IsFinite(deleteDays.Value) && archiveDays.Value == Math.Truncate(archiveDays.Value) && deleteDays.Value == Math.Truncate(deleteDays.Value)
                && archiveDays.Value is >= 1 and <= 3650 && deleteDays.Value is >= 1 and <= 3650 && (!archive.IsOn || !deletion.IsOn || deleteDays.Value > archiveDays.Value);
            error.Text = valid ? "" : WorkflowText("Indiquez des jours entiers de 1 à 3650. La suppression doit avoir lieu après l’archivage.", "Enter whole days from 1 to 3650. Deletion must happen after archiving."); return valid;
        }
        return (FluentDesign.Surface(panel, 16), target => {
            target.AutoArchiveConversations = archive.IsOn; target.AutoArchiveDays = (int)archiveDays.Value;
            target.AutoDeleteConversations = deletion.IsOn; target.AutoDeleteDays = (int)deleteDays.Value;
        }, Validate);
    }
    async Task MaintainConversationsAsync()
    {
        if (conversationRetentionBusy || databaseMaintenanceBusy || editingSettings || conversationLoading) return;
        var config = FeatureSettings.Read(state.FeaturesJson);
        if (!config.AutoArchiveConversations && !config.AutoDeleteConversations) return;
        conversationRetentionBusy = true; RefreshGenerationControls();
        try
        {
            var excluded = conversationRuns.Keys.Concat(namingChats).Concat(pendingChatAttention.Keys).Concat(failedUnreadChats)
                .Concat(conversationDrafts.Where(x => !string.IsNullOrWhiteSpace(x.Value.Text) || x.Value.Images.Count > 0).Select(x => x.Key))
                .Concat(chat != null ? [chat.Id] : state.ChatId.HasValue ? [state.ChatId.Value] : Array.Empty<int>()).Distinct().ToArray();
            ConversationRetentionResult result = new([], []);
            async Task Apply() => result = await Task.Run(() => ConversationRetention.ApplyAsync(HarnessDb.DatabasePath, config, excluded, DateTime.UtcNow));
            if (scheduler != null) { if (!await scheduler.TryRunMaintenanceAsync(Apply)) return; } else await Apply();
            foreach (var id in result.Archived)
                if (db.Chats.Local.FirstOrDefault(x => x.Id == id) is { } item)
                { item.IsArchived = true; db.Entry(item).Property(x => x.IsArchived).OriginalValue = true; }
            foreach (var id in result.Deleted)
            {
                await terminals.RemoveChatAsync(id); CloseConversationBrowser(id);
                conversationDrafts.Remove(id); conversationHistory.Remove(id); conversationStatuses.Remove(id);
                if (db.Chats.Local.FirstOrDefault(x => x.Id == id) is { } item) db.Entry(item).State = EntityState.Detached;
            }
            if (result.Archived.Count > 0 || result.Deleted.Count > 0) await SelectProject();
        }
        finally { conversationRetentionBusy = false; RefreshGenerationControls(); }
    }
}
