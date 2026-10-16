using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    (UIElement Panel, Action<FeatureSettings> Save, Action RefreshTheme) BuildChatBackgroundSettings(
        Window owner, FeatureSettings initial, Func<AppearanceTheme> currentTheme)
    {
        var choices = ChatBackgroundStyles.All.Select(style =>
            new ChatBackgroundChoice(style.Id, WorkflowText(style.French, style.English))).ToList();
        var selector = new ComboBox
        {
            Header = WorkflowText("Animation de fond du chat", "Chat background animation"),
            ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedItem = choices.First(choice => choice.Id == ChatBackgroundStyles.Normalize(initial.ChatBackgroundAnimation))
        };
        var opacity = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1,
            Value = ChatBackgroundStyles.NormalizeOpacity(initial.ChatBackgroundOpacityPercent), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(opacity, WorkflowText("Opacité du fond du chat en pourcentage", "Chat background opacity percentage"));
        var opacityValue = Label("", 12); opacityValue.Foreground = FluentDesign.Secondary;
        opacityValue.HorizontalAlignment = HorizontalAlignment.Right; opacityValue.VerticalAlignment = VerticalAlignment.Center;
        var opacityHeader = new Grid();
        opacityHeader.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        opacityHeader.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        opacityHeader.Children.Add(Label(WorkflowText("Opacité", "Opacity"), 13));
        Grid.SetColumn(opacityValue, 1); opacityHeader.Children.Add(opacityValue);
        var opacityControls = new StackPanel { Spacing = 2 }; opacityControls.Children.Add(opacityHeader); opacityControls.Children.Add(opacity);
        SettingsSearch.Register(opacityControls, WorkflowText("Opacité du fond du chat", "Chat background opacity"), "0–100 %", opacity);

        var customColor = ChatBackgroundStyles.NormalizeColor(initial.ChatBackgroundColor) ?? currentTheme().Accent;
        var followTheme = new ToggleSwitch
        {
            Header = WorkflowText("Couleur de l’animation", "Animation color"),
            OnContent = WorkflowText("Suivre le thème", "Follow theme"), OffContent = WorkflowText("Personnalisée", "Custom"),
            IsOn = ChatBackgroundStyles.NormalizeColor(initial.ChatBackgroundColor) == null
        };
        var swatch = new Border { Width = 20, Height = 20, CornerRadius = new(4), BorderThickness = new(1), BorderBrush = FluentDesign.Stroke };
        var colorValue = Label(customColor, 13); colorValue.VerticalAlignment = VerticalAlignment.Center;
        var colorButton = new Button { Content = Row(swatch, colorValue), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(colorButton, WorkflowText("Choisir une couleur personnalisée", "Choose a custom color"));
        ToolTipService.SetToolTip(colorButton, WorkflowText("Sélecteur de couleur et saisie #RRGGBB", "Color picker and #RRGGBB input"));
        Flyout? colorFlyout = null;

        var previewView = new ChatBackgroundView();
        var previewLabel = Label("", 12);
        previewLabel.Margin = new(14); previewLabel.VerticalAlignment = VerticalAlignment.Bottom;
        previewLabel.Foreground = FluentDesign.Secondary; previewLabel.IsHitTestVisible = false;
        var previewContent = new Grid(); previewContent.Children.Add(previewView); previewContent.Children.Add(previewLabel);
        var preview = new Border { Child = previewContent, Height = 150, CornerRadius = new(12), BorderThickness = new(1),
            BorderBrush = FluentDesign.Stroke, Background = FluentDesign.Card };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(selector);
        panel.Children.Add(Label(WorkflowText("Désactivé par défaut. Réglez l’intensité et la couleur ; 0 % masque le fond et suspend ses animations. Les animations système désactivées figent les effets.",
            "Disabled by default. Adjust intensity and color; 0% hides the background and stops its animations. Disabling system animations leaves the effects still."), 12));
        panel.Children.Add(opacityControls); panel.Children.Add(followTheme); panel.Children.Add(colorButton); panel.Children.Add(preview);
        var surface = FluentDesign.Surface(panel, 16);
        SettingsSearch.Register(surface, WorkflowText("Animation de fond du chat", "Chat background animation"),
            WorkflowText("Opacité, couleur, code flottant, aurore, constellation, ondulations, particules, grille lumineuse", "Opacity, color, floating code, aurora, constellation, ripples, particles, luminous grid"), selector);

        string? SelectedColor() => followTheme.IsOn ? null : customColor;
        int SelectedOpacity() => (int)Math.Round(opacity.Value);
        void Refresh(bool applyToChat)
        {
            var choice = selector.SelectedItem as ChatBackgroundChoice;
            var theme = currentTheme();
            var enabled = choice?.Id != ChatBackgroundStyles.Disabled;
            opacity.IsEnabled = followTheme.IsEnabled = enabled;
            opacityHeader.Opacity = enabled ? 1 : .5;
            colorButton.Visibility = followTheme.IsOn ? Visibility.Collapsed : Visibility.Visible;
            colorButton.IsEnabled = enabled;
            if (!enabled || followTheme.IsOn) colorFlyout?.Hide();
            var color = SelectedColor() ?? theme.Accent;
            swatch.Background = new SolidColorBrush(ParseSidebarColor(color)); colorValue.Text = color;
            var percent = SelectedOpacity(); opacityValue.Text = $"{percent} %";
            previewView.Configure(choice?.Id, theme, percent, SelectedColor());
            previewLabel.Text = !enabled ? WorkflowText("Animation désactivée", "Animation disabled")
                : percent == 0 ? WorkflowText("Fond masqué · 0 %", "Background hidden · 0%")
                : WorkflowText("Aperçu · ", "Preview · ") + choice?.Name + $" · {percent} %";
            if (applyToChat) ConfigureChatBackground(choice?.Id, percent, SelectedColor());
        }
        colorButton.Click += (_, _) =>
        {
            colorFlyout?.Hide();
            var picker = new ColorPicker { Color = ParseSidebarColor(customColor), IsAlphaEnabled = false,
                IsHexInputVisible = true, IsColorSpectrumVisible = true, IsColorSliderVisible = true, Width = 280 };
            AutomationProperties.SetName(picker, WorkflowText("Couleur du fond du chat", "Chat background color"));
            picker.ColorChanged += (_, e) =>
            {
                customColor = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
                Refresh(true);
            };
            colorFlyout = new Flyout { Content = picker }; colorFlyout.ShowAt(colorButton);
        };
        selector.SelectionChanged += (_, _) => Refresh(true);
        opacity.ValueChanged += (_, _) => Refresh(true);
        followTheme.Toggled += (_, _) => Refresh(true);
        owner.Closed += (_, _) => { colorFlyout?.Hide(); previewView.Shutdown(); };
        Refresh(false);
        return (surface, settings =>
        {
            settings.ChatBackgroundAnimation = (selector.SelectedItem as ChatBackgroundChoice)?.Id ?? ChatBackgroundStyles.Disabled;
            settings.ChatBackgroundOpacityPercent = SelectedOpacity();
            settings.ChatBackgroundColor = SelectedColor();
        }, () => Refresh(false));
    }
}
