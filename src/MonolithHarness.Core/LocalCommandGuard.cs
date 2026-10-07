using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MonolithHarness.Core;

public static class LocalCommandGuard
{
    public const string Version = "0.4.3";
    sealed record ModelFile(string Path, long Size, string Sha256);
    sealed record ModelManifest(string Revision, string Version, List<ModelFile> Files);
    static readonly SemaphoreSlim Gate = new(1, 1);
    static readonly HttpClient DownloadClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    static Process? worker;
    static string? workerFolder;
    static Task<string>? workerErrors;
    static readonly ModelManifest Manifest = LoadManifest();
    static string Rid => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    public static string Folder => Path.Combine(PortableStorage.Folder("models"), "command_guard", "lancet-" + Version + "-" + Rid);
    public static long ModelBytes => Manifest.Files.Sum(file => file.Size);
    public static bool Installed
    {
        get
        {
            try
            {
                SandboxWorkspace.AssertNoLinks(Folder);
                return File.Exists(Path.Combine(Folder, ".ready")) && File.ReadAllText(Path.Combine(Folder, ".ready")) == Manifest.Revision
                    && File.Exists(Path.Combine(Folder, "classify.py")) && Directory.Exists(Path.Combine(Folder, "dependencies"))
                    && File.Exists(PythonRuntime.Executable(PythonRuntime.Bundle()));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
    }
    static void InvalidateInstallation()
    {
        try { File.Delete(Inside(Folder, ".ready")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    static LocalCommandGuard() => AppDomain.CurrentDomain.ProcessExit += (_, _) => StopWorker();
    static ModelManifest LoadManifest()
    {
        using var data = typeof(LocalCommandGuard).Assembly.GetManifestResourceStream("MonolithHarness.Core.Models.command_guard.manifest.json")
            ?? throw new IOException("Command guard manifest missing.");
        return JsonSerializer.Deserialize<ModelManifest>(data) ?? throw new IOException("Invalid command guard manifest.");
    }
    static string Inside(string folder, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, PlatformSupport.PathComparison)) throw new IOException("Command guard path escapes installation.");
        SandboxWorkspace.AssertNoLinks(path); return path;
    }
    static async Task VerifyAsync(string folder, CancellationToken ct)
    {
        foreach (var file in Manifest.Files)
        {
            var path = Inside(folder, file.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size) throw new IOException("Incomplete command guard: " + file.Path);
            await using var input = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
            if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Command guard checksum mismatch: " + file.Path);
        }
    }
    public static Task InstallAsync(IProgress<CommandGuardProgress>? progress, CancellationToken ct) => PrepareAsync(null, progress, ct);
    public static Task ImportAsync(string directory, IProgress<CommandGuardProgress>? progress, CancellationToken ct) => PrepareAsync(directory, progress, ct);
    static async Task PrepareAsync(string? sourceFolder, IProgress<CommandGuardProgress>? progress, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        string? staging = null;
        try
        {
            if (sourceFolder == null && Installed)
            {
                try { await EnsureWorkerAsync(Folder, ct); return; }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
                { StopWorker(); InvalidateInstallation(); }
            }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(15));
            var token = deadline.Token;
            if (sourceFolder != null)
            {
                sourceFolder = Path.GetFullPath(sourceFolder); SandboxWorkspace.AssertNoLinks(sourceFolder);
                if (!File.Exists(Path.Combine(sourceFolder, "classify.py")) && Directory.Exists(Path.Combine(sourceFolder, "bundle")))
                    sourceFolder = Inside(sourceFolder, "bundle");
                progress?.Report(new("verify")); await VerifyAsync(sourceFolder, token);
            }
            progress?.Report(new("runtime"));
            var python = await PythonRuntime.EnsureAsync(token);
            var folder = Folder; SandboxWorkspace.AssertNoLinks(folder); Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
            staging = folder + ".install-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(staging);
            long done = 0;
            foreach (var file in Manifest.Files)
            {
                var path = Inside(staging, file.Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var url = "https://huggingface.co/fingerthief/lancet-nano/resolve/" + Manifest.Revision + "/bundle/" + file.Path;
                using var response = sourceFolder == null ? await DownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token) : null;
                response?.EnsureSuccessStatusCode();
                await using var source = sourceFolder == null ? await response!.Content.ReadAsStreamAsync(token)
                    : File.OpenRead(Inside(sourceFolder, file.Path));
                await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                var buffer = new byte[65536]; long received = 0; int count;
                while ((count = await source.ReadAsync(buffer, token)) > 0)
                {
                    received += count; if (received > file.Size) throw new IOException("Unexpected command guard file size.");
                    await target.WriteAsync(buffer.AsMemory(0, count), token);
                    progress?.Report(new(sourceFolder == null ? "download" : "import", done + received, ModelBytes));
                }
                if (received != file.Size) throw new IOException("Incomplete download: " + file.Path);
                done += received;
            }
            progress?.Report(new("verify", done, ModelBytes)); await VerifyAsync(staging, token);
            progress?.Report(new("dependencies", done, ModelBytes));
            var dependencies = Inside(staging, "dependencies"); Directory.CreateDirectory(dependencies);
            var wheels = sourceFolder == null ? null : Inside(sourceFolder, "wheels");
            var pip = new List<string> { "-I", "-m", "pip", "--isolated", "install", "--only-binary=:all:",
                "--disable-pip-version-check", "--no-warn-script-location", "--target", dependencies };
            if (wheels != null && Directory.Exists(wheels)) pip.AddRange(["--no-index", "--find-links", wheels]);
            else pip.AddRange(["--index-url", "https://pypi.org/simple"]);
            pip.AddRange(["numpy==2.2.6", "tokenizers==0.22.2", "onnxruntime==1.23.2"]);
            await RunPythonAsync(python, pip, token);
            StopWorker();
            // Preserve an incomplete previous installation instead of recursively deleting unknown contents.
            if (Directory.Exists(folder)) Directory.Move(folder, folder + ".incomplete-" + Guid.NewGuid().ToString("N"));
            Directory.Move(staging, folder); staging = null;
            progress?.Report(new("prepare", done, ModelBytes));
            await EnsureWorkerAsync(folder, token);
            await File.WriteAllTextAsync(Path.Combine(folder, ".ready"), Manifest.Revision, token);
            progress?.Report(new("ready", done, ModelBytes));
        }
        finally
        {
            try
            {
                if (staging != null && Directory.Exists(staging))
                {
                    var expected = Folder + ".install-";
                    if (Path.GetFullPath(staging).StartsWith(expected, PlatformSupport.PathComparison)) { SandboxWorkspace.AssertNoLinks(staging); Directory.Delete(staging, true); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            finally { Gate.Release(); }
        }
    }
    public static async Task<CommandGuardResult> CheckAsync(CommandApproval? request, CancellationToken ct, IProgress<CommandValidationProgress>? progress = null)
    {
        if (request == null) return CommandGuardResult.Review("missing-command");
        if (request.Shell is not ("bash" or "powershell" or "cmd")) return CommandGuardResult.Review("unsupported-shell");
        if (string.IsNullOrWhiteSpace(request.Command) || request.Command.Contains('\0') || Encoding.UTF8.GetByteCount(request.Command) > 8192) return CommandGuardResult.Review("invalid-command");
        if (!Installed) return CommandGuardResult.Review("not-installed");
        await Gate.WaitAsync(ct);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(45));
            progress?.Report(new("preparing"));
            await EnsureWorkerAsync(Folder, deadline.Token);
            var requestId = Guid.NewGuid().ToString("N");
            var payload = JsonSerializer.Serialize(new { request_id = requestId, command = request.Command, shell = request.Shell });
            progress?.Report(new("analyzing"));
            await worker!.StandardInput.WriteLineAsync(payload.AsMemory(), deadline.Token);
            await worker.StandardInput.FlushAsync(deadline.Token);
            var line = await worker.StandardOutput.ReadLineAsync(deadline.Token);
            if (line == null || line.Length > 8192) throw new IOException("No valid command guard response.");
            progress?.Report(new("checking"));
            using var response = JsonDocument.Parse(line);
            var value = response.RootElement;
            if (value.GetProperty("request_id").GetString() != requestId) throw new IOException("Mismatched command guard response.");
            var classification = value.GetProperty("classification").GetString() ?? "review";
            double? score = value.TryGetProperty("score", out var number) && number.ValueKind == JsonValueKind.Number ? number.GetDouble() : null;
            var reason = value.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString() : null;
            if (classification is not ("review" or "risky" or "not_flagged") || score is double invalid && !double.IsFinite(invalid)) return CommandGuardResult.Review("invalid-response");
            return new(classification, score, reason);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { StopWorker(); throw; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { StopWorker(); return CommandGuardResult.Review("timeout"); }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { StopWorker(); InvalidateInstallation(); return CommandGuardResult.Review("unavailable"); }
        finally { Gate.Release(); }
    }
    static ProcessStartInfo PythonStart(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        start.Environment["OPENBLAS_NUM_THREADS"] = "1"; start.Environment["OMP_NUM_THREADS"] = "2";
        return start;
    }
    static async Task RunPythonAsync(string executable, IEnumerable<string> arguments, CancellationToken ct)
    {
        using var process = Process.Start(PythonStart(executable, arguments)) ?? throw new IOException("Python setup could not start.");
        process.StandardInput.Close();
        var output = ReadBoundedAsync(process.StandardOutput); var errors = ReadBoundedAsync(process.StandardError);
        try { await process.WaitForExitAsync(ct); }
        finally { if (!process.HasExited) process.Kill(true); }
        var message = await errors; await output;
        if (process.ExitCode != 0) throw new IOException("Command guard dependency setup failed: " + message);
    }
    static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var builder = new StringBuilder(); var buffer = new char[2048]; int count;
        while ((count = await reader.ReadAsync(buffer)) > 0) { if (builder.Length < 8000) builder.Append(buffer, 0, Math.Min(count, 8000 - builder.Length)); }
        return builder.ToString();
    }
    static async Task EnsureWorkerAsync(string folder, CancellationToken ct)
    {
        if (worker != null && !worker.HasExited && workerFolder == folder) return;
        StopWorker(); await VerifyAsync(folder, ct);
        var python = PythonRuntime.Executable(PythonRuntime.Bundle());
        if (!File.Exists(python)) throw new IOException("Prepared Python runtime missing.");
        var bridge = """
import importlib.util, json, pathlib, sys
sys.stdin.reconfigure(encoding='utf-8')
sys.stdout.reconfigure(encoding='utf-8')
folder = pathlib.Path(sys.argv[1])
sys.path.insert(0, str(folder / 'dependencies'))
spec = importlib.util.spec_from_file_location('monolith_lancet', folder / 'classify.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
predictor = module.LancetNano(folder / 'model', threads=2)
print(json.dumps({'ready': True}), flush=True)
for line in sys.stdin:
    row = {}
    try:
        row = json.loads(line)
        result = predictor.score(row['command'], row['shell'])
    except Exception:
        result = {'classification': 'review', 'score': None, 'reason': 'inference-error'}
    result['request_id'] = row.get('request_id')
    print(json.dumps(result, ensure_ascii=False, allow_nan=False), flush=True)
""";
        worker = Process.Start(PythonStart(python, ["-I", "-B", "-u", "-c", bridge, folder])) ?? throw new IOException("Command guard could not start.");
        workerFolder = folder; workerErrors = ReadBoundedAsync(worker.StandardError);
        try
        {
            var ready = await worker.StandardOutput.ReadLineAsync(ct);
            if (ready == null) throw new IOException("Command guard preparation failed.");
            using var status = JsonDocument.Parse(ready);
            if (!status.RootElement.GetProperty("ready").GetBoolean()) throw new IOException("Command guard preparation failed.");
        }
        catch { StopWorker(); throw; }
    }
    static void StopWorker()
    {
        var previous = worker; worker = null; workerFolder = null;
        if (previous == null) return;
        try { if (!previous.HasExited) previous.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        previous.Dispose(); workerErrors = null;
    }
}
