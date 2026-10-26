using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    static readonly Dictionary<string, byte[]> providerLogoBytes = [];
    static FrameworkElement ProviderConnectionIcon(ProviderDraft draft, double size)
    {
        var id = draft.Kind switch
        {
            "vertex" => "vertex", "opencode" => "opencode", "chatgpt-acp" => "openai", "claude-acp" => "claude",
            _ => ProviderPresets.Resolve(draft.BaseUrl)?.Id ?? ""
        };
        if (id.Length == 0 && Uri.TryCreate(draft.BaseUrl, UriKind.Absolute, out var endpoint))
            id = endpoint.Host.ToLowerInvariant() switch { "api.openai.com" => "openai", "api.deepseek.com" => "deepseek", _ => "" };
        if (id is "google-studio" or "gemini") id = "google";
        if (id is "openai" or "opencode" or "zai") id += FluentDesign.CurrentTheme.Dark ? "-dark" : "-light";
        if (id.Length > 0)
        {
            if (!providerLogoBytes.TryGetValue(id, out var bytes))
            {
                using var source = typeof(MainWindow).Assembly.GetManifestResourceStream("MonolithHarness.App.Assets.ProviderIcons." + id + ".png");
                if (source != null) { using var data = new MemoryStream(); source.CopyTo(data); providerLogoBytes[id] = bytes = data.ToArray(); }
            }
            if (bytes != null)
            {
                var image = new BitmapImage(); using var stream = new MemoryStream(bytes); using var source = stream.AsRandomAccessStream(); image.SetSource(source);
                return new Image { Source = image, Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
            }
        }
        return FluentDesign.Icon(draft.Kind switch { "local" => "\uE7F4", "composite" => "\uE8D4", _ => "\uE968" }, size);
    }
}
