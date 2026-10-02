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
    async Task SmokeProviderNavigation(string output)
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
        static T Find<T>(DependencyObject parent, string name) where T : FrameworkElement => Descendants(parent).OfType<T>().Single(x => x.Name == name);
        static void Click(Button button) => (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new InvalidOperationException("Button invocation unavailable.")).Invoke();
        static async Task Wait(Func<bool> condition)
        {
            for (var attempt = 0; attempt < 200; attempt++) { if (condition()) return; await Task.Delay(25); }
            throw new TimeoutException("Provider navigation did not reach the expected state.");
        }
        static double Top(FrameworkElement element, FrameworkElement parent) => element.TransformToVisual(parent).TransformPoint(new(0, 0)).Y;

        UiText.Language = state.Language = "fr"; ApplyTheme("fluent-dark");
        var features = FeatureSettings.Read(state.FeaturesJson); features.GuiUpdateMode = AutomaticUpdateMode.Disabled;
        state.FeaturesJson = features.Json(); ConfigureAutomaticUpdates();
        var fixtures = Enumerable.Range(1, 20).Select(i => new Provider
        {
            Name = $"Navigation fixture {i:00}", BaseUrl = "https://api.openai.com/v1", Model = "model-a",
            DetectedModelsJson = "[\"model-a\",\"model-b\"]", SelectedModelsJson = "[\"model-a\",\"model-b\"]"
        }).ToArray();
        db.Providers.AddRange(fixtures); await db.SaveChangesAsync(); provider = fixtures[0]; PopulateModelSelector();
        var providerCount = db.Providers.Local.Count;

        var cancelled = Settings(showProviders: true); settingsWindow!.Close(); await cancelled;
        Check(settingsWindow == null && settingsNavigation == null && !providersRequested && !editingSettings,
            "Closing during loading clears the provider destination and leaves no orphan settings window.");
        var loading = Settings();
        Check(settingsWindow?.Content is Border, "Settings show a loading placeholder immediately.");
        await Settings(showProviders: true);
        await Wait(() => settingsNavigation != null && settingsWindow?.Content is Grid);
        Check(settingsNavigation!.SelectedIndex == settingsProviderTab, "A provider request during loading selects Providers once settings are ready.");
        settingsWindow!.Close(); await loading;

        OpenModelOptions(); await Task.Delay(150);
        var modelPanel = (FrameworkElement)modelOptionsFlyout!.Content;
        var shortcut = Find<Button>(modelPanel, "ProviderSettingsButton");
        Check(AutomationProperties.GetName(shortcut) == "Réglages des fournisseurs" && ToolTipService.GetToolTip(shortcut)?.ToString() == "Réglages des fournisseurs", "The model shortcut has a French accessible name and tooltip.");
        Check(Grid.GetColumn(shortcut) + 1 == Grid.GetColumn(refreshModelsBtn), "The settings icon sits immediately before model refresh.");
        await Capture(modelPanel, Path.Combine(output, "model-provider-shortcut.png"));
        var closed = false; modelOptionsFlyout.Closed += (_, _) => closed = true;
        Click(shortcut);
        await Wait(() => closed && settingsNavigation != null && settingsWindow?.Content is Grid &&
            Descendants(settingsNavigation).OfType<DropDownButton>().Any(x => x.Name == "AddProvider"));
        var owner = settingsWindow!; var settingsRoot = (FrameworkElement)owner.Content!;
        var navigation = settingsNavigation!;
        Check(navigation.SelectedIndex == settingsProviderTab, "The model gear closes its flyout and opens Settings directly on Providers.");
        var body = Find<ScrollViewer>(navigation, "SettingsPageBody");
        var add = Find<DropDownButton>(navigation, "AddProvider");
        var back = Find<Button>(navigation, "BackToProviders");
        var title = Descendants(navigation).OfType<TextBlock>().Single(x => x.FontSize == 24 && x.Text == UiText.T("Fournisseurs"));
        await Task.Delay(150); await Capture(settingsRoot, Path.Combine(output, "providers-list-top.png"));
        Check(back.Visibility == Visibility.Collapsed && body.ScrollableHeight > 300, "The provider list scrolls and hides its editor-only Back action.");
        var listTitleTop = Top(title, settingsRoot); var listAddTop = Top(add, settingsRoot);
        body.ChangeView(null, body.ScrollableHeight, null, true); await Wait(() => body.VerticalOffset > 300);
        Check(Math.Abs(Top(title, settingsRoot) - listTitleTop) < 1 && Math.Abs(Top(add, settingsRoot) - listAddTop) < 1,
            "The title and Add provider stay fixed while the provider list scrolls.");
        await Capture(settingsRoot, Path.Combine(output, "providers-list-scrolled.png"));
        var lastGear = Descendants((DependencyObject)body.Content).OfType<Button>().Single(x => AutomationProperties.GetName(x) == fixtures[^1].Name);
        Click(lastGear); await Task.Delay(150);
        Check(body.VerticalOffset < 1 && back.Visibility == Visibility.Visible, "Opening a provider returns its form to the top and exposes the fixed Back action.");
        var name = Descendants((DependencyObject)body.Content).OfType<TextBox>().Single(x => x.Header?.ToString() == UiText.T("Nom du fournisseur"));
        var key = Descendants((DependencyObject)body.Content).OfType<PasswordBox>().Single();
        name.Text = "Provider draft retained"; key.Password = "offline-smoke-draft";
        await Capture(settingsRoot, Path.Combine(output, "provider-editor-top.png"));
        var titleTop = Top(title, settingsRoot); var addTop = Top(add, settingsRoot); var backTop = Top(back, settingsRoot);
        Check(body.ScrollableHeight > 0, "A long provider form has scrollable content below its fixed header.");
        body.ChangeView(null, body.ScrollableHeight, null, true); await Wait(() => body.VerticalOffset > 0);
        Check(Math.Abs(Top(title, settingsRoot) - titleTop) < 1 && Math.Abs(Top(add, settingsRoot) - addTop) < 1 && Math.Abs(Top(back, settingsRoot) - backTop) < 1,
            "The title, Add provider and Back stay fixed while editing a scrolled provider.");
        await Capture(settingsRoot, Path.Combine(output, "provider-editor-scrolled.png"));

        navigation.SelectedIndex = 0; Activate(); OpenModelOptions(); await Task.Delay(100); Click(shortcut);
        await Wait(() => navigation.SelectedIndex == settingsProviderTab);
        Check(ReferenceEquals(owner, settingsWindow) && ReferenceEquals(navigation, settingsNavigation) && name.Text == "Provider draft retained" && key.Password == "offline-smoke-draft",
            "The shortcut reuses an open settings window and retains unsaved provider fields when switching tabs.");
        body.ChangeView(null, body.ScrollableHeight, null, true); await Task.Delay(100); Click(back); await Task.Delay(150);
        Check(body.VerticalOffset < 1 && back.Visibility == Visibility.Collapsed && add.Visibility == Visibility.Visible,
            "Sticky Back returns to the top of the list and leaves Add provider available.");
        var retainedGear = Descendants((DependencyObject)body.Content).OfType<Button>().Single(x => AutomationProperties.GetName(x) == "Provider draft retained");
        Click(retainedGear); await Task.Delay(100);
        Check(name.Text == "Provider draft retained" && key.Password == "offline-smoke-draft", "Returning to a provider preserves its name and pending key draft.");
        body.ChangeView(null, body.ScrollableHeight, null, true); await Task.Delay(100);
        var presets = (MenuFlyout)add.Flyout; presets.ShowAt(add); await Task.Delay(100);
        var deepSeek = presets.Items.OfType<MenuFlyoutItem>().Single(x => x.Text == "+ DeepSeek");
        (new MenuFlyoutItemAutomationPeer(deepSeek).GetPattern(PatternInterface.Invoke) as IInvokeProvider
            ?? throw new InvalidOperationException("Provider preset invocation unavailable.")).Invoke();
        presets.Hide(); await Task.Delay(150);
        Check(name.Text == "DeepSeek 2" && body.VerticalOffset < 1 && back.Visibility == Visibility.Visible,
            "Adding a preset from the fixed header opens its new form at the top after scrolling.");

        owner.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 640, Height = 760 });
        ApplyTheme("fluent-light"); await Task.Delay(200); await Capture(settingsRoot, Path.Combine(output, "provider-header-narrow-light.png"));
        await Wait(() => settingsRoot.ActualWidth > 0 && settingsRoot.ActualWidth <= 640);
        await Capture(settingsRoot, Path.Combine(output, "provider-header-narrow-light.png"));
        var addPosition = add.TransformToVisual(settingsRoot).TransformPoint(new(0, 0));
        Check(add.ActualWidth > 0 && addPosition.X >= 0 && addPosition.X + add.ActualWidth <= settingsRoot.ActualWidth + 1,
            "The fixed provider actions remain inside a narrow settings window.");
        UiText.Language = state.Language = "en"; Activate(); OpenModelOptions(); await Task.Delay(100);
        Check(AutomationProperties.GetName(shortcut) == "Provider settings" && ToolTipService.GetToolTip(shortcut)?.ToString() == "Provider settings",
            "Reopening the model flyout updates the shortcut's accessible name and tooltip to English.");
        modelOptionsFlyout.Hide(); UiText.Language = state.Language = "fr";
        var cancel = Descendants(settingsRoot).OfType<Button>().Single(x => x.Content?.ToString() == UiText.T("Annuler"));
        Click(cancel); await Wait(() => !editingSettings);
        Check(settingsWindow == null && settingsNavigation == null && settingsProviderTab == -1, "Closing settings clears its navigation target.");
        Check(db.Providers.Local.Count == providerCount && fixtures[^1].Name == "Navigation fixture 20",
            "Cancelling settings discards added providers and edited drafts without modifying stored providers.");
        File.WriteAllLines(Path.Combine(output, "smoke-ok.txt"), passed);
    }
}
