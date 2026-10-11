namespace MonolithHarness.Server.Components;

/// <summary>Small formatting helpers shared by pages (kept out of Razor code blocks, where "&lt;" confuses the parser).</summary>
public static class Display
{
    public static string Size(long bytes) => bytes < 1024 ? $"{bytes} o" : bytes < 1024 * 1024 ? $"{bytes / 1024} Ko" : $"{bytes / 1024 / 1024} Mo";
}
