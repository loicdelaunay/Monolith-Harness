using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly DropDownButton modelOptionsButton = new() { HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, MinHeight = 44, Padding = new(10, 5, 10, 5) };
    readonly TextBlock selectedModelName = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None,
        Foreground = FluentDesign.Primary, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock selectedThinking = new() { FontSize = 11, Foreground = FluentDesign.Primary };
    readonly TextBlock modelPickerProvider = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary };
    readonly TextBlock thinkingDescription = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary };
    readonly ProgressRing modelRefreshProgress = new() { Width = 18, Height = 18 };
    readonly ModelContextEditor modelContextEditor = new(compact: true);
    object? modelRefreshIcon;
    bool refreshingModels;
    Flyout? modelOptionsFlyout;
    bool chooseModelOnOpen;

    DropDownButton BuildModelOptions()
    {
        var selection = new Grid { ColumnSpacing = 8, RowSpacing = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        selection.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        selection.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        selection.RowDefinitions.Add(new() { Height = GridLength.Auto });
        selection.RowDefinitions.Add(new() { Height = GridLength.Auto });
        selection.Children.Add(selectedModelName);
        modelProviderSubtitle.FontSize = 10; modelProviderSubtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetRow(modelProviderSubtitle, 1); selection.Children.Add(modelProviderSubtitle);
        var badge = new Border { Child = selectedThinking, CornerRadius = new(8), Padding = new(7, 3, 7, 3),
            Background = FluentDesign.Resource("ControlSelectedBrush"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(badge, 1); Grid.SetRowSpan(badge, 2); selection.Children.Add(badge);
        selection.SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < 280 * TextZoom.ForWindow(root) / 100d;
            Grid.SetColumnSpan(selectedModelName, narrow ? 2 : 1);
            Grid.SetRow(badge, narrow ? 1 : 0); Grid.SetRowSpan(badge, narrow ? 1 : 2);
        };
        modelOptionsButton.Content = selection;
        var panel = new Grid { RowSpacing = 10 };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new TextBlock { FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = FluentDesign.Primary, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(heading);
        var sections = new StackPanel { Spacing = 10 };
        var body = new ScrollViewer { Content = sections, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(body, 1); panel.Children.Add(body);
        var modelSection = new StackPanel { Spacing = 4 };
        var modelHeader = new Grid { ColumnSpacing = 10 };
        modelHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        modelHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        modelHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var modelHeading = new TextBlock { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = FluentDesign.Primary, VerticalAlignment = VerticalAlignment.Center };
        modelHeader.Children.Add(modelHeading);
        var providerSettings = new Button { Name = "ProviderSettingsButton", Width = 32, Height = 32, Padding = new(0) };
        FluentDesign.IconButton(providerSettings, "\uE713", WorkflowText("Réglages des fournisseurs", "Provider settings"), false);
        Grid.SetColumn(providerSettings, 1); modelHeader.Children.Add(providerSettings);
        providerSettings.Click += async (_, _) =>
        {
            modelOptionsFlyout?.Hide();
            await Guard(async () => await Settings(showProviders: true));
        };
        refreshModelsBtn.Width = refreshModelsBtn.Height = 32;
        FluentDesign.IconButton(refreshModelsBtn, "\uE72C", WorkflowText("Actualiser les modèles", "Refresh models"), false);
        modelRefreshIcon = refreshModelsBtn.Content;
        Grid.SetColumn(refreshModelsBtn, 2); modelHeader.Children.Add(refreshModelsBtn);
        modelSelector.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <StackPanel Spacing="3" Padding="0,4">
                    <TextBlock Text="{Binding Model}" FontSize="14" TextWrapping="Wrap" TextTrimming="None" />
                    <TextBlock Text="{Binding ProviderName}" FontSize="11" Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
                </StackPanel>
            </DataTemplate>
            """);
        modelSection.Children.Add(modelHeader); modelSection.Children.Add(modelSelector);
        // A grid lets long connection names wrap without forcing a horizontal scrollbar.
        var providerRow = new Grid { ColumnSpacing = 6 };
        providerRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        providerRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        providerRow.Children.Add(new FontIcon { Glyph = "\uE968", FontSize = 12, Foreground = FluentDesign.Secondary,
            VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(modelPickerProvider, 1); providerRow.Children.Add(modelPickerProvider);
        modelSection.Children.Add(providerRow); sections.Children.Add(modelSection);
        var thinkingSection = new Grid { ColumnSpacing = 10 };
        thinkingSection.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        thinkingSection.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var thinkingHeading = new TextBlock { FontSize = 13, Foreground = FluentDesign.Primary, VerticalAlignment = VerticalAlignment.Center };
        thinkingSelector.MinHeight = 32; thinkingSelector.Padding = new(8, 3, 8, 3); thinkingSelector.FontSize = 13;
        thinkingSection.Children.Add(thinkingHeading); Grid.SetColumn(thinkingSelector, 1); thinkingSection.Children.Add(thinkingSelector);
        sections.Children.Add(thinkingSection);
        sections.Children.Add(modelContextEditor);
        var done = new Button { HorizontalAlignment = HorizontalAlignment.Right, MinHeight = 32, Padding = new(12, 4, 12, 4), FontSize = 13,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        Grid.SetRow(done, 2); panel.Children.Add(done);
        TextZoom.Observe(panel);
        modelOptionsFlyout = new Flyout { Content = panel };
        modelOptionsFlyout.FlyoutPresenterStyle = new Style { TargetType = typeof(FlyoutPresenter) };
        modelOptionsFlyout.FlyoutPresenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12)));
        modelOptionsFlyout.FlyoutPresenterStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, double.PositiveInfinity));
        modelOptionsButton.Flyout = modelOptionsFlyout;
        modelOptionsFlyout.Opening += (_, _) =>
        {
            var scale = TextZoom.ForWindow(root) / 100d;
            panel.Width = Math.Max(120, Math.Min(350 * scale, root.ActualWidth - 48));
            panel.MaxHeight = Math.Max(180, root.ActualHeight - 96);
            modelSelector.MaxSuggestionListHeight = Math.Max(100, Math.Min(260 * scale, root.ActualHeight / 2));
            heading.Text = WorkflowText("Modèle et réflexion", "Model and thinking");
            modelHeading.Text = WorkflowText("Modèle", "Model");
            modelSelector.RefreshLanguage();
            thinkingSelector.Header = null;
            thinkingHeading.Text = WorkflowText("Réflexion", "Thinking");
            done.Content = WorkflowText("Terminer", "Done");
            var providerSettingsLabel = WorkflowText("Réglages des fournisseurs", "Provider settings");
            ToolTipService.SetToolTip(providerSettings, providerSettingsLabel);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(providerSettings, providerSettingsLabel);
            ToolTipService.SetToolTip(refreshModelsBtn, WorkflowText("Actualiser les modèles", "Refresh models"));
            RefreshModelOptions();
            TextZoom.SetWindow(panel, TextZoom.ForWindow(root));
        };
        modelOptionsFlyout.Opened += (_, _) =>
        {
            if (!chooseModelOnOpen) return;
            chooseModelOnOpen = false;
            modelSelector.OpenSuggestions();
        };
        modelOptionsFlyout.Closed += (_, _) => { chooseModelOnOpen = false; modelSelector.ResetSearch(); };
        done.Click += (_, _) => modelOptionsFlyout.Hide();
        return modelOptionsButton;
    }

    void OpenModelOptions(bool chooseModel = false)
    {
        chooseModelOnOpen = chooseModel;
        modelOptionsFlyout?.ShowAt(modelOptionsButton);
    }

    void RefreshModelOptions()
    {
        modelContextEditor.Bind(provider, async _ => await Guard(async () => { await db.SaveChangesAsync(); RefreshContextInfo(); }));
        var choice = modelSelector.SelectedItem as ModelChoice;
        selectedModelName.Text = choice?.Model ?? WorkflowText("Choisir un modèle", "Choose a model");
        selectedThinking.Text = thinkingSelector.SelectedItem?.ToString() ?? "🧠 Auto";
        modelPickerProvider.Text = choice?.ProviderName ?? WorkflowText("Aucun modèle sélectionné", "No model selected");
        ToolTipService.SetToolTip(modelPickerProvider, modelPickerProvider.Text);
        RefreshInfoHeading();
        thinkingDescription.Text = ChatInteraction
            ? WorkflowText("Le mode Chat désactive le raisonnement. Choisissez un modèle qui permet sa désactivation.", "Chat mode disables reasoning. Choose a model that supports disabling it.")
            : thinkingSelector.SelectedIndex switch
        {
            1 => WorkflowText("Demande une réflexion courte pour privilégier la rapidité.", "Requests brief thinking to favor speed."),
            2 => WorkflowText("Demande un équilibre entre réflexion et rapidité.", "Requests a balance between thinking and speed."),
            3 => WorkflowText("Demande une réflexion approfondie pour les tâches complexes.", "Requests deeper thinking for complex tasks."),
            4 => WorkflowText("Demande une réponse sans raisonnement étendu.", "Requests a response without extended reasoning."),
            _ => WorkflowText("Utilise le niveau de réflexion par défaut du modèle.", "Uses the model's default thinking level.")
        };
        ToolTipService.SetToolTip(thinkingDescription, WorkflowText("La prise en charge des niveaux de réflexion dépend du fournisseur et du modèle.",
            "Support for thinking levels depends on the provider and model."));
        ToolTipService.SetToolTip(thinkingSelector, thinkingDescription.Text + "\n" + WorkflowText("La prise en charge des niveaux de réflexion dépend du fournisseur et du modèle.",
            "Support for thinking levels depends on the provider and model."));
        var details = selectedModelName.Text + (choice == null ? "" : "\n" + choice.ProviderName) + "\n" +
            WorkflowText("Réflexion : ", "Thinking: ") + selectedThinking.Text;
        ToolTipService.SetToolTip(modelOptionsButton, details);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(modelOptionsButton,
            WorkflowText("Choisir le modèle et sa réflexion", "Choose model and thinking") + " · " + details);
    }

    void SetModelRefreshBusy(bool busy)
    {
        refreshingModels = busy;
        modelRefreshProgress.IsActive = busy;
        refreshModelsBtn.Content = busy ? modelRefreshProgress : modelRefreshIcon;
        refreshModelsBtn.IsEnabled = !busy && provider != null && !provider.IsComposite && ActiveRun == null;
    }
}
