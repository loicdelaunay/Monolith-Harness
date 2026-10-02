using Microsoft.EntityFrameworkCore;

namespace MonolithHarness.Core.Hosting;

public sealed partial class HarnessService
{
    readonly CancellationTokenSource artifactMaintenanceStop = new();
    Task? artifactMaintenance;
    async Task InitializeArtifactMaintenanceAsync()
    {
        await CleanArtifactsAsync(artifactMaintenanceStop.Token);
        artifactMaintenance = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
            try
            {
                while (await timer.WaitForNextTickAsync(artifactMaintenanceStop.Token))
                    await CleanArtifactsAsync(artifactMaintenanceStop.Token);
            }
            catch (OperationCanceledException) when (artifactMaintenanceStop.IsCancellationRequested) { }
        });
    }
    async Task CleanArtifactsAsync(CancellationToken ct)
    {
        try
        {
            await using var db = Db();
            var state = await db.States.AsNoTracking().SingleAsync(ct);
            var settings = FeatureSettings.Read(state.FeaturesJson);
            if (!settings.AutoCleanArtifacts) return;
            var persistedRuns = await db.Messages.AsNoTracking().Where(x => x.State == "streaming").Select(x => x.ChatId).Distinct().ToArrayAsync(ct);
            var excluded = runs.Keys.Concat(persistedRuns).Concat(state.ChatId is { } id ? [id] : Array.Empty<int>());
            await ConversationArtifacts.CleanAsync(database, settings, excluded, DateTime.UtcNow, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "artifacts.cleanup.failed", ex); }
    }
}
