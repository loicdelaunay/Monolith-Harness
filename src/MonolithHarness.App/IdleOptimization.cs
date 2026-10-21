using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    bool windowActive = true, idleClosed;
    bool VisualUpdatesAllowed => !idleClosed && windowActive && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };
    readonly Dictionary<string, long> uiWork = [];
    readonly Stopwatch uiWorkClock = Stopwatch.StartNew();
    long renderedFrames, lastMeasuredFrames;
    Dictionary<string, long> lastMeasuredWork = [];
    double lastMeasuredAt;
    object? passiveFrameTarget;
    EventInfo? passiveFrameEvent;
    Action? passiveFrameHandler;
    int liveActivityQueued;

    void InitializeIdleOptimization()
    {
        TokenConsumption.LiveActivityChanged += LiveActivityChanged;
        root.PointerPressed += (_, _) => RecordUiWork("input");
        root.KeyDown += (_, _) => RecordUiWork("input");
        Activated += (_, args) => { windowActive = args.WindowActivationState != Windows.UI.Core.CoreWindowActivationState.Deactivated; RefreshIdlePresentation(); };
        AppWindow.Changed += (_, _) => DispatcherQueue.TryEnqueue(RefreshIdlePresentation);
        root.LayoutUpdated += (_, _) => RecordUiWork("layout");
        root.Loaded += (_, _) => { AttachPassiveFrameCounter(); RefreshIdlePresentation(); };
        Closed += (_, _) =>
        {
            idleClosed = true; TokenConsumption.LiveActivityChanged -= LiveActivityChanged;
            try { if (passiveFrameHandler != null) passiveFrameEvent?.GetRemoveMethod(true)?.Invoke(passiveFrameTarget, [passiveFrameHandler]); } catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.Message); }
        };
    }
    void LiveActivityChanged(string database)
    {
        if (idleClosed || !PlatformSupport.PathComparer.Equals(database, db.FilePath) || Interlocked.Exchange(ref liveActivityQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() => { Interlocked.Exchange(ref liveActivityQueued, 0); if (!idleClosed) { RecordUiWork("generationChanges"); RefreshGlobalTokenSpeed(); RefreshRecentComposerSpeed(); } }))
            Interlocked.Exchange(ref liveActivityQueued, 0);
    }
    void RefreshIdlePresentation()
    {
        if (idleClosed) return;
        BusySpinner.SetRootActive(root.XamlRoot, VisualUpdatesAllowed);
        RefreshChatBackgroundMotion(); RefreshStatusMotion();
        RefreshModelActivity(); RefreshGlobalTokenSpeed(); RefreshRecentComposerSpeed();
        RefreshTerminalPresentation(); RefreshBrowserPower();
        if (!VisualUpdatesAllowed) messageVirtualizationTimer.Stop();
        else if (messageVirtualizationQueued && !messageVirtualizationTimer.IsEnabled) messageVirtualizationTimer.Start();
    }
    void RecordUiWork(string name) => uiWork[name] = uiWork.GetValueOrDefault(name) + 1;
    void SetVisualValue(DependencyObject target, DependencyProperty property, object? value)
    {
        if (Equals(target.GetValue(property), value)) return;
        target.SetValue(property, value); RecordUiWork("writes");
    }
    void SetTextIfChanged(TextBlock target, string value) => SetVisualValue(target, TextBlock.TextProperty, value);
    void SetTooltipIfChanged(DependencyObject target, object? value)
    {
        if (Equals(ToolTipService.GetToolTip(target), value)) return;
        ToolTipService.SetToolTip(target, value); RecordUiWork("writes");
    }
    // Unlike CompositionTarget.Rendering, this private Uno event observes completed
    // drawing without asking the compositor to produce another frame. Optional across versions.
    void AttachPassiveFrameCounter()
    {
#if !WINDOWS
        if (root.XamlRoot == null || passiveFrameHandler != null) return;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var queue = new Queue<object>(); queue.Enqueue(root.XamlRoot);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        try
        {
            while (queue.TryDequeue(out var node) && visited.Count < 12)
            {
                if (!visited.Add(node)) continue;
                var type = node.GetType();
                if (type.GetEvent("FrameRendered", flags) is { EventHandlerType: var handlerType } observed && handlerType == typeof(Action))
                {
                    passiveFrameHandler = () => Interlocked.Increment(ref renderedFrames);
                    observed.GetAddMethod(true)!.Invoke(node, [passiveFrameHandler]);
                    passiveFrameTarget = node; passiveFrameEvent = observed; return;
                }
                foreach (var name in new[] { "CompositionTarget", "VisualTree", "ContentRoot" })
                    if (type.GetProperty(name, flags)?.GetValue(node) is { } next) queue.Enqueue(next);
            }
        }
        catch (Exception ex) { passiveFrameHandler = null; AppLog.Write(AppLogLevel.Debug, "ui.passive_frames_unavailable", ex); }
#endif
    }
    async Task<string> MeasureIdleAsync(CancellationToken ct)
    {
        if (conversationRuns.Count > 0 || TokenConsumption.HasLiveGenerations(db.FilePath))
            throw new InvalidOperationException(WorkflowText("Terminez les générations avant de mesurer le repos.", "Finish generations before measuring idle usage."));
        Activate();
        await Task.Delay(500, ct); // Exclude window activation and the diagnostic button itself.
        AttachPassiveFrameCounter();
        var before = new Dictionary<string, long>(uiWork);
        var maintenanceWakes = CoalescedTimer.WakeCount;
        var frames = Interlocked.Read(ref renderedFrames);
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime; var clock = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(30), ct);
        process.Refresh();
        var seconds = clock.Elapsed.TotalSeconds;
        var percent = (process.TotalProcessorTime - cpu).TotalSeconds / seconds / Environment.ProcessorCount * 100;
        long Delta(string key) => uiWork.GetValueOrDefault(key) - before.GetValueOrDefault(key);
        var interrupted = Delta("input") > 0 || Delta("generationChanges") > 0 || conversationRuns.Count > 0;
        return WorkflowText("Mesure de repos", "Idle measurement") + $" · {seconds:F1} s · CPU {percent:F2} %\n" +
            WorkflowText("Réveils débit / activité / terminal / planificateur / maintenance", "Speed / activity / terminal / scheduler / maintenance wakes") +
            $" : {Delta("speed")} / {Delta("activity")} / {Delta("terminal")} / {Delta("schedule")} / {CoalescedTimer.WakeCount - maintenanceWakes}\n" +
            WorkflowText("Recalculs de disposition", "Layout updates") + $" : {Delta("layout")} · " + WorkflowText("Écritures visuelles", "Visual writes") + $" : {Delta("writes")}\n" +
            WorkflowText("Images dessinées", "Drawn frames") + " : " + (passiveFrameHandler == null ? "—" : (Interlocked.Read(ref renderedFrames) - frames).ToString()) + "\n" +
            WorkflowText("GPU : non mesuré. CPU du processus Monolith uniquement, hors processus du navigateur et outils externes.", "GPU: not measured. Monolith process CPU only, excluding browser processes and external tools.") + "\n" +
            (interrupted ? WorkflowText("Une interaction ou génération est survenue : cette mesure ne représente pas un repos complet.", "An interaction or generation occurred: this sample does not represent full idle." ) : "") +
            (chatBackgroundAnimation != ChatBackgroundStyles.Disabled ? WorkflowText("Une animation de fond est activée et peut produire des images.", "A background animation is enabled and may produce frames.") : "");
    }
    string IdleDiagnostics()
    {
        var now = uiWorkClock.Elapsed.TotalSeconds; var seconds = Math.Max(.001, now - lastMeasuredAt);
        var frames = Interlocked.Read(ref renderedFrames);
        string Count(string name) => $"{uiWork.GetValueOrDefault(name)} (+{uiWork.GetValueOrDefault(name) - lastMeasuredWork.GetValueOrDefault(name)})";
        var result = WorkflowText("Actualisations depuis le démarrage (+ depuis le relevé précédent)", "Refreshes since startup (+ since previous sample)") + $" · {seconds:F1} s\n" +
            $"Débit / Speed: {Count("speed")} · Activité / Activity: {Count("activity")}\n" +
            $"Terminaux / Terminals: {Count("terminal")} · Planification / Scheduler: {Count("schedule")} · Maintenance: {Count("maintenance")}\n" +
            $"Layouts: {Count("layout")} · " + WorkflowText("Propriétés visuelles modifiées", "Changed visual properties") + $": {Count("writes")}\n" +
            WorkflowText("Images dessinées (observation passive)", "Drawn frames (passive observation)") + " : " +
            (passiveFrameHandler == null ? WorkflowText("indisponible sur ce moteur", "unavailable on this renderer") : $"{frames} (+{frames - lastMeasuredFrames}, {(frames - lastMeasuredFrames) / seconds:F1}/s)") + "\n" +
            WorkflowText("Actualisations visuelles", "Visual refreshes") + " : " + (VisualUpdatesAllowed ? WorkflowText("actives", "active") : WorkflowText("suspendues", "suspended")) +
            $" · Timers: speed={composerSpeedTimer.IsEnabled}, activity={modelActivityTimer.IsEnabled}, terminal={terminalRefresh.IsEnabled}, scheduler={scheduleTimer.IsEnabled}\n" +
            WorkflowText("Prochaine tâche", "Next scheduled task") + " : " + (scheduler?.NextWakeUtc?.ToLocalTime().ToString("g") ?? "—") + "\n" +
            WorkflowText("Pages suspendues", "Suspended pages") + $" : {conversationBrowsers.Values.Count(item => item.Suspended)}\n";
        lastMeasuredAt = now; lastMeasuredFrames = frames; lastMeasuredWork = new(uiWork);
        return result + WorkflowText("Réveils communs de maintenance", "Shared maintenance wakes") + $" : {CoalescedTimer.WakeCount}\n";
    }
}
