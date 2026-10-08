using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using System.Text.Json;

namespace MonolithHarness.App;

internal sealed class AdvancedMarkdownView : UserControl
{
    Microsoft.UI.Xaml.Controls.WebView2 view = new();
    readonly Func<string, Task>? openFile;
    string markdown = "", searchQuery = "";
    bool starting, ready, painting, again;
    int revision;
    string diagnostic = "";
    string? paintedMarkdown;
    PaintOptions? paintedOptions;
    sealed record PaintOptions(string Foreground, bool Dark, double FontSize, double LineHeight, bool Mermaid, bool Math, string Language, string Palette);
    internal bool HasSelection { get; private set; }
    static readonly Lazy<string> document = new(BuildDocument);
    static readonly Lazy<string[]> libraries = new(() => new[] { "katex.min.js", "auto-render.min.js", "mermaid.min.js", "visuals.js" }.Select(Resource).ToArray());
    internal AdvancedMarkdownView(Func<string, Task>? openFile)
    {
        MarkdownRenderer.RegisterVisual(this);
        this.openFile = openFile; Height = 160; HorizontalAlignment = HorizontalAlignment.Stretch;
        Content = view;
        Loaded += (_, _) => Begin(); SizeChanged += (_, _) => Begin();
        Unloaded += (_, _) =>
        {
            revision++; ready = starting = HasSelection = false; paintedMarkdown = null; paintedOptions = null;
#if WINDOWS
            view.Close();
#else
            view.Dispose();
#endif
            view = new(); Content = view;
        };
    }
    internal void Update(string text) { markdown = text; Paint(); }
    internal void RefreshVisuals() => Paint();
    internal async void SetSearchQuery(string query)
    {
        if (searchQuery == query) return; searchQuery = query;
        if (!ready) return;
        try { await view.ExecuteScriptAsync($"markdownVisuals.search({JsonSerializer.Serialize(query)})"); }
        catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "markdown.search", ex); }
    }
    async void Begin()
    {
        if (starting || ActualWidth <= 0) return;
        starting = true; var current = view; var version = revision;
        try
        {
            await current.EnsureCoreWebView2Async(); if (version != revision) return;
#if WINDOWS
            current.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
            current.CoreWebView2.PermissionRequested += (_, e) => e.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
#endif
            current.CoreWebView2.WebMessageReceived += async (_, e) =>
            {
                if (version != revision) return;
                try
                {
                    using var message = JsonDocument.Parse(e.WebMessageAsJson); var data = message.RootElement;
                    var kind = data.GetProperty("kind").GetString();
                    if (kind == "diagram-cache-get")
                    {
                        var svg = MermaidDiagramCache.Read(data.GetProperty("source").GetString() ?? "", data.GetProperty("dark").GetBoolean(), data.TryGetProperty("palette", out var palette) ? palette.GetString() ?? "" : "");
                        await current.ExecuteScriptAsync($"markdownVisuals.resolveCache({data.GetProperty("id").GetInt32()},{JsonSerializer.Serialize(svg)})");
                    }
                    else if (kind == "diagram-cache-put")
                        MermaidDiagramCache.Write(data.GetProperty("source").GetString() ?? "", data.GetProperty("dark").GetBoolean(), data.GetProperty("svg").GetString() ?? "", data.TryGetProperty("palette", out var palette) ? palette.GetString() ?? "" : "");
                    else if (kind == "selection") HasSelection = data.GetProperty("selected").GetBoolean();
                    else if (kind == "size")
                    {
                        var height = Math.Clamp(data.GetProperty("height").GetDouble(), 48, 50000);
                        if (Math.Abs(Height - height) > 1) Height = height;
                    }
                    else if (data.GetProperty("kind").GetString() == "link" && openFile != null)
                    {
                        var url = data.GetProperty("url").GetString() ?? "";
                        url = LocalFileLinks.PathFromUrl(url) ?? url;
                        if (url.Length > 0 && (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is "file" or "http" or "https" || Path.IsPathRooted(url))) await openFile(url);
                    }
                }
                catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "markdown.message", ex); }
            };
            current.CoreWebView2.NavigationCompleted += async (_, e) =>
            {
                if (version != revision) return;
                if (!e.IsSuccess) { diagnostic = "Navigation failed: " + e.WebErrorStatus; return; }
                try
                {
                    // NavigateToString is limited to 2 MB on Windows WebView2.
                    // Load the fixed bundled libraries after the small document has loaded.
                    foreach (var script in libraries.Value)
                    {
                        if (version != revision) return;
                        await current.ExecuteScriptAsync(script);
                    }
                    if (version == revision) { ready = true; Paint(); }
                }
                catch (Exception ex) { diagnostic = ex.ToString(); }
            };
            EmbeddedPageNavigation.RestrictToDocument(current, document.Value); current.NavigateToString(document.Value);
        }
        catch (Exception ex)
        {
            if (version != revision) return;
            diagnostic = ex.ToString();
            Content = new TextBlock { Text = markdown + "\n" + ex.Message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        }
    }
    async void Paint()
    {
        if (!ready) return;
        if (painting) { again = true; return; }
        painting = true; var version = revision;
        try
        {
            do
            {
                again = false;
                var foreground = FluentDesign.Primary is Microsoft.UI.Xaml.Media.SolidColorBrush brush ? brush.Color : Microsoft.UI.Colors.White;
                var options = new PaintOptions($"#{foreground.R:X2}{foreground.G:X2}{foreground.B:X2}", FluentDesign.CurrentTheme.Dark,
                    ChatDensity.FontSize, ChatDensity.LineHeight, MarkdownRenderer.RenderMermaid, MarkdownRenderer.RenderMath, UiText.Language, JsonSerializer.Serialize(new { primaryColor = FluentDesign.CurrentTheme.Surface, primaryTextColor = FluentDesign.CurrentTheme.Text, primaryBorderColor = FluentDesign.CurrentTheme.Accent, lineColor = FluentDesign.CurrentTheme.Muted, secondaryColor = FluentDesign.CurrentTheme.Background, tertiaryColor = FluentDesign.CurrentTheme.Surface, darkMode = FluentDesign.CurrentTheme.Dark }));
                var text = markdown;
                if (paintedMarkdown == text && paintedOptions == options) continue;
                var colors = new { text = options.Foreground, background = "transparent", dark = options.Dark, palette = JsonSerializer.Deserialize<JsonElement>(options.Palette) };
                await view.ExecuteScriptAsync($"document.documentElement.lang={JsonSerializer.Serialize(options.Language)};markdownVisuals.set({JsonSerializer.Serialize(MarkdownDisplay.Html(text, options.Math))},{JsonSerializer.Serialize(colors)},{options.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)},{options.LineHeight.ToString(System.Globalization.CultureInfo.InvariantCulture)},{JsonSerializer.Serialize(new { mermaid = options.Mermaid, math = options.Math })})");
                if (version == revision) { paintedMarkdown = text; paintedOptions = options; await view.ExecuteScriptAsync($"markdownVisuals.search({JsonSerializer.Serialize(searchQuery)})"); }
            } while (again && version == revision);
        }
        catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, "markdown.render", ex); }
        finally { painting = false; if (again && ready && version != revision) Paint(); }
    }
    internal async Task<string> InspectAsync()
    {
        if (!ready) return JsonSerializer.Serialize(new { ready, starting, ActualWidth, ActualHeight, diagnostic });
        return await view.ExecuteScriptAsync("JSON.stringify({math:document.querySelectorAll('.katex').length,diagrams:document.querySelectorAll('.mermaid-diagram svg').length,errors:document.querySelectorAll('.visual-error').length,height:document.getElementById('markdown').getBoundingClientRect().height})");
    }
    static string Resource(string name)
    {
        using var stream = typeof(AdvancedMarkdownView).Assembly.GetManifestResourceStream("MonolithHarness.App.Assets.Markdown." + name) ?? throw new IOException("Markdown resource missing: " + name);
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    static string BuildDocument() => "<!doctype html><html lang='fr'><head><meta charset='utf-8'><meta http-equiv='Content-Security-Policy' content=\"default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; font-src data:; img-src data:; connect-src 'none'; base-uri 'none'; form-action 'none'\"><style>" +
        Resource("katex.min.css") + Resource("visuals.css") + "</style></head><body><main id='markdown'></main></body></html>";
}
