using MonolithHarness.Server.Services;

namespace MonolithHarness.Server;

/// <summary>
/// The update check made before the server starts: a bounded look at the release feed, then, depending on <c>updateMode</c>, a Y/N question on the
/// console (which expires: no answer is "no"), an automatic installation, or just a notice. Whatever happens, a failure never prevents the server
/// from starting in the version that is installed.
/// </summary>
public static class StartupUpdate
{
    /// <summary>Returns null to carry on starting, or the exit code of this process when an update was installed and a successor takes over.</summary>
    public static async Task<int?> RunAsync(UpdateService updates, ServerOptions options, CancellationToken ct = default)
    {
        if (options.UpdateMode == "off") return null;
        var check = await updates.CheckAsync(TimeSpan.FromSeconds(options.UpdateTimeoutSeconds), ct);
        if (check.Release is not { } release)
        {
            if (check.Error != null) Console.WriteLine($"Mise à jour : vérification impossible ({check.Error}) ; démarrage en version {check.Current}.");
            return null;
        }
        Console.WriteLine($"Une nouvelle version du serveur est disponible : {release.Version} (version actuelle : {check.Current}). Notes : {release.Page}");
        if (!UpdateService.CanApply)
        {
            Console.WriteLine("Cette instance ne vient pas d'un exécutable publié : mise à jour automatique indisponible.");
            return null;
        }
        var interactive = !ServerRuntime.IsService && Environment.UserInteractive && !Console.IsInputRedirected && !Console.IsOutputRedirected;
        bool apply;
        if (options.UpdateMode == "auto") apply = true;
        else if (options.UpdateMode == "ask" && interactive) apply = await AskAsync(options.UpdatePromptSeconds, ct);
        else
        {
            Console.WriteLine("Elle peut être installée depuis l'interface web (Administration › Maintenance) ou en relançant le serveur avec « --update auto ».");
            return null;
        }
        if (!apply) return null;
        try
        {
            var last = -1;
            var progress = new Progress<int>(percent =>
            {
                if (percent / 10 != last / 10 || percent == 100) { last = percent; Console.Write($"\rTéléchargement… {percent} %   "); }
            });
            Console.WriteLine($"Téléchargement de la version {release.Version}…");
            var staged = await updates.DownloadAsync(release, progress, ct);
            Console.WriteLine();
            var target = Environment.ProcessPath!;
            var previous = updates.Install(staged, target);
            Console.WriteLine($"Version {release.Version} installée (l'ancien exécutable est conservé : {Path.GetFileName(previous)}). Redémarrage…");
            if (ServerRuntime.IsService) return ServerLifecycle.RestartExitCode;
            ServerRuntime.Relaunch();
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine($"La mise à jour a échoué ({ex.Message}). Le serveur démarre en version {check.Current}.");
            return null;
        }
    }

    /// <summary>"Y" (or "O" for oui) installs; anything else, or no answer within the delay, does not.</summary>
    static async Task<bool> AskAsync(int seconds, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var shown = -1;
        Console.WriteLine("Mettre à jour maintenant et redémarrer ? [Y/N]");
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var remaining = (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds);
            if (remaining != shown) { shown = remaining; Console.Write($"\rRéponse attendue dans {remaining} s (sans réponse : N)   "); }
            try
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true).KeyChar;
                    Console.WriteLine(key);
                    return char.ToUpperInvariant(key) is 'Y' or 'O';
                }
            }
            catch (InvalidOperationException) { break; }
            await Task.Delay(100, ct);
        }
        Console.WriteLine();
        Console.WriteLine("Pas de réponse : le serveur démarre sans se mettre à jour.");
        return false;
    }
}
