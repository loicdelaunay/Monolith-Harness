using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    (StackPanel Panel, Action<FeatureSettings> Save) BuildImageGenerationSettings(List<ProviderDraft> drafts)
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        var panel = new StackPanel { Spacing = 10 };
        var source = new ComboBox { Header = WorkflowText("Fournisseur de génération", "Generation provider"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var model = new ComboBox { Header = WorkflowText("Modèle d’image", "Image model"), PlaceholderText = WorkflowText("Identifiant exact du modèle", "Exact model identifier"), IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        var width = new NumberBox { Header = WorkflowText("Largeur (multiple de 64)", "Width (multiple of 64)"), Minimum = 256, Maximum = 2048, SmallChange = 64, Value = config.ImageGenerationWidth };
        var height = new NumberBox { Header = WorkflowText("Hauteur (multiple de 64)", "Height (multiple of 64)"), Minimum = 256, Maximum = 2048, SmallChange = 64, Value = config.ImageGenerationHeight };
        var steps = new NumberBox { Header = WorkflowText("Étapes · modèle local", "Steps · local model"), Minimum = 1, Maximum = 100, Value = config.ImageGenerationSteps };
        var note = Label("", 12); note.Tag = null;
        var previous = config.ImageGenerationProviderId; bool populating = false;
        void PopulateModels(string selected)
        {
            if (source.SelectedItem is not ProviderDraft provider) { SettingsModelPicker.Populate(model, [], selected); return; }
            var local = provider.Kind == "local";
            var choices = local ? LocalProviderSettings.Read(provider.LocalModelsJson).Models.Where(x => x.Purpose == "image").Select(x => x.Id).ToList()
                : ProviderModels.Normalize(ProviderModels.Parse(provider.DetectedModelsJson).Where(x => x.Contains("image", StringComparison.OrdinalIgnoreCase) || x.Contains("dall", StringComparison.OrdinalIgnoreCase)));
            if (!local && ProviderPresets.Resolve(provider.BaseUrl) is { } preset)
                choices = ProviderModels.Normalize(choices.Concat(preset.SuggestedImageModels));
            if (local && !choices.Contains(selected)) selected = choices.FirstOrDefault() ?? "";
            SettingsModelPicker.Populate(model, choices, selected); steps.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
            note.Text = local ? WorkflowText("Importez un checkpoint complet et préparez son moteur dans Fournisseurs → Local. Le modèle est chargé à chaque génération, puis libère sa mémoire.",
                "Import a complete checkpoint and prepare its engine in Providers → Local. The model loads for each generation and releases its memory afterwards.")
                : WorkflowText("Le fournisseur doit accepter POST images/generations (API compatible OpenAI). La liste de modèles de chat ne garantit pas la génération d’images. Vous pouvez saisir l’identifiant exact ; les résolutions acceptées et le tarif dépendent du fournisseur.",
                    "The provider must support POST images/generations (OpenAI-compatible API). A chat model list does not guarantee image generation. Enter the exact identifier; supported sizes and pricing depend on the provider.");
            if (!local && ProviderPresets.Resolve(provider.BaseUrl)?.SuggestedImageModels.Count > 0)
                note.Text += WorkflowText("\nLes identifiants proposés incluent des exemples documentés : leur accès n’est pas vérifié pour votre compte.", "\nSuggested identifiers include documented examples; access is not verified for your account.");
        }
        void RefreshProviders()
        {
            var selected = source.SelectedItem as ProviderDraft;
            var options = drafts.Where(x => x.Kind is not ("composite" or "opencode") && (x.Kind == "local" || (ProviderPresets.Resolve(x.BaseUrl)?.ImageGeneration ?? true))).ToList();
            populating = true; source.ItemsSource = options; source.SelectedItem = selected != null && options.Contains(selected) ? selected : options.FirstOrDefault(x => x.Id == config.ImageGenerationProviderId && x.Id > 0);
            populating = false; PopulateModels(SettingsModelPicker.Read(model));
        }
        source.SelectionChanged += (_, _) =>
        {
            if (populating) return;
            var selected = source.SelectedItem as ProviderDraft;
            var value = selected?.Id == previous && previous > 0 ? SettingsModelPicker.Read(model) : "";
            previous = selected?.Id ?? 0; PopulateModels(value);
        };
        model.SelectionChanged += (_, _) => { if (model.SelectedItem is string value) model.Text = value; };
        source.DropDownOpened += (_, _) => RefreshProviders(); panel.Loaded += (_, _) => RefreshProviders();
        foreach (var element in new UIElement[] { source, model, width, height, steps, note }) panel.Children.Add(element);
        panel.Children.Add(Label(WorkflowText("L’IA appelle generate_image avec une description. Le skill produit une image, l’affiche dans le chat et enregistre le fichier dans images/chat-…/. Les autorisations du chat s’appliquent ; une API distante peut facturer la génération. Bêta : création d’une image par appel, sans retouche d’image existante.",
            "The AI calls generate_image with a description. The skill shows the resulting image in chat and saves it in images/chat-…/. Chat permissions apply; remote APIs may charge for generation. Beta: one new image per call, no image editing."), 12));
        RefreshProviders(); PopulateModels(config.ImageGenerationModel);
        return (panel, settings =>
        {
            settings.ImageGenerationProviderId = source.SelectedItem is ProviderDraft selected && drafts.Contains(selected) ? selected.Id : 0;
            settings.ImageGenerationModel = SettingsModelPicker.Read(model);
            settings.ImageGenerationWidth = double.IsFinite(width.Value) ? (int)width.Value : 1024;
            settings.ImageGenerationHeight = double.IsFinite(height.Value) ? (int)height.Value : 1024;
            settings.ImageGenerationSteps = double.IsFinite(steps.Value) ? (int)steps.Value : 28;
        });
    }
}
