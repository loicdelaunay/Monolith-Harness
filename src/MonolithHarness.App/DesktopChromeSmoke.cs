using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Foundation;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    // This fixture runs in a real, isolated desktop window. Native hit tests are
    // essential here: invoking a Button peer alone misses title-bar drag overlays.
    static class ChromeTestNative
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
        internal static nint Point(int x, int y) => (nint)((y & 0xffff) << 16 | (x & 0xffff));
    }
    async Task SmokeDesktopChrome(string output)
    {
        var passed = new List<string>(); var measurements = new List<object>(); var hits = new List<object>();
        void Report() => File.WriteAllText(Path.Combine(output, "desktop-chrome-results.json"),
            JsonSerializer.Serialize(new { checks = passed, measurements, hits }, new JsonSerializerOptions { WriteIndented = true }));
        void Check(bool valid, string name)
        {
            if (!valid) { Report(); throw new InvalidOperationException(name); }
            passed.Add(name);
        }
        static IEnumerable<FrameworkElement> Elements(DependencyObject parent)
        {
            if (parent is FrameworkElement element) yield return element;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Elements(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
        FrameworkElement FullVisual()
        {
            FrameworkElement top = root;
            for (DependencyObject? parent = root; parent != null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is FrameworkElement element) top = element;
                if (parent.GetType().Name == "WindowChrome") return top;
            }
            return top;
        }
        Rect Bounds(FrameworkElement element) => element.TransformToVisual(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        ApplicationWindow OwnWindow() => DesktopApplications.List().Where(w => w.ProcessId == Environment.ProcessId && w.Visible && !w.Minimized)
            .OrderBy(w => Math.Abs(w.Width - root.ActualWidth) + Math.Abs(w.Height - root.ActualHeight)).First();
        async Task Shot(string name)
        {
            root.UpdateLayout(); await Task.Delay(100);
            if (!OperatingSystem.IsWindows()) { await Capture(FullVisual(), Path.Combine(output, name + ".png")); return; }
            var snapshot = await CaptureApplicationPixelsAsync(OwnWindow(), DesktopInterop.GetCursorSnapshot(), CancellationToken.None);
            using var bitmap = new SkiaSharp.SKBitmap(snapshot.Width, snapshot.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
            Marshal.Copy(snapshot.Pixels, 0, bitmap.GetPixels(), snapshot.Pixels.Length);
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap); using var png = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(Path.Combine(output, name + ".png"), png.ToArray());
        }
        async Task Wait(Func<bool> condition, string reason)
        {
            for (var i = 0; i < 200; i++) { if (condition()) return; await Task.Delay(25); }
            throw new TimeoutException(reason);
        }
        int NativeHit(FrameworkElement element)
        {
            using var dpi = DesktopApplications.PhysicalCoordinates();
            var window = (nint)OwnWindow().NativeId;
            var client = DesktopInterop.ClientArea(window);
            var point = element.TransformToVisual(null).TransformPoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
            var scale = root.XamlRoot!.RasterizationScale;
            return (int)ChromeTestNative.SendMessageW(window, 0x84, 0,
                ChromeTestNative.Point(client.X + (int)Math.Round(point.X * scale), client.Y + (int)Math.Round(point.Y * scale)));
        }
        async Task NativeClick(Button button, string name)
        {
            Check(button.Visibility == Visibility.Visible && button.IsEnabled && button.ActualWidth > 0, name + " is visible and enabled.");
            var hit = NativeHit(button); hits.Add(new { name, hit });
            Check(hit == 1, name + " receives native client input (not caption drag).");
            // Check both native and XAML hit testing, then invoke the real control.
            // This headed UI test stays independent of the user's foreground window.
            var point = button.TransformToVisual(null).TransformPoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2));
            // Uno 6.7's default helper skips transparent containers. Include their
            // descendants and separately verify effective visibility/input eligibility.
            var targets = VisualTreeHelper.FindElementsInHostCoordinates(point, FullVisual(), true).ToArray();
            bool enabledPath = true;
            for (DependencyObject? parent = button; parent != null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is UIElement e && (!e.IsHitTestVisible || e.Visibility != Visibility.Visible)) enabledPath = false;
            Check(targets.Contains(button) && enabledPath, name + " has visible, input-enabled XAML geometry.");
            (new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) as IInvokeProvider
                ?? throw new InvalidOperationException("The control has no native invocation provider.")).Invoke();
            await Task.Delay(180);
        }
        try
        {
            Check(OperatingSystem.IsWindows(), "The regression runs against the real Windows desktop host.");
            UiText.Language = state.Language = "fr"; ApplyLanguage(); ApplyTheme("fluent-dark");
            var config = FeatureSettings.Read(state.FeaturesJson); config.GuiUpdateMode = AutomaticUpdateMode.Disabled;
            state.FeaturesJson = config.Json(); ConfigureAutomaticUpdates();
            var source = Path.Combine(output, "sources"); Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "demo.cs"), "// Desktop chrome fixture\nConsole.WriteLine(\"Monolith\");\n");
            var fixture = new Project { Name = "Projet de démonstration", Chats = [new Chat { Title = "Vérification de la barre et des outils" }] };
            fixture.SetSourceFolders([source]); db.Projects.Add(fixture); await db.SaveChangesAsync();
            loading = true; projects.ItemsSource = new[] { fixture }; projects.SelectedItem = fixture; loading = false; await SelectProject();
            await Wait(() => chat?.ProjectId == fixture.Id && conversationReady, "The fixture conversation did not open.");
            ClearMessagePanel(messages); AddAssistantMessage("Les panneaux Web, Terminal, Git, Fichiers, Assets et Aperçu sont accessibles dans la barre supérieure. Les outils dédiés restent à côté de Réglages.");
            AppWindow.Resize(new() { Width = 1440, Height = 920 }); await Task.Delay(350); root.UpdateLayout();
            var caption = Elements(FullVisual()).Single(e => e.Name == "TitleBarMinMaxCloseContainer");
            Check(Elements(caption).OfType<Button>().Count() == 3, "Exactly one native set of three window buttons is present.");
            Check(!Elements(root).OfType<Button>().Any(b => b.Content is FontIcon { Glyph: "\uE921" or "\uE922" or "\uE923" or "\uE8BB" }), "No second set of caption buttons is drawn inside app content.");
            Check(NativeHit(Elements(caption).Single(e => e.Name == "MinimizeButton")) == 8, "The native minimize hit region is preserved.");
            Check(NativeHit(Elements(caption).Single(e => e.Name == "MaximizeButton")) == 9, "The native maximize/snap hit region is preserved.");
            Check(NativeHit(Elements(caption).Single(e => e.Name == "CloseButton")) == 20, "The native close hit region is preserved.");
            var maximize = (Button)Elements(caption).Single(e => e.Name == "MaximizeButton");
            (new ButtonAutomationPeer(maximize).GetPattern(PatternInterface.Invoke) as IInvokeProvider)!.Invoke();
            await Wait(() => AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized }, "The native maximize button did not maximize the test window.");
            Check(true, "The native maximize button works.");
            (new ButtonAutomationPeer(maximize).GetPattern(PatternInterface.Invoke) as IInvokeProvider)!.Invoke();
            await Wait(() => AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored }, "The native restore button did not restore the test window.");
            Check(true, "The native restore button works.");
            var minimize = (Button)Elements(caption).Single(e => e.Name == "MinimizeButton");
            (new ButtonAutomationPeer(minimize).GetPattern(PatternInterface.Invoke) as IInvokeProvider)!.Invoke();
            await Wait(() => AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }, "The native minimize button did not minimize the test window.");
            Check(true, "The native minimize button works.");
            ((OverlappedPresenter)AppWindow.Presenter).Restore(); await Task.Delay(180);
            Check(dedicatedToolsMenuButton.Flyout is MenuFlyout menu && menu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text.Contains("Diagnostics")), "The dedicated dropdown includes its existing diagnostic tools.");
            var footer = (Grid)dedicatedToolsMenuButton.Parent;
            var settings = footer.Children.OfType<Button>().Single(b => Grid.GetRow(b) == 1 && Grid.GetColumn(b) == 0 && !ReferenceEquals(b, dedicatedToolsMenuButton));
            Check(Math.Abs(Bounds(settings).Top - Bounds(dedicatedToolsMenuButton).Top) < 2 && Bounds(settings).Right <= Bounds(dedicatedToolsMenuButton).Left, "Settings and the dedicated Tools dropdown share the same row.");
            await Shot("wide-chat");
            for (var round = 0; round < 2; round++)
                foreach (var index in new[] { 1, 2, 3, 4, 5, 0 })
                {
                    var name = WorkspaceToolsChrome[index].English + " round " + (round + 1);
                    await NativeClick(workspaceToolButtons[index], name);
                    await Wait(() => browserVisible && toolTabs.SelectedIndex == index, name + " did not open after invoking its visible UI control.");
                    Check(browserPanel.Visibility == Visibility.Visible, name + " opens its panel.");
                    if (round == 0 && index is 0 or 1 or 3) await Shot("tool-" + WorkspaceToolsChrome[index].English.ToLowerInvariant());
                }
            await Wait(() => ShownBrowser()?.Ready == true, "The browser did not initialize.");
            await browser.ExecuteScriptAsync("document.body.innerHTML='<h1>Monolith</h1><p>Le navigateur intégré est ouvert.</p>';document.body.style='font:16px system-ui;padding:32px;background:#16191e;color:#edf2f7'");
            await Shot("browser");
            await NativeClick(workspaceToolButtons[0], "Close Browser");
            Check(!browserVisible, "Clicking the selected shortcut returns to chat.");
            for (var zoom = 100; zoom <= 150; zoom += 50)
            {
                TextZoom.Set(zoom);
                foreach (var width in new[] { 1440, 1100, 760, 480 })
                {
                    AppWindow.Resize(new() { Width = width, Height = 920 }); await Task.Delay(250); root.UpdateLayout();
                    var visible = workspaceToolButtons.Where(b => b.Visibility == Visibility.Visible).ToArray();
                    var right = Bounds(desktopTitleBar).Right - titleRightInset.Width.Value;
                    Check(visible.All(b => Bounds(b).Left >= 0 && Bounds(b).Right < right) && Bounds(titleToolsMenu).Right <= right,
                        $"Shortcuts stay outside the native caption buttons at {width}px / {zoom}%.");
                    foreach (var button in visible) Check(NativeHit(button) == 1, $"{AutomationProperties.GetName(button)} remains clickable at {width}px / {zoom}%.");
                    if (shell.DisplayMode == SplitViewDisplayMode.Inline)
                    {
                        var heading = Elements((FrameworkElement)shell.Pane).Single(e => Equals(e.Tag, "sidebar-projects-heading"));
                        var delta = Bounds(heading).Top - Bounds(mainArea).Top;
                        Check(Math.Abs(delta) <= 1, $"Projects aligns with the chat section at {width}px / {zoom}% (delta {delta:0.##}).");
                        measurements.Add(new { width, zoom, rootWidth = root.ActualWidth, projectTop = Bounds(heading).Top, chatTop = Bounds(mainArea).Top, delta });
                    }
                    await Shot($"responsive-{width}-{zoom}");
                    await NativeClick(titleToolsMenu, $"Overflow {width}px / {zoom}%");
                    Check(VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Any(p => p.IsOpen), $"The overflow menu opens at {width}px / {zoom}%.");
                    // Dismiss via the current open popup's own flyout; the menu remains available
                    // through the button's Click handler, verified by the native hit test above.
                    foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)) popup.IsOpen = false;
                }
            }
            TextZoom.Set(100); AppWindow.Resize(new() { Width = 1440, Height = 920 }); await Task.Delay(250);
            ((MenuFlyout)dedicatedToolsMenuButton.Flyout).ShowAt(dedicatedToolsMenuButton); await Task.Delay(180); await Shot("dedicated-tools-menu");
            ((MenuFlyout)dedicatedToolsMenuButton.Flyout).Hide();
            ApplyTheme("fluent-light"); await Task.Delay(150); await Shot("light-chat");
            Report(); File.WriteAllText(Path.Combine(output, "smoke-ok.txt"), $"Desktop chrome: {passed.Count} checks passed, native/XAML hit tests, control invocations, caption regions, sidebar alignment, dedicated dropdown, 4 widths and 2 zoom levels.");
        }
        catch { Report(); await Shot("failed"); throw; }
    }
}
