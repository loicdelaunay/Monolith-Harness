using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MonolithHarness.App;

// Index labels, never editable values (provider keys, prompts or file contents).
internal static class SettingsSearch
{
    sealed record Metadata(string Title, string Description, Control Control);
    internal sealed record Entry(int Page, string Section, string Title, string Description,
        FrameworkElement Target, Control? Control, IReadOnlyList<Expander> Expanders);
    static readonly ConditionalWeakTable<FrameworkElement, Metadata> cards = new();

    internal static void Register(FrameworkElement card, string title, string description, Control control) =>
        cards.Add(card, new(title, description, control));

    internal static IEnumerable<Entry> Entries(UIElement page, int index, string section)
    {
        if (page is FrameworkElement root) yield return new(index, section, section, "", root, null, []);
        var covered = new HashSet<Control>();
        foreach (var entry in Walk(page, [])) yield return entry;
        IEnumerable<Entry> Walk(UIElement element, IReadOnlyList<Expander> expanders)
        {
            if (element is FrameworkElement target && cards.TryGetValue(target, out var metadata))
            {
                covered.Add(metadata.Control);
                yield return new(index, section, metadata.Title, metadata.Description, target, metadata.Control, expanders);
            }
            else if (element is Control control && !covered.Contains(control))
            {
                var label = control switch
                {
                    PasswordBox box => box.Header as string,
                    TextBox box => box.Header as string,
                    ComboBox box => box.Header as string,
                    NumberBox box => box.Header as string,
                    Slider slider => slider.Header as string,
                    ToggleSwitch toggle => toggle.Header as string,
                    CheckBox check => check.Content as string,
                    Expander expander => expander.Header as string,
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(label))
                    yield return new(index, section, label, "", control, control, expanders);
            }
            var parents = element is Expander parent ? expanders.Append(parent).ToArray() : expanders;
            foreach (var child in Children(element))
                foreach (var entry in Walk(child, parents)) yield return entry;
        }
    }

    internal static bool Matches(Entry entry, string query)
    {
        var text = entry.Section + " " + entry.Title + " " + entry.Description;
        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(term =>
            CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, term,
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);
    }

    static IEnumerable<UIElement> Children(UIElement element) => element switch
    {
        Panel panel => panel.Children,
        Border { Child: { } child } => [child],
        UserControl { Content: UIElement child } => [child],
        ScrollViewer { Content: UIElement child } => [child],
        ContentControl { Content: UIElement child } => [child],
        ContentPresenter { Content: UIElement child } => [child],
        _ => []
    };
}
