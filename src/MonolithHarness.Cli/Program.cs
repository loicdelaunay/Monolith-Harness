using System.Text;
using MonolithHarness.Cli;

Console.OutputEncoding = new UTF8Encoding(false);
try
{
    var options = CliOptions.Parse(args);
    if (options.Help) { Console.WriteLine(CliOptions.HelpText); return 0; }
    if (options.Version) { Console.WriteLine(MonolithHarness.Core.BrandingAssets.DefaultName + " CLI " + CliOptions.CurrentVersion); return 0; }
    if (options.RenderDemo)
    {
        Console.Write(TerminalUi.DemoFrame(options.Theme));
        return 0;
    }
    if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        throw new PlatformNotSupportedException("This build supports Windows, macOS and Linux.");
    if (options.Run) return await Headless.RunAsync(options);
    if (Console.IsInputRedirected || Console.IsOutputRedirected)
        throw new ArgumentException("Interactive mode needs a terminal. Use: MonolithHarness run \"prompt\" [--json]");
    using var ui = new TerminalUi(options);
    return await ui.RunAsync();
}
catch (OperationCanceledException) { return 130; }
catch (Exception ex) { MonolithHarness.Core.AppLog.Write(MonolithHarness.Core.AppLogLevel.Error, "cli.failed", ex); Console.Error.WriteLine(TerminalText.Clean(ex.Message)); return 1; }
