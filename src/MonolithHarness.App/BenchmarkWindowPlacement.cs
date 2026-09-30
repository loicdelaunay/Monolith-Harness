using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;

namespace MonolithHarness.App;

internal static class BenchmarkWindowPlacement
{
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    delegate bool Enumerate(nint window, nint parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(Enumerate callback, nint parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint window, StringBuilder text, int count);
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    internal static void Fit(Window window, bool preview)
    {
        if (OperatingSystem.IsWindows())
        {
            nint target = 0;
            EnumWindows((handle, _) =>
            {
                GetWindowThreadProcessId(handle, out var process);
                if (process != Environment.ProcessId) return true;
                var title = new StringBuilder(512); GetWindowText(handle, title, title.Capacity);
                if (title.ToString() != window.Title) return true;
                target = handle; return false;
            }, 0);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (target != 0 && GetMonitorInfo(MonitorFromWindow(target, 2), ref info))
            {
                var work = info.Work; var width = Math.Min(preview ? 1800 : 1160, work.Right - work.Left - 24); var height = Math.Min(preview ? 1020 : 940, work.Bottom - work.Top - 24);
                SetWindowPos(target, 0, work.Left + (work.Right - work.Left - width) / 2, work.Top + (work.Bottom - work.Top - height) / 2, width, height, 0x0014);
                return;
            }
        }
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = preview ? 1440 : 1160, Height = 940 });
    }
}
