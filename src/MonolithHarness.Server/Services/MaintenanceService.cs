using System.Security.Claims;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

/// <summary>What the Maintenance page and its API show about updates.</summary>
public sealed record UpdateInfo(string Current, string Mode, bool CanApply, string Platform, DateTime? CheckedUtc, string? Error, ServerRelease? Available, int ActiveRuns, bool Applying);

/// <summary>
/// The administrator's controls over the running server: look for a newer version and install it, turn the server into a service (or back).
/// Every operation needs the permission to set the server up, is written to the audit log, and ends with the server restarting or stopping by itself.
/// </summary>
public sealed class MaintenanceService(UpdateService updates, ServerOptions options, ServerPaths paths, ServerLifecycle lifecycle, RunService runs, AccessService access, AuditService audit)
{
    int applying;

    public async Task<UpdateInfo> UpdateInfoAsync(ClaimsPrincipal user)
    {
        access.Require(user, Permissions.SettingsManage);
        var last = updates.Last;
        return new(UpdateService.CurrentVersion, options.UpdateMode, UpdateService.CanApply, UpdateService.Runtime, last?.CheckedUtc, last?.Error, last?.Release,
            await runs.ActiveCountAsync(), Volatile.Read(ref applying) == 1);
    }

    public async Task<UpdateInfo> CheckAsync(ClaimsPrincipal user)
    {
        access.Require(user, Permissions.SettingsManage);
        await updates.CheckAsync(TimeSpan.FromSeconds(Math.Max(options.UpdateTimeoutSeconds, 10)));
        return await UpdateInfoAsync(user);
    }

    /// <summary>Downloads, verifies and installs the release found by the last check, then restarts the server a moment later (the response is sent first).</summary>
    public async Task<UpdateInfo> ApplyUpdateAsync(ClaimsPrincipal user)
    {
        access.Require(user, Permissions.SettingsManage);
        if (!UpdateService.CanApply) throw ApiException.BadRequest("update_unavailable", "Cette instance ne vient pas d'un exécutable publié : elle ne peut pas se mettre à jour elle-même.");
        var release = updates.Available ?? throw ApiException.Conflict("no_update", "Aucune mise à jour n'est disponible : lancez d'abord la recherche.");
        if (Interlocked.CompareExchange(ref applying, 1, 0) != 0) throw ApiException.Conflict("update_running", "Une mise à jour est déjà en cours.");
        try
        {
            string staged;
            try { staged = await updates.DownloadAsync(release); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or InvalidOperationException or IOException or OperationCanceledException)
            {
                await audit.WriteAsync("update.failed", release.Version, ex.Message, false, actor: user);
                throw ApiException.BadRequest("update_failed", "La mise à jour n'a pas pu être téléchargée ou vérifiée : " + ex.Message);
            }
            var previous = updates.Install(staged, Environment.ProcessPath!);
            await audit.WriteAsync("update.installed", release.Version, $"from {UpdateService.CurrentVersion}; previous launcher kept as {Path.GetFileName(previous)}", actor: user);
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                lifecycle.Restart("update to " + release.Version);
            });
            return await UpdateInfoAsync(user);
        }
        finally { Interlocked.Exchange(ref applying, 0); }
    }

    // ---- Service ----

    public Task<ServiceState> ServiceStateAsync(ClaimsPrincipal user)
    {
        access.Require(user, Permissions.SettingsManage);
        return ServiceInstaller.StatusAsync(paths);
    }

    /// <summary>Prepares the service (elevation on this machine, key ring, recovery actions), then stops this server so that the service takes over its port.</summary>
    public async Task<ServiceResult> InstallServiceAsync(ClaimsPrincipal user)
    {
        access.Require(user, Permissions.SettingsManage);
        var state = await ServiceInstaller.StatusAsync(paths);
        if (state.ThisProcess) throw ApiException.Conflict("already_service", "Ce serveur tourne déjà comme service.");
        if (!state.CanInstallHere) throw ApiException.BadRequest("cannot_install", state.Detail.Length > 0 ? state.Detail : "L'installation du service n'est pas possible depuis cette instance.");
        var handle = await ServiceInstaller.BeginInstallAsync(paths, ServerRuntime.Args, stopThisProcess: true);
        var result = await ServiceInstaller.WaitAsync(handle, TimeSpan.FromMinutes(3), CancellationToken.None, "prepared", "started");
        if (result.State == "failed")
        {
            await audit.WriteAsync("service.install_failed", ServiceInstaller.Name, result.Message, false, actor: user);
            throw ApiException.BadRequest("service_failed", result.Message);
        }
        await audit.WriteAsync("service.installed", ServiceInstaller.Name, "", actor: user);
        // The helper starts the service once this process has ended; the page loses its connection and comes back on the service.
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            lifecycle.Stop("switching to the service");
        });
        return result;
    }

    public async Task<ServiceResult> UninstallServiceAsync(ClaimsPrincipal user)
    {
        access.Require(user, Permissions.SettingsManage);
        var handle = await ServiceInstaller.BeginUninstallAsync(paths);
        var result = await ServiceInstaller.WaitAsync(handle, TimeSpan.FromMinutes(3), CancellationToken.None, "removed");
        if (result.State == "failed")
        {
            await audit.WriteAsync("service.remove_failed", ServiceInstaller.Name, result.Message, false, actor: user);
            throw ApiException.BadRequest("service_failed", result.Message);
        }
        await audit.WriteAsync("service.removed", ServiceInstaller.Name, "", actor: user);
        return result;
    }
}
