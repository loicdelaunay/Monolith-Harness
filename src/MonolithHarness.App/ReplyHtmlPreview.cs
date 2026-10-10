using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

internal sealed class ReplyHtmlPreview : UserControl
{
    static readonly List<WeakReference<ReplyHtmlPreview>> previews = [];
    readonly ReplyWebDocument artifact;
    readonly Func<FrameworkElement> native;
    readonly ReplyRenderOptions options;
    readonly bool dynamicHeight;
    readonly Grid host = new();
    readonly DispatcherTimer unloadDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly DispatcherTimer resizeDelay = new() { Interval = TimeSpan.FromMilliseconds(150) };
    Microsoft.UI.Xaml.Controls.WebView2? view;
    CancellationTokenSource? lifetime;
    ReplyPreviewPalette? paintedPalette;
    double pendingHeight;
    bool beginQueued, recoverQueued, initializing, closed;

    static ReplyPreviewPalette CurrentPalette()
    {
        var theme = FluentDesign.CurrentTheme;
        return new(theme.Dark, theme.Background, theme.Surface, theme.Text, theme.Muted, theme.Accent);
    }

    internal ReplyHtmlPreview(ReplyWebDocument artifact, Func<FrameworkElement> native, bool dynamicHeight = false)
    {
        this.artifact = artifact; this.native = native; this.dynamicHeight = dynamicHeight;
        options = MarkdownRenderer.ReplyRendering;
        HorizontalAlignment = HorizontalAlignment.Stretch; Content = host;
        if (previews.Count % 64 == 0) previews.RemoveAll(item => !item.TryGetTarget(out _));
        previews.Add(new(this));
        Loaded += (_, _) => { unloadDelay.Stop(); QueueBegin(); };
        SizeChanged += (_, _) => QueueBegin();
        // Native initialization pumps the window loop. Defer disposal across transient reattachments.
        Unloaded += (_, _) => { unloadDelay.Stop(); if (!closed) unloadDelay.Start(); };
        unloadDelay.Tick += (_, _) =>
        {
            unloadDelay.Stop();
            if (IsLoaded || closed) return;
            if (initializing) unloadDelay.Start(); else Release();
        };
        resizeDelay.Tick += (_, _) =>
        {
            resizeDelay.Stop();
            if (!closed && IsLoaded && lifetime != null &&
                (!double.IsFinite(Height) || Math.Abs(Height - pendingHeight) > 2)) Height = pendingHeight;
        };
    }

    void QueueBegin()
    {
        if (beginQueued || closed) return;
        beginQueued = true;
        DispatcherQueue.TryEnqueue(() => { beginQueued = false; Begin(); });
    }

    internal static void RefreshThemes()
    {
        var palette = CurrentPalette();
        previews.RemoveAll(item => !item.TryGetTarget(out _));
        foreach (var reference in previews.ToArray())
            if (reference.TryGetTarget(out var preview) && preview.options.FollowApplicationTheme &&
                preview.paintedPalette != null && preview.paintedPalette != palette) preview.Recover();
    }

    internal void Recover()
    {
        if (closed || recoverQueued || !IsLoaded) return;
        recoverQueued = true;
        if (initializing) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            recoverQueued = false;
            if (closed || !IsLoaded) return;
            Release(); QueueBegin();
        });
    }

    async void Begin()
    {
        if (closed || initializing || lifetime != null || !IsLoaded || ActualWidth <= 0 || ActualHeight <= 0) return;
        initializing = true;
        var operation = new CancellationTokenSource(); lifetime = operation;
        var token = operation.Token;
        var palette = CurrentPalette(); paintedPalette = palette;
        var rendering = options.FollowApplicationTheme ? options with { Palette = palette } : options;
        var channel = dynamicHeight ? Guid.NewGuid().ToString("N") : null;
        try
        {
            var page = ReplyWebDocument.SandboxedShell(true, palette.Dark, channel, palette.Background);
            var script = await Task.Run(() => artifact.PreviewLoadScript(true, palette.Dark, rendering, channel), token);
            if (!ReferenceEquals(lifetime, operation)) return;
            var current = new Microsoft.UI.Xaml.Controls.WebView2 { Background = FluentDesign.Card }; view = current;
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            current.Loaded += (_, _) => ready.TrySetResult();
            host.Children.Clear(); host.Children.Add(current);
            if (current.IsLoaded) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            await current.EnsureCoreWebView2Async().AsTask().WaitAsync(TimeSpan.FromSeconds(15), token);
            if (!ReferenceEquals(lifetime, operation)) return;
#if WINDOWS
            current.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
            current.CoreWebView2.PermissionRequested += (_, e) => e.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
#endif
            if (channel != null) current.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                if (!ReferenceEquals(lifetime, operation)) return;
                try
                {
                    var json = e.WebMessageAsJson;
                    if (json.Length > 1024) return;
                    using var message = JsonDocument.Parse(json);
                    var data = message.RootElement;
                    if (data.ValueKind != JsonValueKind.Object ||
                        !data.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "monolith-size" ||
                        !data.TryGetProperty("channel", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != channel ||
                        !data.TryGetProperty("height", out var value) || value.ValueKind != JsonValueKind.Number ||
                        !value.TryGetDouble(out var height) || !double.IsFinite(height)) return;
                    pendingHeight = Math.Clamp(Math.Ceiling(height), 80, 2000);
                    if (!resizeDelay.IsEnabled) resizeDelay.Start();
                }
                catch (JsonException) { }
            };
            var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            current.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess) navigated.TrySetResult();
                else navigated.TrySetException(new IOException("Chargement de l’aperçu impossible : " + e.WebErrorStatus));
            };
            EmbeddedPageNavigation.RestrictToDocument(current, page); current.NavigateToString(page);
            await navigated.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            if (!ReferenceEquals(lifetime, operation)) return;
            await current.ExecuteScriptAsync(script).AsTask().WaitAsync(TimeSpan.FromSeconds(20), token);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!ReferenceEquals(lifetime, operation)) return;
            CloseView();
            AppLog.Write(AppLogLevel.Warning, "reply.html.preview", ex);
            var fallback = new StackPanel { Spacing = 12 };
            fallback.Children.Add(new TextBlock
            {
                Text = UiText.Resolve("Aperçu WebView indisponible. Rendu natif affiché.", "WebView preview unavailable. Showing native rendering."),
                Foreground = FluentDesign.Secondary, TextWrapping = TextWrapping.Wrap
            });
            fallback.Children.Add(native()); host.Children.Add(new ScrollViewer
            { Content = fallback, HorizontalScrollMode = ScrollMode.Disabled });
            if (dynamicHeight) { Height = double.NaN; MinHeight = 80; MaxHeight = 2000; }
        }
        finally
        {
            initializing = false;
            if (closed) Release();
            else if (!IsLoaded) { unloadDelay.Stop(); unloadDelay.Start(); }
            else if (recoverQueued) { recoverQueued = false; Recover(); }
        }
    }

    internal void Close()
    {
        if (closed) return; closed = true;
        unloadDelay.Stop(); Release();
    }

    void Release()
    {
        unloadDelay.Stop(); resizeDelay.Stop();
        var previous = lifetime; lifetime = null;
        previous?.Cancel();
        // Disposal cannot tear down a native constructor that is pumping the event loop.
        if (!initializing) CloseView();
        previous?.Dispose();
    }

    void CloseView()
    {
        var previous = view; view = null; host.Children.Clear();
        if (previous == null) return;
        try
        {
#if WINDOWS
            previous.Close();
#else
            previous.Dispose();
#endif
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { AppLog.Write(AppLogLevel.Warning, "reply.html.preview.close", ex); }
    }
}
