using Microsoft.UI.Xaml;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    DiagnosticsWindow? diagnosticsWindow;
    Task ShowDiagnosticsAsync()
    {
        if (diagnosticsWindow is { } existing) { existing.Activate(); return Task.CompletedTask; }
        string LiveState()
        {
            var active = conversationRuns.Values.Where(run => !run.Cancellation.IsCancellationRequested).ToArray();
            return WorkflowText("Conversations en cours", "Running conversations") + " : " + active.Length + "\n" +
                WorkflowText("En attente du modèle", "Waiting for model") + " : " + active.Count(run => run.WaitingForModel) + "\n" +
                WorkflowText("Outils en cours", "Running tools") + " : " + active.Count(run => run.CurrentTool != null) + "\n" +
                WorkflowText("Demande d’autorisation ouverte", "Open approval request") + " : " + (approvalQueue.CurrentCount == 0) + "\n" +
                WorkflowText("Mode d’autorisation", "Permission mode") + " : " + PermissionModes.Normalize(state.PermissionMode) + "\n" +
                WorkflowText("Thème", "Theme") + " : " + ReadOnlyFeatures.Theme + "\n" +
                WorkflowText("Rendu de l’interface", "Interface rendering") + " : " + RenderingBackend() + "\n" +
                RenderingStartupDescription() +
                WorkflowText("Base de données", "Database") + " : " + db.FilePath;
        }
        var window = new DiagnosticsWindow(db.FilePath, root.RequestedTheme, LiveState) { Title = DisplayApplicationName + " · " + WorkflowText("Débug · Diagnostics", "Debug · Diagnostics") };
        diagnosticsWindow = window; window.Closed += (_, _) => diagnosticsWindow = null;
        ApplyBrandingIcon(window); ObserveTextZoom(window.Panel); window.Activate(); return Task.CompletedTask;
    }

    string RenderingStartupDescription()
    {
#if WINDOWS
        return "";
#else
        var config = DesktopRendering.StartupSettings;
        var backend = !config.HardwareAcceleration ? WorkflowText("logiciel (CPU)", "software (CPU)")
            : OperatingSystem.IsMacOS() ? "Metal" : config.RenderingBackend switch
            { "vulkan" => "Vulkan", "opengl" => "OpenGL", _ => WorkflowText("automatique", "automatic") };
        var refresh = DesktopRendering.FollowsDisplay ? WorkflowText("suit l’écran", "follows display")
            : DesktopRendering.TargetFrameRate + WorkflowText(" images/s (cible)", " frames/s (target)");
        return WorkflowText("Configuration demandée au démarrage", "Configuration requested at startup") + " : " + backend + " · " + refresh + "\n";
#endif
    }

    /// <summary>Whether Skia draws with the GPU or fell back to the much slower software renderer.</summary>
    string RenderingBackend()
    {
#if WINDOWS
        return WorkflowText("GPU (WinUI natif)", "GPU (native WinUI)");
#else
        if (SoftwareRendering() is { } software)
            return software
                ? WorkflowText("logiciel (CPU) — interface plus lente : vérifiez le pilote graphique ou la carte choisie dans Réglages", "software (CPU) — slower interface: check the graphics driver or the card selected in Settings")
                : WorkflowText("GPU (accélération matérielle)", "GPU (hardware acceleration)");
        return WorkflowText("inconnu", "unknown");
#endif
    }
}
