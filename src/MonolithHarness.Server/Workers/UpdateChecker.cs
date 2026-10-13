using MonolithHarness.Server.Services;

namespace MonolithHarness.Server.Workers;

/// <summary>
/// Looks for a newer server shortly after the start and then a few times a day, so that a server that stays up for weeks (a service has no
/// console to ask Y/N on) still tells its administrators in the web interface. It only looks: installing is always an explicit choice.
/// </summary>
public sealed class UpdateChecker(UpdateService updates, ServerOptions options, ILogger<UpdateChecker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (options.UpdateMode == "off") return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stop);
            while (!stop.IsCancellationRequested)
            {
                // The startup check already looked (and said so on the console): do not ask the feed again right away.
                if (updates.Last is not { } known || DateTime.UtcNow - known.CheckedUtc > TimeSpan.FromHours(6))
                {
                    var check = await updates.CheckAsync(TimeSpan.FromSeconds(Math.Max(options.UpdateTimeoutSeconds, 10)), stop);
                    if (check.Release is { } release) log.LogInformation("A newer server is available: {Version} (running {Current}). Install it from Administration > Maintenance.", release.Version, check.Current);
                }
                await Task.Delay(TimeSpan.FromHours(6), stop);
            }
        }
        catch (OperationCanceledException) { /* stopping */ }
    }
}
