using MonolithHarness.Core;
namespace MonolithHarness.Core.Mobile;
public static class MobileServices
{
    public static PlatformCapabilities Capabilities { get; } = new(false,false,false,false);
    public static IChatEngineRuntime ChatRuntime { get; } = new ApiOnlyRuntime();
    public static string DataDirectory => Android.App.Application.Context.FilesDir!.AbsolutePath;
}
