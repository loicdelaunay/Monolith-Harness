using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task SmokeAcpProviders(string output)
    {
        var passed = new List<string>();
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); passed.Add(name); }
        static IEnumerable<FrameworkElement> Descendants(DependencyObject parent)
        {
            if (parent is FrameworkElement element) yield return element;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
        UiText.Language = state.Language = "fr"; ApplyTheme("fluent-dark");
        var originalCount = db.Providers.Local.Count;
        var form = BuildProviderEditor(state.ProviderId);
        var area = new StackPanel { Spacing = 16, Margin = new(24) }; area.Children.Add(form.Header); area.Children.Add(form.Panel);
        var surface = new ScrollViewer { Content = area }; root.Children.Clear(); root.Children.Add(surface);
        await Task.Delay(150);
        var add = Descendants(area).OfType<DropDownButton>().Single(x => x.Name == "AddProvider");
        var menu = (MenuFlyout)add.Flyout;
        foreach (var preset in AcpProviders.Presets)
        {
            menu.ShowAt(add); await Task.Delay(100);
            var item = menu.Items.OfType<MenuFlyoutItem>().Single(x => x.Text == "+ " + preset.Name);
            (new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke) as IInvokeProvider ?? throw new Exception("ACP preset invocation unavailable")).Invoke();
            menu.Hide(); await Task.Delay(100);
            Check(form.Selected?.Kind == preset.Kind && form.Selected.Model == "default", preset.Name + " creates an ACP draft with the agent default model.");
            var launch = Descendants(area).OfType<Expander>().Single(x => x.Name == "AcpLaunchOptions");
            Check(!launch.IsExpanded, "ACP technical launch options are collapsed by default."); launch.IsExpanded = true; await Task.Delay(80);
            var fields = Descendants(area).OfType<TextBox>().ToList();
            Check(fields.Single(x => x.Header?.ToString() == "URL de base de l’API").Visibility == Visibility.Collapsed &&
                Descendants(area).OfType<PasswordBox>().All(x => x.Visibility == Visibility.Collapsed), preset.Name + " hides HTTP and API-key fields.");
            Check(fields.Any(x => x.Header?.ToString() == "Exécutable ACP personnalisé (facultatif)" && x.Visibility == Visibility.Visible), "Custom executable field remains available.");
            var account = Descendants(area).OfType<Button>().Single(x => x.Name == "AcpConnectAccount");
            Check((account.Visibility == Visibility.Visible) == (preset.Kind == "chatgpt-acp"), "ChatGPT offers account sign-in only for the subscription connector.");
            form.Commit(); Check(ValidateProviderDrafts(form) == null, "ACP drafts validate without an HTTP URL or API key.");
            var modelPicker = Descendants(area).OfType<ComboBox>().Single(x => x.Header?.ToString() == "Identifiant du modèle");
            Check(modelPicker.SelectedItem?.ToString() == "default", "The model picker visibly selects the agent default.");
            launch.IsExpanded = false; await Task.Delay(450);
            await Capture(root, Path.Combine(output, preset.Kind + ".png"));
        }
        var saved = await SaveProviderDraftsAsync(form); state.ProviderId = saved!.Id;
        Check(db.Providers.Local.Count == originalCount + 2 && db.Providers.Local.Where(x => x.IsAcp).All(x => x.ProtectedKey.Length == 0), "Both ACP connections save without API credentials or deleting existing providers.");
        var welcome = BuildWelcomeProvider(); Check(welcome.Validate() == null, "Welcome form restores the saved ACP connection.");
        File.WriteAllLines(Path.Combine(output, "smoke-ok.txt"), passed);
    }
}
