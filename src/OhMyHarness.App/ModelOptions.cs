using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;
using static OhMyHarness.App.UiText;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    readonly DropDownButton modelOptionsButton = new() { HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, MinHeight = 44, Padding = new(10, 5, 10, 5) };
    readonly TextBlock selectedModelName = new() { FontSize = 14, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None,
        Foreground = FluentDesign.Primary, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock selectedThinking = new() { FontSize = 11, Foreground = FluentDesign.Primary };
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
        var panel = new StackPanel { Spacing = 14 };
        var heading = new TextBlock { FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = FluentDesign.Primary };
        panel.Children.Add(heading);
        var modelRow = new Grid { ColumnSpacing = 8 };
        modelRow.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        modelRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        modelRow.Children.Add(modelSelector);
        refreshModelsBtn.VerticalAlignment = VerticalAlignment.Bottom;
        FluentDesign.IconButton(refreshModelsBtn, "\uE72C", T("Recharger les modèles de l’API"), false);
        Grid.SetColumn(refreshModelsBtn, 1); modelRow.Children.Add(refreshModelsBtn);
        modelSelector.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <StackPanel Spacing="2">
                    <TextBlock Text="{Binding Model}" FontSize="14" TextWrapping="Wrap" TextTrimming="None" />
                    <TextBlock Text="{Binding ProviderName}" FontSize="11" Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
                </StackPanel>
            </DataTemplate>
            """);
        panel.Children.Add(modelRow); panel.Children.Add(thinkingSelector);
        TextZoom.Observe(panel);
        modelOptionsFlyout = new Flyout { Content = panel };
        modelOptionsButton.Flyout = modelOptionsFlyout;
        modelOptionsFlyout.Opening += (_, _) =>
        {
            panel.Width = Math.Max(180, Math.Min(440, root.ActualWidth - 90));
            heading.Text = WorkflowText("Modèle et réflexion", "Model and thinking");
            modelSelector.Header = WorkflowText("Modèle", "Model");
            thinkingSelector.Header = WorkflowText("Niveau de réflexion", "Thinking level");
            ToolTipService.SetToolTip(refreshModelsBtn, T("Recharger les modèles de l’API"));
            TextZoom.SetWindow(panel, TextZoom.ForWindow(root));
        };
        modelOptionsFlyout.Opened += (_, _) =>
        {
            if (!chooseModelOnOpen) return;
            chooseModelOnOpen = false;
            modelSelector.Focus(FocusState.Programmatic); modelSelector.IsDropDownOpen = true;
        };
        return modelOptionsButton;
    }

    void OpenModelOptions(bool chooseModel = false)
    {
        chooseModelOnOpen = chooseModel;
        modelOptionsFlyout?.ShowAt(modelOptionsButton);
    }

    void RefreshModelOptions()
    {
        var choice = modelSelector.SelectedItem as ModelChoice;
        selectedModelName.Text = choice?.Model ?? WorkflowText("Choisir un modèle", "Choose a model");
        selectedThinking.Text = thinkingSelector.SelectedItem?.ToString() ?? "🧠 Auto";
        var details = selectedModelName.Text + (choice == null ? "" : "\n" + choice.ProviderName) + "\n" +
            WorkflowText("Réflexion : ", "Thinking: ") + selectedThinking.Text;
        ToolTipService.SetToolTip(modelOptionsButton, details);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(modelOptionsButton,
            WorkflowText("Choisir le modèle et sa réflexion", "Choose model and thinking") + " · " + details);
    }
}
