using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    // Keep the complete catalogue and committed selection separate from transient suggestions.
    sealed class ModelPicker : UserControl
    {
        readonly AutoSuggestBox search = new() { HorizontalAlignment = HorizontalAlignment.Stretch,
            TextMemberPath = nameof(ModelChoice.Model), MaxSuggestionListHeight = 260, MinHeight = 36, FontSize = 13, Padding = new(8, 3, 8, 3),
            Foreground = FluentDesign.Primary, Background = FluentDesign.Card, BorderBrush = FluentDesign.Stroke };
        readonly Button browse = new() { Width = 30, Height = 36, Padding = new(0),
            Content = new FontIcon { Glyph = "\uE70D", FontSize = 12 } };
        readonly TextBlock hint = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary };
        List<ModelChoice> choices = [];
        ModelChoice? selected;
        bool syncing;
        string query = "";
        int matchCount;

        public event EventHandler? SelectionChanged;
        public IReadOnlyList<ModelChoice> Items => choices;
        public IEnumerable<ModelChoice> ItemsSource
        {
            get => choices;
            set
            {
                choices = value.ToList();
                selected = choices.FirstOrDefault(x => x == selected);
                ResetSearch();
            }
        }
        public ModelChoice? SelectedItem
        {
            get => selected;
            set
            {
                if (value != null && !choices.Contains(value)) return;
                bool changed = selected != value;
                selected = value;
                ResetSearch();
                if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public DataTemplate ItemTemplate { set => search.ItemTemplate = value; }
        public double MaxSuggestionListHeight { set => search.MaxSuggestionListHeight = value; }

        public ModelPicker()
        {
            var field = new Grid { ColumnSpacing = 6 };
            field.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            field.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            field.Children.Add(search); Grid.SetColumn(browse, 1); field.Children.Add(browse);
            var content = new StackPanel { Spacing = 5 }; content.Children.Add(field); content.Children.Add(hint);
            Content = content;
            search.GotFocus += (_, _) =>
            {
                if (query.Length != 0 || search.Text != selected?.Model) return;
                search.DispatcherQueue.TryEnqueue(() =>
                {
                    if (query.Length == 0 && search.Text == selected?.Model) FindTextBox(search)?.SelectAll();
                });
            };
            search.TextChanged += (_, e) =>
            {
                if (syncing || e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
                query = search.Text.Trim();
                Filter(query);
                search.IsSuggestionListOpen = matchCount > 0;
            };
            search.QuerySubmitted += (_, e) =>
            {
                var choice = e.ChosenSuggestion as ModelChoice;
                if (choice == null)
                {
                    var exact = choices.Where(x => x.Model.Equals(e.QueryText.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                    // A duplicate model name needs an explicit provider choice unless its current connection matches.
                    choice = exact.Count == 1 ? exact[0] : exact.FirstOrDefault(x => x.ProviderId == selected?.ProviderId);
                }
                if (choice != null) SelectedItem = choice;
                else { Filter(e.QueryText.Trim()); search.IsSuggestionListOpen = matchCount > 0; }
            };
            browse.Click += (_, _) => { if (search.IsSuggestionListOpen) search.IsSuggestionListOpen = false; else OpenSuggestions(); };
        }

        void Filter(string text)
        {
            var terms = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var matches = choices.Where(x => terms.All(term => x.Model.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || x.ProviderName.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(x => x.Model.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x.ProviderId == selected?.ProviderId ? 0 : 1)
                .ThenBy(x => x.Model, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ProviderName, StringComparer.OrdinalIgnoreCase).ToList();
            matchCount = matches.Count;
            search.ItemsSource = matches.Take(50).ToList();
            RefreshHint();
        }

        public void RefreshLanguage()
        {
            search.PlaceholderText = UiText.Resolve("Rechercher un modèle ou un fournisseur…", "Search models or providers…");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, UiText.Resolve("Rechercher un modèle", "Search models"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(browse, UiText.Resolve("Afficher les modèles activés", "Show enabled models"));
            ToolTipService.SetToolTip(browse, UiText.Resolve("Afficher les modèles activés", "Show enabled models"));
            RefreshHint();
        }

        void RefreshHint()
        {
            hint.Text = choices.Count == 0
                ? UiText.Resolve("Activez des modèles dans Réglages > Fournisseurs.", "Enable models in Settings > Providers.")
                : query.Length > 0 && matchCount == 0
                    ? UiText.Resolve("Aucun modèle correspondant. Essayez un autre nom ou fournisseur.", "No matching model. Try another name or provider.")
                    : "";
            hint.Visibility = hint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void ResetSearch()
        {
            syncing = true;
            try
            {
                query = "";
                search.Text = selected?.Model ?? "";
                Filter("");
                search.IsSuggestionListOpen = false;
            }
            finally { syncing = false; }
        }

        public void OpenSuggestions()
        {
            syncing = true;
            try { query = ""; search.Text = ""; Filter(""); }
            finally { syncing = false; }
            search.Focus(FocusState.Programmatic);
            search.IsSuggestionListOpen = choices.Count > 0;
        }

        static TextBox? FindTextBox(DependencyObject root)
        {
            if (root is TextBox text) return text;
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
                if (FindTextBox(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i)) is { } child) return child;
            return null;
        }
    }
}
