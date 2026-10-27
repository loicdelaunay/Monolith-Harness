using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    static readonly string[] ReasoningModes = ["auto", "manual", "default"];

    // Per-model thinking: automatic discovery (API metadata, catalogues, identifiers), a manual list of levels, or no parameter at all.
    FrameworkElement BuildProviderReasoning(ProviderDraft draft, Func<Provider> current, Action changed, bool editable)
    {
        var french = state.Language == "fr";
        var panel = new StackPanel { Name = "ProviderReasoning", Spacing = 10 };
        panel.Children.Add(Label(WorkflowText("Réflexion", "Thinking"), 16));
        var model = new ComboBox { Name = "ProviderReasoningModel", Header = WorkflowText("Modèle", "Model"), HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = ProviderModels.Normalize(ProviderModels.Visible(current()).Append(draft.Model)), SelectedItem = draft.Model };
        var mode = new ComboBox { Name = "ProviderReasoningMode", Header = WorkflowText("Mode de réflexion", "Thinking mode"), HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = editable,
            ItemsSource = new[] { WorkflowText("Découverte automatique", "Automatic discovery"), WorkflowText("Manuel", "Manual"), WorkflowText("Par défaut · aucun paramètre", "Default · no parameter") } };
        var status = Label("", 12); status.Foreground = FluentDesign.Secondary;
        var checks = ReasoningCapabilities.All.Where(x => x != "auto")
            .Select(level => new CheckBox { Content = ReasoningCapabilities.Label(level, french), Tag = level, IsEnabled = editable, MinHeight = 32 }).ToArray();
        var levels = new StackPanel { Name = "ProviderReasoningLevels", Spacing = 0 };
        foreach (var check in checks) levels.Children.Add(check);
        var loading = false;
        string Selected() => model.SelectedItem as string ?? draft.Model;
        string Names(IEnumerable<string> values) => string.Join(", ", values.Where(x => x != "auto").Select(x => ReasoningCapabilities.Label(x, french)));
        void Render()
        {
            loading = true;
            var target = current(); var id = Selected();
            var (preference, manual) = ModelContexts.ReasoningPreference(target, id);
            var detected = ReasoningCapabilities.Detected(target, id);
            var found = detected.Levels.Where(x => x != "auto").ToArray();
            mode.SelectedIndex = Array.IndexOf(ReasoningModes, preference);
            // Manual mode starts from what discovery found.
            foreach (var check in checks) check.IsChecked = (preference == "manual" ? manual : found).Contains((string)check.Tag);
            levels.Visibility = preference == "manual" ? Visibility.Visible : Visibility.Collapsed;
            var detection = found.Length > 0
                ? WorkflowText($"Découverte automatique : {Names(found)} · {detected.Source}", $"Automatic discovery: {Names(found)} · {detected.Source}")
                : WorkflowText("Découverte automatique : aucun niveau déclaré pour ce modèle, seul Auto est proposé. Importez les modèles pour interroger l’API, ou choisissez Manuel.",
                    "Automatic discovery: no level is declared for this model, so only Auto is offered. Import models to query the API, or choose Manual.");
            status.Text = preference switch
            {
                "manual" => WorkflowText("Cochez les niveaux acceptés par ce modèle. Un niveau refusé par l’API est retiré pendant la session.", "Select the levels this model accepts. A level rejected by the API is removed for the session.") + "\n" + detection,
                "default" => WorkflowText("Aucun paramètre de réflexion n’est envoyé : le fournisseur applique son comportement par défaut et seul Auto est proposé.",
                    "No thinking parameter is sent: the provider applies its default behaviour and only Auto is offered."),
                _ => detection
            };
            loading = false;
        }
        void Save()
        {
            if (loading || !editable) return;
            var target = current();
            ModelContexts.SetReasoningPreference(target, Selected(), ReasoningModes[Math.Max(0, mode.SelectedIndex)], checks.Where(x => x.IsChecked == true).Select(x => (string)x.Tag));
            draft.ModelContextsJson = target.ModelContextsJson; changed(); Render();
        }
        model.SelectionChanged += (_, _) => Render();
        mode.SelectionChanged += (_, _) => Save();
        foreach (var check in checks) { check.Checked += (_, _) => Save(); check.Unchecked += (_, _) => Save(); }
        panel.Children.Add(model); panel.Children.Add(mode); panel.Children.Add(status); panel.Children.Add(levels);
        Render();
        return panel;
    }
}
