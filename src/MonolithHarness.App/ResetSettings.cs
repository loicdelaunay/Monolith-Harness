using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using Microsoft.EntityFrameworkCore;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    bool databaseMaintenanceBusy;
    StackPanel BuildResetSettings(Window owner)
    {
        var panel = new StackPanel { Spacing = 14 };
        var information = Label("", 13); information.Tag = null;
        var optimize = new Button { Content = WorkflowText("Optimiser la base de données", "Optimize database") };
        var reset = new Button { Content = WorkflowText("Réinitialiser les données", "Reset data"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Optimiser", "Optimize"), WorkflowText("Compacte la base SQLite et optimise ses index. Fermez les autres instances de l’application avant de lancer l’opération.", "Compact SQLite and optimize its indexes. Close other application instances before starting."), optimize));
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Réinitialiser les données des projets", "Reset project data"), WorkflowText("Supprime les projets, conversations, messages, images enregistrées, mémoires, tâches, templates et autorisations. Les réglages et fournisseurs sont conservés. Les fichiers sources sur disque sont conservés.", "Delete projects, chats, messages, saved images, memories, tasks, templates and approvals. Settings, providers and source files on disk are kept."), reset));
        panel.Children.Add(information);
        async Task Perform(bool clear)
        {
            if (databaseMaintenanceBusy) return;
            if (conversationRuns.Count > 0 || conversationLoading)
            { information.Text = WorkflowText("Terminez les conversations en cours avant cette opération.", "Finish running conversations before this operation."); return; }
            if (clear)
            {
                var confirm = new TextBox { Header = WorkflowText("Tapez RESET pour confirmer", "Type RESET to confirm") };
                var content = new StackPanel { Spacing = 12 };
                content.Children.Add(Label(WorkflowText("Cette suppression est définitive. Toutes les données des projets et les mémoires seront effacées.", "This deletion is permanent. All project data and memories will be erased."), 14)); content.Children.Add(confirm);
                var dialog = new ContentDialog { XamlRoot = (owner.Content as FrameworkElement)?.XamlRoot ?? root.XamlRoot, Title = WorkflowText("Réinitialiser les données ?", "Reset data?"), Content = content, PrimaryButtonText = WorkflowText("Réinitialiser", "Reset"), CloseButtonText = UiText.T("Annuler"), IsPrimaryButtonEnabled = false };
                confirm.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = confirm.Text == "RESET";
                if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;
            }
            // Recheck after the modal wait: a scheduled conversation may have started.
            if (conversationRuns.Count > 0 || conversationLoading) { information.Text = WorkflowText("Une conversation vient de démarrer. Réessayez après sa fin.", "A conversation just started. Try again when it finishes."); return; }
            databaseMaintenanceBusy = true; optimize.IsEnabled = reset.IsEnabled = false; shell.IsEnabled = false;
            scheduleTimer.Stop();
            information.Text = WorkflowText("Opération en cours…", "Operation in progress…");
            try
            {
                async Task Maintain(Func<Task> operation)
                { if (scheduler != null) await scheduler.RunMaintenanceAsync(operation); else await operation(); }
                if (clear)
                {
                    var resetChatIds = await ReadStoreAsync(store => store.Chats.AsNoTracking().Select(x => x.Id).ToList());
                    foreach (var window in agentSettingsWindows.Values.ToArray()) window.Close();
                    memoryDatabaseWindow?.Close(); consumptionWindow?.Close(); aiDetectorWindow?.Close(); tasksWindow?.Close();
                    foreach (var id in conversationBrowsers.Keys.Select(x => x.ChatId).Distinct().ToArray()) CloseConversationBrowser(id);
                    foreach (var id in resetChatIds) await terminals.RemoveChatAsync(id);
                    await Maintain(() => Task.Run(() => DatabaseMaintenance.ResetProjectDataAsync(HarnessDb.DatabasePath)));
                    chatNotices.Clear(); conversationDrafts.Clear(); conversationHistory.Clear(); conversationStatuses.Clear(); chatSearchAnchors.Clear();
                    activityProjects.Clear(); pendingChatAttention.Clear(); completedUnreadChats.Clear(); failedUnreadChats.Clear();
                    expandedSidebarProjects.Clear(); expandedSidebarArchives.Clear(); subagentViews.Clear();
                    project = null; chat = null; db.ChangeTracker.Clear();
                    loading = true; owner.Close(); await InitializeAsync();
                    ShowStatus(WorkflowText("Données réinitialisées.", "Data reset."), StatusKind.Notice);
                }
                else
                {
                    await Maintain(() => Task.Run(() => DatabaseMaintenance.OptimizeAsync(HarnessDb.DatabasePath)));
                    information.Text = WorkflowText("Base de données optimisée.", "Database optimized.");
                }
            }
            catch (Exception ex) { information.Text = ex.Message; AppLog.Write(AppLogLevel.Error, "database.maintenance_failed", ex); }
            finally { databaseMaintenanceBusy = false; optimize.IsEnabled = reset.IsEnabled = true; shell.IsEnabled = true; scheduleTimer.Start(); }
        }
        optimize.Click += async (_, _) => await Perform(false);
        reset.Click += async (_, _) => await Perform(true);
        return panel;
    }
}
