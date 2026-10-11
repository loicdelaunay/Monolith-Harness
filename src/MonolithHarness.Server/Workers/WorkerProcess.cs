using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Server.Workers;

/// <summary>
/// One worker process speaking the engine's JSON-lines protocol: requests <c>{id, method, parameters}</c> in, then
/// <c>ready</c>, <c>{id, result|error}</c>, <c>{event: ...}</c> and <c>{hostRequest, method, parameters}</c> out.
/// </summary>
public sealed class WorkerProcess : IDisposable
{
    static readonly string[] InheritedVariables =
    [
        "PATH", "PATHEXT", "SystemRoot", "SYSTEMROOT", "windir", "ComSpec", "OS", "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS",
        "LANG", "LC_ALL", "DOTNET_ROOT", "DOCKER_HOST", "CONTAINER_HOST"
    ];

    readonly Process process;
    readonly SemaphoreSlim writeLock = new(1, 1);
    readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> calls = new();
    readonly StringBuilder errorTail = new();
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Func<string, JsonObject, Task<JsonNode?>> onHostRequest;
    readonly Action<JsonObject> onEvent;
    Task reader = Task.CompletedTask;
    int sequence;

    WorkerProcess(Process process, Func<string, JsonObject, Task<JsonNode?>> onHostRequest, Action<JsonObject> onEvent)
    {
        this.process = process;
        this.onHostRequest = onHostRequest;
        this.onEvent = onEvent;
    }

    public bool HasExited => process.HasExited;
    public int? ExitCode => process.HasExited ? process.ExitCode : null;
    /// <summary>Completes when the worker's output is closed, i.e. when it has exited.</summary>
    public Task Completion => reader;
    public string ErrorTail { get { lock (errorTail) return errorTail.ToString(); } }

    public static WorkerProcess Start(ServerPaths paths, WorkerLauncher launcher, string jobFile, string workingDirectory, string temporaryDirectory, string homeDirectory,
        Func<string, JsonObject, Task<JsonNode?>> onHostRequest, Action<JsonObject> onEvent)
    {
        var start = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.FileName = launcher.FileName;
        foreach (var argument in launcher.Prefix) start.ArgumentList.Add(argument);
        start.ArgumentList.Add("worker");
        start.ArgumentList.Add("--job");
        start.ArgumentList.Add(jobFile);

        start.Environment.Clear();
        foreach (var (name, value) in EnvironmentFor(paths, temporaryDirectory, homeDirectory)) start.Environment[name] = value;

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var worker = new WorkerProcess(process, onHostRequest, onEvent);
        if (!process.Start()) throw new InvalidOperationException("The worker could not be started.");
        worker.reader = Task.Run(worker.ReadOutputAsync);
        _ = Task.Run(worker.ReadErrorsAsync);
        return worker;
    }

    /// <summary>
    /// The worker gets a minimal environment: server variables (certificate password, tokens, ...) never leak into it,
    /// and temporary and home folders point inside the project.
    /// </summary>
    public static Dictionary<string, string> EnvironmentFor(ServerPaths paths, string temporaryDirectory, string homeDirectory)
    {
        var environment = new Dictionary<string, string>();
        foreach (var name in InheritedVariables)
            if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] = value;
        environment["TEMP"] = environment["TMP"] = environment["TMPDIR"] = temporaryDirectory;
        environment["HOME"] = environment["USERPROFILE"] = homeDirectory;
        environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(paths.Workspace, "runtime");
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        environment["DOTNET_NOLOGO"] = "1";
        return environment;
    }

    public async Task WaitReadyAsync(TimeSpan timeout, CancellationToken ct)
    {
        var finished = await Task.WhenAny(ready.Task, reader, Task.Delay(timeout, ct));
        if (finished != ready.Task) throw new IOException("The worker did not become ready. " + ErrorTail.Trim());
    }

    public async Task<JsonNode?> CallAsync(string method, object parameters, CancellationToken ct)
    {
        var id = "c" + Interlocked.Increment(ref sequence);
        var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        calls[id] = completion;
        try
        {
            await WriteAsync(new { id, method, parameters });
            // A worker that dies completes the reader: fail the call instead of waiting forever.
            var done = await Task.WhenAny(completion.Task, reader);
            if (done != completion.Task) throw new IOException("The worker stopped. " + ErrorTail.Trim());
            return await completion.Task.WaitAsync(ct);
        }
        finally { calls.TryRemove(id, out _); }
    }

    public long MemoryBytes()
    {
        try { process.Refresh(); return process.HasExited ? 0 : process.WorkingSet64; } catch (InvalidOperationException) { return 0; }
    }

    public TimeSpan CpuTime()
    {
        try { return process.HasExited ? TimeSpan.Zero : process.TotalProcessorTime; } catch (InvalidOperationException) { return TimeSpan.Zero; }
    }

    public void CloseInput() { try { process.StandardInput.Close(); } catch (IOException) { } }

    public void Kill()
    {
        try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
    }

    public async Task WaitForExitAsync(TimeSpan timeout)
    {
        try { await process.WaitForExitAsync().WaitAsync(timeout); }
        catch (TimeoutException) { Kill(); }
    }

    async Task WriteAsync(object value)
    {
        var line = JsonSerializer.Serialize(value, WorkerMode.Json);
        await writeLock.WaitAsync();
        try { await process.StandardInput.WriteLineAsync(line); await process.StandardInput.FlushAsync(); }
        finally { writeLock.Release(); }
    }

    async Task ReadOutputAsync()
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                JsonObject? message;
                try { message = JsonNode.Parse(line) as JsonObject; }
                catch (JsonException) { continue; } // native libraries may print plain text
                if (message == null) continue;
                if (message["ready"]?.GetValue<bool>() == true) ready.TrySetResult();
                else if (message["hostRequest"] is JsonValue requestId) _ = Task.Run(() => AnswerHostRequest(requestId.GetValue<string>(), message));
                else if (message["id"] is JsonValue id && calls.TryGetValue(id.GetValue<string>(), out var call))
                {
                    if (message["error"] is JsonValue error) call.TrySetException(new IOException(error.GetValue<string>()));
                    else call.TrySetResult(message["result"]?.DeepClone());
                }
                else if (message["event"] != null) onEvent(message);
            }
        }
        catch (IOException) { /* the pipe closed with the process */ }
        finally
        {
            ready.TrySetException(new IOException("The worker stopped. " + ErrorTail.Trim()));
            foreach (var call in calls.Values) call.TrySetException(new IOException("The worker stopped. " + ErrorTail.Trim()));
        }
    }

    async Task AnswerHostRequest(string id, JsonObject message)
    {
        try
        {
            var result = await onHostRequest(message["method"]?.GetValue<string>() ?? "", message["parameters"] as JsonObject ?? []);
            await WriteAsync(new { hostResponse = id, result });
        }
        catch (Exception ex)
        {
            try { await WriteAsync(new { hostResponse = id, error = ex.Message }); } catch (Exception) { /* worker already gone */ }
        }
    }

    async Task ReadErrorsAsync()
    {
        try
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
                lock (errorTail)
                {
                    errorTail.AppendLine(line);
                    if (errorTail.Length > 4000) errorTail.Remove(0, errorTail.Length - 4000);
                }
        }
        catch (IOException) { }
    }

    public void Dispose()
    {
        Kill();
        process.Dispose();
        writeLock.Dispose();
    }
}
