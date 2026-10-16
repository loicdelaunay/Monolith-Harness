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
                WorkflowText("Base de données", "Database") + " : " + db.FilePath;
        }
        var window = new DiagnosticsWindow(db.FilePath, root.RequestedTheme, LiveState) { Title = DisplayApplicationName + " · " + WorkflowText("Débug · Diagnostics", "Debug · Diagnostics") };
        diagnosticsWindow = window; window.Closed += (_, _) => diagnosticsWindow = null;
        ApplyBrandingIcon(window); ObserveTextZoom(window.Panel); window.Activate(); return Task.CompletedTask;
    }

    /// <summary>Whether Skia draws with the GPU or fell back to the much slower software renderer.</summary>
    string RenderingBackend()
    {
#if WINDOWS
        return WorkflowText("GPU (WinUI natif)", "GPU (native WinUI)");
#else
        try
        {
            // Uno only exposes the software fallback internally; read it without depending on it.
            var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(root).Compositor;
            var property = compositor.GetType().GetProperty("IsSoftwareRenderer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (property?.GetValue(compositor) is bool software)
                return software
                    ? WorkflowText("logiciel (CPU) — interface plus lente : vérifiez le pilote graphique ou la carte choisie dans Réglages", "software (CPU) — slower interface: check the graphics driver or the card selected in Settings")
                    : WorkflowText("GPU (accélération matérielle)", "GPU (hardware acceleration)");
        }
        catch (Exception ex) { AppLog.Write(AppLogLevel.Debug, "diagnostics.rendering_backend", ex); }
        return WorkflowText("inconnu", "unknown");
#endif
    }
}
