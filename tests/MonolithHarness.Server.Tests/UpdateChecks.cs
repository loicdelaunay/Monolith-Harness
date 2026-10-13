using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MonolithHarness.Server;
using MonolithHarness.Server.Services;

/// <summary>The server's update: choosing a release, verifying the download, swapping the launcher without touching the data, the startup question.</summary>
static class UpdateChecks
{
    const string Current = "1.96.0";

    static JsonObject Release(string tag, string name, string url, long size, string? digest, bool draft = false, bool prerelease = false) => new()
    {
        ["tag_name"] = tag, ["draft"] = draft, ["prerelease"] = prerelease,
        ["assets"] = new JsonArray(new JsonObject { ["name"] = name, ["browser_download_url"] = url, ["size"] = size, ["digest"] = digest })
    };

    const string Official = "https://github.com/loicdelaunay/Monolith-Harness/releases/download/";

    public static async Task Run(Action<bool, string> check)
    {
        var sha = "sha256:" + new string('a', 64);

        // ---- Choosing the release ----
        var releases = new JsonArray(
            Release("v1.99.0", "MonolithHarness-v1.99.0-win-x64.zip", Official + "v1.99.0/MonolithHarness-v1.99.0-win-x64.zip", 100, sha),
            Release("cli-v1.99.0", "MonolithHarness-CLI-v1.99.0-win-x64.zip", Official + "cli-v1.99.0/MonolithHarness-CLI-v1.99.0-win-x64.zip", 100, sha),
            Release("server-v1.97.0", "MonolithHarness-Server-v1.97.0-win-x64.zip", Official + "server-v1.97.0/MonolithHarness-Server-v1.97.0-win-x64.zip", 100, sha),
            Release("server-v1.98.0", "MonolithHarness-Server-v1.98.0-win-x64.zip", Official + "server-v1.98.0/MonolithHarness-Server-v1.98.0-win-x64.zip", 5000, sha),
            Release("server-v2.0.0", "MonolithHarness-Server-v2.0.0-win-x64.zip", Official + "server-v2.0.0/MonolithHarness-Server-v2.0.0-win-x64.zip", 100, sha, draft: true),
            Release("server-v2.1.0", "MonolithHarness-Server-v2.1.0-win-x64.zip", Official + "server-v2.1.0/MonolithHarness-Server-v2.1.0-win-x64.zip", 100, sha, prerelease: true),
            Release("server-v1.96.0", "MonolithHarness-Server-v1.96.0-win-x64.zip", Official + "server-v1.96.0/MonolithHarness-Server-v1.96.0-win-x64.zip", 100, sha),
            Release("server-v3.0.0", "MonolithHarness-Server-v3.0.0-win-x64.zip", "https://evil.example/MonolithHarness-Server-v3.0.0-win-x64.zip", 100, sha),
            Release("server-v3.1.0", "MonolithHarness-Server-v3.1.0-win-x64.zip", Official + "server-v3.1.0/MonolithHarness-Server-v3.1.0-win-x64.zip", 100, null),
            Release("server-v3.2.0", "MonolithHarness-Server-v3.2.0-linux-x64.tar.gz", Official + "server-v3.2.0/MonolithHarness-Server-v3.2.0-linux-x64.tar.gz", 100, sha),
            Release("server-v3.3.0", "MonolithHarness-Server-v3.3.0-win-x64.zip", Official + "x/..", 0, sha));
        var chosen = UpdateService.Select(releases, Current, "win-x64");
        check(chosen is { Version: "1.98.0", Tag: "server-v1.98.0", Size: 5000 }, "On retient la version serveur stable la plus récente qui a une archive pour cette plateforme");
        check(chosen?.Sha256 == new string('a', 64), "La somme SHA-256 est celle publiée avec l’archive");
        check(UpdateService.Select(releases, "1.98.0", "win-x64") == null && UpdateService.Select(releases, "9.0.0", "win-x64") == null, "Pas de mise à jour quand on est à jour ou plus récent");
        check(UpdateService.Select(releases, Current, "linux-x64")?.Version == "3.2.0" && UpdateService.Select(releases, Current, "osx-arm64") == null,
            "Chaque plateforme prend l’archive qui lui correspond; brouillons, préversions, sources non officielles et archives sans somme sont ignorés");
        var mirror = new JsonArray(Release("server-v1.97.0", "MonolithHarness-Server-v1.97.0-win-x64.zip", "http://127.0.0.1:9/dl/a.zip", 10, sha));
        check(UpdateService.Select(mirror, Current, "win-x64") == null && UpdateService.Select(mirror, Current, "win-x64", new Uri("http://127.0.0.1:9/releases")) != null,
            "Un miroir configuré peut héberger ses archives, rien d’autre que la source officielle sinon");

        // ---- Options ----
        var paths = new ServerPaths(Path.Combine(Path.GetTempPath(), "mh-update-" + Guid.NewGuid().ToString("N")));
        paths.Ensure();
        try
        {
            check(Throws(() => new ServerOptions { UpdateMode = "sometimes" }.Validate()), "Un mode de mise à jour inconnu est refusé");
            check(Throws(() => new ServerOptions { UpdateFeed = "http://updates.example/feed" }.Validate()), "Un flux de mises à jour en HTTP clair hors de cette machine est refusé");
            check(!Throws(() => new ServerOptions { UpdateFeed = "http://127.0.0.1:1/feed", UpdateMode = "NOTIFY" }.Validate()), "Un flux local en HTTP est accepté (miroir, essais)");
            check(Throws(() => new ServerOptions { UpdateTimeoutSeconds = 0 }.Validate()) && Throws(() => new ServerOptions { UpdatePromptSeconds = 1 }.Validate()), "Les délais sont bornés");
            var loaded = ServerOptions.Load(paths, ["--no-update-check"]);
            check(loaded.UpdateMode == "off", "--no-update-check coupe la recherche");
            check(ServerOptions.Load(paths, ["--update", "auto"]).UpdateMode == "auto", "--update règle le mode");

            // ---- A feed and a download, locally ----
            var archive = Zip(("MonolithHarness.exe", Encoding.ASCII.GetBytes("MZ" + new string('x', 4000))), ("LICENSE", "MIT"u8.ToArray()), ("README.txt", "hello"u8.ToArray()), ("../escape.txt", "no"u8.ToArray()));
            var good = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
            var served = archive;
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var feed = builder.Build();
            var releasesJson = "";
            feed.MapGet("/releases", () => Results.Text(releasesJson, "application/json"));
            feed.MapGet("/download/{name}", (string name) => Results.Bytes(served, "application/zip"));
            feed.MapGet("/hang", async (HttpContext http) => await Task.Delay(Timeout.Infinite, http.RequestAborted));
            await feed.StartAsync();
            var origin = feed.Urls.First().TrimEnd('/');
            string Json(string digest, long size, string version = "9.9.9") =>
                new JsonArray(Release("server-v" + version, $"MonolithHarness-Server-v{version}-{UpdateService.Runtime}.zip", $"{origin}/download/MonolithHarness-Server-v{version}-{UpdateService.Runtime}.zip", size, "sha256:" + digest)).ToJsonString();
            releasesJson = Json(good, archive.Length);
            var options = new ServerOptions { UpdateFeed = origin + "/releases", UpdateMode = "notify" };
            var updates = new UpdateService(paths, options) { VerifyLauncher = (_, _, _) => Task.CompletedTask };
            var found = await updates.CheckAsync(TimeSpan.FromSeconds(5));
            check(found.Release is { Version: "9.9.9" } && found.Error == null && updates.Available?.Version == "9.9.9", "Le flux de releases est lu et la mise à jour détectée");

            var percents = new List<int>();
            var staged = await updates.DownloadAsync(found.Release!, new Progress<int>(percents.Add));
            check(File.Exists(staged) && Path.GetFileName(staged) == UpdateService.Launcher && File.ReadAllBytes(staged).Length > 4000, "L’exécutable est extrait de l’archive vérifiée");
            await Task.Delay(100);
            check(percents.Count > 0 && percents.Last() == 100, "La progression du téléchargement est rapportée jusqu’à 100 %");
            check(!File.Exists(Path.Combine(Path.GetDirectoryName(staged)!, "..", "escape.txt")) && !File.Exists(Path.Combine(paths.Workspace, "escape.txt")), "Rien d’autre que l’exécutable n’est extrait de l’archive");

            // A different archive than the one announced is refused, and leaves nothing behind.
            served = Zip(("MonolithHarness.exe", Encoding.ASCII.GetBytes("MZ" + new string('y', 4000))));
            releasesJson = Json(good, served.Length);
            var tampered = await updates.CheckAsync(TimeSpan.FromSeconds(5));
            var stagingBefore = Directory.Exists(Path.Combine(paths.Workspace, "updates")) ? Directory.GetDirectories(Path.Combine(paths.Workspace, "updates")).Length : 0;
            check(await Throws(() => updates.DownloadAsync(tampered.Release!)), "Une archive dont la somme SHA-256 ne correspond pas est refusée");
            check((Directory.Exists(Path.Combine(paths.Workspace, "updates")) ? Directory.GetDirectories(Path.Combine(paths.Workspace, "updates")).Length : 0) == stagingBefore, "…et son dossier de travail est supprimé");
            served = Zip(("MonolithHarness.exe", Encoding.ASCII.GetBytes("NOPE" + new string('z', 100))));
            releasesJson = Json(Convert.ToHexString(SHA256.HashData(served)).ToLowerInvariant(), served.Length);
            var notExecutable = (await updates.CheckAsync(TimeSpan.FromSeconds(5))).Release!;
            check(await Throws(() => updates.DownloadAsync(notExecutable)) || !OperatingSystem.IsWindows(), "Un fichier qui n’est pas un exécutable de la plateforme est refusé");
            served = Zip(("something-else.exe", Encoding.ASCII.GetBytes("MZ" + new string('q', 100))));
            releasesJson = Json(Convert.ToHexString(SHA256.HashData(served)).ToLowerInvariant(), served.Length);
            var noLauncher = (await updates.CheckAsync(TimeSpan.FromSeconds(5))).Release!;
            check(await Throws(() => updates.DownloadAsync(noLauncher)), "Une archive sans le lanceur attendu est refusée");

            // The new launcher must start and announce the promised version.
            served = archive;
            releasesJson = Json(good, archive.Length);
            var strict = new UpdateService(paths, options);
            var fake = (await strict.CheckAsync(TimeSpan.FromSeconds(5))).Release!;
            check(await Throws(() => strict.DownloadAsync(fake)), "Un exécutable qui ne démarre pas (ici un faux) fait échouer la mise à jour avant tout remplacement");

            // ---- The feed is slow or gone ----
            var slow = new UpdateService(paths, new ServerOptions { UpdateFeed = origin + "/hang" });
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var late = await slow.CheckAsync(TimeSpan.FromSeconds(1));
            check(late.Release == null && late.Error != null && clock.Elapsed.TotalSeconds < 4, "Un flux qui ne répond pas est abandonné au délai prévu");
            var gone = await new UpdateService(paths, new ServerOptions { UpdateFeed = "http://127.0.0.1:1/none" }).CheckAsync(TimeSpan.FromSeconds(3));
            check(gone.Release == null && gone.Error != null, "Un flux injoignable est signalé sans lever d’exception");

            // ---- The startup step ----
            var output = new StringWriter();
            var original = Console.Out;
            Console.SetOut(output);
            try
            {
                releasesJson = Json(good, archive.Length);
                var notify = new UpdateService(paths, new ServerOptions { UpdateFeed = origin + "/releases", UpdateMode = "notify" });
                var outcome = await StartupUpdate.RunAsync(notify, new ServerOptions { UpdateFeed = origin + "/releases", UpdateMode = "notify", UpdateTimeoutSeconds = 5 });
                check(outcome == null && output.ToString().Contains("9.9.9"), "Mode notify: la nouvelle version est annoncée et le serveur démarre");
                output.GetStringBuilder().Clear();
                var off = await StartupUpdate.RunAsync(notify, new ServerOptions { UpdateMode = "off", UpdateFeed = "http://127.0.0.1:1/none" });
                check(off == null && output.ToString().Length == 0, "Mode off: aucune recherche, aucun message");
                var ask = await StartupUpdate.RunAsync(notify, new ServerOptions { UpdateFeed = origin + "/releases", UpdateMode = "ask", UpdateTimeoutSeconds = 5 });
                check(ask == null, "Mode ask sans console interactive (tests, service): on ne bloque jamais le démarrage");
                var unreachable = await StartupUpdate.RunAsync(new UpdateService(paths, new ServerOptions { UpdateFeed = "http://127.0.0.1:1/none" }), new ServerOptions { UpdateFeed = "http://127.0.0.1:1/none", UpdateTimeoutSeconds = 2 });
                check(unreachable == null && output.ToString().Contains("vérification impossible"), "Sans réseau, le démarrage continue en le disant");
            }
            finally { Console.SetOut(original); }

            // ---- Swapping the launcher ----
            var folder = Path.Combine(paths.Root, "install");
            Directory.CreateDirectory(Path.Combine(folder, "wwwroot"));
            File.WriteAllText(Path.Combine(folder, "wwwroot", "app.css"), "old");
            File.WriteAllText(Path.Combine(folder, "MonolithHarness.Server.staticwebassets.endpoints.json"), "{}");
            File.WriteAllText(Path.Combine(folder, "database.sqlite"), "my data");
            File.WriteAllText(Path.Combine(folder, "server.json"), "{}");
            var target = Path.Combine(folder, UpdateService.Launcher);
            File.WriteAllText(target, "OLD LAUNCHER");
            var fresh = Path.Combine(paths.Workspace, "updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fresh);
            var newLauncher = Path.Combine(fresh, UpdateService.Launcher);
            File.WriteAllText(newLauncher, "NEW LAUNCHER");
            var previous = updates.Install(newLauncher, target);
            check(File.ReadAllText(target) == "NEW LAUNCHER" && File.ReadAllText(previous) == "OLD LAUNCHER" && previous == target + ".previous", "L’exécutable est remplacé et l’ancien est gardé (.previous) pour revenir en arrière");
            check(File.ReadAllText(Path.Combine(folder, "database.sqlite")) == "my data" && File.Exists(Path.Combine(folder, "server.json")), "La base et server.json ne sont pas touchées");
            check(!Directory.Exists(Path.Combine(folder, "wwwroot")) && Directory.Exists(Path.Combine(paths.Workspace, "updates", "replaced-web-files", "wwwroot")), "Les fichiers web d’un dossier publié sont mis de côté (le nouvel exécutable embarque les siens)");
            var second = Path.Combine(paths.Workspace, "updates", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(second, UpdateService.Launcher), "NEWER");
            updates.Install(Path.Combine(second, UpdateService.Launcher), target);
            check(File.ReadAllText(target) == "NEWER" && File.ReadAllText(target + ".previous") == "NEW LAUNCHER", "Une mise à jour suivante remplace l’ancienne sauvegarde");
            updates.CleanStaging();
            check(Directory.GetDirectories(Path.Combine(paths.Workspace, "updates")).All(x => Path.GetFileName(x) == "replaced-web-files"), "Les dossiers de travail laissés par une mise à jour sont nettoyés au démarrage");
        }
        finally
        {
            try { Directory.Delete(paths.Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        // ---- The administrator's view ----
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        var (_, user) = await server.NewUserAsync(admin, "updater@example.com");
        var (status, info) = await server.Get("/api/admin/update", admin);
        check(status == 200 && info.GetProperty("current").GetString() == UpdateService.CurrentVersion && !info.GetProperty("canApply").GetBoolean() && info.GetProperty("mode").GetString() == "off", "L’administrateur voit la version, le mode et si la mise à jour automatique est possible");
        check((await server.Get("/api/admin/update", user)).Status == 403 && (await server.Post("/api/admin/update/apply", null, user)).Status == 403, "Un utilisateur ordinaire n’y a pas accès");
        var (applyStatus, apply) = await server.Post("/api/admin/update/apply", null, admin);
        check(applyStatus == 400 && apply.GetProperty("code").GetString() == "update_unavailable", "Une instance de développement refuse de se mettre à jour elle-même");
    }

    static byte[] Zip(params (string Name, byte[] Content)[] files)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var (name, content) in files)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(content);
            }
        return stream.ToArray();
    }

    static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
    }

    static async Task<bool> Throws(Func<Task> action)
    {
        try { await action(); return false; }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or HttpRequestException or IOException) { return true; }
    }
}
