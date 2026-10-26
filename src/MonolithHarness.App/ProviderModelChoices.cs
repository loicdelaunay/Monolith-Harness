using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    FrameworkElement BuildProviderModelChoices(ProviderDraft draft, Func<Provider> current, Action changed)
    {
        var panel = new StackPanel { Spacing = 10 };
        var source = current();
        var selected = ProviderModels.Visible(source).ToHashSet(StringComparer.Ordinal);
        var models = ProviderModels.Normalize(ModelCatalog.GetModelsForProvider(source).Concat(selected).Append(draft.Model));
        panel.Children.Add(Label(WorkflowText("Modèles disponibles dans le chat", "Models available in chat"), 16));
        var help = Label(WorkflowText("Cochez les modèles à retrouver dans le sélecteur du chat. Vous pouvez aussi saisir un identifiant dans le modèle par défaut.",
            "Select the models to show in the chat selector. You can also enter an identifier in the default model field."), 12);
        help.Foreground = FluentDesign.Secondary; panel.Children.Add(help);
        if (ProviderModels.Available(source).Count == 0 && models.Count > 0 && draft.Kind != "local")
            panel.Children.Add(Label(WorkflowText("Exemples et identifiants saisis : accès non vérifié. Importez les modèles pour consulter le catalogue de cette connexion.",
                "Examples and entered identifiers: access not verified. Import models to discover this connection's catalogue."), 12));
        var search = new AutoSuggestBox { Name = "ProviderModelSearch", Text = draft.ModelSearch,
            PlaceholderText = WorkflowText("Rechercher un modèle…", "Search models…"), QueryIcon = new SymbolIcon(Symbol.Find), MaxSuggestionListHeight = 180 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, WorkflowText("Rechercher un modèle", "Search models"));
        var all = new Button { Content = WorkflowText("Tout cocher", "Select all") };
        var clear = new Button { Content = WorkflowText("Tout décocher", "Clear all") };
        var bulk = new SettingsHeaderPanel { Spacing = 8 }; bulk.Children.Add(all); bulk.Children.Add(clear);
        var count = Label("", 12); count.Foreground = FluentDesign.Secondary;
        var rows = new StackPanel { Spacing = 2 };
        var previous = new Button { Content = WorkflowText("Précédent", "Previous") };
        var next = new Button { Content = WorkflowText("Suivant", "Next") };
        var page = Label("", 12);
        var pagination = new SettingsHeaderPanel { Spacing = 10 }; pagination.Children.Add(previous); pagination.Children.Add(page); pagination.Children.Add(next);
        void SaveSelection()
        {
            draft.SelectedModelsJson = System.Text.Json.JsonSerializer.Serialize(ProviderModels.Normalize(selected));
            if (!selected.Contains(draft.Model) && selected.Count > 0) draft.Model = selected.Order(StringComparer.OrdinalIgnoreCase).First();
            changed();
        }
        void Render()
        {
            const int size = 50;
            var filtered = string.IsNullOrWhiteSpace(draft.ModelSearch) ? models
                : models.Where(id => id.Contains(draft.ModelSearch, StringComparison.OrdinalIgnoreCase)).ToList();
            var pages = Math.Max(1, (filtered.Count + size - 1) / size);
            draft.ModelPage = Math.Clamp(draft.ModelPage, 0, pages - 1); rows.Children.Clear();
            void Counts() => count.Text = WorkflowText($"{selected.Count} cochés · {filtered.Count} résultats", $"{selected.Count} selected · {filtered.Count} results");
            foreach (var id in filtered.Skip(draft.ModelPage * size).Take(size))
            {
                var text = Label(id, 13);
                var check = new CheckBox { Content = text, IsChecked = selected.Contains(id), MinHeight = 38,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(check, id);
                void Update() { if (check.IsChecked == true) selected.Add(id); else selected.Remove(id); SaveSelection(); Counts(); }
                check.Checked += (_, _) => Update(); check.Unchecked += (_, _) => Update(); rows.Children.Add(check);
            }
            if (filtered.Count == 0) rows.Children.Add(Label(WorkflowText("Aucun modèle trouvé. Saisissez son identifiant ci-dessus ou importez le catalogue.",
                "No models found. Enter an identifier above or import the catalogue."), 13));
            Counts(); page.Text = $"{draft.ModelPage + 1} / {pages}";
            previous.IsEnabled = draft.ModelPage > 0; next.IsEnabled = draft.ModelPage + 1 < pages;
            pagination.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        search.TextChanged += (_, e) =>
        {
            draft.ModelSearch = search.Text; draft.ModelPage = 0; Render();
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                search.ItemsSource = string.IsNullOrWhiteSpace(search.Text) ? null : models.Where(id => id.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
        };
        search.SuggestionChosen += (_, e) => { search.Text = e.SelectedItem as string ?? search.Text; search.IsSuggestionListOpen = false; };
        search.QuerySubmitted += (_, _) => search.IsSuggestionListOpen = false;
        all.Click += (_, _) => { selected.Clear(); foreach (var id in models) selected.Add(id); SaveSelection(); Render(); };
        clear.Click += (_, _) => { selected.Clear(); SaveSelection(); Render(); };
        previous.Click += (_, _) => { draft.ModelPage--; Render(); }; next.Click += (_, _) => { draft.ModelPage++; Render(); };
        panel.Children.Add(search); panel.Children.Add(bulk); panel.Children.Add(count); panel.Children.Add(rows); panel.Children.Add(pagination);
        panel.Children.Add(Label(WorkflowText("Tout cocher / décocher concerne aussi les modèles masqués par la recherche.",
            "Select all / clear all also includes models hidden by the search."), 12));
        Render(); return panel;
    }
}
