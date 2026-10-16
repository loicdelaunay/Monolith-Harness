using System.Runtime.InteropServices;
using Uno.UI.Hosting;

namespace MonolithHarness.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--service"))
        {
            MonolithHarness.Core.Hosting.ServiceHost.RunAsync(args).GetAwaiter().GetResult();
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            // By default Uno presents each frame with vsync (SwapBuffers interval 1). OpenGL drivers
            // such as NVIDIA's busy-wait inside SwapBuffers, so every requested frame cost a full CPU
            // core on the render thread. A timer at the display refresh rate paces frames instead.
            Uno.UI.FeatureConfiguration.CompositionTarget.SetFrameRateAsScreenRefreshRate = false;
            Uno.UI.FeatureConfiguration.CompositionTarget.FrameRate = DisplayRefreshRate();
        }
        UnoPlatformHostBuilder.Create().App(() => new App()).UseWin32().UseMacOS().UseX11().Build().Run();
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
