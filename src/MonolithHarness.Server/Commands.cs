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

        Options:
          --port <n>             HTTP port (default 5800, or "port" in server.json)
          --host <address>       Listening address (default 127.0.0.1; use 0.0.0.0 for remote access)
          --https-port <n>       Also listen with HTTPS on this port (needs --cert)
          --cert <file.pfx>      PKCS#12 certificate for HTTPS
          --cert-password <pw>   Certificate password
          --behind-proxy         Trust X-Forwarded-* headers from a reverse proxy
          --redirect-https       Redirect HTTP to HTTPS
          --data <folder>        Folder holding database.sqlite, server.json and workspace/ (default: beside the executable)
          --version, --help

        The database, server.json and the workspace folder live beside the executable, like the Desktop.
        """;

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(Help); return 0; }
            if (args.Contains("--version")) { Console.WriteLine($"{Api.ApiSupport.Name} {Api.ApiSupport.Version}"); return 0; }
            var command = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : "serve";
            var rest = command == "serve" && (args.Length == 0 || args[0].StartsWith('-')) ? args : args.Skip(1).ToArray();
            switch (command)
            {
                case "worker": return await WorkerMode.RunAsync(rest);
                case "serve": return await ServeAsync(rest);
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
        var app = await ServerApp.BuildAsync(paths, options);
        app.Lifetime.ApplicationStarted.Register(() => Banner(app, paths, options));
        await app.RunAsync();
        return 0;
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
