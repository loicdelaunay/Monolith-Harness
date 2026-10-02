using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    (StackPanel Panel, Action<FeatureSettings> Save) BuildWebHttpSettings()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        var panel = new StackPanel { Spacing = 8, Margin = new(0, 8, 0, 0) };
        var mode = new ComboBox {
            Header = WorkflowText("Résultat des requêtes HTTP", "HTTP request results"),
            ItemsSource = new[] { WorkflowText("Smart · lecture ciblée (recommandé)", "Smart · targeted reading (recommended)"),
                WorkflowText("Legacy · page entière dans le contexte", "Legacy · entire page in context") },
            SelectedIndex = config.WebHttpResponseMode == "legacy" ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var explanation = Label("", 12);
        void Refresh() => explanation.Text = mode.SelectedIndex == 1
            ? WorkflowText("Legacy renvoie le contenu brut et les en-têtes directement au modèle. Les grandes pages consomment davantage de contexte. La limite de téléchargement reste de 1 Mio, configurable par l’outil ; les pages tronquées sont signalées.",
                "Legacy returns the raw body and headers directly to the model. Large pages consume more context. The download limit remains 1 MiB and can be configured by the tool; truncated pages are marked.")
            : WorkflowText("Smart économise le contexte : la page est enregistrée dans le cache des outils. Le modèle reçoit un aperçu et un sommaire, puis recherche et lit uniquement les passages utiles. Le HTML brut reste consultable par extraits. La durée de conservation se règle dans Général → Entretien des conversations.",
                "Smart saves context: the page is saved in the tool cache. The model receives a preview and outline, then searches and reads only relevant passages. Raw HTML remains available in excerpts. Set retention in General → Conversation maintenance.");
        mode.SelectionChanged += (_, _) => Refresh(); Refresh();
        panel.Children.Add(mode); panel.Children.Add(explanation);
        return (panel, settings => settings.WebHttpResponseMode = mode.SelectedIndex == 1 ? "legacy" : "smart");
    }
}
