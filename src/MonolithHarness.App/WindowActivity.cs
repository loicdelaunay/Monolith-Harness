using Microsoft.UI.Xaml;

namespace MonolithHarness.App;

internal static class WindowActivity
{
    public static bool IsActive(WindowActivatedEventArgs args) =>
#if WINDOWS
        args.WindowActivationState != WindowActivationState.Deactivated;
#else
        args.WindowActivationState != Windows.UI.Core.CoreWindowActivationState.Deactivated;
#endif
}
