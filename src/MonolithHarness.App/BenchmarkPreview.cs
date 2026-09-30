using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MonolithHarness.App;

// The model document runs only in a sandboxed opaque-origin iframe. There is no
// host object bridge, network, shared origin, application data or native execution.
internal sealed class BenchmarkPreview : UserControl
{
    readonly Microsoft.UI.Xaml.Controls.WebView2 view = new();
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool starting, closed;
    int callId;
    internal BenchmarkPreview()
    {
        MinHeight = 120;
        Content = new Border { Child = view, BorderBrush = FluentDesign.Stroke, BorderThickness = new(1), CornerRadius = new(10) };
        view.Loaded += (_, _) => Begin(); view.SizeChanged += (_, _) => Begin();
    }
    async void Begin()
    {
        if (starting || closed || view.ActualWidth <= 0 || view.ActualHeight <= 0) return;
        starting = true;
        try
        {
            await view.EnsureCoreWebView2Async();
            if (closed) return;
#if WINDOWS
            view.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
            view.CoreWebView2.PermissionRequested += (_, e) => e.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
#endif
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.CoreWebView2.NavigationCompleted += (_, e) => { if (e.IsSuccess) loaded.TrySetResult(); else loaded.TrySetException(new IOException("Aperçu indisponible / Preview unavailable.")); };
            using var stream = typeof(BenchmarkPreview).Assembly.GetManifestResourceStream("MonolithHarness.App.Assets.benchmark-preview.html") ?? throw new IOException("Preview resource missing.");
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();
            EmbeddedPageNavigation.RestrictToDocument(view, html);
            view.NavigateToString(html);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20)); ready.TrySetResult();
        }
        catch (Exception ex) { ready.TrySetException(ex); }
    }
    internal async Task LoadAsync(string html, CancellationToken ct)
    {
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(25), ct);
        await Script($"omhPreview.load({JsonSerializer.Serialize(html)},{JsonSerializer.Serialize(Guid.NewGuid().ToString("N"))})").WaitAsync(TimeSpan.FromSeconds(10), ct);
        await AwaitReply("ready", 20, ct);
    }
    internal async Task<JsonNode?> InvokeAsync(string method, object? arguments, CancellationToken ct)
    {
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(25), ct);
        var id = (++callId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Script($"omhPreview.call({JsonSerializer.Serialize(id)},{JsonSerializer.Serialize(method)},{JsonSerializer.Serialize(arguments)})").WaitAsync(TimeSpan.FromSeconds(8), ct);
        return await AwaitReply(id, method == "solve" ? 35 : 12, ct);
    }
    async Task<JsonNode?> AwaitReply(string id, int seconds, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var raw = await Script($"omhPreview.read({JsonSerializer.Serialize(id)})").WaitAsync(timeout.Token);
                var result = JsonNode.Parse(JsonSerializer.Deserialize<string>(raw) ?? "{}");
                if (result?["done"]?.GetValue<bool>() == true)
                {
                    if (result["error"]?.GetValue<string>() is { Length: > 0 } error) throw new IOException(error);
                    await Script($"omhPreview.forget({JsonSerializer.Serialize(id)})").WaitAsync(timeout.Token);
                    return result["value"]?.DeepClone();
                }
                await Task.Delay(60, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException("L’application générée ne répond pas dans le délai imparti. / Generated application timed out."); }
    }
    internal async Task ClearAsync()
    {
        if (!closed && ready.Task.IsCompletedSuccessfully) await view.ExecuteScriptAsync("omhPreview.clear()");
    }
    async Task<string> Script(string script) => await view.ExecuteScriptAsync(script);
    internal void ClosePreview()
    {
        closed = true; ready.TrySetCanceled(); if (Content is Border border) border.Child = null;
#if WINDOWS
        view.Close();
#else
        view.Dispose();
#endif
    }
}
