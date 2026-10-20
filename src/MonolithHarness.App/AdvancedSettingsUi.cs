using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    (FrameworkElement Panel, Action<FeatureSettings> Save, Func<bool> Apply) BuildAdvancedSettings()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        var gpu = BuildRenderingGpuSettings();
#if WINDOWS
        var supported = false;
#else
        var supported = true;
#endif
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(Label(WorkflowText("Rendu et performances", "Rendering and performance"), 22));
        panel.Children.Add(Label(WorkflowText("Les modifications de rendu et de rafraîchissement prennent effet après redémarrage. Elles concernent l’interface de bureau ; la fréquence de l’écran et le calcul des modèles IA restent gérés séparément.",
            "Rendering and refresh changes take effect after restart. They affect the desktop interface; display refresh and AI model computation are managed separately."), 13));
        if (!supported)
            panel.Children.Add(Label(WorkflowText("Cette édition WinUI native utilise le rendu Windows. Le choix du moteur, l’accélération et le rafraîchissement ci-dessous sont disponibles dans l’édition Uno Desktop.",
                "This native WinUI edition uses Windows rendering. The engine, acceleration and refresh controls below are available in the Uno Desktop edition."), 13));
        var acceleration = new ToggleSwitch { IsOn = config.HardwareAcceleration, IsEnabled = supported,
            OnContent = UiText.T("Activé"), OffContent = UiText.T("Désactivé") };
        var rendering = new StackPanel { Spacing = 12 };
        rendering.Children.Add(FluentDesign.Setting(WorkflowText("Accélération matérielle", "Hardware acceleration"),
            WorkflowText("Utilise le GPU pour dessiner l’interface. Désactivée : rendu logiciel sur le CPU. Les navigateurs intégrés gardent leur propre configuration graphique.",
                "Use the GPU to draw the interface. When disabled: CPU software rendering. Embedded browsers retain their own graphics configuration."), acceleration));
        string[] backends = ["auto", "opengl", "vulkan"];
        var backend = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { WorkflowText("Automatique", "Automatic"), "OpenGL", "Vulkan" },
            SelectedIndex = Math.Max(0, Array.IndexOf(backends, config.RenderingBackend)) };
        rendering.Children.Add(FluentDesign.Setting(WorkflowText("Technologie de rendu", "Rendering technology"),
            OperatingSystem.IsMacOS()
                ? WorkflowText("macOS utilise Metal. OpenGL et Vulkan sont proposés sous Windows et Linux.", "macOS uses Metal. OpenGL and Vulkan are offered on Windows and Linux.")
                : WorkflowText("Vulkan est essayé en priorité lorsqu’il est sélectionné ; le moteur revient à OpenGL puis au rendu logiciel si nécessaire.",
                    "When selected, Vulkan is tried first; the engine falls back to OpenGL then software rendering if necessary."), backend));
        var backendNote = Label("", 12); backendNote.Tag = null; rendering.Children.Add(backendNote);
        void RefreshBackend()
        {
            backend.IsEnabled = supported && acceleration.IsOn && !OperatingSystem.IsMacOS();
            backendNote.Text = !acceleration.IsOn
                ? WorkflowText("Rendu logiciel sélectionné. Le choix OpenGL/Vulkan est conservé pour la réactivation du GPU.", "Software rendering selected. The OpenGL/Vulkan choice is retained for GPU reactivation.")
                : OperatingSystem.IsMacOS() ? "Metal" : WorkflowText("Automatique conserve le choix par défaut du moteur (OpenGL sur cette version).", "Automatic keeps the engine default (OpenGL in this version).");
        }
        acceleration.Toggled += (_, _) => RefreshBackend(); RefreshBackend();
        panel.Children.Add(FluentDesign.Surface(rendering, 16));
        panel.Children.Add(gpu.Panel);
        string[] modes = ["auto", "screen", "manual"];
        var mode = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = supported,
            ItemsSource = new[] { WorkflowText("Auto", "Auto"), WorkflowText("En fonction de l’écran", "Follow display"), WorkflowText("Manuel", "Manual") },
            SelectedIndex = Math.Max(0, Array.IndexOf(modes, config.RenderingRefreshMode)) };
        var rate = new NumberBox { Header = WorkflowText("Limite manuelle (images/s)", "Manual limit (frames/s)"),
            Value = config.RenderingFrameRate, Minimum = 15, Maximum = 500, SmallChange = 1, LargeChange = 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, HorizontalAlignment = HorizontalAlignment.Stretch };
        var refresh = new StackPanel { Spacing = 12 };
        refresh.Children.Add(FluentDesign.Setting(WorkflowText("Rafraîchissement de l’interface", "Interface refresh"),
            WorkflowText("Choisit le rythme de rendu lorsque l’interface change. Une limite élevée ne garantit pas autant d’images par seconde.",
                "Choose the rendering pace while the interface changes. A high limit does not guarantee that many frames per second."), mode));
        var refreshNote = Label("", 12); refreshNote.Tag = null; refresh.Children.Add(refreshNote); refresh.Children.Add(rate);
        void RefreshMode()
        {
            rate.Visibility = mode.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            rate.IsEnabled = supported && mode.SelectedIndex == 2;
            refreshNote.Text = mode.SelectedIndex switch
            {
                1 => WorkflowText("Synchronisation avec l’écran par le moteur graphique. La cadence suit les changements d’écran lorsque la plateforme le permet.",
                    "Synchronize with the display through the graphics engine. Pacing follows display changes where supported by the platform."),
                2 => WorkflowText("Cadence fixe de 15 à 500 images/s, sans modifier la fréquence de votre écran. Sous Windows, la synchronisation verticale est désactivée pour respecter cette limite.",
                    "Fixed pacing from 15 to 500 frames/s without changing display refresh. On Windows, vertical synchronization is disabled to honor this limit."),
                _ => OperatingSystem.IsWindows()
                    ? WorkflowText("Windows : cadence temporisée selon l’écran principal au démarrage, entre 60 et 240 images/s, pour limiter l’attente active de certains pilotes OpenGL.",
                        "Windows: timer pacing based on the primary display at startup, between 60 and 240 frames/s, to limit busy waiting in some OpenGL drivers.")
                    : WorkflowText("Utilise la synchronisation par défaut du moteur graphique.", "Use the graphics engine’s default synchronization.")
            };
        }
        mode.SelectionChanged += (_, _) => RefreshMode(); RefreshMode();
        refresh.Children.Add(BuildActiveRefreshStatus());
        panel.Children.Add(FluentDesign.Surface(refresh, 16));
        var error = Label("", 13); error.Foreground = FluentDesign.Resource("ToolMessageErrorStrokeBrush"); panel.Children.Add(error);
        void Save(FeatureSettings target)
        {
            gpu.Save(target);
            target.RenderingBackend = backends[Math.Clamp(backend.SelectedIndex, 0, backends.Length - 1)];
            target.HardwareAcceleration = acceleration.IsOn;
            target.RenderingRefreshMode = modes[Math.Clamp(mode.SelectedIndex, 0, modes.Length - 1)];
            target.RenderingFrameRate = double.IsFinite(rate.Value) && rate.Value >= 15 && rate.Value <= 500 && rate.Value == Math.Truncate(rate.Value) ? (int)rate.Value : config.RenderingFrameRate;
        }
        bool Apply()
        {
            error.Text = "";
            if (supported && mode.SelectedIndex == 2 && (!double.IsFinite(rate.Value) || rate.Value < 15 || rate.Value > 500 || rate.Value != Math.Truncate(rate.Value)))
            {
                error.Text = WorkflowText("Saisissez un nombre entier de 15 à 500 images/s.", "Enter a whole number from 15 to 500 frames/s."); return false;
            }
            return gpu.Apply();
        }
        return (panel, Save, Apply);
    }
}
