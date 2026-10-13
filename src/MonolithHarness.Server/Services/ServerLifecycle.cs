using System.ServiceProcess;

namespace MonolithHarness.Server.Services;

/// <summary>
/// Stops or restarts this server on request (an update, the switch to a service). A console instance starts its successor first, which waits for
/// this process to end; a service ends with a failure code so that the service manager starts it again by itself (recovery actions).
/// </summary>
public sealed class ServerLifecycle(IHostApplicationLifetime lifetime, IHostLifetime hostLifetime, ILogger<ServerLifecycle> log)
{
    public bool RestartRequested { get; private set; }
    public bool StopRequested { get; private set; }

    /// <summary>Exit code of a service that wants to be started again (the recovery actions of the service manager apply to failures).</summary>
    public static int RestartExitCode => 1;

    public void Restart(string reason)
    {
        if (RestartRequested || StopRequested) return;
        RestartRequested = true;
        log.LogWarning("Restarting the server: {Reason}", reason);
        if (ServerRuntime.IsService) ReportFailureToServiceManager();
        else ServerRuntime.Relaunch();
        lifetime.StopApplication();
    }

    public void Stop(string reason)
    {
        if (RestartRequested || StopRequested) return;
        StopRequested = true;
        log.LogWarning("Stopping the server: {Reason}", reason);
        lifetime.StopApplication();
    }

    void ReportFailureToServiceManager()
    {
        if (OperatingSystem.IsWindows() && hostLifetime is ServiceBase service) service.ExitCode = RestartExitCode;
    }
}
