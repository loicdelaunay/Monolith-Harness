using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly DropDownButton modelOptionsButton = new() { HorizontalAlignment = HorizontalAlignment.Right,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, MinHeight = 38,
        MaxWidth = 240, Padding = new(8, 4, 8, 4), CornerRadius = new(8), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0) };
    readonly TextBlock selectedModelName = new() { FontSize = 12, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
        Foreground = FluentDesign.Primary, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock selectedThinking = new() { FontSize = 11, Foreground = FluentDesign.Primary };
    readonly TextBlock thinkingDescription = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary };
    readonly ProviderPicker providerSelector = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly SegmentedOptions thinkingSegments = new();
    FrameworkElement? modelOptionsFooter, modelHeaderActions;
    Button? modelResetButton;
    bool updatingProviderSelector, changingModelProvider;
    readonly ProgressRing modelRefreshProgress = new() { Width = 18, Height = 18 };
    readonly ModelContextEditor modelContextEditor = new(compact: true);
    object? modelRefreshIcon;
    bool refreshingModels;
    Flyout? modelOptionsFlyout;
    bool chooseModelOnOpen;

    void BuildModelOptions()
    {
        var selection = new Grid { ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        selection.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        selection.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        selection.Children.Add(selectedModelName);
        var thinkingSummary = Row(composerThinkingIcon, composerThinkingMarker); thinkingSummary.Spacing = 2;
        var badge = new Border { Child = thinkingSummary, CornerRadius = new(6), Padding = new(4, 2, 4, 2),
            Background = FluentDesign.Resource("ControlSelectedBrush"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(badge, 1); selection.Children.Add(badge); modelOptionsButton.Content = selection;
        var panel = modelOptionsPanel = new Grid { RowSpacing = 12 };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new TextBlock { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = FluentDesign.Primary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(BuildQuickModelHeader(heading));
        var providerSettings = new Button { Name = "ProviderSettingsButton", Width = 34, Height = 34, Padding = new(0) };
        FluentDesign.IconButton(providerSettings, "\uE713", WorkflowText("Réglages des fournisseurs", "Provider settings"), false);
        providerSettings.Click += async (_, _) =>
        {
            modelOptionsFlyout?.Hide(); await Guard(async () => await Settings(showProviders: true));
        };
        var more = new Button { Width = 34, Height = 34, Padding = new(0) };
        FluentDesign.IconButton(more, "\uE712", WorkflowText("Options du modèle", "Model options"), false);
        var refreshLabel = new TextBlock { FontSize = 13 };
        var refreshContent = Row(FluentDesign.Icon("\uE72C", 15), refreshLabel);
        refreshModelsBtn.MinHeight = 36; refreshModelsBtn.Padding = new(10, 6, 10, 6);
        refreshModelsBtn.HorizontalContentAlignment = HorizontalAlignment.Left;
        refreshModelsBtn.Content = refreshContent; modelRefreshIcon = refreshContent;
        var moreFlyout = new Flyout { Content = refreshModelsBtn }; more.Flyout = moreFlyout;
        refreshModelsBtn.Click += (_, _) => moreFlyout.Hide();
        modelHeaderActions = Row(providerSettings, more); Grid.SetColumn(modelHeaderActions, 1); header.Children.Add(modelHeaderActions);
        panel.Children.Add(header);

        var sections = new StackPanel { Spacing = 14 };
        var body = new ScrollViewer { Content = sections, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(body, 1); panel.Children.Add(body); sections.Children.Add(BuildQuickLevelPicker());
        var modelSection = modelAdvancedSection = new StackPanel { Spacing = 12 };
        var providerHeading = ModelOptionHeading(); var modelHeading = ModelOptionHeading();
        var providerSection = new StackPanel { Spacing = 6 };
        providerSection.Children.Add(providerHeading); providerSection.Children.Add(providerSelector); modelSection.Children.Add(providerSection);
        var modelField = new StackPanel { Spacing = 6 };
        modelField.Children.Add(modelHeading); modelField.Children.Add(modelSelector); modelSection.Children.Add(modelField);
        providerSelector.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <TextBlock Text="{Binding Caption}" FontSize="14" Padding="0,4" TextWrapping="Wrap" Foreground="{ThemeResource TextFillColorPrimaryBrush}" />
            </DataTemplate>
            """);
        modelSelector.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <TextBlock Text="{Binding Model}" FontSize="14" Padding="0,4" TextWrapping="Wrap" Foreground="{ThemeResource TextFillColorPrimaryBrush}" />
            </DataTemplate>
            """);
        sections.Children.Add(modelSection);
        var thinkingSection = thinkingAdvancedSection = new Grid { RowSpacing = 8 };
        thinkingSection.RowDefinitions.Add(new() { Height = GridLength.Auto });
        thinkingSection.RowDefinitions.Add(new() { Height = GridLength.Auto });
        thinkingSection.RowDefinitions.Add(new() { Height = GridLength.Auto });
        thinkingSection.Children.Add(new Border { Height = 1, Background = FluentDesign.Stroke, Margin = new(0, 0, 0, 4) });
        var thinkingHeading = ModelOptionHeading(); Grid.SetRow(thinkingHeading, 1); thinkingSection.Children.Add(thinkingHeading);
        Grid.SetRow(thinkingSegments, 2); thinkingSection.Children.Add(thinkingSegments); sections.Children.Add(thinkingSection);
        thinkingSegments.SetLabels("Auto", "Faible", "Moyenne", "Élevée", "Sans");
        thinkingSegments.SelectionChanged += (_, _) =>
        {
            if (loading || updatingThinkingSelector || applyingQuickLevel || ActiveRun != null) return;
            if (thinkingSegments.SelectedIndex != thinkingSelector.SelectedIndex) thinkingSelector.SelectedIndex = thinkingSegments.SelectedIndex;
        };
        sections.Children.Add(modelContextEditor);

        var done = modelDoneButton = new Button { HorizontalAlignment = HorizontalAlignment.Right, MinHeight = 36,
            Padding = new(14, 6, 14, 6), FontSize = 14, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var resetLabel = new TextBlock { FontSize = 11, Foreground = FluentDesign.Secondary, TextWrapping = TextWrapping.Wrap };
        var reset = modelResetButton = new Button { Content = resetLabel, MinWidth = 0, MinHeight = 36, Padding = new(0, 6, 8, 6),
            HorizontalContentAlignment = HorizontalAlignment.Left, BorderThickness = new(0), Background = FluentDesign.Resource("TransparentBrush") };
        reset.Click += async (_, _) => await Guard(async () =>
        {
            if (provider == null || ActiveRun != null || applyingQuickLevel) return;
            modelContextEditor.ResetToAutomatic(notify: false);
            if (ChatInteraction) state.ChatThinkingLevel = "none"; else state.ThinkingLevel = "auto";
            PopulateThinkingSelector(); await db.SaveChangesAsync(); RefreshContextInfo(); RefreshModelOptions();
        });
        var footer = new Grid { ColumnSpacing = 12 };
        footer.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.Children.Add(reset); Grid.SetColumn(done, 1); footer.Children.Add(done);
        modelOptionsFooter = new Border { Child = footer, BorderThickness = new(0, 1, 0, 0), BorderBrush = FluentDesign.Stroke, Padding = new(0, 12, 0, 0) };
        Grid.SetRow(modelOptionsFooter, 2); panel.Children.Add(modelOptionsFooter);
        TextZoom.Observe(panel);
        modelOptionsFlyout = new Flyout { Content = panel, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedRight };
        modelOptionsFlyout.FlyoutPresenterStyle = new Style { TargetType = typeof(FlyoutPresenter) };
        modelOptionsFlyout.FlyoutPresenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16)));
        modelOptionsFlyout.FlyoutPresenterStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, double.PositiveInfinity));
        modelOptionsButton.Flyout = modelOptionsFlyout;
        modelOptionsFlyout.Opening += (_, _) =>
        {
            modelOptionsIsOpen = true; previewQuickLevelId = null;
            var scale = TextZoom.ForWindow(root) / 100d; ResizeModelPicker();
            panel.MaxHeight = Math.Max(180, root.ActualHeight - 96);
            modelSelector.MaxSuggestionListHeight = providerSelector.MaxSuggestionListHeight = Math.Max(100, Math.Min(260 * scale, root.ActualHeight / 2));
            heading.Text = WorkflowText("Modèle et réflexion", "Model and thinking");
            providerHeading.Text = WorkflowText("Fournisseur", "Provider"); modelHeading.Text = WorkflowText("Modèle", "Model");
            modelSelector.RefreshLanguage(); providerSelector.RefreshLanguage();
            thinkingHeading.Text = WorkflowText("Réflexion", "Thinking");
            thinkingSegments.SetLabels("Auto", WorkflowText("Faible", "Low"), WorkflowText("Moyenne", "Medium"), WorkflowText("Élevée", "High"), WorkflowText("Sans", "Off"));
            done.Content = WorkflowText("Terminer", "Done");
            resetLabel.Text = WorkflowText("Rétablir les valeurs par défaut", "Restore defaults");
            ToolTipService.SetToolTip(reset, WorkflowText("Rétablir la réflexion et le contexte par défaut, sans changer le fournisseur ni le modèle.", "Restore default thinking and context without changing the provider or model."));
            refreshLabel.Text = WorkflowText("Actualiser les modèles", "Refresh models");
            var settingsLabel = WorkflowText("Réglages des fournisseurs", "Provider settings");
            ToolTipService.SetToolTip(providerSettings, settingsLabel);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(providerSettings, settingsLabel);
            var moreLabel = WorkflowText("Options du modèle", "Model options"); ToolTipService.SetToolTip(more, moreLabel);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(more, moreLabel);
            ToolTipService.SetToolTip(refreshModelsBtn, refreshLabel.Text);
            RefreshModelOptions(); TextZoom.SetWindow(panel, TextZoom.ForWindow(root));
        };
        modelOptionsFlyout.Opened += (_, _) =>
        {
            if (!chooseModelOnOpen) return; chooseModelOnOpen = false;
            if (providerSelector.SelectedItem == null) providerSelector.OpenSuggestions(); else modelSelector.OpenSuggestions();
        };
        modelOptionsFlyout.Closed += (_, _) =>
        {
            modelOptionsIsOpen = false; chooseModelOnOpen = false; modelSelector.ResetSearch(); providerSelector.ResetSearch(); moreFlyout.Hide();
        };
        done.Click += (_, _) => modelOptionsFlyout.Hide();
    }

    static TextBlock ModelOptionHeading() => new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = FluentDesign.Primary, VerticalAlignment = VerticalAlignment.Center };

    async void OpenModelOptions(bool chooseModel = false)
    {
        await Guard(async () =>
        {
            if (chooseModel) await SetQuickSelectionModeAsync("advanced");
            chooseModelOnOpen = chooseModel;
            modelOptionsFlyout?.ShowAt(modelOptionsButton);
        });
    }

    void RefreshModelOptions()
    {
        modelContextEditor.Bind(provider, async _ => await Guard(async () => { await db.SaveChangesAsync(); RefreshContextInfo(); }));
        var choice = modelSelector.SelectedItem as ModelChoice;
        selectedModelName.Text = choice?.Model ?? WorkflowText("Choisir un modèle", "Choose a model");
        selectedThinking.Text = thinkingSelector.SelectedItem?.ToString() ?? "🧠 Auto";
        thinkingSegments.SelectedIndex = thinkingSelector.SelectedIndex;
        thinkingDescription.Text = thinkingSelector.SelectedIndex switch
        {
            1 => WorkflowText("Demande une réflexion courte pour privilégier la rapidité.", "Requests brief thinking to favor speed."),
            2 => WorkflowText("Demande un équilibre entre réflexion et rapidité.", "Requests a balance between thinking and speed."),
            3 => WorkflowText("Demande une réflexion approfondie pour les tâches complexes.", "Requests deeper thinking for complex tasks."),
            4 => ChatInteraction
                ? WorkflowText("Réponse sans raisonnement étendu, sélectionnée par défaut en mode Chat.", "Response without extended reasoning, selected by default in Chat mode.")
                : WorkflowText("Demande une réponse sans raisonnement étendu.", "Requests a response without extended reasoning."),
            _ => WorkflowText("Utilise le niveau de réflexion par défaut du modèle.", "Uses the model's default thinking level.")
        };
        ToolTipService.SetToolTip(thinkingDescription, WorkflowText("La prise en charge des niveaux de réflexion dépend du fournisseur et du modèle.",
            "Support for thinking levels depends on the provider and model."));
        ToolTipService.SetToolTip(thinkingSelector, thinkingDescription.Text + "\n" + WorkflowText("La prise en charge des niveaux de réflexion dépend du fournisseur et du modèle.",
            "Support for thinking levels depends on the provider and model."));
        ToolTipService.SetToolTip(thinkingSegments, thinkingDescription.Text + "\n" + WorkflowText("La prise en charge dépend du fournisseur et du modèle.", "Support depends on the provider and model."));
        var details = selectedModelName.Text + (choice == null ? "" : "\n" + choice.ProviderName) + "\n" +
            WorkflowText("Réflexion : ", "Thinking: ") + selectedThinking.Text;
        ToolTipService.SetToolTip(modelOptionsButton, details);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(modelOptionsButton,
            WorkflowText("Choisir le modèle et sa réflexion", "Choose model and thinking") + " · " + details);
        RefreshQuickModelUi();
    }

    void SetModelRefreshBusy(bool busy)
    {
        refreshingModels = busy;
        modelRefreshProgress.IsActive = busy;
        refreshModelsBtn.Content = busy ? modelRefreshProgress : modelRefreshIcon;
        refreshModelsBtn.IsEnabled = !busy && provider != null && !provider.IsComposite && ActiveRun == null;
        RefreshQuickModelUi();
    }
}
