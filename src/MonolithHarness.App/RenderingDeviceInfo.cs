using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using MonolithHarness.Core;

namespace MonolithHarness.App;

/// <summary>Read-only Windows observations. An installed adapter alone never proves it renders this process.</summary>
internal static class RenderingDeviceInfo
{
    static readonly object cacheLock = new();
    static Task<string[]>? cachedAdapters;
    static long cacheUntil;
    sealed record NativeHandle(nint Value);
    static readonly ConditionalWeakTable<Window, NativeHandle> nativeWindows = new();

    public static Task<string[]> ProcessGraphicsAdaptersAsync()
    {
        lock (cacheLock)
        {
            if (cachedAdapters == null || cachedAdapters.IsCompleted && Environment.TickCount64 >= cacheUntil)
            {
                cacheUntil = Environment.TickCount64 + 10000;
                cachedAdapters = Task.Run(ReadProcessGraphicsAdapters);
            }
            return cachedAdapters;
        }
    }

    static string[] ReadProcessGraphicsAdapters()
    {
        if (!OperatingSystem.IsWindows()) return [];
        try
        {
            var used = ProcessGraphicsActivity();
            if (used.Count == 0) return [];
            var names = AdapterNames();
            return used.OrderByDescending(x => x.Value).Where(x => names.ContainsKey(x.Key))
                .Select(x => names[x.Key]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex) when (ex is ExternalException or DllNotFoundException or EntryPointNotFoundException or ArgumentException or OverflowException)
        {
            AppLog.Write(AppLogLevel.Debug, "rendering.active_gpu", ex); return [];
        }
    }

    // GPU Engine instances include the process ID and adapter LUID. Running Time is cumulative:
    // it still identifies observed 3D activity when the application is idle at the time of reading.
    // Show every matching adapter rather than guessing which one owns the main Skia surface.
    static Dictionary<ulong, long> ProcessGraphicsActivity()
    {
        var result = new Dictionary<ulong, long>();
        if (PdhOpenQuery(null, 0, out var query) != 0) return result;
        try
        {
            if (PdhAddEnglishCounter(query, @"\GPU Engine(*)\Running Time", 0, out var counter) != 0 || PdhCollectQueryData(query) != 0) return result;
            uint bytes = 0, count = 0;
            const uint MoreData = 0x800007D2;
            if (PdhGetRawCounterArray(counter, ref bytes, ref count, 0) != MoreData || bytes is 0 or > 16777216) return result;
            var buffer = Marshal.AllocHGlobal((int)bytes);
            try
            {
                if (PdhGetRawCounterArray(counter, ref bytes, ref count, buffer) != 0) return result;
                var stride = Marshal.SizeOf<CounterItem>();
                if (count > bytes / stride) return result;
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(buffer + i * stride);
                    if (item.Value.Status > 1 || item.Value.First <= 0) continue;
                    var name = Marshal.PtrToStringUni(item.Name) ?? "";
                    var match = Regex.Match(name, @"^pid_" + Environment.ProcessId + @"_luid_0x([0-9a-f]{1,8})_0x([0-9a-f]{1,8})_phys_\d+_eng_\d+_engtype_3D(?:#\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (!match.Success) continue;
                    var id = (ulong.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) << 32)
                        | uint.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    result[id] = result.GetValueOrDefault(id) + item.Value.First;
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { PdhCloseQuery(query); }
        return result;
    }

    static Dictionary<ulong, string> AdapterNames()
    {
        var result = new Dictionary<ulong, string>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
        if (CreateDXGIFactory1(in iid, out var factory) < 0 || factory == 0) return result;
        try
        {
            // IUnknown (3), IDXGIObject (4), IDXGIFactory (5), then EnumAdapters1.
            var enumerate = Marshal.GetDelegateForFunctionPointer<EnumAdapters>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 12 * IntPtr.Size));
            for (uint i = 0; i < 64; i++)
            {
                if (enumerate(factory, i, out var adapter) < 0 || adapter == 0) break;
                try
                {
                    // IUnknown (3), IDXGIObject (4), IDXGIAdapter (3), then GetDesc1.
                    var describe = Marshal.GetDelegateForFunctionPointer<GetDescription>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter), 10 * IntPtr.Size));
                    if (describe(adapter, out var description) >= 0 && !string.IsNullOrWhiteSpace(description.Description))
                        result[((ulong)(uint)description.High << 32) | description.Low] = description.Description.Trim();
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return result;
    }

    public static int? ScreenRefreshRate(Window window)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var handle = WindowHandle(window);
            if (handle == 0) return null;
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
            if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) return null;
            var mode = new DisplayMode { Size = (ushort)Marshal.SizeOf<DisplayMode>() };
            return EnumDisplaySettings(monitor.Device, -1, ref mode) && mode.Frequency > 1 ? (int)mode.Frequency : null;
        }
        catch (Exception ex) when (ex is ExternalException or DllNotFoundException or EntryPointNotFoundException)
        {
            AppLog.Write(AppLogLevel.Debug, "rendering.screen_refresh", ex); return null;
        }
    }

    static nint WindowHandle(Window window)
    {
#if WINDOWS
        return Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
#else
        // Win32Interop.GetWindowFromWindowId is not implemented by this Uno Skia version.
        // Restrict enumeration to our process and an exact, unique title; never choose another app.
        if (nativeWindows.TryGetValue(window, out var cached) && IsWindow(cached.Value)) return cached.Value;
        nativeWindows.Remove(window);
        var title = window.Title;
        var matches = new List<nint>();
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var process);
            if (process != Environment.ProcessId) return true;
            var caption = new StringBuilder(1024); GetWindowText(handle, caption, caption.Capacity);
            if (string.Equals(caption.ToString(), title, StringComparison.Ordinal)) matches.Add(handle);
            return true;
        }, 0);
        if (matches.Count != 1) return 0;
        nativeWindows.Add(window, new(matches[0]));
        return matches[0];
#endif
    }

    [StructLayout(LayoutKind.Sequential)] struct CounterValue
    { public uint Status; public System.Runtime.InteropServices.ComTypes.FILETIME Time; public long First, Second; public uint MultiCount; }
    [StructLayout(LayoutKind.Sequential)] struct CounterItem { public nint Name; public CounterValue Value; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Vendor, Device, Subsystem, Revision;
        public nuint DedicatedVideo, DedicatedSystem, SharedSystem;
        public uint Low; public int High; public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MonitorInfo
    {
        public int Size; public Rect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    // DEVMODEW: dmSize and dmDisplayFrequency; the other fields are not read or changed.
    [StructLayout(LayoutKind.Explicit, Size = 220)] struct DisplayMode
    { [FieldOffset(68)] public ushort Size; [FieldOffset(184)] public uint Frequency; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumAdapters(nint factory, uint index, out nint adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetDescription(nint adapter, out AdapterDescription description);
    [DllImport("dxgi.dll", ExactSpelling = true)] static extern int CreateDXGIFactory1(in Guid iid, out nint factory);
    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)] static extern uint PdhOpenQuery(string? source, nuint data, out nint query);
    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounter(nint query, string path, nuint data, out nint counter);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll", EntryPoint = "PdhGetRawCounterArrayW")] static extern uint PdhGetRawCounterArray(nint counter, ref uint bytes, ref uint count, nint buffer);
    [DllImport("pdh.dll")] static extern uint PdhCloseQuery(nint query);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate bool EnumerateWindow(nint window, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool EnumWindows(EnumerateWindow callback, nint parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool EnumDisplaySettings(string device, int mode, ref DisplayMode settings);
}
