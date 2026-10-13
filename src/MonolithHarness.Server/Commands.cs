using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;
using MonolithHarness.Server.Services;
using MonolithHarness.Server.Workers;

namespace MonolithHarness.Server;

/// <summary>Command line: serve (default), reset-password, restore, plus the internal worker mode.</summary>
public static class Commands
{
    const string Help = """
        Monolith Harness Server

        Usage: MonolithHarness [serve] [options]
               MonolithHarness reset-password --email <address> [--data <folder>]
               MonolithHarness restore --from <backup.sqlite> [--data <folder>]
               MonolithHarness service <install|uninstall|status> [--data <folder>]

        Options:
          --port <n>             HTTP port (default 5800, or "port" in server.json)
          --host <address>       Listening address (default 127.0.0.1; use 0.0.0.0 for remote access)
          --https-port <n>       Also listen with HTTPS on this port (needs --cert)
          --cert <file.pfx>      PKCS#12 certificate for HTTPS
          --cert-password <pw>   Certificate password
          --behind-proxy         Trust X-Forwarded-* headers from a reverse proxy
          --redirect-https       Redirect HTTP to HTTPS
          --data <folder>        Folder holding database.sqlite, server.json and workspace/ (default: beside the executable)
          --update <mode>        New versions at startup: ask (Y/N on the console, default), auto, notify or off
          --no-update-check      Same as --update off
          --update-feed <url>    Release feed to read (default: the project's GitHub releases)
          --version, --help

        The database, server.json and the workspace folder live beside the executable, like the Desktop.

        "service install" registers the server as a service that starts with the machine and restarts after a failure
        (Windows service, systemd unit, launchd daemon); administrator rights are asked for when needed.
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            await ServerRuntime.InitializeAsync(args);
            args = [.. ServerRuntime.Args];
            if (args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(Help); return 0; }
            if (args.Contains("--version")) { Console.WriteLine($"{Api.ApiSupport.Name} {Api.ApiSupport.Version}"); return 0; }
            var command = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : "serve";
            var rest = command == "serve" && (args.Length == 0 || args[0].StartsWith('-')) ? args : args.Skip(1).ToArray();
            switch (command)
            {
                case "worker": return await WorkerMode.RunAsync(rest);
                case "serve": return await ServeAsync(rest);
                case "service": return await ServiceAsync(rest);
                case "reset-password": return await ResetPasswordAsync(rest);
                case "restore": return Restore(rest);
                default: throw new ArgumentException("Unknown command: " + command + ". Use --help.");
            }
        }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }
        catch (OperationCanceledException) { return 130; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }

    static ServerPaths Paths(IReadOnlyList<string> args)
    {
        var index = args.ToList().IndexOf("--data");
        var folder = index >= 0 && index + 1 < args.Count ? args[index + 1] : Environment.GetEnvironmentVariable("MONOLITH_SERVER_DATA");
        return new ServerPaths(string.IsNullOrWhiteSpace(folder) ? ServerPaths.DefaultRoot() : folder);
    }

    static async Task<int> ServeAsync(string[] args)
    {
        var paths = Paths(args);
        paths.Ensure();
        ServerOptions.EnsureFile(paths);
        var options = ServerOptions.Load(paths, args);
        var updates = new Services.UpdateService(paths, options);
        updates.CleanStaging();
        if (await StartupUpdate.RunAsync(updates, options) is { } handedOver) return handedOver;
        var app = await ServerApp.BuildAsync(paths, options, builder => builder.Services.AddSingleton(updates));
        app.Lifetime.ApplicationStarted.Register(() => Banner(app, paths, options));
        var lifecycle = app.Services.GetRequiredService<Services.ServerLifecycle>();
        await app.RunAsync();
        // A service that asked to be restarted ends with a failure code, which is what its recovery actions react to.
        return ServerRuntime.IsService && lifecycle.RestartRequested ? Services.ServerLifecycle.RestartExitCode : 0;
    }

    static async Task<int> ServiceAsync(string[] args)
    {
        var action = args.Length > 0 ? args[0] : "status";
        var rest = args.Skip(1).ToArray();
        var paths = Paths(rest);
        paths.Ensure();
        string? Option(string name) => ServerRuntime.ArgumentOf(rest, name);
        switch (action)
        {
            case "status":
            {
                var state = await Services.ServiceInstaller.StatusAsync(paths);
                Console.WriteLine($"{state.Platform} · {state.Name} : {(state.Installed ? (state.Running ? "installé, en cours d'exécution" : "installé, arrêté") : "non installé")}");
                if (state.Detail.Length > 0) Console.WriteLine(state.Detail);
                return 0;
            }
            case "install":
            {
                var handle = await Services.ServiceInstaller.BeginInstallAsync(paths, rest, stopThisProcess: false);
                var result = await Services.ServiceInstaller.WaitAsync(handle, TimeSpan.FromMinutes(3), CancellationToken.None, "started");
                Console.WriteLine(result.Message);
                return result.State == "started" ? 0 : 1;
            }
            case "uninstall":
            {
                var handle = await Services.ServiceInstaller.BeginUninstallAsync(paths);
                var result = await Services.ServiceInstaller.WaitAsync(handle, TimeSpan.FromMinutes(3), CancellationToken.None, "removed");
                Console.WriteLine(result.Message);
                return result.State == "removed" ? 0 : 1;
            }
            // Internal: the elevated helper started by the commands above or by the web interface.
            case "apply-install":
            {
                var separator = Array.IndexOf(rest, "--");
                var wait = int.TryParse(Option("--wait-pid"), out var pid) ? pid : (int?)null;
                return await Services.ServiceInstaller.ApplyInstallAsync(paths, Option("--result"), wait, separator < 0 ? [] : rest[(separator + 1)..]);
            }
            case "apply-uninstall": return await Services.ServiceInstaller.ApplyUninstallAsync(paths, Option("--result"));
            default: throw new ArgumentException("Unknown service action: " + action + ". Use install, uninstall or status.");
        }
    }

    static void Banner(WebApplication app, ServerPaths paths, ServerOptions options)
    {
        var log = app.Logger;
        foreach (var address in app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses ?? [])
            log.LogInformation("Listening on {Address}   (web interface, REST at /api, Swagger at /swagger, WebSocket at /ws)", address);
        log.LogInformation("Data folder: {Folder}", paths.Root);
        if (options.Address.Equals(System.Net.IPAddress.Loopback)) log.LogInformation("Loopback only: pass --host 0.0.0.0 (or set \"host\" in server.json) to accept remote clients.");
        else if (options.HttpsPort == null && !options.BehindProxy) log.LogWarning("Remote access over plain HTTP: use --https-port with a certificate, or a TLS reverse proxy (--behind-proxy).");
    }

    /// <summary>Console access to the machine is the proof of ownership: prints a one-hour recovery code for the account.</summary>
    static async Task<int> ResetPasswordAsync(string[] args)
    {
        var index = args.ToList().IndexOf("--email");
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("reset-password requires --email <address>.");
        var email = AccountService.NormalizeEmail(args[index + 1]);
        var paths = Paths(args);
        paths.Ensure();
        var factory = new PooledDbContextFactory<ServerDb>(ServerDb.Options(paths.Database));
        await using (var db = await factory.CreateDbContextAsync()) await db.Database.MigrateAsync();
        var settings = new SettingsService(factory);
        await settings.LoadAsync();
        var audit = new AuditService(factory, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
        var accounts = new AccountService(factory, settings, new SessionService(factory, settings), new LoginThrottle(settings), audit);
        Guid id;
        await using (var db = await factory.CreateDbContextAsync())
            id = (await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email))?.Id ?? throw new InvalidOperationException("No account uses " + email);
        var (token, expires) = await accounts.IssueRecoveryAsync(id, "console");
        Console.WriteLine($"Recovery code for {email} (valid until {expires:u}):");
        Console.WriteLine(token);
        Console.WriteLine("Use it on the sign-in page (\"Recovery code\") or with POST /api/auth/recover.");
        return 0;
    }

    static int Restore(string[] args)
    {
        var index = args.ToList().IndexOf("--from");
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("restore requires --from <backup.sqlite>.");
        var paths = Paths(args);
        paths.Ensure();
        var kept = BackupService.Restore(paths, args[index + 1]);
        Console.WriteLine("Database restored. Start the server again." + (kept.Length > 0 ? " Previous database kept as " + kept : ""));
        return 0;
    }
}
