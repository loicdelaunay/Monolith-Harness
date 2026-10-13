using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using MonolithHarness.Server;
using MonolithHarness.Server.Security;
using MonolithHarness.Server.Services;

/// <summary>Installing the server as a service: what gets registered (without installing anything), the key ring, the web interface's view.</summary>
static class ServiceChecks
{
    static IDataProtectionProvider Ring(string folder, bool machine)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName("test").PersistKeysToFileSystem(new DirectoryInfo(folder)).ProtectKeysWithDpapi(machine);
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "mh-service-" + Guid.NewGuid().ToString("N"));
        var paths = new ServerPaths(root);
        try
        {
            // ---- The command line of the service ----
            var current = new[] { "serve", "--port", "5871", "--data", @"C:\old", "--after-pid", "42", "--service", "--update", "notify" };
            var arguments = ServiceInstaller.ServeArguments(paths, current);
            check(arguments.SequenceEqual(["serve", "--data", paths.Root, "--service", "--port", "5871", "--update", "notify"]), "Le service reçoit le dossier de données explicite et les options du lancement en cours");
            check(ServiceInstaller.ServeArguments(paths, []).SequenceEqual(["serve", "--data", paths.Root, "--service"]), "Sans option, seule la base est passée");
            var binary = ServiceInstaller.WindowsBinaryPath(@"C:\Program Files\Monolith\MonolithHarness.exe", ["serve", "--data", @"C:\Data Folder\srv", "--service"]);
            check(binary == "\"C:\\Program Files\\Monolith\\MonolithHarness.exe\" serve --data \"C:\\Data Folder\\srv\" --service", "La ligne de commande du service Windows protège les chemins avec espaces");

            var unit = ServiceInstaller.SystemdUnit("/opt/monolith/MonolithHarness", "/srv/mh data", ["serve", "--data", "/srv/mh data", "--service"], "monolith");
            check(unit.Contains("ExecStart=/opt/monolith/MonolithHarness serve --data \"/srv/mh data\" --service") && unit.Contains("User=monolith") && unit.Contains("Restart=always") && unit.Contains("RestartSec=5")
                && unit.Contains("WantedBy=multi-user.target") && unit.Contains("After=network-online.target"), "L’unité systemd démarre au boot, redémarre toujours et tourne sous le propriétaire des données");
            check(!ServiceInstaller.SystemdUnit("/x", "/y", ["serve"], null).Contains("User="), "Sans propriétaire connu, aucune ligne User=");
            var plist = XDocument.Parse(ServiceInstaller.LaunchdPlist("/Applications/Monolith & Co/MonolithHarness", "/srv/mh", ["serve", "--service"], "monolith"));
            var keys = plist.Descendants("key").Select(x => x.Value).ToList();
            check(keys.Contains("RunAtLoad") && keys.Contains("KeepAlive") && keys.Contains("UserName") && plist.Descendants("string").Any(x => x.Value == "/Applications/Monolith & Co/MonolithHarness"), "Le plist launchd est un XML valide qui démarre au boot et relance après arrêt");

            // ---- Handing over to a successor: it waits for its predecessor to end ----
            var info = OperatingSystem.IsWindows() ? new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 > nul") : new System.Diagnostics.ProcessStartInfo("sleep", "2");
            info.CreateNoWindow = true; info.UseShellExecute = false;
            using (var predecessor = System.Diagnostics.Process.Start(info)!)
            {
                var waited = System.Diagnostics.Stopwatch.StartNew();
                await ServerRuntime.InitializeAsync(["serve", "--port", "5800", ServerRuntime.AfterPid, predecessor.Id.ToString(), ServerRuntime.ServiceFlag]);
                check(waited.Elapsed.TotalSeconds > 0.8 && predecessor.HasExited, "Un successeur attend la fin de son prédécesseur avant de démarrer");
                check(ServerRuntime.Args.SequenceEqual(["serve", "--port", "5800", ServerRuntime.ServiceFlag]) && ServerRuntime.IsService, "…l’option interne n’est pas conservée, le mode service est reconnu");
            }
            await ServerRuntime.InitializeAsync([]);

            // ---- Progress files of the privileged helper ----
            var file = ServiceInstaller.ResultFile(paths, "install");
            check(ServiceInstaller.Read(file) == null, "Sans assistant en cours, aucun résultat");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(new { state = "prepared", message = "ok" }));
            var handle = new ServiceInstaller.Handle(file, "install");
            var result = await ServiceInstaller.WaitAsync(handle, TimeSpan.FromSeconds(2), CancellationToken.None, "prepared");
            check(result.State == "prepared", "L’état annoncé par l’assistant est relu");
            File.WriteAllText(file, JsonSerializer.Serialize(new { state = "failed", message = "refusé" }));
            result = await ServiceInstaller.WaitAsync(handle, TimeSpan.FromSeconds(2), CancellationToken.None, "started");
            check(result.State == "failed" && result.Message == "refusé", "Un échec est rapporté tel quel, sans attendre la fin du délai");
            File.Delete(file);
            var timeout = await ServiceInstaller.WaitAsync(handle, TimeSpan.FromMilliseconds(700), CancellationToken.None, "started");
            check(timeout.State == "failed", "Sans réponse dans le délai, l’opération est déclarée échouée");

            // ---- Key ring: the same keys readable by the console and the service ----
            if (OperatingSystem.IsWindows())
            {
                var keys1 = Path.Combine(root, "keys");
                Directory.CreateDirectory(keys1);
                var secret = Ring(keys1, machine: false).CreateProtector("providers").Protect("sk-my-secret");
                var before = Directory.GetFiles(keys1, "*.xml").Select(File.ReadAllText).ToList();
                var count = KeyRing.ProtectToMachine(keys1, Path.Combine(root, "keys-backup"));
                var after = Directory.GetFiles(keys1, "*.xml").Select(File.ReadAllText).ToList();
                check(count == 1 && !before.SequenceEqual(after), "Les clés protégées pour l’utilisateur sont re-protégées pour la machine");
                check(Directory.GetFiles(Path.Combine(root, "keys-backup"), "*.xml").Length == 1, "Une copie de sécurité des clés est faite avant");
                check(Ring(keys1, machine: true).CreateProtector("providers").Unprotect(secret) == "sk-my-secret", "Les secrets déjà chiffrés restent lisibles avec les clés re-protégées");
                check(KeyRing.ProtectToMachine(keys1, Path.Combine(root, "keys-backup2")) == 1 && Directory.GetFiles(keys1, "*.xml").Length == 1, "Refaire l’opération est sans danger");
            }
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        // ---- The administrator's view ----
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        var (_, user2) = await server.NewUserAsync(admin, "svc@example.com");
        var (status, state) = await server.Get("/api/admin/service", admin);
        check(status == 200 && !state.GetProperty("installed").GetBoolean() && !state.GetProperty("thisProcess").GetBoolean() && state.GetProperty("name").GetString() == ServiceInstaller.Name, "L’administrateur voit que le service n’est pas installé");
        check(!state.GetProperty("canInstallHere").GetBoolean() && state.GetProperty("detail").GetString()!.Length > 0, "Une instance de développement explique pourquoi elle ne peut pas devenir un service");
        check((await server.Get("/api/admin/service", user2)).Status == 403 && (await server.Post("/api/admin/service/install", null, user2)).Status == 403 && (await server.Delete("/api/admin/service", user2)).Status == 403, "Un utilisateur ordinaire ne peut ni voir ni changer le service");
        var (installStatus, install) = await server.Post("/api/admin/service/install", null, admin);
        check(installStatus == 400 && install.GetProperty("code").GetString() == "cannot_install", "Installer depuis une instance de développement est refusé clairement");
        var (removeStatus, remove) = await server.Delete("/api/admin/service", admin);
        check(removeStatus == 400 && remove.GetProperty("code").GetString() == "not_published", "Désinstaller depuis une instance de développement est refusé clairement");
    }
}
