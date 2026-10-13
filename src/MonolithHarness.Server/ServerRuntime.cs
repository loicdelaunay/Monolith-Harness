using System.Diagnostics;
using Microsoft.Extensions.Hosting.WindowsServices;
using MonolithHarness.Core;

namespace MonolithHarness.Server;

/// <summary>
/// What this process is: the arguments it was started with and whether it runs as a service. Also how a console instance hands over to its
/// successor (update, switch to a service): the successor is started with <c>--after-pid</c> and waits for this process to end before it
/// opens the port and the database.
/// </summary>
public static class ServerRuntime
{
    public const string AfterPid = "--after-pid";
    public const string ServiceFlag = "--service";

    /// <summary>The arguments without the internal hand-over option.</summary>
    public static IReadOnlyList<string> Args { get; private set; } = [];

    public static bool IsService { get; private set; }

    /// <summary>Remembers the arguments and, if a predecessor is named, waits (up to 90 s) for it to end.</summary>
    public static async Task InitializeAsync(string[] args)
    {
        var kept = new List<string>();
        int? predecessor = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == AfterPid && i + 1 < args.Length && int.TryParse(args[i + 1], out var pid)) { predecessor = pid; i++; continue; }
            kept.Add(args[i]);
        }
        Args = kept;
        IsService = kept.Contains(ServiceFlag) || (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService());
        if (predecessor is { } waiting) await WaitForExitAsync(waiting, TimeSpan.FromSeconds(90));
    }

    public static async Task WaitForExitAsync(int pid, TimeSpan limit)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            using var timeout = new CancellationTokenSource(limit);
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            // Already gone (the usual case), or it never ended in time: the successor tries to start anyway and reports a busy port itself.
        }
    }

    /// <summary>Starts the same program again with the same arguments; the new process waits for this one to end.</summary>
    public static void Relaunch()
    {
        var launcher = Environment.ProcessPath ?? throw new InvalidOperationException("The launcher path is unknown.");
        var arguments = Args.Concat([AfterPid, Environment.ProcessId.ToString()]).ToList();
        var info = new ProcessStartInfo(launcher) { WorkingDirectory = Environment.CurrentDirectory };
        // Windows, started by a person in a console: a window of its own for the successor (a child sharing the console would keep writing after the prompt
        // came back). Started with its output captured (a script, a supervisor), the successor simply inherits it.
        if (OperatingSystem.IsWindows() && Environment.UserInteractive && !Console.IsOutputRedirected && !Console.IsInputRedirected)
        {
            info.UseShellExecute = true;
            info.Arguments = string.Join(' ', arguments.Select(GitHubUpdates.QuoteWindowsArgument));
        }
        else
        {
            info.UseShellExecute = false;
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
        }
        using var started = Process.Start(info) ?? throw new InvalidOperationException("The new instance could not be started.");
    }

    /// <summary>Where the data lives for this process, as given by --data (or the default).</summary>
    public static string? ArgumentOf(IEnumerable<string> args, string name)
    {
        var list = args.ToList();
        var index = list.IndexOf(name);
        return index >= 0 && index + 1 < list.Count ? list[index + 1] : null;
    }
}
