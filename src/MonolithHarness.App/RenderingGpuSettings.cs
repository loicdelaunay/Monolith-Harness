using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using MonolithHarness.Core;

namespace MonolithHarness.App;

static class RenderingGpuSettings
{
    public static void Apply(string preference)
    {
        if (!OperatingSystem.IsWindows()) return;
        var executable = Environment.ProcessPath ?? throw new IOException("Chemin de l’exécutable indisponible / Executable path unavailable.");
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Appliquez ce réglage depuis l’exécutable publié / Apply this setting from the published executable.");
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", writable: true)
            ?? throw new IOException("Impossible d’enregistrer la préférence GPU Windows / Cannot save Windows GPU preference.");
        var fields = (key.GetValue(executable) as string ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !x.StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase)).ToList();
        if (preference != "auto") fields.Add("GpuPreference=" + (preference == "high-performance" ? "2" : "1"));
        if (fields.Count == 0) key.DeleteValue(executable, throwOnMissingValue: false);
        else key.SetValue(executable, string.Join(';', fields) + ";", RegistryValueKind.String);
    }
}

public sealed partial class MainWindow
{
    (FrameworkElement Panel, Action<FeatureSettings> Save, Func<bool> Apply) BuildRenderingGpuSettings()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        string[] values = ["auto", "high-performance", "power-saving"];
        var selector = new ComboBox { ItemsSource = new[] {
            WorkflowText("Auto — choix de Windows", "Auto — Windows decides"),
            WorkflowText("Haute performance — GPU dédié", "High performance — dedicated GPU"),
            WorkflowText("Basse consommation — GPU intégré", "Power saving — integrated GPU") },
            SelectedIndex = Math.Max(0, Array.IndexOf(values, config.RenderingGpuPreference)), HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = OperatingSystem.IsWindows() };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Label(WorkflowText("Carte graphique du rendu", "Rendering graphics card"), 16));
        panel.Children.Add(selector);
        panel.Children.Add(Label(OperatingSystem.IsWindows()
            ? WorkflowText("S’applique à cet exécutable après redémarrage. Windows et les pilotes choisissent la carte disponible correspondant à cette préférence. Ce réglage concerne l’interface, pas le calcul des modèles IA.", "Applies to this executable after restart. Windows and drivers choose an available GPU matching the preference. This affects the interface, not AI model computation.")
            : WorkflowText("Ce choix est disponible sous Windows. Le système gère le GPU sur cette plateforme.", "This choice is available on Windows. The system manages the GPU on this platform."), 12));
        var error = Label("", 12); error.Foreground = FluentDesign.Resource("ToolMessageErrorStrokeBrush"); panel.Children.Add(error);
        string Selected() => values[Math.Clamp(selector.SelectedIndex, 0, values.Length - 1)];
        return (FluentDesign.Surface(panel, 16), target => target.RenderingGpuPreference = Selected(), () => {
            if (!OperatingSystem.IsWindows() || Selected() == "auto" && config.RenderingGpuPreference == "auto") return true;
            try { RenderingGpuSettings.Apply(Selected()); error.Text = ""; return true; }
            catch (Exception ex) { error.Text = ex.Message; return false; }
        });
    }
}
