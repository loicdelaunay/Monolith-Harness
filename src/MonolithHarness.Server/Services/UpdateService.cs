using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MonolithHarness.Core;
using MonolithHarness.Server.Api;

namespace MonolithHarness.Server.Services;

/// <summary>A server release found on the release feed: the archive for this platform, its size and its SHA-256.</summary>
public sealed record ServerRelease(string Version, string Tag, string Page, string AssetName, string DownloadUrl, string Sha256, long Size);

public sealed record UpdateCheck(ServerRelease? Release, string? Error, DateTime CheckedUtc, string Current);

/// <summary>
/// Finds, downloads, verifies and installs a newer server. The release feed is the project's GitHub releases (tag <c>server-vX.Y.Z</c>, archive
/// <c>MonolithHarness-Server-vX.Y.Z-&lt;runtime&gt;</c>); an archive is only accepted when its SHA-256 matches the one published with it, when it holds
/// the launcher and nothing else is taken from it, and when that launcher starts and reports the expected version. Installing renames the running
/// launcher aside (a running executable can be renamed, not overwritten) and puts the new one in its place; the data (database, server.json, workspace/)
/// is never touched.
/// </summary>
public sealed class UpdateService(ServerPaths paths, ServerOptions options, HttpClient? client = null)
{
    public const string Repository = GitHubUpdates.Repository;
    const long MaxBytes = 600L * 1024 * 1024;
    static readonly Regex Digest = new("^sha256:[0-9a-fA-F]{64}$", RegexOptions.Compiled);
    readonly HttpClient http = client ?? new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15), AllowAutoRedirect = true });
    readonly object gate = new();
    UpdateCheck? last;

    public static string CurrentVersion => ApiSupport.Version;

    /// <summary>The runtime identifier used in archive names, or one nobody publishes for (then no update is ever offered).</summary>
    public static string Runtime => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-"
        + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");

    public static string Launcher => OperatingSystem.IsWindows() ? "MonolithHarness.exe" : "MonolithHarness";

    /// <summary>Only a published single-file launcher can replace itself; a development run (dotnet MonolithHarness.Server.dll) cannot.</summary>
    public static bool CanApply => Environment.ProcessPath is { } process && !Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        && Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>().Any(x => x.Key == "MonolithHarness.SingleFile" && x.Value == "true") == true;

    public UpdateCheck? Last { get { lock (gate) return last; } }

    public ServerRelease? Available => Last?.Release;

    /// <summary>Test hook: replaces the "does the downloaded launcher start?" check.</summary>
    internal Func<string, string, CancellationToken, Task>? VerifyLauncher { get; set; }

    // ---- Finding ----

    /// <summary>Looks for a newer release. Never throws: a failure (offline, timeout, bad feed) is reported in the result.</summary>
    public async Task<UpdateCheck> CheckAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        UpdateCheck result;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, options.UpdateFeed);
            request.Headers.UserAgent.ParseAdd("MonolithHarness-Server/" + CurrentVersion);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, limit.Token);
            response.EnsureSuccessStatusCode();
            var releases = JsonNode.Parse(await response.Content.ReadAsStringAsync(limit.Token)) as JsonArray ?? throw new InvalidDataException("The release feed is not a list.");
            result = new(Select(releases, CurrentVersion, Runtime, FeedOrigin), null, DateTime.UtcNow, CurrentVersion);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result = new(null, $"Pas de réponse en {timeout.TotalSeconds:0} s.", DateTime.UtcNow, CurrentVersion); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or UriFormatException)
        { result = new(null, ex is HttpRequestException ? "Le serveur de mises à jour est injoignable : " + ex.Message : ex.Message, DateTime.UtcNow, CurrentVersion); }
        lock (gate) last = result;
        return result;
    }

    /// <summary>Origin allowed for asset downloads besides the project's own releases (only when the feed is not GitHub's: a mirror, a test).</summary>
    Uri? FeedOrigin => Uri.TryCreate(options.UpdateFeed, UriKind.Absolute, out var feed) && !feed.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ? feed : null;

    /// <summary>The newest stable <c>server-vX.Y.Z</c> release above <paramref name="current"/> that has an archive for <paramref name="runtime"/>.</summary>
    public static ServerRelease? Select(JsonArray releases, string current, string runtime, Uri? mirror = null)
    {
        var running = Version.Parse(current);
        ServerRelease? best = null;
        foreach (var release in releases)
        {
            if (release?["draft"]?.GetValue<bool>() == true || release?["prerelease"]?.GetValue<bool>() == true) continue;
            var tag = release?["tag_name"]?.ToString() ?? "";
            var match = Regex.Match(tag, @"^server-v(\d+\.\d+\.\d+)$");
            if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version) || version <= running) continue;
            if (best != null && version <= Version.Parse(best.Version)) continue;
            var extension = runtime.StartsWith("win-", StringComparison.Ordinal) ? "zip" : "tar.gz";
            var expected = $"MonolithHarness-Server-v{version}-{runtime}.{extension}";
            foreach (var asset in release?["assets"]?.AsArray() ?? [])
            {
                var url = asset?["browser_download_url"]?.ToString() ?? "";
                var digest = asset?["digest"]?.ToString() ?? "";
                var size = asset?["size"]?.GetValue<long>() ?? 0;
                if (asset?["name"]?.ToString() != expected || size is <= 0 or > MaxBytes || !Digest.IsMatch(digest) || !TrustedAsset(url, mirror)) continue;
                best = new(version.ToString(), tag, Repository + "/releases/tag/" + tag, expected, url, digest[7..].ToLowerInvariant(), size);
                break;
            }
        }
        return best;
    }

    static bool TrustedAsset(string url, Uri? mirror)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0) return false;
        if (mirror != null && uri.Scheme == mirror.Scheme && uri.Host == mirror.Host && uri.Port == mirror.Port) return true;
        return uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com" && uri.IsDefaultPort
            && uri.AbsolutePath.StartsWith("/loicdelaunay/Monolith-Harness/releases/download/", StringComparison.Ordinal);
    }

    // ---- Downloading ----

    /// <summary>Downloads the archive, checks it and unpacks the launcher beside the data, in a folder of its own. Returns the path of the new launcher.</summary>
    public async Task<string> DownloadAsync(ServerRelease release, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var folder = Path.Combine(paths.Workspace, "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var archive = Path.Combine(folder, release.AssetName);
        var staged = Path.Combine(folder, Launcher);
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromMinutes(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
            request.Headers.UserAgent.ParseAdd("MonolithHarness-Server/" + CurrentVersion);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(limit.Token))
            await using (var output = File.Create(archive))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read, lastPercent = -1;
                while ((read = await input.ReadAsync(buffer, limit.Token)) > 0)
                {
                    total += read;
                    if (total > release.Size) throw new InvalidDataException("Le téléchargement dépasse la taille annoncée.");
                    await output.WriteAsync(buffer.AsMemory(0, read), limit.Token);
                    var percent = (int)(total * 100 / release.Size);
                    if (percent != lastPercent) { lastPercent = percent; progress?.Report(percent); }
                }
                if (total != release.Size) throw new InvalidDataException("Téléchargement incomplet.");
            }
            await using (var file = File.OpenRead(archive))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(file, limit.Token)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("La somme SHA-256 du téléchargement ne correspond pas à celle publiée : mise à jour refusée.");
            await ExtractLauncherAsync(archive, staged, limit.Token);
            File.Delete(archive);
            if (VerifyLauncher is { } verify) await verify(staged, release.Version, limit.Token);
            else await VerifyStartsAsync(staged, release.Version, limit.Token);
            return staged;
        }
        catch
        {
            TryDelete(folder);
            throw;
        }
    }

    /// <summary>The archive must hold exactly the launcher (plus the licence and readme): any other path is ignored, never extracted.</summary>
    public static async Task ExtractLauncherAsync(string archive, string staged, CancellationToken ct = default)
    {
        await using var output = File.Create(staged);
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(archive);
            var entries = zip.Entries.Where(x => x.FullName == Launcher).ToList();
            if (entries.Count != 1 || entries[0].Length is <= 0 or > MaxBytes) throw new InvalidDataException("L'archive ne contient pas exactement un exécutable " + Launcher + ".");
            await using var input = entries[0].Open();
            await input.CopyToAsync(output, ct);
        }
        else
        {
            await using var file = File.OpenRead(archive);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            var found = false;
            while (await tar.GetNextEntryAsync(cancellationToken: ct) is { } entry)
            {
                if (entry.EntryType != TarEntryType.RegularFile || entry.Name != Launcher || entry.DataStream == null) continue;
                if (found) throw new InvalidDataException("L'archive contient plusieurs exécutables.");
                found = true;
                await entry.DataStream.CopyToAsync(output, ct);
            }
            if (!found) throw new InvalidDataException("L'archive ne contient pas l'exécutable " + Launcher + ".");
        }
        await output.FlushAsync(ct);
        output.Close();
        using (var probe = File.OpenRead(staged))
        {
            var head = new byte[4];
            var count = await probe.ReadAsync(head, ct);
            var windows = count >= 2 && head[0] == 'M' && head[1] == 'Z';
            var elf = count >= 4 && head[0] == 0x7F && head[1] == 'E' && head[2] == 'L' && head[3] == 'F';
            var mach = count >= 4 && (head[0] == 0xCF || head[0] == 0xCA) && (head[1] == 0xFA || head[1] == 0xFE);
            if (OperatingSystem.IsWindows() ? !windows : !(elf || mach)) throw new InvalidDataException("Le fichier extrait n'est pas un exécutable de cette plateforme.");
        }
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(staged, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// <summary>Runs the new launcher with --version: it must start on this machine and announce the version that was promised.</summary>
    static async Task VerifyStartsAsync(string launcher, string version, CancellationToken ct)
    {
        var info = new ProcessStartInfo(launcher, "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var process = StartOrFail(info);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(60));
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            throw new InvalidOperationException("Le nouvel exécutable ne répond pas (--version).");
        }
        var text = await output;
        if (process.ExitCode != 0 || !text.Contains(version, StringComparison.Ordinal)) throw new InvalidDataException($"Le nouvel exécutable n'annonce pas la version {version}.");
    }

    static Process StartOrFail(ProcessStartInfo info)
    {
        try { return Process.Start(info) ?? throw new InvalidOperationException("Le nouvel exécutable ne démarre pas."); }
        catch (System.ComponentModel.Win32Exception ex) { throw new InvalidOperationException("Le nouvel exécutable ne démarre pas sur cette machine : " + ex.Message, ex); }
    }

    // ---- Installing ----

    /// <summary>
    /// Puts the downloaded launcher in place of <paramref name="target"/> (the running executable): the old one is renamed to <c>.previous</c> (kept, to go back),
    /// and the web files a published folder keeps beside it are set aside because the new launcher carries its own. Nothing else is touched.
    /// </summary>
    public string Install(string staged, string target)
    {
        target = Path.GetFullPath(target);
        var previous = target + ".previous";
        TryDelete(previous);
        File.Move(target, previous);
        try { File.Move(staged, target); }
        catch
        {
            File.Move(previous, target);
            throw;
        }
        SetAsideLooseWebFiles(Path.GetDirectoryName(target)!);
        TryDelete(Path.GetDirectoryName(staged)!);
        return previous;
    }

    /// <summary>A folder made by <c>dotnet publish</c> has wwwroot and the static-assets manifest beside the launcher; the new launcher's own copy would be ignored in favour of these old ones.</summary>
    void SetAsideLooseWebFiles(string folder)
    {
        var aside = Path.Combine(paths.Workspace, "updates", "replaced-web-files");
        foreach (var name in new[] { "wwwroot", "MonolithHarness.Server.staticwebassets.endpoints.json" })
        {
            var source = Path.Combine(folder, name);
            try
            {
                if (!Directory.Exists(source) && !File.Exists(source)) continue;
                Directory.CreateDirectory(aside);
                var destination = Path.Combine(aside, name);
                TryDelete(destination);
                if (Directory.Exists(source)) Directory.Move(source, destination); else File.Move(source, destination);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the old web files keep being served: cosmetic only */ }
        }
    }

    /// <summary>Leftovers of a previous update that are no longer needed (staging folders of failed or finished updates).</summary>
    public void CleanStaging()
    {
        var root = Path.Combine(paths.Workspace, "updates");
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (var folder in Directory.EnumerateDirectories(root).Where(x => Path.GetFileName(x) != "replaced-web-files" && Path.GetFileName(x).Length == 32))
                TryDelete(folder);
        }
        catch (IOException) { }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
