using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeConversationWorkspace(string output)
    {
        static IEnumerable<FrameworkElement> Walk(DependencyObject parent)
        {
            if (parent is FrameworkElement element) yield return element;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Walk(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
        static void Click(Button button) => (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider ?? throw new Exception("Button automation unavailable.")).Invoke();
        var settings = FeatureSettings.Read(state.FeaturesJson); settings.NotificationSound = false; settings.GuiCheckUpdates = false; state.FeaturesJson = settings.Json();
        var fixture = new Project { Name = "OhMyHarness", Chats = [
            new() { Title = "Améliorer la liste des conversations", UpdatedUtc = DateTime.UtcNow.AddDays(-3) },
            new() { Title = "Réglages des agents et de leurs rôles", UpdatedUtc = DateTime.UtcNow.AddDays(-10) },
            new() { Title = "Traduction et correction des emails", UpdatedUtc = DateTime.UtcNow.AddDays(-10) },
            new() { Title = "Benchmark visuel du modèle", UpdatedUtc = DateTime.UtcNow.AddDays(-10) },
            new() { Title = "Barre d’information", UpdatedUtc = DateTime.UtcNow.AddDays(-12) },
            new() { Title = "Ancienne conversation", IsArchived = true, UpdatedUtc = DateTime.UtcNow.AddDays(-20) }
        ] };
        db.Projects.AddRange(fixture, new Project { Name = "MangaReader" }, new Project { Name = "Website" }, new Project { Name = "BrianBrecon" });
        await db.SaveChangesAsync();
        loading = true; projects.ItemsSource = await db.Projects.OrderBy(x => x.Id).ToListAsync(); projects.SelectedItem = fixture; loading = false;
        state.ChatId = fixture.Chats[0].Id; await SelectProject(); root.UpdateLayout(); await Task.Delay(250);
        if (conversationArea.Parent is not Border || projectNavigation.Children.Count < 5) throw new Exception("Project navigation groups unavailable.");
        var current = chat!;
        collapsedSidebarProjects.Add(fixture.Id); RebuildProjectNavigation(); root.UpdateLayout();
        if (conversationArea.Parent != null) throw new Exception("Project did not collapse.");
        collapsedSidebarProjects.Remove(fixture.Id); RebuildProjectNavigation(); root.UpdateLayout(); await Task.Delay(100);
        archiveExpander.IsExpanded = true; root.UpdateLayout(); await Task.Delay(100);
        archiveExpander.IsExpanded = false; root.UpdateLayout(); await Task.Delay(100);
        archiveExpander.IsExpanded = true; root.UpdateLayout(); await Task.Delay(150);
        if (archivedChats.ActualHeight <= 0 || archivedChats.Items.Count != 1) throw new Exception("Archive did not reopen with a bounded viewport.");
        archiveExpander.IsExpanded = false; await Task.Delay(300); root.UpdateLayout();
        await Capture((FrameworkElement)shell.Pane, Path.Combine(output, "sidebar.png"));
        namingChats.Add(current.Id); RefreshNamingState(); root.UpdateLayout();
        if (namingSpinner.Visibility != Visibility.Visible || title.Text != "Nommage…") throw new Exception("Naming spinner missing.");
        await Capture(root, Path.Combine(output, "naming.png"));
        namingChats.Remove(current.Id); RefreshNamingState();
        using (var run = new ConversationRun(current, fixture, new(), state, "", []) { Messages = messages })
        {
            NotifyChat(run, true);
            if (!chatNotices.Any(x => x.ChatId == current.Id && x.ActionRequired)) throw new Exception("Action notification missing.");
            NotifyChat(run, false);
            if (chatNotices.Count != 1 || chatNotices[0].ActionRequired) throw new Exception("Completion notification missing or duplicated.");
        }
        root.UpdateLayout(); await Capture(root, Path.Combine(output, "notifications.png"));
        AcknowledgeChatNotice(current.Id);
        composer.Text = "beforeafter"; composer.Select(6, 0); await HandleComposerEnterAsync(true);
        if (!composer.Text.Replace("\r\n", "\n").Replace("\r", "\n").Equals("before\nafter")) throw new Exception("Shift+Enter did not insert a line at the caret.");
        composer.Text = "";
        var separate = new Grid(); var sample = new TextBlock { Text = "Zoom", FontSize = 20 }; separate.Children.Add(sample); TextZoom.Observe(separate);
        TextZoom.Apply(separate); TextZoom.SetWindow(separate, 130);
        if (Math.Abs(sample.FontSize - 26) > .1 || TextZoom.ForWindow(root) != 100) throw new Exception("Local zoom affected another window.");
        TextZoom.Set(110);
        if (Math.Abs(sample.FontSize - 22) > .1 || TextZoom.ForWindow(root) != 110) throw new Exception("Global zoom did not synchronize windows.");
        TextZoom.Set(100);
        await ConfigureAgentsAsync(current); await Task.Delay(250);
        var agentWindow = agentSettingsWindows[current.Id];
        var agentRoot = agentWindow.Content as FrameworkElement ?? throw new Exception("Agent window content unavailable.");
        var controls = Walk(agentRoot).ToList();
        controls.OfType<ToggleSwitch>().Single(x => Equals(x.Header, "Nombre automatique")).IsOn = false;
        controls.OfType<NumberBox>().Single().Value = 3;
        controls.OfType<ComboBox>().Single(x => Equals(x.Header, "Mode sous-agents")).SelectedIndex = 2;
        controls.OfType<TextBox>().Single(x => Equals(x.Header, "Nom du preset")).Text = "Équipe de vérification";
        Click(controls.OfType<Button>().Single(x => Equals(x.Content, "Sauvegarder"))); await Task.Delay(200);
        if (!FeatureSettings.Read(state.FeaturesJson).AgentPresets.Any(x => x.Name == "Équipe de vérification")) throw new Exception("Preset was not saved.");
        await Capture(agentRoot, Path.Combine(output, "agent-settings.png"));
        Click(controls.OfType<Button>().Single(x => Equals(x.Content, "Enregistrer"))); await Task.Delay(200);
        if (agentSettingsWindows.ContainsKey(current.Id) || ConversationAgents.Read(current.AgentOptionsJson).Count != 3 || current.OrchestrationMode != "forced") throw new Exception("Conversation agent settings were not saved.");
        await SelectProject();
        if (chat?.Id != current.Id || ConversationAgents.Read(chat.AgentOptionsJson).Count != 3) throw new Exception("Agent settings lost on reload.");
        await ConfigureAgentsAsync(chat); await Task.Delay(200);
        agentWindow = agentSettingsWindows[current.Id]; agentRoot = agentWindow.Content as FrameworkElement ?? throw new Exception("Agent window content unavailable."); controls = Walk(agentRoot).ToList();
        controls.OfType<ComboBox>().Single(x => Equals(x.Header, "Preset enregistré")).SelectedIndex = 0;
        Click(controls.OfType<Button>().First(x => Equals(x.Content, "Supprimer"))); await Task.Delay(200);
        if (FeatureSettings.Read(state.FeaturesJson).AgentPresets.Count != 0) throw new Exception("Preset deletion failed.");
        agentWindow.Close();
        var notificationPanel = BuildNotificationSettings();
        var overlay = new Border { Width = 680, Padding = new(24), Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush"), Child = notificationPanel.Panel };
        root.Children.Add(overlay); await Task.Delay(120); await Capture(root, Path.Combine(output, "notification-settings.png")); root.Children.Remove(overlay);
        File.WriteAllText(Path.Combine(output, "smoke-ok.txt"), "Project groups, archive reopening, naming spinner, completion/action bells, Shift+Enter, local/global font zoom, agent count/mode persistence and preset save/delete passed.");
    }
}
