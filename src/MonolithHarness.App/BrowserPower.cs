using System.Reflection;
using Microsoft.UI.Xaml;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    void RefreshBrowserPower()
    {
        foreach (var item in conversationBrowsers.Values.ToArray())
        {
            if (!item.Ready || item.Closed) continue;
            var shown = item.Shown = VisualUpdatesAllowed && item.Frame.Parent == browserHost;
            // Keep every tab available while its agent is using the browser, including hidden chats.
            var working = conversationRuns.ContainsKey(item.Id) || item.IsNavigating;
            item.DesiredSuspended = !shown && !working;
            var visibility = item.DesiredSuspended ? Visibility.Collapsed : Visibility.Visible;
            if (item.View.Visibility != visibility) item.View.Visibility = visibility;
            if (item.DesiredSuspended && item.SuspensionUnavailable) continue;
            if (item.PowerOperation?.IsCompleted != false) item.PowerOperation = ApplyBrowserPowerAsync(item);
        }
    }
    async Task WakeBrowserAsync(ConversationBrowser item)
    {
        item.DesiredSuspended = false;
        item.View.Visibility = Visibility.Visible;
        if (item.PowerOperation?.IsCompleted != false) item.PowerOperation = ApplyBrowserPowerAsync(item);
        await item.PowerOperation;
        if (item.Suspended) throw new IOException(WorkflowText("Impossible de réveiller la page du navigateur.", "Could not resume the browser page."));
    }
    async Task ApplyBrowserPowerAsync(ConversationBrowser item)
    {
        try
        {
#if !WINDOWS
            if (!item.DesiredSuspended && item.Shown && OperatingSystem.IsWindows()) SetNativeBrowserVisible(item, true);
#endif
            while (!item.Closed && item.Ready && item.Suspended != item.DesiredSuspended)
            {
                var suspend = item.DesiredSuspended;
                if (suspend)
                {
                    var suspended = await SuspendBrowserCoreAsync(item);
                    if (!suspended) break; // Unsupported/busy pages are never retried in a polling loop.
                    item.Suspended = true;
                }
                else { ResumeBrowserCore(item); item.Suspended = false; }
            }
        }
        catch (OperationCanceledException) when (item.Closed) { }
        catch (Exception ex) when (ex is NotSupportedException or NotImplementedException or MissingMemberException)
        { item.SuspensionUnavailable = true; AppLog.Write(AppLogLevel.Debug, "browser.suspension_unavailable", ex); }
        catch (Exception ex) { AppLog.Write(AppLogLevel.Debug, "browser.power_transition_failed", ex); }
    }
#if !WINDOWS
    static object? NativeBrowser(ConversationBrowser item)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var adapter = item.View.CoreWebView2.GetType().GetField("_nativeWebView", flags)?.GetValue(item.View.CoreWebView2);
        return adapter?.GetType().GetField("_nativeWebView", flags)?.GetValue(adapter);
    }
#endif
    static async Task<bool> SuspendBrowserCoreAsync(ConversationBrowser item)
    {
#if WINDOWS
        return await item.View.CoreWebView2.TrySuspendAsync();
#else
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("Native page suspension is unavailable on this WebView backend.");
        return await SuspendWindowsBrowserAsync(item);
#endif
    }
    static void ResumeBrowserCore(ConversationBrowser item)
    {
#if WINDOWS
        item.View.CoreWebView2.Resume();
#else
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("Native page resume is unavailable on this WebView backend.");
        ResumeWindowsBrowser(item);
#endif
    }
#if !WINDOWS
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static async Task<bool> SuspendWindowsBrowserAsync(ConversationBrowser item)
    {
        SetNativeBrowserVisible(item, false);
        var native = NativeBrowser(item);
        if (native is WebView2.ICoreWebView2_3 core)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new WebView2.Utilities.CoreWebView2TrySuspendCompletedHandler((error, success) =>
            { if (error.Value < 0) completion.TrySetException(new System.Runtime.InteropServices.COMException("WebView suspension failed", error.Value)); else completion.TrySetResult(success.Value != 0); });
            var result = core.TrySuspend(handler);
            if (result.Value < 0) throw new System.Runtime.InteropServices.COMException("WebView suspension failed", result.Value);
            // The native callback owns the pending operation. No timeout that could leave a late suspension untracked.
            return await completion.Task.WaitAsync(item.PowerLifetime.Token);
        }
        if (native?.GetType().GetMethod("TrySuspendAsync", Type.EmptyTypes)?.Invoke(native, null) is Task<bool> task) return await task.WaitAsync(item.PowerLifetime.Token);
        throw new NotSupportedException("Native page suspension is unavailable on this WebView backend.");
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static void ResumeWindowsBrowser(ConversationBrowser item)
    {
        var native = NativeBrowser(item);
        if (native is WebView2.ICoreWebView2_3 core)
        {
            var result = core.Resume();
            if (result.Value < 0) throw new System.Runtime.InteropServices.COMException("WebView resume failed", result.Value);
        }
        else if (native?.GetType().GetMethod("Resume", Type.EmptyTypes) is { } resume) resume.Invoke(native, null);
        else throw new NotSupportedException("Native page resume is unavailable on this WebView backend.");
        if (item.Shown) SetNativeBrowserVisible(item, true);
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static void SetNativeBrowserVisible(ConversationBrowser item, bool visible)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var adapter = item.View.CoreWebView2.GetType().GetField("_nativeWebView", flags)?.GetValue(item.View.CoreWebView2);
        var controller = adapter?.GetType().GetField("_controller", flags)?.GetValue(adapter);
        if (controller is WebView2.ICoreWebView2Controller nativeController)
        {
            var result = nativeController.put_IsVisible(new(visible ? 1 : 0));
            if (result.Value < 0) throw new System.Runtime.InteropServices.COMException("WebView visibility update failed", result.Value);
        }
        else controller?.GetType().GetProperty("IsVisible")?.SetValue(controller, visible);
    }
#endif
}
