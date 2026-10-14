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
                WorkflowText("Thème", "Theme") + " : " + FeatureSettings.Read(state.FeaturesJson).Theme + "\n" +
                WorkflowText("Base de données", "Database") + " : " + db.FilePath;
        }
        var window = new DiagnosticsWindow(db.FilePath, root.RequestedTheme, LiveState) { Title = DisplayApplicationName + " · " + WorkflowText("Débug · Diagnostics", "Debug · Diagnostics") };
        diagnosticsWindow = window; window.Closed += (_, _) => diagnosticsWindow = null;
        ApplyBrandingIcon(window); ObserveTextZoom(window.Panel); window.Activate(); return Task.CompletedTask;
    }
}
