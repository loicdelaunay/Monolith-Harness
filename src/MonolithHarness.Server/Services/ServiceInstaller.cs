using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using MonolithHarness.Core;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

/// <summary>Where the service stands on this machine, in the terms the interface needs.</summary>
public sealed record ServiceState(bool Supported, string Platform, string Name, bool Installed, bool Running, bool ThisProcess, bool Elevated, bool CanInstallHere,
    string Detail, IReadOnlyList<string> ManualSteps);

/// <summary>State of an installation or removal carried out by the privileged helper: working, prepared, started, removed or failed.</summary>
public sealed record ServiceResult(string State, string Message);

/// <summary>
/// Installs the server as a service that starts with the machine and is restarted when it fails: a Windows service (virtual account, recovery
/// actions), a systemd unit on Linux, a launchd daemon on macOS. The privileged part runs in a helper (this same launcher, elevated); it
/// reports its progress in a small file so that the web interface can follow it. The server can be stopped once the service is prepared, and the
/// helper starts the service as soon as this process is gone, so that the port is free.
/// </summary>
public static class ServiceInstaller
{
    public const string WindowsName = "MonolithHarnessServer";
    public const string SystemdName = "monolith-harness-server";
    public const string LaunchdLabel = "com.monolithharness.server";
    public const string DisplayName = "Monolith Harness Server";
    public const string Description = "Monolith Harness Server: cloud projects, shared conversations and provider relay. Starts with the machine and restarts after a failure.";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Name => OperatingSystem.IsWindows() ? WindowsName : OperatingSystem.IsMacOS() ? LaunchdLabel : SystemdName;

    static string PlatformName => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";

    static string UnitPath => $"/etc/systemd/system/{SystemdName}.service";

    static string PlistPath => $"/Library/LaunchDaemons/{LaunchdLabel}.plist";

    public static string ResultFile(ServerPaths paths, string operation) => Path.Combine(paths.Workspace, "service", operation + "-result.json");

    // ---- Status ----

    public static async Task<ServiceState> StatusAsync(ServerPaths paths)
    {
        var elevated = IsElevated();
        var published = UpdateService.CanApply;
        bool installed, running;
        if (OperatingSystem.IsWindows())
        {
            (installed, running) = WindowsStatus();
        }
        else if (OperatingSystem.IsMacOS())
        {
            installed = File.Exists(PlistPath);
            running = installed && (await Run("launchctl", "print", "system/" + LaunchdLabel)).Exit == 0;
        }
        else
        {
            installed = File.Exists(UnitPath);
            running = installed && (await Run("systemctl", "is-active", "--quiet", SystemdName)).Exit == 0;
        }
        var interactive = Environment.UserInteractive;
        var can = published && (OperatingSystem.IsWindows() ? interactive || elevated : elevated);
        var detail = !published ? "Cette instance ne vient pas d'un exécutable publié (lancement de développement) : elle ne peut pas devenir un service."
            : OperatingSystem.IsWindows() && !can ? "Aucune session interactive pour accepter la demande d'élévation (UAC)."
            : !OperatingSystem.IsWindows() && !elevated ? "Les droits administrateur (root) sont nécessaires : lancez la commande ci-dessous."
            : "";
        var manual = new List<string>();
        var launcher = Environment.ProcessPath ?? UpdateService.Launcher;
        if (!OperatingSystem.IsWindows()) manual.Add($"sudo \"{launcher}\" service install --data \"{paths.Root}\"");
        else manual.Add($"Dans un terminal administrateur : \"{launcher}\" service install --data \"{paths.Root}\"");
        return new(true, PlatformName, Name, installed, running, ServerRuntime.IsService, elevated, can, detail, manual);
    }

    static (bool Installed, bool Running) WindowsStatus()
    {
        if (!OperatingSystem.IsWindows()) return (false, false);
        try
        {
            using var controller = new ServiceController(WindowsName);
            return (true, controller.Status == ServiceControllerStatus.Running);
        }
        catch (InvalidOperationException) { return (false, false); }
    }

    public static bool IsElevated()
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        return Environment.IsPrivilegedProcess;
    }

    // ---- Building what gets installed (pure: tested without installing anything) ----

    /// <summary>The command line of the service: always the explicit data folder and the service flag, then the options of the current run.</summary>
    public static List<string> ServeArguments(ServerPaths paths, IEnumerable<string> current)
    {
        var result = new List<string> { "serve", "--data", paths.Root, ServerRuntime.ServiceFlag };
        var list = current.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (i == 0 && list[i] == "serve") continue;
            if (list[i] == ServerRuntime.ServiceFlag) continue;
            if (list[i] is "--data" or ServerRuntime.AfterPid) { i++; continue; }
            result.Add(list[i]);
        }
        return result;
    }

    public static string WindowsBinaryPath(string launcher, IEnumerable<string> serveArguments) =>
        string.Join(' ', new[] { launcher }.Concat(serveArguments).Select(QuoteIfNeeded));

    /// <summary>Quotes only what needs it, so that the command line shown by the service manager stays readable.</summary>
    static string QuoteIfNeeded(string value) => value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"') ? value : GitHubUpdates.QuoteWindowsArgument(value);

    public static string SystemdUnit(string launcher, string root, IEnumerable<string> serveArguments, string? user)
    {
        var lines = new List<string>
        {
            "[Unit]", "Description=" + DisplayName, "After=network-online.target", "Wants=network-online.target", "",
            "[Service]", "Type=simple"
        };
        if (!string.IsNullOrWhiteSpace(user)) lines.Add("User=" + user);
        lines.Add("WorkingDirectory=" + SystemdQuote(root));
        lines.Add("Environment=" + SystemdQuote("DOTNET_BUNDLE_EXTRACT_BASE_DIR=" + Path.Combine(root, "workspace", ".net-bundle")));
        lines.Add("ExecStart=" + string.Join(' ', new[] { launcher }.Concat(serveArguments).Select(SystemdQuote)));
        // Any exit restarts it: a crash, and the deliberate restart that follows an update.
        lines.AddRange(["Restart=always", "RestartSec=5", "", "[Install]", "WantedBy=multi-user.target", ""]);
        return string.Join('\n', lines);
    }

    static string SystemdQuote(string value) => value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || "/._-=:".Contains(c)) ? value : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static string LaunchdPlist(string launcher, string root, IEnumerable<string> serveArguments, string? user)
    {
        static string X(string value) => System.Security.SecurityElement.Escape(value);
        var arguments = string.Concat(new[] { launcher }.Concat(serveArguments).Select(x => $"        <string>{X(x)}</string>\n"));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key><string>{LaunchdLabel}</string>
                <key>ProgramArguments</key>
                <array>
            {arguments.TrimEnd('\n')}
                </array>
                <key>WorkingDirectory</key><string>{X(root)}</string>
                <key>RunAtLoad</key><true/>
                <key>KeepAlive</key><true/>
            {(string.IsNullOrWhiteSpace(user) ? "" : $"    <key>UserName</key><string>{X(user)}</string>\n")}    <key>StandardOutPath</key><string>{X(Path.Combine(root, "workspace", "logs", "service-out.log"))}</string>
                <key>StandardErrorPath</key><string>{X(Path.Combine(root, "workspace", "logs", "service-err.log"))}</string>
            </dict>
            </plist>
            """;
    }

    // ---- Asking for an installation or a removal ----

    /// <summary>A privileged helper that was started and writes its progress in <see cref="ResultFile"/>.</summary>
    public sealed record Handle(string ResultFile, string Operation);

    /// <summary>
    /// Prepares and starts an installation: re-protects the key ring for the machine (Windows, as the account that owns the keys), then starts the
    /// helper with administrator rights. With <paramref name="stopThisProcess"/> the helper waits for this process to end before starting the service.
    /// </summary>
    public static async Task<Handle> BeginInstallAsync(ServerPaths paths, IEnumerable<string> currentArguments, bool stopThisProcess)
    {
        RequirePublishedLauncher();
        if (OperatingSystem.IsWindows())
        {
            var protectedKeys = KeyRing.ProtectToMachine(paths.Keys, Path.Combine(paths.Workspace, "keys-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            if (protectedKeys > 0) Console.Error.WriteLine($"{protectedKeys} clé(s) de chiffrement protégée(s) pour la machine (copie de sécurité dans workspace/).");
        }
        var result = ResultFile(paths, "install");
        Directory.CreateDirectory(Path.GetDirectoryName(result)!);
        File.Delete(result);
        var serve = ServeArguments(paths, currentArguments);
        var arguments = new List<string> { "service", "apply-install", "--data", paths.Root, "--result", result };
        if (stopThisProcess) arguments.AddRange(["--wait-pid", Environment.ProcessId.ToString()]);
        arguments.Add("--");
        arguments.AddRange(serve.Skip(1));
        await StartHelperAsync(arguments);
        return new(result, "install");
    }

    public static async Task<Handle> BeginUninstallAsync(ServerPaths paths)
    {
        RequirePublishedLauncher();
        var result = ResultFile(paths, "uninstall");
        Directory.CreateDirectory(Path.GetDirectoryName(result)!);
        File.Delete(result);
        await StartHelperAsync(["service", "apply-uninstall", "--data", paths.Root, "--result", result]);
        return new(result, "uninstall");
    }

    static void RequirePublishedLauncher()
    {
        if (!UpdateService.CanApply) throw ApiException.BadRequest("not_published", "Seul l'exécutable publié peut être installé comme service (pas un lancement de développement).");
    }

    static Task StartHelperAsync(IReadOnlyList<string> arguments)
    {
        var launcher = Environment.ProcessPath!;
        var info = new ProcessStartInfo(launcher);
        if (OperatingSystem.IsWindows())
        {
            // Elevation needs the shell: the user sees the standard UAC prompt on the machine where the server runs.
            info.UseShellExecute = true;
            info.Verb = "runas";
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.Arguments = string.Join(' ', arguments.Select(GitHubUpdates.QuoteWindowsArgument));
        }
        else
        {
            if (!IsElevated()) throw new ApiException(403, "not_elevated", "Les droits administrateur (root) sont nécessaires : lancez la commande indiquée dans un terminal.");
            info.UseShellExecute = false;
            info.RedirectStandardOutput = info.RedirectStandardError = true;
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
        }
        try { Process.Start(info)?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { throw new ApiException(403, "elevation_refused", "La demande d'élévation (UAC) a été refusée ou a expiré."); }
        return Task.CompletedTask;
    }

    /// <summary>Waits until the helper reports one of the states (or fails, or the time is over).</summary>
    public static async Task<ServiceResult> WaitAsync(Handle handle, TimeSpan timeout, CancellationToken ct, params string[] states)
    {
        var deadline = DateTime.UtcNow + timeout;
        ServiceResult? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = Read(handle.ResultFile) ?? last;
            if (last != null && (last.State == "failed" || states.Contains(last.State))) return last;
            await Task.Delay(300, ct);
        }
        return last is { State: not "failed" } ? new ServiceResult("failed", "Délai dépassé : l'opération n'a pas abouti (demande d'élévation sans réponse ?).") : last ?? new ServiceResult("failed", "Délai dépassé : aucune réponse de l'assistant d'installation.");
    }

    public static ServiceResult? Read(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<ServiceResult>(File.ReadAllText(file), Json) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    static void Report(string? file, string state, string message)
    {
        if (file == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temporary = file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new ServiceResult(state, message), Json));
            File.Move(temporary, file, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ---- The privileged helper ----

    /// <summary><c>service apply-install</c>: creates (or updates) the service, then starts it once the server that asked for it is gone.</summary>
    public static async Task<int> ApplyInstallAsync(ServerPaths paths, string? resultFile, int? waitPid, IReadOnlyList<string> serveRest)
    {
        try
        {
            if (!IsElevated()) throw new InvalidOperationException("Cette opération exige les droits administrateur.");
            var launcher = Environment.ProcessPath ?? throw new InvalidOperationException("Le chemin de l'exécutable est inconnu.");
            var serve = new List<string> { "serve", "--data", paths.Root, ServerRuntime.ServiceFlag };
            serve.AddRange(serveRest);
            Report(resultFile, "working", "Installation du service…");
            if (OperatingSystem.IsWindows()) await InstallWindowsAsync(launcher, paths, serve);
            else if (OperatingSystem.IsMacOS()) await InstallMacAsync(launcher, paths, serve);
            else await InstallLinuxAsync(launcher, paths, serve);
            Report(resultFile, "prepared", "Service installé. Démarrage dès que ce serveur est arrêté.");
            if (waitPid is { } pid) await ServerRuntime.WaitForExitAsync(pid, TimeSpan.FromSeconds(120));
            await StartAsync();
            Report(resultFile, "started", "Le service est démarré : il se lancera avec la machine et redémarrera après une panne.");
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Report(resultFile, "failed", ex.Message);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    public static async Task<int> ApplyUninstallAsync(ServerPaths paths, string? resultFile)
    {
        try
        {
            if (!IsElevated()) throw new InvalidOperationException("Cette opération exige les droits administrateur.");
            Report(resultFile, "working", "Suppression du service…");
            if (OperatingSystem.IsWindows())
            {
                await Checked("sc.exe", ["stop", WindowsName], allowFailure: true);
                await WaitWindowsStoppedAsync(TimeSpan.FromSeconds(60));
                await Checked("sc.exe", ["delete", WindowsName]);
            }
            else if (OperatingSystem.IsMacOS())
            {
                await Checked("launchctl", ["bootout", "system/" + LaunchdLabel], allowFailure: true);
                File.Delete(PlistPath);
            }
            else
            {
                await Checked("systemctl", ["disable", "--now", SystemdName], allowFailure: true);
                File.Delete(UnitPath);
                await Checked("systemctl", ["daemon-reload"], allowFailure: true);
            }
            Report(resultFile, "removed", "Le service a été supprimé. Les données du serveur n'ont pas été touchées.");
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Report(resultFile, "failed", ex.Message);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    static async Task InstallWindowsAsync(string launcher, ServerPaths paths, IReadOnlyList<string> serve)
    {
        var account = @"NT SERVICE\" + WindowsName;
        var binary = WindowsBinaryPath(launcher, serve);
        var exists = WindowsStatus().Installed;
        // A virtual account: no password to keep, no administrator rights for the code the server runs, and its own identity for file permissions.
        await Checked("sc.exe", [exists ? "config" : "create", WindowsName, "binPath=", binary, "start=", "auto", "DisplayName=", DisplayName, "obj=", account]);
        await Checked("sc.exe", ["description", WindowsName, Description]);
        // Restart after 5 s, 15 s then 60 s; the counter of failures resets after a day without any. The flag extends this to stops with an error code
        // (what the server does to restart after an update).
        await Checked("sc.exe", ["failure", WindowsName, "reset=", "86400", "actions=", "restart/5000/restart/15000/restart/60000"]);
        await Checked("sc.exe", ["failureflag", WindowsName, "1"]);
        await Checked("sc.exe", ["sidtype", WindowsName, "unrestricted"]);
        var folders = new[] { paths.Root, Path.GetDirectoryName(launcher)! }.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var folder in folders)
            await Checked("icacls.exe", [folder, "/grant", account + ":(OI)(CI)M", "/Q"]);
    }

    static async Task InstallLinuxAsync(string launcher, ServerPaths paths, IReadOnlyList<string> serve)
    {
        var user = await OwnerOfAsync(paths.Root);
        await File.WriteAllTextAsync(UnitPath, SystemdUnit(launcher, paths.Root, serve, user));
        Directory.CreateDirectory(Path.Combine(paths.Workspace, ".net-bundle"));
        await Checked("systemctl", ["daemon-reload"]);
        await Checked("systemctl", ["enable", SystemdName]);
    }

    static async Task InstallMacAsync(string launcher, ServerPaths paths, IReadOnlyList<string> serve)
    {
        var user = await OwnerOfAsync(paths.Root);
        Directory.CreateDirectory(Path.Combine(paths.Workspace, "logs"));
        await File.WriteAllTextAsync(PlistPath, LaunchdPlist(launcher, paths.Root, serve, user));
        await Checked("chmod", ["644", PlistPath]);
    }

    static async Task StartAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            await Checked("sc.exe", ["start", WindowsName]);
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                using var controller = new ServiceController(WindowsName);
                if (controller.Status == ServiceControllerStatus.Running) return;
                if (controller.Status == ServiceControllerStatus.Stopped) await Task.Delay(1500);
                await Task.Delay(500);
            }
            throw new InvalidOperationException("Le service a été créé mais ne s'est pas mis à fonctionner. Consultez workspace/logs/ et l'observateur d'événements Windows.");
        }
        if (OperatingSystem.IsMacOS()) await Checked("launchctl", ["bootstrap", "system", PlistPath]);
        else await Checked("systemctl", ["start", SystemdName]);
    }

    static async Task WaitWindowsStoppedAsync(TimeSpan limit)
    {
        if (!OperatingSystem.IsWindows()) return;
        var deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var controller = new ServiceController(WindowsName);
                if (controller.Status == ServiceControllerStatus.Stopped) return;
            }
            catch (InvalidOperationException) { return; }
            await Task.Delay(500);
        }
    }

    static async Task<string?> OwnerOfAsync(string folder)
    {
        if (Environment.GetEnvironmentVariable("SUDO_USER") is { Length: > 0 } sudo && sudo != "root") return sudo;
        var result = await Run("stat", OperatingSystem.IsMacOS() ? "-f" : "-c", OperatingSystem.IsMacOS() ? "%Su" : "%U", folder);
        return result.Exit == 0 && result.Output.Trim() is { Length: > 0 } owner && owner != "root" ? owner : null;
    }

    // ---- Processes ----

    public sealed record RunResult(int Exit, string Output);

    public static async Task<RunResult> Run(string file, params string[] arguments)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException(file + " ne démarre pas.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new(process.ExitCode, (await output + await error).Trim());
        }
        catch (System.ComponentModel.Win32Exception ex) { return new(127, ex.Message); }
    }

    static async Task Checked(string file, string[] arguments, bool allowFailure = false)
    {
        var result = await Run(file, arguments);
        if (result.Exit != 0 && !allowFailure)
            throw new InvalidOperationException($"{Path.GetFileNameWithoutExtension(file)} {arguments[0]} a échoué (code {result.Exit}) : {(result.Output.Length > 400 ? result.Output[..400] : result.Output)}");
    }
}
