namespace MonolithHarness.Core;

/// <summary>Libraries available in response previews. These flags never grant host permissions.</summary>
public sealed record ReplyRenderOptions(bool Tailwind = true, bool ChartJs = true,
    bool FollowApplicationTheme = true, ReplyPreviewPalette? Palette = null)
{
    public static ReplyRenderOptions Default { get; } = new();

    public string Instructions => "LOCAL PREVIEW LIBRARIES: " +
        (Tailwind
            ? "Tailwind CSS " + ReplyWebLibraries.TailwindVersion + " is already bundled and enabled in HTML previews. Use Tailwind v4 utility classes directly, including responsive variants. Use light and dark: color variants for surfaces and text. Use <style type=\"text/tailwindcss\"> for @theme/custom utilities when needed. "
            : "Tailwind CSS is disabled: use ordinary CSS in <style>, not Tailwind utilities or type=text/tailwindcss. ") +
        (FollowApplicationTheme
            ? "The preview follows the application theme by default: dark: is driven by the application, never OS preferences. " +
                (Tailwind ? "Use bg-monolith-bg, bg-monolith-surface, text-monolith-text, text-monolith-muted and border-monolith-accent for colors that match the application. "
                    : "Use --monolith-bg, --monolith-surface, --monolith-text, --monolith-muted and --monolith-accent in ordinary CSS for application colors. ") +
                "Avoid forcing a single light or dark palette. " +
                (Palette is { } palette ? "The current application is " + (palette.Dark ? "dark" : "light") + ": background " + palette.Background + ", surface " + palette.Surface + ", text " + palette.Foreground + ", accent " + palette.Accent + ". " : "")
            : "Application theme synchronization is disabled. Keep the document's own palette and dark-mode rules. ") +
        (ChartJs
            ? "Chart.js " + ReplyWebLibraries.ChartJsVersion + " is already loaded as the global Chart before your scripts run. Use <canvas> and new Chart(canvas, {type, data, options}) inside the same HTML block; put initialization after the canvas markup or inside DOMContentLoaded. Wrap each canvas in a container with an explicit responsive height; use responsive:true and maintainAspectRatio:false. Built-in chart types are available, but external plugins and time/date adapters are not. "
            : "Chart.js is disabled: do not call Chart; use ordinary HTML, CSS or inline SVG for charts. ") +
        "Do not add library script/link URLs, CDNs, imports or fetch calls: enabled libraries are supplied locally by the application, even offline. Keep your JavaScript inline and produce one complete fenced html document. Full layouts and interactive charts appear in web previews, including directly in the chat if that option is enabled; the native fallback remains simplified.";
}

/// <summary>Only validated color values are inserted into preview CSS.</summary>
public sealed record ReplyPreviewPalette(bool Dark, string Background, string Surface, string Foreground, string Muted, string Accent)
{
    internal bool IsValid => new[] { Background, Surface, Foreground, Muted, Accent }.All(color =>
        color is { Length: 7 } && color[0] == '#' && color.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0);
}
