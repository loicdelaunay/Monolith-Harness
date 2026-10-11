using System.Collections.Concurrent;
using MonolithHarness.Server.Services;

namespace MonolithHarness.Server.Workers;

/// <summary>Starts queued requests as capacity allows, in fair order, and stops everything cleanly on shutdown.</summary>
public sealed class RunDispatcher(RunService runs, IRunExecutor executor, ILogger<RunDispatcher> log) : BackgroundService
{
    readonly SemaphoreSlim wake = new(0, int.MaxValue);
    readonly ConcurrentDictionary<Guid, Task> tasks = new();

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        runs.WorkAvailable += () => wake.Release();
        while (!stop.IsCancellationRequested)
        {
            try
            {
                foreach (var next in await runs.NextToStartAsync())
                {
                    if (await runs.BeginAsync(next.Id) is not { } active) continue;
                    tasks[next.Id] = Task.Run(async () =>
                    {
                        try { await executor.ExecuteAsync(next.Id, active); }
                        finally { tasks.TryRemove(next.Id, out _); }
                    }, CancellationToken.None);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Dispatch failed"); }
            try
            {
                await wake.WaitAsync(TimeSpan.FromSeconds(5), stop);
                while (wake.Wait(0)) { }
            }
            catch (OperationCanceledException) { break; }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        foreach (var run in runs.Active.Values) run.Cancel("The server is stopping.");
        try { await Task.WhenAll(tasks.Values).WaitAsync(TimeSpan.FromSeconds(20), cancellationToken); } catch (Exception) { }
    }
}
