using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using Windows.ApplicationModel.DataTransfer;

namespace MonolithHarness.App;

// Changes arrive from the local page. No script polling or navigation while idle.
internal sealed partial class FormattedTextEditor : UserControl
{
    readonly Microsoft.UI.Xaml.Controls.WebView2 view = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly SemaphoreSlim scripts = new(1, 1);
    readonly CancellationTokenSource lifetime = new();
    readonly string placeholder;
    readonly bool readOnly;
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task pending = Task.CompletedTask;
    bool starting, disposed, reading, dirty;
    int revision = -1, changeVersion;
    FormattedText document = FormattedText.Empty;
    string text = "";
    internal event EventHandler? TextChanged;
    internal event Action<string>? Error;
    internal FormattedText Document => document;
    internal int DocumentVersion => changeVersion;
    internal string Text { get => text; set => SetDocument(FormattedText.Plain(value)); }

    internal FormattedTextEditor(string placeholder, bool readOnly = false)
    {
        this.placeholder = placeholder; this.readOnly = readOnly;
        MinHeight = 120; HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Stretch;
        Content = new Border { Child = view, BorderBrush = FluentDesign.Stroke, BorderThickness = new(1), CornerRadius = new(10) };
        Loaded += (_, _) => Begin(); SizeChanged += (_, _) => Begin();
        view.Loaded += (_, _) => Begin(); view.SizeChanged += (_, _) => Begin();
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (disposed || !ready.Task.IsCompletedSuccessfully) return;
            if (reading || !pending.IsCompleted) { timer.Start(); return; }
            reading = true; dirty = false;
            try { if (!await Read()) dirty = true; }
            catch (OperationCanceledException) when (disposed) { }
            catch (Exception ex) { if (!disposed) Error?.Invoke(ex.Message); }
            finally { reading = false; if (dirty && !disposed) timer.Start(); }
        };
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => Queue(async () => await Script($"omhEditor.enable({(IsEnabled ? "true" : "false")})")));
    }

    async void Begin()
    {
        if (starting || disposed || view.ActualWidth <= 0 || view.ActualHeight <= 0) return;
        starting = true;
        try
        {
            await view.EnsureCoreWebView2Async();
            if (disposed) return;
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.CoreWebView2.NavigationCompleted += (_, e) => { if (e.IsSuccess) loaded.TrySetResult(); else loaded.TrySetException(new IOException("Éditeur indisponible / Editor unavailable.")); };
            view.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                if (disposed) return;
                try
                {
                    using var message = JsonDocument.Parse(e.WebMessageAsJson);
                    var kind = message.RootElement.GetProperty("kind").GetString();
                    if (kind == "changed") DispatcherQueue.TryEnqueue(() => { if (!disposed) { dirty = true; timer.Stop(); timer.Start(); } });
                    else if (kind == "error")
                    {
                        var error = message.RootElement.GetProperty("message").GetString() ?? "";
                        DispatcherQueue.TryEnqueue(() => { if (!disposed) Error?.Invoke(error); });
                    }
                }
                catch (JsonException) { }
            };
            using var stream = typeof(FormattedTextEditor).Assembly.GetManifestResourceStream("MonolithHarness.App.Assets.rich-text-editor.html") ?? throw new IOException("Editor resource missing.");
            using var reader = new StreamReader(stream);
            var html = (await reader.ReadToEndAsync()).Replace("__NONCE__", Guid.NewGuid().ToString("N"));
            EmbeddedPageNavigation.RestrictToDocument(view, html);
            view.NavigateToString(html);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20), lifetime.Token);
            await Script($"omhEditor.configure({JsonSerializer.Serialize(placeholder)}, {(readOnly ? "true" : "false")});omhEditor.enable({(IsEnabled ? "true" : "false")});");
            ready.TrySetResult();
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception ex) { ready.TrySetException(ex); if (!disposed) Error?.Invoke("Impossible d’ouvrir l’éditeur formaté : " + ex.Message); }
    }

    internal int ScriptCallCount { get; private set; }
    async Task<string> Script(string code)
    {
        await scripts.WaitAsync(lifetime.Token);
        try { lifetime.Token.ThrowIfCancellationRequested(); ScriptCallCount++; return await view.ExecuteScriptAsync(code); }
        finally { scripts.Release(); }
    }
    void Queue(Func<Task> action)
    {
        var previous = pending;
        pending = Execute();
        async Task Execute()
        {
            try { await previous; await ready.Task; if (!disposed) await action(); }
            catch (OperationCanceledException) when (disposed) { }
            catch (Exception ex) { if (!disposed) Error?.Invoke(ex.Message); }
        }
    }
    static bool Same(FormattedText a, FormattedText b) => a.Template == b.Template && a.Runs.SequenceEqual(b.Runs);
    void Accept(FormattedText value)
    {
        document = value; text = value.Text; changeVersion++; TextChanged?.Invoke(this, EventArgs.Empty);
    }
    internal void SetDocument(FormattedText value)
    {
        if (disposed || Same(value, document)) return;
        Accept(value);
        var version = changeVersion;
        Queue(async () =>
        {
            // Large HTML serialization stays off the window's dispatcher; later replacements supersede it.
            var code = await Task.Run(() => "omhEditor.set(" + JsonSerializer.Serialize(value.Html) + ")", lifetime.Token);
            if (version != changeVersion || disposed) return;
            var result = await Script(code);
            if (version == changeVersion && int.TryParse(result, out var updated)) revision = updated;
        });
    }
    internal async Task<FormattedText> CaptureAsync()
    {
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(25), lifetime.Token); await pending;
        timer.Stop();
        var until = DateTime.UtcNow.AddSeconds(25);
        while (!await Read(true))
        {
            if (DateTime.UtcNow >= until) throw new IOException("Le collage n’est pas terminé. Réessayez. / Paste is still processing; please retry.");
            await Task.Delay(50, lifetime.Token);
        }
        return document;
    }
    async Task<bool> Read(bool force = false)
    {
        var version = changeVersion;
        var result = await Script($"omhEditor.read({(force ? -1 : revision)})");
        if (disposed || version != changeVersion || string.IsNullOrWhiteSpace(result) || result == "null") return true;
        var snapshot = await Task.Run(() =>
        {
            var json = JsonSerializer.Deserialize<string>(result);
            return json == null ? null : JsonSerializer.Deserialize<EditorSnapshot>(json);
        }, lifetime.Token);
        if (snapshot?.Pending == true) return false;
        if (snapshot?.Document == null || disposed || version != changeVersion) return true;
        revision = snapshot.Revision;
        if (!Same(snapshot.Document, document)) Accept(snapshot.Document);
        return true;
    }
    sealed record EditorSnapshot(int Revision, FormattedText? Document, bool Pending = false);
    internal void Select(int start, int length) => Queue(async () => await Script($"omhEditor.select({start}, {length})"));
    internal async Task CopyAsync()
    {
        var value = await CaptureAsync();
        var html = await Task.Run(() => value.Html, lifetime.Token);
        if (OperatingSystem.IsWindows()) { await FormattedClipboard.WriteAsync(html, value.Text); return; }
        var data = new DataPackage(); data.SetText(value.Text); data.SetHtmlFormat(FormattedClipboard.HtmlFormat(html));
        Clipboard.SetContent(data); Clipboard.Flush();
    }
    internal void CloseEditor()
    {
        if (disposed) return;
        disposed = true; timer.Stop(); lifetime.Cancel(); ready.TrySetCanceled();
        if (Content is Border border) border.Child = null;
#if WINDOWS
        view.Close();
#else
        view.Dispose();
#endif
    }
}
