using Microsoft.UI.Xaml;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    FileSystemWatcher? scheduleWatcher;
    bool schedulerBusy, scheduleChangedPending, schedulerWatchFailed;
    int schedulerWakeQueued;
    string ScheduleMarker => db.FilePath + ".schedule";
    void WatchScheduleChanges()
    {
        try
        {
            scheduleWatcher = new(Path.GetDirectoryName(db.FilePath)!, Path.GetFileName(ScheduleMarker))
            { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            scheduleWatcher.Changed += (_, _) => QueueSchedulerWake();
            scheduleWatcher.Created += (_, _) => QueueSchedulerWake();
            scheduleWatcher.Renamed += (_, _) => QueueSchedulerWake();
            scheduleWatcher.Error += (_, _) => { schedulerWatchFailed = true; QueueSchedulerWake(); };
            scheduleWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            schedulerWatchFailed = true;
            AppLog.Write(AppLogLevel.Warning, "scheduler.notifications_unavailable", ex);
        }
    }
    void NotifyScheduleChanged()
    {
        QueueSchedulerWake();
        // A dedicated marker wakes other GUI instances without watching every chat/database write.
        try { File.WriteAllText(ScheduleMarker, Guid.NewGuid().ToString("N")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { AppLog.Write(AppLogLevel.Warning, "scheduler.notification_failed", ex); }
    }
    void QueueSchedulerWake()
    {
        if (idleClosed || Interlocked.Exchange(ref schedulerWakeQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref schedulerWakeQueued, 0);
            if (idleClosed || scheduler == null) return;
            scheduleChangedPending = true;
            if (!schedulerBusy && !databaseMaintenanceBusy && !updatingApplication)
            { scheduleTimer.Stop(); scheduleTimer.Interval = TimeSpan.FromMilliseconds(250); scheduleTimer.Start(); }
        })) Interlocked.Exchange(ref schedulerWakeQueued, 0);
    }
    async Task WakeSchedulerAsync()
    {
        scheduleTimer.Stop();
        if (idleClosed || scheduler == null) return;
        if (schedulerBusy || databaseMaintenanceBusy || updatingApplication) { scheduleChangedPending = true; return; }
        schedulerBusy = true; scheduleChangedPending = false;
        DateTime? retry = null;
        try { RecordUiWork("schedule"); await scheduler.TickAsync(DateTime.UtcNow); }
        catch (OperationCanceledException) when (idleClosed) { }
        catch (Exception ex) { retry = DateTime.UtcNow.AddMinutes(1); AppLog.Write(AppLogLevel.Warning, "scheduler.wake_failed", ex); }
        finally { schedulerBusy = false; }
        if (idleClosed) return;
        var next = scheduleChangedPending ? DateTime.UtcNow.AddMilliseconds(250) : retry ?? scheduler.NextWakeUtc;
        if (schedulerWatchFailed && (!next.HasValue || next > DateTime.UtcNow.AddMinutes(1))) next = DateTime.UtcNow.AddMinutes(1);
        if (!next.HasValue) return;
        scheduleTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp((next.Value - DateTime.UtcNow).TotalMilliseconds, 100, int.MaxValue));
        scheduleTimer.Start();
    }
}
