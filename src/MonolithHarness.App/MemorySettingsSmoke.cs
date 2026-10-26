using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeMemorySettingsAsync(FrameworkElement settingsRoot, string output)
    {
        static IEnumerable<FrameworkElement> Find(DependencyObject parent)
        {
            if (parent is FrameworkElement element) yield return element;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var descendant in Find(VisualTreeHelper.GetChild(parent, i))) yield return descendant;
        }
        static void Click(Button button) => (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new InvalidOperationException("Button invocation unavailable")).Invoke();
        var navigation = Find(settingsRoot).OfType<SettingsNavigation>().Single();
        // Find the Memory page by content: its position changes as settings pages are added.
        FrameworkElement? memory = null;
        for (var page = 0; page < 64 && memory == null; page++)
        {
            navigation.SelectedIndex = page;
            if (navigation.SelectedIndex != page) break;
            await Task.Delay(50);
            memory = Find(settingsRoot).SingleOrDefault(x => Equals(x.Tag, "memory-settings"));
        }
        if (memory == null) throw new Exception("Memory settings page was not found.");
        await Task.Delay(100);
        if (Find(memory).OfType<ToggleSwitch>().Count(x => x.Tag is "memory-force-read" or "memory-force-write" && !x.IsOn) != 2)
            throw new Exception("Forced memory read/write toggles are missing or not disabled by default.");
        Click(Find(memory).OfType<Button>().Single(x => x.Content?.ToString() == "Voir la mémoire"));
        for (var attempt = 0; attempt < 100 && memoryDatabaseWindow?.Content is not DependencyObject; attempt++) await Task.Delay(20);
        if (memoryDatabaseWindow?.Content is not DependencyObject viewer) throw new Exception("Memory database window did not open.");
        // The filter row lives in a ScrollViewer, which only exposes its content once the window is laid out.
        bool HasFilters() => Find(viewer).OfType<TextBox>().Any(x => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(x) == "Filtrer Content");
        for (var attempt = 0; attempt < 100 && !HasFilters(); attempt++) await Task.Delay(20);
        if (!HasFilters()) throw new Exception("Memory database filters are missing.");
        Click(Find(viewer).OfType<Button>().Single(x => x.Content?.ToString() == "Fermer"));
        Click(Find(memory).OfType<Button>().Single(x => x.Content?.ToString() == "Modifier les mémoires…"));
        for (var attempt = 0; attempt < 100 && !Find(memory).OfType<TextBlock>().Any(x => x.Text == "Aucune mémoire trouvée."); attempt++) await Task.Delay(20);
        if (!Find(memory).OfType<TextBlock>().Any(x => x.Text == "Aucune mémoire trouvée.")) throw new Exception("Memory viewer failed to load.");
        Click(Find(memory).OfType<Button>().Single(x => x.Content?.ToString() == "Créer une mémoire"));
        await Task.Delay(50);
        Find(memory).OfType<ComboBox>().Single(x => Equals(x.Tag, "memory-edit-scope")).SelectedIndex = 1;
        Find(memory).OfType<ComboBox>().Single(x => Equals(x.Tag, "memory-edit-category")).SelectedIndex = 2;
        Find(memory).OfType<TextBox>().Single(x => x.Header?.ToString() == "Clé stable").Text = "smoke.preference";
        var title = Find(memory).OfType<TextBox>().Single(x => x.Header?.ToString() == "Titre"); title.Text = "Préférence de réponse";
        var content = Find(memory).OfType<TextBox>().Single(x => x.Header?.ToString() == "Information mémorisée"); content.Text = "Des explications concises avec exemples.";
        var save = Find(memory).OfType<Button>().Single(x => x.Content?.ToString() == "Enregistrer la mémoire");
        var store = new MemoryStore(HarnessDb.DatabasePath); var access = new MemoryAccess(project!.Id, chat!.Id);
        Click(save);
        MemoryHit? saved = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            saved = (await store.SearchAsync(access, "smoke.preference")).Items.SingleOrDefault();
            if (saved != null && Find(memory).OfType<TextBlock>().Any(x => x.Text.Contains($"#{saved.Id} · v1"))) break;
            await Task.Delay(20);
        }
        if (saved == null || saved.Scope != "shared" || saved.Category != "user") throw new Exception("Memory editor did not save the requested scope/category.");
        content.Text = "Une préférence modifiée et persistante.";
        Click(save);
        for (var attempt = 0; attempt < 100 && (await store.ReadAsync(access, saved.Id)).Version != 2; attempt++) await Task.Delay(20);
        if ((await store.ReadAsync(access, saved.Id)).Version != 2) throw new Exception("Memory editor did not update the existing entry.");
        var search = Find(memory).OfType<TextBox>().Single(x => x.PlaceholderText?.StartsWith("Rechercher dans") == true);
        search.Text = "persist";
        await Task.Delay(450);
        if (!Find(memory).OfType<TextBlock>().Any(x => x.Text.Contains("[persistante]"))) throw new Exception("Memory UI did not display the indexed search excerpt.");
        Click(Find(memory).OfType<Button>().Single(x => x.Content?.ToString() == "Fermer"));
        search.StartBringIntoView();
        await Task.Delay(100);
        await Capture(settingsRoot, Path.Combine(output, "memory-settings.png"));
        await store.DeleteAsync(access, saved.Id, 2);
        navigation.SelectedIndex = 0;
        await Task.Delay(100);
    }
}
