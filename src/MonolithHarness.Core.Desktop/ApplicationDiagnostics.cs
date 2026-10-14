using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MonolithHarness.Core;

public sealed record ProcessDiagnostics(double? CpuPercent, long WorkingSet, long PrivateMemory, long ManagedMemory,
    TimeSpan Uptime, int Threads, int? Handles, int ProcessId, string Runtime, string OperatingSystem, string Architecture,
    int LogicalProcessors, int[] Collections, int PendingLogs, long DroppedLogs, bool LogsEnabled);

public sealed class ApplicationDiagnostics : IDisposable
{
    readonly Process process = Process.GetCurrentProcess();
    TimeSpan? lastCpu;
    long lastTimestamp;
    public ProcessDiagnostics Sample()
    {
        process.Refresh(); var now = Stopwatch.GetTimestamp(); var cpu = process.TotalProcessorTime;
        double? percent = lastCpu is { } previous && now > lastTimestamp
            ? Math.Clamp((cpu - previous).TotalSeconds / Stopwatch.GetElapsedTime(lastTimestamp, now).TotalSeconds / Environment.ProcessorCount * 100, 0, 100) : null;
        lastCpu = cpu; lastTimestamp = now;
        int? handles = null;
        try { handles = process.HandleCount; } catch (Exception ex) when (ex is PlatformNotSupportedException or System.ComponentModel.Win32Exception) { }
        return new(percent, process.WorkingSet64, process.PrivateMemorySize64, GC.GetTotalMemory(false),
            DateTime.UtcNow - process.StartTime.ToUniversalTime(), process.Threads.Count, handles, process.Id,
            RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
            [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)], AppLog.PendingCount, AppLog.DroppedCount, AppLog.Enabled);
    }
    public void Dispose() => process.Dispose();
    public static async Task<string[]> ReadRecentLogsAsync(CancellationToken ct)
    {
        return await Task.Run(async () =>
        {
            var lines = new List<string>();
            if (!Directory.Exists(AppLog.DirectoryPath)) return [];
            foreach (var file in Directory.EnumerateFiles(AppLog.DirectoryPath, "omh-*.jsonl").OrderByDescending(File.GetLastWriteTimeUtc).Take(4))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, true);
                    var start = Math.Max(0, stream.Length - 256 * 1024); stream.Seek(start, SeekOrigin.Begin);
                    using var reader = new StreamReader(stream);
                    if (start > 0) await reader.ReadLineAsync(ct);
                    var fileLines = new List<string>();
                    while (await reader.ReadLineAsync(ct) is { } line)
                    {
                        if (line.Length <= 32768 && line.Length > 0) fileLines.Add(line);
                    }
                    lines.AddRange(fileLines.AsEnumerable().Reverse());
                    if (lines.Count >= 1000) break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return lines.Take(1000).ToArray();
        }, ct).ConfigureAwait(false);
    }
    public static async Task<string[]> ReadGenerationErrorsAsync(string database, CancellationToken ct)
    {
        await using var store = new HarnessDb(database);
        var messages = await store.Messages.AsNoTracking().Where(x => x.Role == "assistant" && (x.State == "error" || x.State == "interrupted"))
            .OrderByDescending(x => x.Id).Take(40).Select(x => new { x.Id, x.ChatId, x.CompletedUtc, x.WireJson }).ToListAsync(ct);
        return messages.Select(m => (m, Ending: GenerationTermination.Read(new Message { WireJson = m.WireJson })))
            .Where(x => x.Ending != null).Select(x => JsonSerializer.Serialize(new { messageId = x.m.Id, chatId = x.m.ChatId, time = x.m.CompletedUtc,
                kind = x.Ending!.Kind, x.Ending.HttpStatus, x.Ending.ErrorType, detail = ToolAuthorization.Clean(x.Ending.Detail),
                x.Ending.ProviderReason, x.Ending.StreamCompleted, x.Ending.RetryAttempts, x.Ending.ResumeAttempts })).ToArray();
    }
}
