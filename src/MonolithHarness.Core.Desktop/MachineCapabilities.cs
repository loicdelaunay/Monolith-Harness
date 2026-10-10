using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public sealed record MachineCapabilities(string System, string Architecture, int CpuThreads, long RamBytes, long AvailableRamBytes,
    string Gpu, long VramBytes, bool VramEstimated)
{
    public bool RuntimeSupported => (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) && Architecture == "X64" && global::System.Runtime.Intrinsics.X86.Avx2.IsSupported
        || OperatingSystem.IsMacOS() && Architecture == "Arm64";
    public static string Size(long bytes) => bytes > 0 ? $"{bytes / (1024d * 1024 * 1024):0.0} Gio" : "inconnu";
    public string Summary => $"{System} · {Architecture} · {CpuThreads} threads" + (Architecture == "X64" ? $" · AVX2 : {(global::System.Runtime.Intrinsics.X86.Avx2.IsSupported ? "oui" : "non")}" : "") + $"\nRAM {Size(RamBytes)} · libre {Size(AvailableRamBytes)}\nGPU : {Gpu} · VRAM {Size(VramBytes)}{(VramEstimated ? " (estimation système)" : "")}";
    public static async Task<MachineCapabilities> ReadAsync(CancellationToken ct)
    {
        long ram = 0, free = 0, vram = 0; string gpu = "non détecté"; bool estimated = true;
        if (OperatingSystem.IsWindows())
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref status)) { ram = (long)status.TotalPhysical; free = (long)status.AvailablePhysical; }
            var value = await OutputAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command",
                "Get-CimInstance Win32_VideoController | Select-Object Name,AdapterRAM | ConvertTo-Json -Compress"], ct);
            try
            {
                var node = JsonNode.Parse(value);
                var items = node is JsonArray array ? array.OfType<JsonObject>().ToList() : node is JsonObject single ? [single] : new List<JsonObject>();
                gpu = string.Join(" / ", items.Select(x => x["Name"]?.ToString()));
                vram = items.Select(x => long.TryParse(x["AdapterRAM"]?.ToString(), out var bytes) ? bytes : 0).DefaultIfEmpty().Max();
            }
            catch (System.Text.Json.JsonException) { }
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                var memory = await File.ReadAllLinesAsync("/proc/meminfo", ct);
                long Read(string name) => memory.Where(x => x.StartsWith(name + ":")).Select(x => long.TryParse(x.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], out var kb) ? kb * 1024 : 0).FirstOrDefault();
                ram = Read("MemTotal"); free = Read("MemAvailable");
            }
            catch (IOException) { }
        }
        else if (OperatingSystem.IsMacOS())
        {
            long.TryParse((await OutputAsync("sysctl", ["-n", "hw.memsize"], ct)).Trim(), out ram);
            gpu = "Apple · mémoire unifiée";
        }
        var nvidia = await OutputAsync("nvidia-smi", ["--query-gpu=name,memory.total", "--format=csv,noheader,nounits"], ct);
        foreach (var line in nvidia.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(',');
            if (parts.Length == 2 && long.TryParse(parts[1].Trim(), out var mib) && mib * 1024 * 1024 > vram)
            { gpu = parts[0].Trim(); vram = mib * 1024 * 1024; estimated = false; }
        }
        return new(OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "macOS", RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount, ram, free, gpu, vram, estimated);
    }
    static async Task<string> OutputAsync(string executable, string[] args, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var process = new Process { StartInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        try
        {
            process.Start(); var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token); await errors; return await output;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return ""; }
        catch (System.ComponentModel.Win32Exception) { return ""; }
        finally { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
    }
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
