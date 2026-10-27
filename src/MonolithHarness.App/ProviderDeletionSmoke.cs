using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    // Deletes connections that are in use (current provider, quick level) and saves, twice in a row.
    async Task SmokeProviderDeletion(string output)
    {
        var passed = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed.Add(name); }
        static IEnumerable<FrameworkElement> Descendants(DependencyObject? parent)
        {
            if (parent == null) yield break;
            if (parent is FrameworkElement element) yield return element;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
        static void Click(Button button) => (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new InvalidOperationException("Button invocation unavailable.")).Invoke();
        static async Task Wait(Func<bool> condition, string what)
        {
            for (var attempt = 0; attempt < 400; attempt++) { if (condition()) return; await Task.Delay(25); }
            throw new TimeoutException(what);
        }

        UiText.Language = state.Language = "fr";
        var features = FeatureSettings.Read(state.FeaturesJson); features.GuiUpdateMode = AutomaticUpdateMode.Disabled;
        Provider Fixture(string name) => new() { Name = name, BaseUrl = "https://api.openai.com/v1", Model = "model-a",
            DetectedModelsJson = "[\"model-a\"]", SelectedModelsJson = "[\"model-a\"]" };
        var keep = Fixture("Connexion conservée"); var first = Fixture("Connexion supprimée A"); var second = Fixture("Connexion supprimée B");
        var shared = Fixture("Connexion du serveur");
        db.Providers.AddRange(keep, first, second, shared); await db.SaveChangesAsync();
        rememberedServerProviders.Add(shared.Id);
        provider = first; state.ProviderId = first.Id;
        features.QuickModelLevelsEnabled = true;
        features.QuickModelLevels = [new() { Name = "Rapide", ProviderId = first.Id, Model = "model-a" }, new() { Name = "Conservé", ProviderId = keep.Id, Model = "model-a" }];
        state.FeaturesJson = features.Json(); ConfigureAutomaticUpdates(); PopulateModelSelector();

        async Task DeleteAndSave(Provider target, string round)
        {
            var run = Settings(showProviders: true);
            bool Listed(string name) => settingsNavigation != null && Descendants(settingsNavigation).OfType<Button>().Any(x => AutomationProperties.GetName(x).StartsWith(name + " · ", StringComparison.Ordinal));
            await Wait(() => settingsWindow?.Content is Grid && Listed(target.Name), round + ": the provider list did not open.");
            var window = settingsWindow!; var navigation = settingsNavigation!; var settingsRoot = (FrameworkElement)window.Content!;
            Click(Descendants(navigation).OfType<Button>().First(x => AutomationProperties.GetName(x).StartsWith(target.Name + " · ", StringComparison.Ordinal)));
            await Task.Delay(100);
            var actions = Descendants(navigation).OfType<Button>().Single(x => x.Name == "ProviderConnectionActions");
            var remove = ((MenuFlyout)actions.Flyout).Items.OfType<MenuFlyoutItem>().Single(x => x.Text == "Supprimer la connexion");
            (new MenuFlyoutItemAutomationPeer(remove).GetPattern(PatternInterface.Invoke) as IInvokeProvider
                ?? throw new InvalidOperationException("Menu invocation unavailable.")).Invoke();
            await Wait(() => !Listed(target.Name), round + ": the deleted connection is still listed.");
            Click(Descendants(settingsRoot).OfType<Button>().Single(x => x.Content?.ToString() == UiText.T("Enregistrer")));
            if (await Task.WhenAny(run, Task.Delay(15000)) != run)
            {
                await Capture(settingsRoot, Path.Combine(output, "provider-deletion-blocked.png"));
                var messages = Descendants(settingsRoot).OfType<TextBlock>().Where(x => x.Visibility == Visibility.Visible && x.Text.Length > 0
                    && (x.Text.Contains("Vérifiez", StringComparison.Ordinal) || x.Foreground is SolidColorBrush { Color.R: > 180, Color.G: < 140 })).Select(x => x.Text);
                throw new InvalidOperationException(round + ": settings were not saved (page " + navigation.SelectedIndex + "): " + string.Join(" | ", messages));
            }
            await run;
            await using var stored = new HarnessDb();
            Check(!stored.Providers.Any(x => x.Id == target.Id), round + ": the provider is removed from the database.");
            Check(settingsWindow == null && !editingSettings, round + ": settings close after saving.");
            Check(FeatureSettings.Read(state.FeaturesJson).QuickModelLevels.All(x => x.ProviderId != target.Id), round + ": quick levels no longer reference the provider.");
        }
        var serverRun = Settings(showProviders: true);
        await Wait(() => settingsWindow?.Content is Grid && settingsNavigation != null && Descendants(settingsNavigation).OfType<Button>()
            .Any(x => AutomationProperties.GetName(x).StartsWith(shared.Name + " · Serveur Monolith", StringComparison.Ordinal)), "Server providers are not labelled in the list.");
        Click(Descendants(settingsNavigation!).OfType<Button>().First(x => AutomationProperties.GetName(x).StartsWith(shared.Name + " · ", StringComparison.Ordinal)));
        await Task.Delay(100);
        var serverActions = Descendants(settingsNavigation!).OfType<Button>().Single(x => x.Name == "ProviderConnectionActions");
        var serverRemove = ((MenuFlyout)serverActions.Flyout).Items.OfType<MenuFlyoutItem>().Single(x => x.Text == "Supprimer la connexion");
        Check(!serverRemove.IsEnabled && Descendants(settingsNavigation!).OfType<InfoBar>().Any(x => x.Name == "ServerManagedProvider" && x.IsOpen)
            && !Descendants(settingsNavigation!).OfType<TextBox>().Single(x => x.Header?.ToString() == "Nom de la connexion").IsEnabled,
            "A server provider is read-only and cannot be deleted from the editor.");
        Check(Descendants(Descendants(settingsNavigation!).OfType<ProviderConnectionLayout>().Single().Connection).OfType<TextBox>().Any(x => x.Header?.ToString() == UiText.T("URL de base de l’API")),
            "The API base URL is on the Connection page.");
        Click(Descendants((FrameworkElement)settingsWindow!.Content!).OfType<Button>().Single(x => x.Content?.ToString() == UiText.T("Enregistrer")));
        await serverRun.WaitAsync(TimeSpan.FromSeconds(15));
        await using (var stored = new HarnessDb()) Check(stored.Providers.Any(x => x.Id == shared.Id), "Saving keeps the server provider.");
        await DeleteAndSave(first, "Deleting the current provider");
        Check(provider?.Id != first.Id && state.ProviderId != first.Id, "The chat switches to a remaining provider.");
        await DeleteAndSave(second, "Deleting another provider afterwards");
        File.WriteAllLines(Path.Combine(output, "smoke-ok.txt"), passed);
    }
}
