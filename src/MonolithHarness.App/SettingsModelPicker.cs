using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

static class SettingsModelPicker
{
    public static void Populate(ComboBox picker, IEnumerable<string> models, string value)
    {
        value = value.Trim();
        var items = ProviderModels.Normalize(models.Concat(string.IsNullOrEmpty(value) ? [] : new[] { value }));
        // Assign the source first: changing ItemsSource can clear both Text and SelectedItem.
        picker.ItemsSource = items;
        picker.SelectedItem = items.FirstOrDefault(x => x == value);
        picker.Text = value;
    }

    public static string Read(ComboBox picker) => string.IsNullOrWhiteSpace(picker.Text)
        ? (picker.SelectedItem as string ?? "").Trim() : picker.Text.Trim();

    public static void Connect(ComboBox provider, ComboBox model, string saved)
    {
        var providerId = (provider.SelectedItem as Provider)?.Id;
        Populate(model, ModelCatalog.GetModelsForProvider(provider.SelectedItem as Provider), saved);
        model.SelectionChanged += (_, _) => { if (model.SelectedItem is string selected) model.Text = selected; };
        provider.SelectionChanged += (_, _) =>
        {
            var selected = provider.SelectedItem as Provider;
            if (selected?.Id == providerId) return; // Ignore deferred initial selection events.
            providerId = selected?.Id;
            Populate(model, ModelCatalog.GetModelsForProvider(selected), selected?.Model ?? "");
        };
    }
}
