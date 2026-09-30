using System.Diagnostics;
using System.Runtime.InteropServices;
using MonolithHarness.Core;

namespace MonolithHarness.App;

static class SystemNotifications
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotificationData
    {
        public uint Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool Shell_NotifyIconW(uint operation, ref NotificationData data);
    [DllImport("user32.dll")] static extern nint LoadIconW(nint instance, nint icon);
    static int sequence;
    static readonly object notificationLock = new();
    static bool iconVisible;
    static string Clip(string text, int length) => text.Length > length ? text[..length] : text;
    public static async Task ShowAsync(string application, string title, string message)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var notification = Interlocked.Increment(ref sequence);
                var data = new NotificationData { Size = (uint)Marshal.SizeOf<NotificationData>(), Window = Process.GetCurrentProcess().MainWindowHandle,
                    Id = 1, Flags = 2 | 4 | 16, Icon = LoadIconW(0, (nint)32516),
                    Tip = Clip(application, 127), Title = Clip(title, 63), Info = Clip(message, 255), InfoFlags = 1 | 16 };
                lock (notificationLock)
                {
                    if (data.Window == 0 || !Shell_NotifyIconW(iconVisible ? 1u : 0u, ref data)) throw new InvalidOperationException("System notification unavailable.");
                    iconVisible = true;
                }
                try { await Task.Delay(TimeSpan.FromSeconds(25)); }
                finally
                {
                    lock (notificationLock)
                        if (notification == sequence) { Shell_NotifyIconW(2, ref data); iconVisible = false; }
                }
                return;
            }
            var start = new ProcessStartInfo { FileName = OperatingSystem.IsMacOS() ? "/usr/bin/osascript" : "notify-send", UseShellExecute = false, CreateNoWindow = true };
            if (OperatingSystem.IsMacOS())
            {
                static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ") + "\"";
                start.ArgumentList.Add("-e"); start.ArgumentList.Add("display notification " + Quote(message) + " with title " + Quote(application) + " subtitle " + Quote(title));
            }
            else { start.ArgumentList.Add("--app-name=" + application); start.ArgumentList.Add("--"); start.ArgumentList.Add(title); start.ArgumentList.Add(message); }
            using var process = Process.Start(start) ?? throw new InvalidOperationException("System notification unavailable.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); throw; }
            if (process.ExitCode != 0) throw new InvalidOperationException("System notification rejected.");
        }
        catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "notification.system_failed", ex); }
    }
}
