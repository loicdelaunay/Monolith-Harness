using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public static class LocalRuntimeInstaller
{
    static readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
    static readonly SemaphoreSlim installGate = new(1, 1);
    public static string Backend(LocalProviderSettings settings, MachineCapabilities machine) => settings.Backend == "auto"
        ? OperatingSystem.IsMacOS() ? "metal" : machine.Gpu is not ("" or "non détecté") ? "vulkan" : "cpu" : settings.Backend;
    static string Folder(LocalProviderSettings settings, string purpose, string backend) => Path.Combine(settings.ModelDirectory, ".runtime", purpose, backend);
    public static string Installed(LocalProviderSettings settings, string purpose, string backend)
    {
        var custom = purpose == "chat" ? settings.ChatExecutable : settings.ImageExecutable;
        if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return Path.GetFullPath(custom);
        var folder = Folder(settings, purpose, backend); var marker = Path.Combine(folder, "installed.json");
        if (!File.Exists(marker)) throw new InvalidOperationException("Préparez le moteur local dans Réglages → Fournisseurs → Local → Préparer / charger.");
        var name = JsonNode.Parse(File.ReadAllText(marker))?["Executable"]?.ToString() ?? "";
        var path = Path.GetFullPath(Path.Combine(folder, name));
        if (!path.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, PlatformSupport.PathComparison) || !File.Exists(path)) throw new IOException("Moteur local incomplet : préparez-le à nouveau.");
        return path;
    }
    public static async Task<string> EnsureAsync(LocalProviderSettings settings, string purpose, MachineCapabilities machine, IProgress<LocalTransferProgress>? progress, CancellationToken ct)
    {
        var backend = Backend(settings, machine);
        await installGate.WaitAsync(ct);
        try
        {
            try { return Installed(settings, purpose, backend); } catch (InvalidOperationException) { }
            if (!machine.RuntimeSupported) throw new PlatformNotSupportedException("Moteurs locaux : Windows/Linux x64 avec AVX2 ou macOS Apple Silicon requis.");
            var repository = purpose == "chat" ? "ggml-org/llama.cpp" : "leejet/stable-diffusion.cpp";
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + repository + "/releases?per_page=5");
            request.Headers.UserAgent.ParseAdd("MonolithHarness/1.41.0");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await http.SendAsync(request, timeout.Token); response.EnsureSuccessStatusCode();
            var releases = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token))?.AsArray() ?? [];
            JsonObject? asset = null; string tag = "";
            foreach (var release in releases.OfType<JsonObject>())
            {
                asset = (release["assets"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault(x => Matches(x["name"]?.ToString() ?? "", purpose, backend));
                if (asset != null) { tag = release["tag_name"]?.ToString() ?? ""; break; }
            }
            if (asset == null) throw new IOException("Aucun moteur précompilé trouvé pour ce système et ce backend. Choisissez CPU ou indiquez un exécutable installé.");
            var folder = Folder(settings, purpose, backend); var staging = folder + ".install-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging); SandboxWorkspace.AssertNoLinks(staging);
            var archive = Path.Combine(staging, "download" + (asset["name"]!.ToString().EndsWith(".zip") ? ".zip" : ".tar.gz"));
            try
            {
                var uri = new Uri(asset["browser_download_url"]!.ToString());
                if (uri.Scheme != "https" || uri.Host != "github.com") throw new IOException("Source du moteur invalide.");
                var hash = asset["digest"]?.ToString() ?? "";
                if (!hash.StartsWith("sha256:")) throw new IOException("Le moteur distant ne fournit pas d’empreinte SHA-256. Utilisez un exécutable installé manuellement.");
                await LocalDownload.CopyAsync(http, uri, archive, asset["size"]!.GetValue<long>(), hash, "", progress, ct);
                await Task.Run(() => Extract(archive, staging, ct), ct); File.Delete(archive);
                var executableName = (purpose == "chat" ? "llama-server" : "sd-cli") + (OperatingSystem.IsWindows() ? ".exe" : "");
                var executable = Directory.EnumerateFiles(staging, executableName, SearchOption.AllDirectories).FirstOrDefault() ?? throw new IOException("L’exécutable attendu est absent de l’archive.");
                await File.WriteAllTextAsync(Path.Combine(staging, "installed.json"), new JsonObject { ["Repository"] = repository, ["Tag"] = tag,
                    ["Asset"] = asset["name"]!.ToString(), ["Sha256"] = hash, ["Executable"] = Path.GetRelativePath(staging, executable) }.ToJsonString(), ct);
                if (Directory.Exists(folder)) throw new IOException("Dossier moteur déjà présent mais incomplet. Choisissez un autre dossier ou un exécutable installé.");
                Directory.Move(staging, folder); return Installed(settings, purpose, backend);
            }
            finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
        }
        finally { installGate.Release(); }
    }
    static bool Matches(string name, string purpose, string backend)
    {
        var lower = name.ToLowerInvariant();
        if (!lower.EndsWith(".zip") && !lower.EndsWith(".tar.gz") || lower.StartsWith("cudart")) return false;
        if (purpose == "chat" && !lower.StartsWith("llama-") || purpose == "image" && !lower.StartsWith("sd-")) return false;
        if (OperatingSystem.IsWindows()) return lower.Contains("bin-win-" + backend + "-x64");
        if (OperatingSystem.IsMacOS()) return lower.Contains(purpose == "chat" ? "macos-arm64" : "darwin") && lower.Contains("arm64");
        if (!lower.Contains(purpose == "chat" ? "ubuntu" : "linux") || !lower.Contains(purpose == "chat" ? "x64" : "x86_64")) return false;
        if (backend == "vulkan") return lower.Contains("vulkan");
        return !new[] { "vulkan", "cuda", "rocm", "sycl", "openvino", "opencl" }.Any(lower.Contains);
    }
    static void Extract(string archive, string directory, CancellationToken ct)
    {
        long total = 0; int count = 0;
        string Target(string name, long size)
        {
            ct.ThrowIfCancellationRequested(); total += size;
            if (++count > 10000 || total > 4L * 1024 * 1024 * 1024) throw new IOException("Archive du moteur trop volumineuse.");
            var path = Path.GetFullPath(Path.Combine(directory, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, PlatformSupport.PathComparison)) throw new IOException("Chemin invalide dans l’archive.");
            return path;
        }
        if (archive.EndsWith(".zip"))
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new IOException("Liens interdits dans le moteur.");
                if (entry.Name.Length == 0) continue;
                var target = Target(entry.FullName, entry.Length); Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target, false);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        else
        {
            using var file = File.OpenRead(archive); using var gzip = new GZipStream(file, CompressionMode.Decompress); using var reader = new TarReader(gzip);
            while (reader.GetNextEntry() is { } entry)
            {
                if (entry.EntryType == TarEntryType.Directory) continue;
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new IOException("Type de fichier non autorisé dans le moteur.");
                var target = Target(entry.Name, entry.Length); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var output = File.Create(target); entry.DataStream?.CopyTo(output);
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }
}
