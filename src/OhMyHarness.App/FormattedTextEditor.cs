using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;
using Windows.ApplicationModel.DataTransfer;

namespace OhMyHarness.App;

// Uses the same embedded browser runtime as the app. The page is local, contains no
// external dependencies, and its CSP disallows requests from pasted email content.
internal sealed class FormattedTextEditor : UserControl
{
    readonly Microsoft.UI.Xaml.Controls.WebView2 view = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly string placeholder;
    readonly bool readOnly;
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Task pending = Task.CompletedTask;
    bool starting, disposed, polling;
    int revision = -1, changeVersion;
    FormattedText document = FormattedText.Empty;
    internal event EventHandler? TextChanged;
    internal event Action<string>? Error;
    internal FormattedText Document => document;
    internal string Text { get => document.Text; set => SetDocument(FormattedText.Plain(value)); }

    internal FormattedTextEditor(string placeholder, bool readOnly = false)
    {
        this.placeholder = placeholder; this.readOnly = readOnly;
        MinHeight = 120; HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Stretch;
        Content = new Border { Child = view, BorderBrush = FluentDesign.Stroke, BorderThickness = new(1), CornerRadius = new(10) };
        Loaded += (_, _) => Begin(); SizeChanged += (_, _) => Begin();
        view.Loaded += (_, _) => Begin(); view.SizeChanged += (_, _) => Begin();
        timer.Tick += async (_, _) =>
        {
            if (polling || disposed || !ready.Task.IsCompletedSuccessfully || !pending.IsCompleted) return;
            polling = true;
            try { await Read(); } catch (Exception ex) { Error?.Invoke(ex.Message); timer.Stop(); }
            finally { polling = false; }
        };
        RegisterPropertyChangedCallback(IsEnabledProperty, (_, _) => Queue(async () => await view.ExecuteScriptAsync($"omhEditor.enable({(IsEnabled ? "true" : "false")})")));
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
            using var stream = typeof(FormattedTextEditor).Assembly.GetManifestResourceStream("OhMyHarness.App.Assets.rich-text-editor.html") ?? throw new IOException("Editor resource missing.");
            using var reader = new StreamReader(stream);
            var html = (await reader.ReadToEndAsync()).Replace("__NONCE__", Guid.NewGuid().ToString("N"));
            EmbeddedPageNavigation.RestrictToDocument(view, html);
            view.NavigateToString(html);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await view.ExecuteScriptAsync($"omhEditor.configure({JsonSerializer.Serialize(placeholder)}, {(readOnly ? "true" : "false")});omhEditor.enable({(IsEnabled ? "true" : "false")});");
            ready.TrySetResult(); timer.Start();
        }
        catch (Exception ex) { ready.TrySetException(ex); Error?.Invoke("Impossible d’ouvrir l’éditeur formaté : " + ex.Message); }
    }

    void Queue(Func<Task> action)
    {
        var previous = pending;
        pending = Execute();
        async Task Execute()
        {
            try { await previous; await ready.Task; if (!disposed) await action(); }
            catch (Exception ex) { if (!disposed) Error?.Invoke(ex.Message); }
        }
    }

    internal void SetDocument(FormattedText value)
    {
        document = value; changeVersion++; TextChanged?.Invoke(this, EventArgs.Empty);
        Queue(async () =>
        {
            // Do not read back here: virtual paragraph separators are normalized only on capture.
            await view.ExecuteScriptAsync($"omhEditor.set({JsonSerializer.Serialize(value.Html)})"); revision = -1;
        });
    }

    internal async Task<FormattedText> CaptureAsync()
    {
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(25)); await pending;
        await Read(true); return document;
    }

    async Task Read(bool force = false)
    {
        var version = changeVersion;
        var result = await view.ExecuteScriptAsync($"omhEditor.read({(force ? -1 : revision)})");
        if (disposed || version != changeVersion || string.IsNullOrWhiteSpace(result) || result == "null") return;
        var json = JsonSerializer.Deserialize<string>(result);
        if (json == null) return;
        var snapshot = JsonSerializer.Deserialize<EditorSnapshot>(json) ?? throw new IOException("Invalid editor response.");
        revision = snapshot.Revision;
        if (snapshot.Document.Html == document.Html && snapshot.Document.Text == document.Text) return;
        document = snapshot.Document; TextChanged?.Invoke(this, EventArgs.Empty);
    }
    sealed record EditorSnapshot(int Revision, FormattedText Document);

    internal void Select(int start, int length) => Queue(async () => await view.ExecuteScriptAsync($"omhEditor.select({start}, {length})"));
    internal async Task CopyAsync()
    {
        var value = await CaptureAsync();
        if (OperatingSystem.IsWindows()) { await FormattedClipboard.WriteAsync(value.Html, value.Text); return; }
        var data = new DataPackage(); data.SetText(value.Text); data.SetHtmlFormat(FormattedClipboard.HtmlFormat(value.Html));
        Clipboard.SetContent(data); Clipboard.Flush();
    }
    internal void CloseEditor()
    {
        disposed = true; timer.Stop(); ready.TrySetCanceled();
        if (Content is Border border) border.Child = null;
#if WINDOWS
        view.Close();
#else
        view.Dispose();
#endif
    }
}
