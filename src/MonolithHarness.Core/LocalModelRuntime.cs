using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace MonolithHarness.Core;

/// <summary>Owns only the inference processes started by this application. Bound to loopback, with per-process credentials.</summary>
public static class LocalModelRuntime
{
    internal sealed class Server(Process process, string url, string key)
    {
        public Process Process { get; } = process;
        public string Url { get; } = url;
        public string Key { get; } = key;
        public int Users;
    }
    public sealed class ChatLease : IDisposable
    {
        readonly Server server;
        internal ChatLease(Server value) => server = value;
        public string Url => server.Url;
        public string Key => server.Key;
        int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) Interlocked.Decrement(ref server.Users); }
    }
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Server> servers = new(PlatformSupport.PathComparer);
    static readonly SemaphoreSlim gate = new(1, 1);
    static readonly SemaphoreSlim imageGate = new(1, 1);
    static readonly HttpClient health = new() { Timeout = TimeSpan.FromSeconds(3) };
    static LocalModelRuntime()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { foreach (var server in servers.Values.ToArray()) Stop(server.Process); };
    }
    static string Identity(LocalModel model, LocalProviderSettings config, int context) => model.FullPath + "|" + config.Backend + "|" + context + "|" + config.ChatExecutable + "|" + config.ModelDirectory;
    public static async Task<ChatLease> ChatAsync(Provider provider, CancellationToken ct)
    {
        var config = LocalProviderSettings.Read(provider.LocalModelsJson); var model = config.Find(provider.Model, "chat");
        var identity = Identity(model, config, provider.ContextLimit);
        await gate.WaitAsync(ct);
        try
        {
            if (servers.TryGetValue(identity, out var existing) && !existing.Process.HasExited)
            { Interlocked.Increment(ref existing.Users); return new(existing); }
            foreach (var entry in servers.Where(x => x.Key.StartsWith(model.FullPath + "|", PlatformSupport.PathComparison) && Volatile.Read(ref x.Value.Users) == 0).ToList())
            { Stop(entry.Value.Process); servers.TryRemove(entry.Key, out _); }
            var machine = await MachineCapabilities.ReadAsync(ct);
            var estimate = LocalModelCompatibility.Estimate(model.FullPath, model.Purpose, model.Family, new FileInfo(model.FullPath).Length, machine, provider.ContextLimit, backend: config.Backend);
            if (!estimate.CanLoad) throw new InvalidOperationException(estimate.Reason);
            await LocalModelImport.InspectAsync(model.FullPath, "chat", ct);
            var backend = LocalRuntimeInstaller.Backend(config, machine);
            var executable = LocalRuntimeInstaller.Installed(config, "chat", backend);
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
            var args = new[] { "--model", model.FullPath, "--alias", model.Id, "--host", "127.0.0.1", "--port", port.ToString(), "--ctx-size", Math.Clamp(provider.ContextLimit, 1024, 262144).ToString(),
                "--parallel", "1", "--threads", Math.Max(1, machine.CpuThreads - 2).ToString(), "--n-gpu-layers", backend == "cpu" ? "0" : "99", "--jinja", "--api-key", key };
            var errors = new StringBuilder(); var process = Start(executable, args, errors);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5));
                var url = "http://127.0.0.1:" + port;
                while (true)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    if (process.HasExited) throw new IOException("Chargement du modèle local interrompu : " + Tail(errors));
                    try { using var response = await health.GetAsync(url + "/health", timeout.Token); if (response.IsSuccessStatusCode) break; }
                    catch (HttpRequestException) { }
                    catch (OperationCanceledException) when (!timeout.IsCancellationRequested) { }
                    await Task.Delay(300, timeout.Token);
                }
                var server = new Server(process, url + "/v1", key) { Users = 1 }; servers[identity] = server; return new(server);
            }
            catch { Stop(process); throw; }
        }
        finally { gate.Release(); }
    }
    public static async Task UnloadAsync(LocalProviderSettings settings, CancellationToken ct, bool onlyIdle = false)
    {
        await gate.WaitAsync(ct);
        try
        {
            var matching = servers.Where(x => settings.Models.Any(m => x.Key.StartsWith(m.FullPath + "|", PlatformSupport.PathComparison))).ToList();
            if (!onlyIdle && matching.Any(x => Volatile.Read(ref x.Value.Users) > 0)) throw new InvalidOperationException("Ce modèle répond encore à une conversation. Attendez la fin avant de le décharger.");
            foreach (var entry in matching.Where(x => Volatile.Read(ref x.Value.Users) == 0)) { Stop(entry.Value.Process); servers.TryRemove(entry.Key, out _); }
        }
        finally { gate.Release(); }
    }
    public static async Task<byte[]> ImageAsync(Provider provider, string modelId, string prompt, string negative, int width, int height, int steps, long seed, CancellationToken ct)
    {
        await imageGate.WaitAsync(ct);
        var temporary = Path.Combine(PortableStorage.Temporary, "image-generation", Guid.NewGuid().ToString("N"));
        try
        {
            var config = LocalProviderSettings.Read(provider.LocalModelsJson); var model = config.Find(modelId, "image");
            var machine = await MachineCapabilities.ReadAsync(ct);
            var estimate = LocalModelCompatibility.Estimate(model.FullPath, "image", model.Family, new FileInfo(model.FullPath).Length, machine, width: width, height: height, backend: config.Backend);
            if (!estimate.CanLoad) throw new InvalidOperationException(estimate.Reason);
            await LocalModelImport.InspectAsync(model.FullPath, "image", ct);
            var executable = LocalRuntimeInstaller.Installed(config, "image", LocalRuntimeInstaller.Backend(config, machine));
            Directory.CreateDirectory(temporary); var output = Path.Combine(temporary, "image.png"); var errors = new StringBuilder();
            using var process = Start(executable, ["-m", model.FullPath, "-p", prompt, "-n", negative, "-o", output, "-W", width.ToString(), "-H", height.ToString(), "--steps", steps.ToString(), "--seed", seed.ToString()], errors);
            try { await process.WaitForExitAsync(ct); }
            catch
            {
                // Kill is asynchronous on Windows; let our process release its output before cleanup.
                try
                {
                    if (!process.HasExited) process.Kill(true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                catch (TimeoutException) { }
                throw;
            }
            if (process.ExitCode != 0 || !File.Exists(output)) throw new IOException("Génération locale interrompue : " + Tail(errors));
            if (new FileInfo(output).Length > 64 * 1024 * 1024) throw new IOException("Image générée trop volumineuse.");
            return await File.ReadAllBytesAsync(output, ct);
        }
        finally
        {
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
            finally { imageGate.Release(); }
        }
    }
    static Process Start(string executable, IEnumerable<string> args, StringBuilder errors)
    {
        var process = new Process { StartInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)!, RedirectStandardError = true, RedirectStandardOutput = true } };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        void Capture(object sender, DataReceivedEventArgs e) { if (e.Data == null) return; lock (errors) { errors.AppendLine(e.Data); if (errors.Length > 8000) errors.Remove(0, errors.Length - 4000); } }
        process.ErrorDataReceived += Capture; process.OutputDataReceived += Capture;
        process.Start(); process.BeginErrorReadLine(); process.BeginOutputReadLine(); return process;
    }
    static string Tail(StringBuilder errors) { lock (errors) return errors.ToString(); }
    static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        process.Dispose();
    }
}
