using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

static class ChatDensity
{
    static readonly List<(WeakReference<FrameworkElement> Target, string Kind)> targets = [];
    public static string Id { get; private set; } = "normal";
    public static double FontSize => Id switch { "compact" => 13.5, "spacious" => 15.5, _ => 14.5 };
    public static double LineHeight => Id switch { "compact" => 20, "spacious" => 27, _ => 23 };
    public static double BlockGap => Id switch { "compact" => 3, "spacious" => 10, _ => 6 };
    public static void Configure(string id)
    {
        if (Id == id) return;
        Id = id;
        targets.RemoveAll(x => !x.Target.TryGetTarget(out _));
        foreach (var (target, kind) in targets) if (target.TryGetTarget(out var item)) Apply(item, kind);
        MarkdownRenderer.RefreshDensity();
    }
    public static T Track<T>(T item, string kind) where T : FrameworkElement
    { targets.Add((new(item), kind)); Apply(item, kind); return item; }
    static void Apply(FrameworkElement item, string kind)
    {
        bool compact = Id == "compact", spacious = Id == "spacious";
        if (item is Border surface && kind == "surface") surface.Padding = compact ? new(12, 8, 12, 8) : spacious ? new(20) : new(16, 12, 16, 12);
        if (item is not StackPanel panel) return;
        panel.Spacing = kind switch { "messages" or "virtual-turn" => compact ? 8 : spacious ? 20 : 12, "body" => BlockGap, _ => compact ? 5 : spacious ? 14 : 8 };
        if (kind == "messages") panel.Padding = compact ? new(4, 12, 12, 12) : spacious ? new(4, 24, 12, 24) : new(4, 16, 12, 16);
    }
}

public sealed partial class MainWindow
{
    (FrameworkElement Panel, Action<FeatureSettings> Save) BuildChatDensitySettings()
    {
        var settings = FeatureSettings.Read(state.FeaturesJson);
        string[] values = ["compact", "normal", "spacious"];
        var selector = new ComboBox { ItemsSource = new[] { WorkflowText("Compact", "Compact"), WorkflowText("Normal", "Normal"), WorkflowText("Espacé", "Spacious") }, SelectedIndex = Math.Max(0, Array.IndexOf(values, settings.ChatMessageDensity)), MinWidth = 150 };
        var panel = FluentDesign.Setting(WorkflowText("Densité des messages", "Message density"),
            WorkflowText("Règle la taille du texte, l’interligne et l’espace entre les messages. S’applique aussi à l’historique déjà affiché.", "Adjust text size, line height and message spacing, including already displayed history."), selector);
        return (panel, target => target.ChatMessageDensity = values[Math.Clamp(selector.SelectedIndex, 0, values.Length - 1)]);
    }
}
