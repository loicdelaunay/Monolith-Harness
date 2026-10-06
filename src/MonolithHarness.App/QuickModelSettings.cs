using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    sealed class QuickLevelDraft
    {
        public required QuickModelLevel Original;
        public required TextBox Name;
        public required ComboBox Provider;
        public required ComboBox Model;
        public required ComboBox Thinking;
        public required ComboBox Mode;
        public required Border Card;
        public required TextBlock Heading;
        public required Button Up;
        public required Button Down;
        public bool Refreshing;
        public ProviderDraft? LastProvider;
        public QuickModelLevel Read() => new() { Id = Original.Id, Name = Name.Text.Trim(),
            ProviderId = (Provider.SelectedItem as ProviderDraft)?.Id ?? Original.ProviderId,
            Model = SettingsModelPicker.Read(Model), ThinkingLevel = QuickModelShortcuts.ThinkingLevels[Math.Clamp(Thinking.SelectedIndex, 0, 4)],
            InteractionMode = Mode.SelectedIndex == 0 ? "chat" : "agent" };
    }

    static Provider QuickSettingsProvider(ProviderDraft draft) => new() { Id = draft.Id, Name = draft.Name, Kind = draft.Kind,
        Model = draft.Model, BaseUrl = draft.BaseUrl, SelectedModelsJson = draft.SelectedModelsJson,
        DetectedModelsJson = draft.DetectedModelsJson, LocalModelsJson = draft.LocalModelsJson, CompositeJson = draft.CompositeJson };

    (StackPanel Panel, Action<FeatureSettings> Save, Func<bool> Validate) BuildQuickModelSettings(ProviderEditorState providerEditor)
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        var panel = new StackPanel { Spacing = 12 };
        var enabled = new ToggleSwitch { IsOn = config.QuickModelLevelsEnabled };
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Réglage rapide modèle et raisonnement", "Quick model and thinking settings"),
            WorkflowText("Associez chaque niveau à un modèle, une réflexion et un mode Chat ou Agent. Jusqu’à 10 niveaux, du plus léger au plus poussé.",
                "Associate each level with a model, thinking effort and Chat or Agent mode. Up to 10 levels, from lightest to most involved."), enabled));
        var editor = new StackPanel { Spacing = 10 };
        panel.Children.Add(editor);
        var count = Label("", 12); editor.Children.Add(count);
        var cards = new StackPanel { Spacing = 10 }; editor.Children.Add(cards);
        var add = new Button { Content = WorkflowText("+ Ajouter un niveau", "+ Add level"), HorizontalAlignment = HorizontalAlignment.Right };
        editor.Children.Add(add);
        editor.Children.Add(Label(WorkflowText("En mode Simple, le curseur suit cet ordre. Les niveaux de raisonnement disponibles dépendent du modèle et du fournisseur.",
            "In Simple mode, the slider follows this order. Thinking support depends on the model and provider."), 12));
        var error = Label("", 12); error.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed); panel.Children.Add(error);
        var rows = new List<QuickLevelDraft>();
        List<ProviderDraft> Sources() => providerEditor.Drafts.Where(d => ProviderModels.CanChat(QuickSettingsProvider(d))).ToList();
        void NumberRows()
        {
            for (var i = 0; i < rows.Count; i++)
            { rows[i].Heading.Text = WorkflowText("Niveau ", "Level ") + (i + 1); rows[i].Up.IsEnabled = i > 0; rows[i].Down.IsEnabled = i < rows.Count - 1; }
            count.Text = rows.Count + " / " + QuickModelShortcuts.MaximumLevels + WorkflowText(" niveaux", " levels");
            add.IsEnabled = rows.Count < QuickModelShortcuts.MaximumLevels;
            error.Text = "";
        }
        void RefreshModels(QuickLevelDraft row, string value)
        {
            row.Refreshing = true;
            try { SettingsModelPicker.Populate(row.Model, row.Provider.SelectedItem is ProviderDraft d ? ProviderModels.Visible(QuickSettingsProvider(d)) : [], value); }
            finally { row.Refreshing = false; }
        }
        void RefreshSources(QuickLevelDraft row)
        {
            var selected = row.Provider.SelectedItem as ProviderDraft;
            var model = SettingsModelPicker.Read(row.Model);
            row.Refreshing = true;
            try
            {
                var sources = Sources();
                row.Provider.ItemsSource = sources; row.Provider.SelectedItem = sources.Contains(selected!) ? selected : null;
                row.LastProvider = row.Provider.SelectedItem as ProviderDraft;
            }
            finally { row.Refreshing = false; }
            RefreshModels(row, model);
        }
        void Move(QuickLevelDraft row, int delta)
        {
            var current = rows.IndexOf(row); var next = current + delta;
            if (next < 0 || next >= rows.Count) return;
            rows.RemoveAt(current); rows.Insert(next, row);
            cards.Children.Remove(row.Card); cards.Children.Insert(next, row.Card); NumberRows();
        }
        void Add(QuickModelLevel value, ProviderDraft? source = null)
        {
            if (rows.Count >= QuickModelShortcuts.MaximumLevels) return;
            source ??= Sources().FirstOrDefault(p => p.Id == value.ProviderId && value.ProviderId > 0);
            var name = new TextBox { Header = WorkflowText("Nom (facultatif)", "Name (optional)"), Text = value.Name, MaxLength = 60 };
            var providerPicker = new ComboBox { Header = WorkflowText("Fournisseur", "Provider"), DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
            var model = new ComboBox { Header = WorkflowText("Modèle", "Model"), IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch };
            var thinking = new ComboBox { Header = WorkflowText("Réflexion", "Thinking"), HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = ThinkingLevelLabels(), SelectedIndex = Array.IndexOf(QuickModelShortcuts.ThinkingLevels, value.ThinkingLevel) };
            var mode = new ComboBox { Header = WorkflowText("Mode", "Mode"), ItemsSource = new[] { "Chat", "Agent" },
                SelectedIndex = value.InteractionMode == "chat" ? 0 : 1, HorizontalAlignment = HorizontalAlignment.Stretch };
            var heading = Label("", 14); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            var up = new Button { Width = 28, Height = 28, Padding = new(0) };
            var down = new Button { Width = 28, Height = 28, Padding = new(0) };
            var remove = new Button { Width = 28, Height = 28, Padding = new(0) };
            FluentDesign.IconButton(up, "\uE74A", WorkflowText("Monter le niveau", "Move level up"), false);
            FluentDesign.IconButton(down, "\uE74B", WorkflowText("Descendre le niveau", "Move level down"), false);
            FluentDesign.IconButton(remove, "\uE74D", WorkflowText("Supprimer le niveau", "Remove level"), false);
            var header = new Grid(); header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            header.Children.Add(heading); var actions = Row(up, down, remove); actions.Spacing = 4; Grid.SetColumn(actions, 1); header.Children.Add(actions);
            var fields = new Grid { ColumnSpacing = 10, RowSpacing = 8 };
            fields.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); fields.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (int i = 0; i < 4; i++) fields.RowDefinitions.Add(new() { Height = GridLength.Auto });
            Control[] controls = [providerPicker, model, thinking, mode];
            foreach (var control in controls) fields.Children.Add(control);
            void Layout(double width) { for (int i = 0; i < controls.Length; i++) { Grid.SetRow(controls[i], width < 450 ? i : i / 2); Grid.SetColumn(controls[i], width < 450 ? 0 : i % 2); Grid.SetColumnSpan(controls[i], width < 450 ? 2 : 1); } }
            Layout(600); fields.SizeChanged += (_, e) => Layout(e.NewSize.Width / (TextZoom.ForWindow(root) / 100d));
            var body = new StackPanel { Spacing = 8 }; body.Children.Add(header); body.Children.Add(name); body.Children.Add(fields);
            var card = new Border { Child = body, Padding = new(12), CornerRadius = new(8), BorderThickness = new(1), BorderBrush = FluentDesign.Stroke, Background = FluentDesign.Card };
            var row = new QuickLevelDraft { Original = value, Name = name, Provider = providerPicker, Model = model, Thinking = thinking, Mode = mode,
                Heading = heading, Up = up, Down = down, Card = card, Refreshing = true, LastProvider = source };
            providerPicker.ItemsSource = Sources(); providerPicker.SelectedItem = source;
            row.Refreshing = false; RefreshModels(row, value.Model);
            providerPicker.SelectionChanged += (_, _) =>
            {
                if (row.Refreshing) return;
                var selected = providerPicker.SelectedItem as ProviderDraft;
                if (ReferenceEquals(selected, row.LastProvider)) return;
                row.LastProvider = selected;
                if (selected == null) return; // Retain the saved model when a provider disappears.
                var available = selected == null ? [] : ProviderModels.Visible(QuickSettingsProvider(selected));
                RefreshModels(row, selected != null && available.Contains(selected.Model) ? selected.Model : available.FirstOrDefault() ?? "");
            };
            model.SelectionChanged += (_, _) => { if (!row.Refreshing && model.SelectedItem is string selected) model.Text = selected; };
            providerPicker.DropDownOpened += (_, _) => RefreshSources(row);
            model.DropDownOpened += (_, _) => RefreshModels(row, SettingsModelPicker.Read(row.Model));
            up.Click += (_, _) => Move(row, -1); down.Click += (_, _) => Move(row, 1);
            remove.Click += (_, _) => { rows.Remove(row); cards.Children.Remove(card); NumberRows(); };
            rows.Add(row); cards.Children.Add(card); NumberRows();
        }
        void AddCurrent()
        {
            var source = Sources().FirstOrDefault(p => p.Id == provider?.Id) ?? Sources().FirstOrDefault();
            Add(new() { ProviderId = source?.Id ?? 0, Model = source?.Model ?? "",
                ThinkingLevel = ConversationModes.EffectiveThinking(chat, state.ThinkingLevel, state.ChatThinkingLevel), InteractionMode = ConversationModes.Normalize(chat?.InteractionMode) }, source);
        }
        foreach (var value in config.QuickModelLevels) Add(value);
        void EnabledChanged() { editor.Visibility = enabled.IsOn ? Visibility.Visible : Visibility.Collapsed; if (enabled.IsOn && rows.Count == 0) AddCurrent(); }
        enabled.Toggled += (_, _) => EnabledChanged(); EnabledChanged(); NumberRows();
        add.Click += (_, _) => AddCurrent();
        panel.Loaded += (_, _) => { foreach (var row in rows) RefreshSources(row); };
        return (panel, saved =>
        {
            saved.QuickModelLevelsEnabled = enabled.IsOn;
            saved.QuickModelLevels = rows.Select(r => r.Read()).ToList();
            saved.QuickModelSelectionMode = enabled.IsOn && !config.QuickModelLevelsEnabled ? "simple" : FeatureSettings.Read(state.FeaturesJson).QuickModelSelectionMode;
        }, () =>
        {
            error.Text = "";
            try
            {
                QuickModelShortcuts.Validate(rows.Select(r => r.Read()).ToList(), false);
                if (!enabled.IsOn) return true;
                if (rows.Count == 0) throw new ArgumentException(WorkflowText("Ajoutez au moins un niveau.", "Add at least one level."));
                foreach (var row in rows)
                {
                    if (row.Provider.SelectedItem is not ProviderDraft source || !providerEditor.Drafts.Contains(source)
                        || !ProviderModels.CanChat(QuickSettingsProvider(source)) || !ProviderModels.Visible(QuickSettingsProvider(source)).Contains(SettingsModelPicker.Read(row.Model)))
                        throw new ArgumentException(row.Heading.Text + WorkflowText(" : choisissez un fournisseur et un modèle activé.", ": choose a provider and an enabled model."));
                }
                return true;
            }
            catch (ArgumentException ex) { error.Text = ex.Message; return false; }
        });
    }
}
