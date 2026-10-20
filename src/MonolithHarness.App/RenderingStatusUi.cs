using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    bool? SoftwareRendering()
    {
#if WINDOWS
        return false;
#else
        if (!DesktopRendering.StartupSettings.HardwareAcceleration) return true;
        try
        {
            var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(root).Compositor;
            return compositor.GetType().GetProperty("IsSoftwareRenderer",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(compositor) as bool?;
        }
        catch (Exception ex) { AppLog.Write(AppLogLevel.Debug, "rendering.software_status", ex); return null; }
#endif
    }

    FrameworkElement BuildActiveGraphicsStatus()
    {
        var status = Label("", 12); status.Tag = null;
        status.Foreground = FluentDesign.Secondary;
        ToolTipService.SetToolTip(status, WorkflowText("Nom obtenu en reliant les compteurs d’activité 3D de ce processus aux cartes Windows. L’activité observée depuis le lancement reste disponible au repos. Plusieurs cartes sont affichées si elles ont participé au rendu ; cela n’identifie pas une surface graphique précise.",
            "Names come from matching this process’s 3D activity counters to Windows adapters. Activity observed since startup remains available while idle. Multiple adapters are shown if they participated in rendering; this does not identify a specific graphics surface."));
        CancellationTokenSource? loading = null;
        status.Loaded += async (_, _) =>
        {
            loading?.Cancel(); loading?.Dispose();
            var source = loading = new CancellationTokenSource();
            if (SoftwareRendering() == true) { status.Text = WorkflowText("Rendu actuel : logiciel (CPU)", "Current rendering: software (CPU)"); return; }
            status.Text = WorkflowText("GPU utilisé par l’application : détection…", "Application GPU: detecting…");
            try
            {
                var names = await RenderingDeviceInfo.ProcessGraphicsAdaptersAsync().WaitAsync(TimeSpan.FromSeconds(4), source.Token);
                if (source.IsCancellationRequested) return;
                status.Text = names.Length > 0
                    ? WorkflowText("GPU observé pour l’application : ", "Observed application GPU: ") + string.Join(" / ", names)
                    : WorkflowText("GPU utilisé par l’application : nom indisponible", "Application GPU: name unavailable");
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { if (!source.IsCancellationRequested) status.Text = WorkflowText("GPU utilisé par l’application : nom indisponible", "Application GPU: name unavailable"); }
        };
        status.Unloaded += (_, _) => { loading?.Cancel(); loading?.Dispose(); loading = null; };
        return status;
    }

    FrameworkElement BuildActiveRefreshStatus()
    {
        var status = Label("", 12); status.Tag = null; status.Foreground = FluentDesign.Secondary;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void Refresh()
        {
            var hz = RenderingDeviceInfo.ScreenRefreshRate(this);
            var screen = hz is { } rate ? rate + " Hz" : WorkflowText("indisponible", "unavailable");
#if WINDOWS
            var rendering = WorkflowText("gérée par Windows", "managed by Windows");
#else
            var rendering = DesktopRendering.FollowsDisplay ? WorkflowText("synchronisée à l’écran", "synchronized with display")
                : DesktopRendering.TargetFrameRate + WorkflowText(" images/s (cible active)", " frames/s (active target)");
#endif
            status.Text = WorkflowText("Écran de la fenêtre principale : ", "Main window display: ") + screen + "\n"
                + WorkflowText("Cadence de rendu appliquée : ", "Applied rendering pace: ") + rendering;
        }
        timer.Tick += (_, _) => Refresh();
        status.Loaded += (_, _) => { Refresh(); timer.Start(); };
        status.Unloaded += (_, _) => timer.Stop();
        ToolTipService.SetToolTip(status, WorkflowText("La fréquence de l’écran est relue chaque seconde lorsque cette section est visible. La cadence de rendu affichée est celle appliquée au démarrage ; changer la sélection demande un redémarrage. Il s’agit d’une cible, pas d’une mesure du nombre d’images réellement dessinées.",
            "Display refresh is read every second while this section is visible. Rendering pace is the value applied at startup; changing the selection requires a restart. This is a target, not a measurement of frames actually drawn."));
        return status;
    }
}
