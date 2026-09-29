using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    static AppearanceTheme CopyTheme(AppearanceTheme theme) => theme with { Colors = new(theme.Colors) };
    sealed record ThemeChoice(string Id, string Name, bool Dark) { public override string ToString() => (Dark ? "☾  " : "☀  ") + Name; }

    (StackPanel Panel, Action<FeatureSettings> Save) BuildAppearanceSettings(Window owner)
    {
        var settings = FeatureSettings.Read(state.FeaturesJson);
        var custom = (settings.CustomThemes ?? []).Where(AppearanceThemes.IsValidCustom).Select(CopyTheme).ToList();
        string selected = AppearanceThemes.Get(settings.Theme, custom).Id;
        var panel = new StackPanel { Spacing = 16 };
        var selector = new ComboBox { Header = WorkflowText("Thème actif", "Active theme"), HorizontalAlignment = HorizontalAlignment.Stretch };
        bool refreshing = false, editing = false;
        void Refresh()
        {
            refreshing = true;
            var choices = AppearanceThemes.WithCustom(custom).Select(x => new ThemeChoice(x.Id, WorkflowText(x.French, x.English), x.Dark)).ToList();
            selector.ItemsSource = choices; selector.SelectedItem = choices.First(x => x.Id == selected);
            refreshing = false;
        }
        Refresh();
        selector.SelectionChanged += (_, _) =>
        {
            if (refreshing || selector.SelectedItem is not ThemeChoice choice) return;
            selected = choice.Id; ApplyTheme(selected, custom);
        };
        var themeTabs = new TabView { IsAddTabButtonVisible = false, CanDragTabs = false, CanReorderTabs = false, TabWidthMode = TabViewWidthMode.Equal };
        themeTabs.TabItems.Add(new TabViewItem { Header = WorkflowText("Thème", "Theme"), IsClosable = false, Content = selector });
        var customize = new StackPanel { Spacing = 12 };
        customize.Children.Add(Label(WorkflowText("Créez vos palettes et réglez chaque couleur de l’interface et des messages.", "Create palettes and adjust individual interface and message colors."), 13));
        var open = new Button { Content = WorkflowText("Ouvrir l’éditeur", "Open editor"), HorizontalAlignment = HorizontalAlignment.Right };
        customize.Children.Add(open);
        themeTabs.TabItems.Add(new TabViewItem { Header = WorkflowText("Customiser", "Customize"), IsClosable = false, Content = customize });
        async Task Edit()
        {
            if (editing) return;
            editing = true;
            try
            {
                var result = await EditCustomThemesAsync(owner, custom, selected);
                if (result is { } accepted && ReferenceEquals(settingsWindow, owner))
                {
                    custom = accepted.Themes; selected = accepted.Selected;
                    Refresh(); ApplyTheme(selected, custom);
                }
            }
            finally { editing = false; themeTabs.SelectedIndex = 0; }
        }
        open.Click += async (_, _) => await Guard(Edit);
        themeTabs.SelectionChanged += async (_, _) => { if (themeTabs.SelectedIndex == 1) await Guard(Edit); };
        panel.Children.Add(FluentDesign.Surface(themeTabs, 16));
        panel.Children.Add(Label(WorkflowText("Le thème est prévisualisé immédiatement. Enregistrez les réglages pour conserver les changements.", "Themes preview immediately. Save settings to keep your changes."), 12));
        var fonts = BuildFontSettings(); panel.Children.Add(fonts.Panel);
        return (panel, target => {
            target.CustomThemes = custom.Select(CopyTheme).ToList(); target.Theme = selected;
            if (AppearanceThemes.IsCustomId(target.CliTheme) && !custom.Any(x => x.Id == target.CliTheme)) target.CliTheme = "shared";
            fonts.Save(target);
        });
    }

    async Task<(List<AppearanceTheme> Themes, string Selected)?> EditCustomThemesAsync(Window owner, List<AppearanceTheme> source, string selectedId)
    {
        if (owner.Content is not FrameworkElement host || host.XamlRoot == null) return null;
        var drafts = source.Select(CopyTheme).ToList();
        var current = AppearanceThemes.Get(selectedId, drafts);
        bool filling = false, valid = true;
        var choice = new ComboBox { Header = WorkflowText("Thème à personnaliser", "Theme to customize"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var name = new TextBox { Header = WorkflowText("Nom du thème", "Theme name"), MaxLength = 60 };
        var dark = new ToggleSwitch { Header = WorkflowText("Mode sombre", "Dark mode"), OnContent = WorkflowText("Sombre", "Dark"), OffContent = WorkflowText("Clair", "Light") };
        var fields = new Dictionary<string, TextBox>();
        var swatches = new Dictionary<string, Border>();
        var colorButtons = new List<Button>();
        var preview = new Border { Padding = new(16), CornerRadius = new(10), MinHeight = 270 };
        var error = Label("", 12); error.Foreground = FluentDesign.Resource("ToolMessageErrorStrokeBrush");
        var contrast = Label("", 12);
        var detail = Label("", 12);
        var create = new Button { Content = WorkflowText("Créer une copie", "Create a copy") };
        var delete = new Button { Content = WorkflowText("Supprimer", "Delete") };
        var colorList = new StackPanel { Spacing = 10 };
        var colors = new ScrollViewer { Content = colorList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var previewColumn = new StackPanel { Spacing = 10 }; previewColumn.Children.Add(Label(WorkflowText("Aperçu", "Preview"), 16)); previewColumn.Children.Add(preview); previewColumn.Children.Add(contrast);
        var content = new StackPanel { Spacing = 12, Width = Math.Max(340, Math.Min(840, host.ActualWidth - 110)) };
        var dialog = new ContentDialog { XamlRoot = host.XamlRoot, RequestedTheme = host.RequestedTheme,
            Title = WorkflowText("Thèmes sur mesure", "Custom themes"), PrimaryButtonText = WorkflowText("Valider", "Apply"), CloseButtonText = WorkflowText("Annuler", "Cancel"), DefaultButton = ContentDialogButton.Primary };
        dialog.Resources["ContentDialogMaxWidth"] = content.Width + 60;
        content.Children.Add(choice);
        var identity = new Grid { ColumnSpacing = 16 };
        identity.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); identity.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        identity.Children.Add(name); Grid.SetColumn(dark, 1); identity.Children.Add(dark); content.Children.Add(identity); content.Children.Add(detail);
        var workspace = new Grid { ColumnSpacing = 20, RowSpacing = 12, Height = Math.Max(200, Math.Min(390, host.ActualHeight - 390)) };
        workspace.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); workspace.ColumnDefinitions.Add(new() { Width = new(260) });
        workspace.Children.Add(colors); Grid.SetColumn(previewColumn, 1); workspace.Children.Add(previewColumn);
        // A narrow settings window retains both sections in one scrollable column.
        if (content.Width < 660)
        {
            colors.Content = null; workspace.Children.Clear();
            var stacked = new StackPanel { Spacing = 16 }; stacked.Children.Add(colorList); stacked.Children.Add(previewColumn);
            var narrow = new ScrollViewer { Content = stacked, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumnSpan(narrow, 2); workspace.Children.Add(narrow);
        }
        content.Children.Add(workspace); content.Children.Add(error);
        content.Children.Add(Label(WorkflowText("Les couleurs « Auto » suivent la palette. Validez ce dialogue, puis enregistrez les réglages. Annuler restaure les thèmes précédents.", "Auto colors follow the palette. Apply this dialog, then save settings. Cancel restores the previous themes."), 12));
        var actions = Row(create, delete); actions.HorizontalAlignment = HorizontalAlignment.Right; content.Children.Add(actions);
        dialog.Content = new ScrollViewer { Content = content, MaxHeight = Math.Max(250, host.ActualHeight - 190), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        (string Key, string Fr, string En)[] slots = [
            ("Background", "Fond de l’application", "Application background"), ("Surface", "Panneaux et champs", "Panels and inputs"),
            ("Text", "Texte principal", "Primary text"), ("Muted", "Texte secondaire", "Secondary text"), ("Accent", "Accent et actions", "Accent and actions"),
            ("ControlStrokeColorDefaultBrush", "Bordures des champs", "Input borders"), ("CardStrokeColorDefaultBrush", "Bordures des panneaux", "Panel borders"),
            ("ControlHoverBrush", "Survol des contrôles", "Control hover"), ("ControlPressedBrush", "Contrôles pressés", "Pressed controls"),
            ("ControlSelectedBrush", "Élément sélectionné", "Selected item"), ("ControlSelectedHoverBrush", "Sélection survolée", "Selected item hover"),
            ("ConversationHoverFillBrush", "Survol des conversations", "Conversation hover"), ("ConversationHoverStrokeBrush", "Bordure au survol", "Conversation hover border"),
            ("UserMessageFillBrush", "Fond de vos messages", "Your message background"), ("UserMessageStrokeBrush", "Bordure de vos messages", "Your message border"),
            ("AssistantMessageFillBrush", "Fond des réponses", "Reply background"), ("AssistantMessageStrokeBrush", "Bordure des réponses", "Reply border"),
            ("ToolMessageFillBrush", "Fond des outils", "Tool background"), ("ToolMessageStrokeBrush", "Bordure des outils", "Tool border"),
            ("ToolMessageTitleBrush", "Titres des outils", "Tool titles"), ("ToolMessageErrorStrokeBrush", "Erreurs", "Errors")
        ];
        string BaseColor(AppearanceTheme t, string key) => key switch { "Background" => t.Background, "Surface" => t.Surface, "Text" => t.Text, "Muted" => t.Muted, "Accent" => t.Accent, _ => t.Colors.GetValueOrDefault(key, "") };
        Windows.UI.Color ActualColor(AppearanceTheme t, string key) => AppearanceThemes.CustomColorKeys.Contains(key) ? FluentDesign.ThemeColor(t, key) : ParseSidebarColor(BaseColor(t, key));
        bool Editable() => current.Id.StartsWith("custom-", StringComparison.Ordinal);
        foreach (var slot in slots)
        {
            var line = new Grid { ColumnSpacing = 8 };
            line.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); line.ColumnDefinitions.Add(new() { Width = new(94) }); line.ColumnDefinitions.Add(new() { Width = new(34) });
            var label = Label(WorkflowText(slot.Fr, slot.En), 12); label.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(label);
            var input = new TextBox { MaxLength = 7, PlaceholderText = "Auto", FontSize = 12, Padding = new(6), VerticalAlignment = VerticalAlignment.Center };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, label.Text + " · #RRGGBB");
            fields.Add(slot.Key, input); Grid.SetColumn(input, 1); line.Children.Add(input);
            var swatch = new Border { Width = 20, Height = 20, CornerRadius = new(4), BorderBrush = FluentDesign.Stroke, BorderThickness = new(1) };
            var pick = new Button { Content = swatch, Padding = new(5), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(pick, label.Text); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pick, label.Text);
            swatches.Add(slot.Key, swatch); colorButtons.Add(pick); Grid.SetColumn(pick, 2); line.Children.Add(pick);
            pick.Click += (_, _) =>
            {
                var picker = new ColorPicker { Color = ActualColor(current, slot.Key), IsAlphaEnabled = false, IsHexInputVisible = true, IsColorSpectrumVisible = true, IsColorSliderVisible = true, Width = 280 };
                picker.ColorChanged += (_, e) => input.Text = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
                var flyout = new Flyout { Content = picker }; flyout.ShowAt(pick);
            };
            input.TextChanged += (_, _) => Changed(); colorList.Children.Add(line);
        }
        void DrawPreview()
        {
            var t = current;
            preview.Background = new SolidColorBrush(ParseSidebarColor(t.Background));
            var samples = new StackPanel { Spacing = 12 };
            samples.Children.Add(new TextBlock { Text = name.Text, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(ParseSidebarColor(t.Text)), FontSize = 18 });
            samples.Children.Add(new TextBlock { Text = WorkflowText("Texte secondaire", "Secondary text"), TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(ParseSidebarColor(t.Muted)), FontSize = 12 });
            void Sample(string text, string background, string border, string foreground)
            {
                samples.Children.Add(new Border { Padding = new(10), CornerRadius = new(7), Background = new SolidColorBrush(FluentDesign.ThemeColor(t, background)), BorderBrush = new SolidColorBrush(FluentDesign.ThemeColor(t, border)), BorderThickness = new(1), Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(FluentDesign.ThemeColor(t, foreground)), FontSize = 13 } });
            }
            Sample(WorkflowText("Votre message", "Your message"), "UserMessageFillBrush", "UserMessageStrokeBrush", "TextFillColorPrimaryBrush");
            Sample(WorkflowText("Réponse du modèle", "Model reply"), "AssistantMessageFillBrush", "AssistantMessageStrokeBrush", "TextFillColorPrimaryBrush");
            Sample(WorkflowText("Résultat d’un outil", "Tool result"), "ToolMessageFillBrush", "ToolMessageStrokeBrush", "ToolMessageTitleBrush");
            samples.Children.Add(new Border { Padding = new(10, 7, 10, 7), CornerRadius = new(5), HorizontalAlignment = HorizontalAlignment.Right, Background = new SolidColorBrush(ParseSidebarColor(t.Accent)), Child = new TextBlock { Text = WorkflowText("Action principale", "Primary action"), Foreground = new SolidColorBrush(ParseSidebarColor(ThemeContrast.On(t.Accent))), FontSize = 13 } });
            preview.Child = samples;
            double ratio = new[] { t.Background, t.Surface }.Min(bg => Math.Min(ThemeContrast.Ratio(t.Text, bg), ThemeContrast.Ratio(t.Muted, bg)));
            contrast.Text = ratio < 4.5 ? WorkflowText("Contraste faible : certains textes risquent d’être difficiles à lire.", "Low contrast: some text may be difficult to read.") : WorkflowText("Contraste du texte lisible.", "Readable text contrast.");
            foreach (var slot in slots) swatches[slot.Key].Background = new SolidColorBrush(ActualColor(t, slot.Key));
        }
        void Changed()
        {
            if (filling) return;
            valid = true; error.Text = "";
            if (Editable())
            {
                string title = name.Text.Trim();
                if (title.Length == 0 || title.Any(char.IsControl)) { valid = false; error.Text = WorkflowText("Donnez un nom au thème.", "Enter a theme name."); }
                else if (AppearanceThemes.WithCustom(drafts).Any(x => x.Id != current.Id && (string.Equals(x.French, title, StringComparison.OrdinalIgnoreCase) || string.Equals(x.English, title, StringComparison.OrdinalIgnoreCase))))
                { valid = false; error.Text = WorkflowText("Ce nom existe déjà.", "This name is already used."); }
                foreach (var slot in slots)
                {
                    var value = fields[slot.Key].Text.Trim();
                    bool optional = AppearanceThemes.CustomColorKeys.Contains(slot.Key);
                    if (!(optional && value.Length == 0) && !AppearanceThemes.IsColor(value))
                    { valid = false; error.Text = WorkflowText(slot.Fr, slot.En) + " : #RRGGBB"; break; }
                }
                if (valid)
                {
                    string Read(string key) => fields[key].Text.Trim().ToUpperInvariant();
                    current = current with { French = title, English = title, Dark = dark.IsOn, Background = Read("Background"), Surface = Read("Surface"), Text = Read("Text"), Muted = Read("Muted"), Accent = Read("Accent"),
                        Colors = AppearanceThemes.CustomColorKeys.Where(key => Read(key).Length > 0).ToDictionary(key => key, Read) };
                    drafts[drafts.FindIndex(x => x.Id == current.Id)] = current;
                }
            }
            dialog.IsPrimaryButtonEnabled = valid; create.IsEnabled = valid && drafts.Count < 50;
            if (valid) DrawPreview();
        }
        void Load()
        {
            filling = true;
            var choices = AppearanceThemes.WithCustom(drafts).Select(x => new ThemeChoice(x.Id, WorkflowText(x.French, x.English), x.Dark)).ToList();
            choice.ItemsSource = choices; choice.SelectedItem = choices.First(x => x.Id == current.Id);
            name.Text = WorkflowText(current.French, current.English); name.IsReadOnly = !Editable(); dark.IsOn = current.Dark; dark.IsEnabled = Editable();
            foreach (var slot in slots) { fields[slot.Key].Text = BaseColor(current, slot.Key); fields[slot.Key].IsReadOnly = !Editable(); }
            foreach (var button in colorButtons) button.IsEnabled = Editable();
            delete.IsEnabled = Editable();
            detail.Text = Editable() ? WorkflowText("Modifiez les couleurs ci-dessous, ou supprimez ce thème personnalisé.", "Edit colors below, or delete this custom theme.") : WorkflowText("Thème intégré : créez une copie pour le personnaliser.", "Built-in theme: create a copy to customize it.");
            filling = false; Changed();
        }
        choice.SelectionChanged += (_, _) =>
        {
            if (filling || choice.SelectedItem is not ThemeChoice item || item.Id == current.Id) return;
            if (!valid) { filling = true; choice.SelectedItem = choice.Items.OfType<ThemeChoice>().First(x => x.Id == current.Id); filling = false; return; }
            current = AppearanceThemes.Get(item.Id, drafts); Load();
        };
        name.TextChanged += (_, _) => Changed(); dark.Toggled += (_, _) => Changed();
        create.Click += (_, _) =>
        {
            if (!valid || drafts.Count >= 50) return;
            int number = 1; string title;
            do { title = WorkflowText("Mon thème ", "My theme ") + number++; } while (AppearanceThemes.WithCustom(drafts).Any(x => x.French.Equals(title, StringComparison.OrdinalIgnoreCase) || x.English.Equals(title, StringComparison.OrdinalIgnoreCase)));
            current = CopyTheme(current) with { Id = "custom-" + Guid.NewGuid().ToString("N"), French = title, English = title };
            drafts.Add(current); Load(); name.Focus(FocusState.Programmatic); name.SelectAll();
        };
        delete.Click += (_, _) => { if (!Editable()) return; drafts.RemoveAll(x => x.Id == current.Id); current = drafts.LastOrDefault() ?? AppearanceThemes.All[0]; Load(); };
        dialog.PrimaryButtonClick += (_, e) => { Changed(); if (!valid) e.Cancel = true; };
        Load(); ObserveTextZoom(content);
        void Closed(object sender, WindowEventArgs args) => dialog.Hide();
        owner.Closed += Closed;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary ? (drafts.Select(CopyTheme).ToList(), current.Id) : null;
        }
        finally { owner.Closed -= Closed; }
    }
}
