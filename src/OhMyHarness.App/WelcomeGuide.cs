using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OhMyHarness.Core;
using static OhMyHarness.App.UiText;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    bool welcomeOpen;

    async Task ShowWelcomeAsync()
    {
        if (welcomeOpen || root.XamlRoot == null) return;
        welcomeOpen = true;
        var initial = FeatureSettings.Read(state.FeaturesJson);
        var chosenTheme = AppearanceThemes.Get(initial.Theme, initial.CustomThemes);
        var providerForm = BuildWelcomeProvider();
        bool finished = false;
        try
        {
            int step = 0;
            bool saving = false;
            var dialog = new ContentDialog { XamlRoot = root.XamlRoot, RequestedTheme = root.RequestedTheme,
                Title = WorkflowText("Bienvenue dans ", "Welcome to ") + DisplayApplicationName,
                PrimaryButtonText = WorkflowText("Suivant", "Next"), CloseButtonText = WorkflowText("Plus tard", "Later"), DefaultButton = ContentDialogButton.Primary };
            dialog.Closing += (_, args) => { if (saving) args.Cancel = true; };
            var body = new StackPanel { Spacing = 20 };
            var steps = new Grid { ColumnSpacing = 8 };
            var stepLabels = new[] { T("Thème"), WorkflowText("Fournisseur", "Provider"), "Skills" };
            var stepChips = new List<Border>();
            for (int i = 0; i < 3; i++)
            {
                steps.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                var chip = new Border { Padding = new(10, 8, 10, 8), CornerRadius = new(8), BorderThickness = new(1),
                    Child = new TextBlock { Text = $"{i + 1} · {stepLabels[i]}", FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Primary } };
                Grid.SetColumn(chip, i); steps.Children.Add(chip); stepChips.Add(chip);
            }
            body.Children.Add(steps);
            var layout = new Grid { ColumnSpacing = 24 };
            layout.ColumnDefinitions.Add(new() { Width = new(244) });
            layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var hero = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
            var picture = new Image { Height = 180, Stretch = Stretch.Uniform };
            var heroTitle = new TextBlock { FontSize = 23, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Primary,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var heroCopy = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary };
            hero.Children.Add(picture); hero.Children.Add(heroTitle); hero.Children.Add(heroCopy);
            layout.Children.Add(hero);
            var compactPicture = new Image { Height = 125, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
            body.Children.Add(compactPicture);
            var viewer = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetColumn(viewer, 1); layout.Children.Add(viewer); body.Children.Add(layout);
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Resource("ToolMessageErrorStrokeBrush"), FontSize = 13,
                Visibility = Visibility.Collapsed, IsTextSelectionEnabled = true };
            body.Children.Add(error);
            dialog.Content = body;
            void Error(string message) { error.Text = message; error.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible; }

            var themePage = new StackPanel { Spacing = 16 };
            themePage.Children.Add(WelcomeCopy(WorkflowText("Choisissez l’ambiance qui vous convient. L’aperçu s’applique tout de suite ; vous pourrez encore changer de thème dans Apparence.",
                "Choose the look you prefer. Preview it immediately; you can change it again in Appearance.")));
            var themes = new ComboBox { Header = T("Thème"), HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var theme in AppearanceThemes.WithCustom(initial.CustomThemes))
            {
                var label = new TextBlock { Text = WorkflowText(theme.French, theme.English), VerticalAlignment = VerticalAlignment.Center };
                var item = new ComboBoxItem { Tag = theme, Content = Row(FluentDesign.Icon(theme.Dark ? "\uE708" : "\uE706"), label) };
                themes.Items.Add(item);
                if (theme.Id == chosenTheme.Id) themes.SelectedItem = item;
            }
            var palette = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            void RefreshPalette()
            {
                palette.Children.Clear();
                foreach (var color in new[] { chosenTheme.Background, chosenTheme.Surface, chosenTheme.Text, chosenTheme.Muted, chosenTheme.Accent })
                {
                    var brush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, Convert.ToByte(color.Substring(1, 2), 16), Convert.ToByte(color.Substring(3, 2), 16), Convert.ToByte(color.Substring(5, 2), 16)));
                    var swatch = new Border { Width = 32, Height = 32, Background = brush, CornerRadius = new(10), BorderBrush = FluentDesign.Stroke, BorderThickness = new(1) };
                    ToolTipService.SetToolTip(swatch, color); palette.Children.Add(swatch);
                }
            }
            RefreshPalette();
            themePage.Children.Add(themes); themePage.Children.Add(palette);
            themePage.Children.Add(FluentDesign.Surface(WelcomeCopy(WorkflowText("Astuce : Apparence permet aussi de choisir vos polices, votre logo et de créer un thème sur mesure.",
                "Tip: Appearance also lets you choose fonts, customize your logo and create your own theme.")), 14));

            var providerPage = new StackPanel { Spacing = 12 };
            providerPage.Children.Add(WelcomeCopy(WorkflowText("Le fournisseur donne accès aux modèles qui répondront dans vos conversations. Choisissez une connexion, renseignez sa clé si nécessaire, puis sélectionnez un modèle.",
                "Your provider gives access to the models used in chats. Choose a connection, enter a key if needed, then select a model.")));
            var skipProvider = new CheckBox { Content = WorkflowText("Je configurerai mon fournisseur plus tard", "I'll set up my provider later") };
            providerPage.Children.Add(skipProvider); providerPage.Children.Add(providerForm.Panel);
            skipProvider.Click += (_, _) => providerForm.Panel.IsEnabled = skipProvider.IsChecked != true;
            providerForm.BusyChanged = busy => { dialog.IsPrimaryButtonEnabled = !busy; dialog.IsSecondaryButtonEnabled = !busy; skipProvider.IsEnabled = !busy; };

            var skillPage = new StackPanel { Spacing = 12 };
            skillPage.Children.Add(WelcomeCopy(WorkflowText("Les skills donnent des capacités spécialisées au modèle. Activez celles qui vous seront utiles ; les opérations restent soumises à vos autorisations. Vous pourrez ajuster cette sélection dans Réglages ou dans le menu ＋ du chat.",
                "Skills give the model specialized capabilities. Enable what you need; operations still follow your permissions. Adjust this selection later in Settings or the chat's ＋ menu.")));
            var available = Skills.Available(project?.GetSourceFolders(), project?.Id ?? 0).ToArray();
            var checks = new Dictionary<string, CheckBox>();
            var selectedCount = WelcomeCopy("");
            void Count() => selectedCount.Text = WorkflowText($"{checks.Values.Count(x => x.IsChecked == true)} skills activés", $"{checks.Values.Count(x => x.IsChecked == true)} skills enabled");
            var essentials = new MenuFlyoutItem { Text = WorkflowText("Essentiels", "Essentials") };
            var coding = new MenuFlyoutItem { Text = WorkflowText("Développement", "Development") };
            var clear = new MenuFlyoutItem { Text = WorkflowText("Tout désactiver", "Disable all") };
            void Preset(params string[] ids) { foreach (var pair in checks) pair.Value.IsChecked = ids.Contains(pair.Key); Count(); }
            essentials.Click += (_, _) => Preset("sources", "web", "planning", "summary");
            coding.Click += (_, _) => Preset(GitTools.SkillId, FileIndexTools.SkillId, "sources", "write_sources", "code_search", "patch_sources", "terminal", "review", "planning", "web");
            clear.Click += (_, _) => Preset();
            var presetMenu = new MenuFlyout(); presetMenu.Items.Add(essentials); presetMenu.Items.Add(coding); presetMenu.Items.Add(clear);
            skillPage.Children.Add(new DropDownButton { Content = WorkflowText("Presets de skills", "Skill presets"), Flyout = presetMenu }); skillPage.Children.Add(selectedCount);
            foreach (var skill in available)
            {
                var check = new CheckBox { Content = SkillLabel(skill), IsChecked = Skills.Enabled(state.EnabledSkills, skill.Id),
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(check, WorkflowText(skill.FrenchName, skill.EnglishName));
                check.Click += (_, _) => Count(); checks[skill.Id] = check;
                var card = new StackPanel { Spacing = 4 }; card.Children.Add(check);
                var description = WelcomeCopy(WorkflowText(skill.FrenchDescription, skill.EnglishDescription)); description.FontSize = 12; description.Margin = new(32, 0, 0, 0);
                card.Children.Add(description); skillPage.Children.Add(FluentDesign.Surface(card, 10));
            }
            Count();
            var gitRead = new CheckBox { Content = WorkflowText("Lecture Git", "Git reading"), IsChecked = !Skills.Enabled(state.EnabledSkills, GitTools.DisableRead) };
            var gitWrite = new CheckBox { Content = WorkflowText("Écriture Git", "Git writing"), IsChecked = !Skills.Enabled(state.EnabledSkills, GitTools.DisableWrite) };
            skillPage.Children.Add(new Expander { Header = WorkflowText("Autorisations Git", "Git permissions"), Content = Row(gitRead, gitWrite),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            var pages = new[] { themePage, providerPage, skillPage };
            var heroTitles = new[] { ("Votre espace, vos couleurs.", "Your space, your colors."), ("Votre premier modèle.", "Your first model."), ("Votre boîte à outils.", "Your toolkit.") };
            var heroTexts = new[] {
                ("Clair, sombre ou sur mesure : commencez par vous sentir chez vous.", "Light, dark or custom: start by making yourself at home."),
                ("Une URL, une clé, un modèle : votre première conversation est à portée de main.", "A URL, a key, a model: your first chat is within reach."),
                ("Recherche, code, mémoire… Composez la boîte à outils qui vous ressemble.", "Research, code, memory… Build the toolkit that suits you.") };
            void Render()
            {
                Error("");
                for (int i = 0; i < stepChips.Count; i++)
                {
                    stepChips[i].Background = FluentDesign.Resource(i == step ? "ControlSelectedBrush" : "TransparentBrush");
                    stepChips[i].BorderBrush = i == step ? FluentDesign.Resource("AccentFillColorDefaultBrush") : FluentDesign.Stroke;
                }
                var image = LogoImage(WelcomeIllustrations.Draw(step, chosenTheme)); picture.Source = image; compactPicture.Source = image;
                heroTitle.Text = WorkflowText(heroTitles[step].Item1, heroTitles[step].Item2);
                heroCopy.Text = WorkflowText(heroTexts[step].Item1, heroTexts[step].Item2);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picture, heroCopy.Text);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(compactPicture, heroCopy.Text);
                viewer.Content = pages[step]; viewer.ChangeView(null, 0, null);
                dialog.PrimaryButtonText = step == 2 ? WorkflowText("Commencer", "Get started") : WorkflowText("Suivant", "Next");
                dialog.SecondaryButtonText = step == 0 ? "" : WorkflowText("Précédent", "Previous");
                AppTypography.Apply(body); TextZoom.Apply(body);
            }
            themes.SelectionChanged += (_, _) =>
            {
                if (themes.SelectedItem is not ComboBoxItem { Tag: AppearanceTheme theme }) return;
                chosenTheme = theme; ApplyTheme(theme.Id, initial.CustomThemes); dialog.RequestedTheme = root.RequestedTheme; RefreshPalette(); Render();
            };
            void Resize()
            {
                body.Width = Math.Clamp(root.ActualWidth - 110, 220, 860);
                var wide = body.Width >= 640;
                layout.ColumnDefinitions[0].Width = new(wide ? 244 : 0); layout.ColumnSpacing = wide ? 24 : 0;
                hero.Visibility = wide ? Visibility.Visible : Visibility.Collapsed; compactPicture.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
                viewer.MaxHeight = Math.Clamp(root.ActualHeight - (wide ? 280 : 390), 150, 500);
                dialog.Resources["ContentDialogMaxWidth"] = body.Width + 60;
            }
            SizeChangedEventHandler resize = (_, _) => Resize();
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                args.Cancel = true;
                if (providerForm.Busy) return;
                var deferral = args.GetDeferral();
                try
                {
                    if (step == 1 && skipProvider.IsChecked != true && providerForm.Validate() is { } invalid) { Error(invalid); return; }
                    if (step < 2) { step++; Render(); return; }
                    saving = true; dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = false;
                    var selection = state.EnabledSkills.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
                    foreach (var pair in checks) { if (pair.Value.IsChecked == true) selection.Add(pair.Key); else selection.Remove(pair.Key); }
                    selection.Remove(GitTools.DisableRead); selection.Remove(GitTools.DisableWrite);
                    if (gitRead.IsChecked != true) selection.Add(GitTools.DisableRead);
                    if (gitWrite.IsChecked != true) selection.Add(GitTools.DisableWrite);
                    await SaveWelcomeAsync(chosenTheme.Id, string.Join(',', selection), skipProvider.IsChecked == true ? null : providerForm.Read());
                    finished = true; saving = false; dialog.Hide();
                }
                catch (Exception ex) { Error(ex.Message); }
                finally { saving = false; dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = true; deferral.Complete(); }
            };
            dialog.SecondaryButtonClick += (_, args) => { args.Cancel = true; if (!providerForm.Busy && step > 0) { step--; Render(); } };
            Resize(); Render(); root.SizeChanged += resize;
            try { await ShowDialogAsync(dialog); }
            finally { root.SizeChanged -= resize; providerForm.Cancel(); }
            if (!finished)
            {
                var config = FeatureSettings.Read(state.FeaturesJson); config.WelcomeCompleted = true;
                state.FeaturesJson = config.Json(); await db.SaveChangesAsync();
            }
            else ShowStatus(WorkflowText("Configuration enregistrée. Bonne découverte !", "Setup saved. Enjoy exploring!"), StatusKind.Notice);
        }
        finally
        {
            providerForm.Cancel(); welcomeOpen = false;
            if (!finished) ApplyTheme(FeatureSettings.Read(state.FeaturesJson).Theme);
        }
    }

    static TextBlock WelcomeCopy(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary, FontSize = 14 };

    async Task SaveWelcomeAsync(string theme, string skills, ProviderDraft? draft)
    {
        // Prepare the encrypted credential before touching tracked entities. Save all choices together.
        Provider? prepared = null;
        if (draft != null)
        {
            prepared = new Provider(); ApplyProviderDraft(draft, prepared);
            ProviderModels.Select(prepared, ProviderModels.Visible(prepared).Append(prepared.Model));
        }
        var previousFeatures = state.FeaturesJson; var previousSkills = state.EnabledSkills; var previousProvider = state.ProviderId;
        var existing = draft == null ? null : db.Providers.Local.FirstOrDefault(x => x.Id == draft.Id);
        var previousValues = existing == null ? null : db.Entry(existing).CurrentValues.Clone();
        var previousOriginal = existing == null ? null : db.Entry(existing).OriginalValues.Clone();
        var previousEntityState = existing == null ? EntityState.Detached : db.Entry(existing).State;
        var previousStateOriginal = db.Entry(state).OriginalValues.Clone();
        var previousStateStatus = db.Entry(state).State;
        Provider? saved = existing;
        try
        {
            if (prepared != null)
            {
                if (saved == null) { saved = prepared; db.Providers.Add(saved); }
                else { prepared.Id = saved.Id; db.Entry(saved).CurrentValues.SetValues(prepared); }
                state.ProviderId = saved.Id;
            }
            var config = FeatureSettings.Read(state.FeaturesJson); config.Theme = theme; config.WelcomeCompleted = true;
            state.FeaturesJson = config.Json(); state.EnabledSkills = skills;
            // A new provider receives its identity before the state references it, in the same transaction.
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.SaveChangesAsync();
            if (saved != null && state.ProviderId != saved.Id) { state.ProviderId = saved.Id; await db.SaveChangesAsync(); }
            await transaction.CommitAsync();
        }
        catch
        {
            state.FeaturesJson = previousFeatures; state.EnabledSkills = previousSkills; state.ProviderId = previousProvider;
            db.Entry(state).OriginalValues.SetValues(previousStateOriginal); db.Entry(state).State = previousStateStatus;
            if (existing != null && previousValues != null && previousOriginal != null)
            {
                db.Entry(existing).CurrentValues.SetValues(previousValues); db.Entry(existing).OriginalValues.SetValues(previousOriginal);
                db.Entry(existing).State = previousEntityState;
            }
            else if (saved != null) db.Entry(saved).State = EntityState.Detached;
            throw;
        }
        var wasLoading = loading;
        try
        {
            loading = true;
            var list = db.Providers.Local.Where(x => db.Entry(x).State != EntityState.Deleted).OrderBy(x => x.Id).ToList();
            provider = saved ?? list.FirstOrDefault(x => x.Id == state.ProviderId) ?? list.FirstOrDefault();
            providers.ItemsSource = list; providers.SelectedItem = provider;
        }
        finally { loading = wasLoading; }
        ApplyAppearance(); UpdateProvider(); PopulateModelSelector(); PopulateThinkingSelector();
    }
}
