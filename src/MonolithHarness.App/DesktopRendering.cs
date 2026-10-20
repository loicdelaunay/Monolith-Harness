#if !WINDOWS
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using MonolithHarness.Core;

namespace MonolithHarness.App;

// The rendering host consumes these flags before App and its first window are created.
internal static class DesktopRendering
{
    public static FeatureSettings StartupSettings { get; private set; } = new();
    public static int TargetFrameRate { get; private set; } = 60;
    public static bool FollowsDisplay { get; private set; }

    public static void Initialize()
    {
        var config = StartupSettings = ReadStartupSettings();
        var gpu = config.HardwareAcceleration;
        Uno.UI.FeatureConfiguration.Rendering.UseVulkanOnWin32 = gpu && config.RenderingBackend == "vulkan";
        Uno.UI.FeatureConfiguration.Rendering.UseVulkanOnX11 = gpu && config.RenderingBackend == "vulkan";
        Uno.UI.FeatureConfiguration.Rendering.UseOpenGLOnWin32 = gpu ? null : false;
        Uno.UI.FeatureConfiguration.Rendering.UseOpenGLOnX11 = gpu ? null : false;
        Uno.UI.FeatureConfiguration.Rendering.UseMetalOnMacOS = gpu ? null : false;
        FollowsDisplay = config.RenderingRefreshMode == "screen" || config.RenderingRefreshMode == "auto" && !OperatingSystem.IsWindows();
        TargetFrameRate = config.RenderingRefreshMode == "manual" ? config.RenderingFrameRate : OperatingSystem.IsWindows() ? DisplayRefreshRate() : 60;
        Uno.UI.FeatureConfiguration.CompositionTarget.SetFrameRateAsScreenRefreshRate = FollowsDisplay;
        Uno.UI.FeatureConfiguration.CompositionTarget.FrameRate = TargetFrameRate;
    }

    static FeatureSettings ReadStartupSettings()
    {
        // Read only, before schema initialization. New/legacy/locked profiles keep safe defaults.
        // UI smoke instances must never take preferences from the user's portable profile.
        var fixture = Environment.GetEnvironmentVariable("MONOLITHHARNESS_UI_SMOKE");
        var path = string.IsNullOrWhiteSpace(fixture) ? HarnessDb.DatabasePath : Path.Combine(Path.GetFullPath(fixture), "database.sqlite");
        if (!File.Exists(path)) return new();
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1 }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT FeaturesJson FROM States ORDER BY Id LIMIT 1";
            return command.ExecuteScalar() is string json ? FeatureSettings.Read(json) : new();
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            AppLog.Write(AppLogLevel.Warning, "rendering.startup_preferences", ex); return new();
        }
    }

    [DllImport("user32.dll")] static extern nint GetDC(nint window);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(nint dc, int index);

    static int DisplayRefreshRate()
    {
        const int VerticalRefresh = 116;
        var dc = GetDC(0);
        try { var rate = dc == 0 ? 0 : GetDeviceCaps(dc, VerticalRefresh); return rate > 1 ? Math.Clamp(rate, 60, 240) : 60; }
        finally { if (dc != 0) ReleaseDC(0, dc); }
    }
}
#endif
