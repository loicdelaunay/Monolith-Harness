using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

sealed class ModelContextEditor : UserControl
{
    readonly ToggleSwitch automatic = new();
    readonly NumberBox tokens = new() { Minimum = 1024, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
    readonly TextBlock model = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary };
    readonly TextBlock source = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = FluentDesign.Secondary };
    readonly Button details = new() { Width = 30, Height = 32, Padding = new(0), Content = new FontIcon { Glyph = "\uE946", FontSize = 14 } };
    readonly bool compact;
    readonly TextBlock contextLabel = new() { FontSize = 13, Foreground = FluentDesign.Primary, VerticalAlignment = VerticalAlignment.Center };
    Provider? provider;
    Action<Provider>? changed;
    bool syncing;
    public ModelContextEditor(bool compact = false)
    {
        this.compact = compact;
        var content = new StackPanel { Spacing = 7 };
        if (compact)
        {
            automatic.FontSize = 12;
            var mode = new Grid { ColumnSpacing = 10 };
            mode.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); mode.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            mode.Children.Add(contextLabel); Grid.SetColumn(automatic, 1); mode.Children.Add(automatic);
            var value = new Grid { ColumnSpacing = 6 };
            value.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            value.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); value.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            tokens.FontSize = 13; tokens.MinHeight = 32; tokens.Padding = new(8, 3, 8, 3);
            value.Children.Add(tokens);
            var unit = new TextBlock { Text = "tokens", FontSize = 11, Foreground = FluentDesign.Secondary, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(unit, 1); value.Children.Add(unit); Grid.SetColumn(details, 2); value.Children.Add(details);
            content.Spacing = 4; content.Children.Add(mode); content.Children.Add(value);
        }
        else { content.Children.Add(automatic); content.Children.Add(tokens); content.Children.Add(model); content.Children.Add(source); }
        Content = content;
        var detailFlyout = new Flyout { Content = new TextBlock { MaxWidth = 300, FontSize = 12, TextWrapping = TextWrapping.Wrap } };
        details.Flyout = detailFlyout;
        detailFlyout.Opening += (_, _) => ((TextBlock)detailFlyout.Content).Text = source.Text;
        automatic.Toggled += (_, _) =>
        {
            if (syncing || provider == null) return;
            ModelContexts.SetOverride(provider, automatic.IsOn ? null : provider.ContextLimit);
            Refresh(); changed?.Invoke(provider);
        };
        tokens.ValueChanged += (_, _) =>
        {
            if (syncing || provider == null || automatic.IsOn || !double.IsFinite(tokens.Value)) return;
            ModelContexts.SetOverride(provider, (int)tokens.Value);
            Refresh(); changed?.Invoke(provider);
        };
    }
    public void Bind(Provider? value, Action<Provider>? onChanged = null)
    {
        provider = value; changed = onChanged; Refresh();
    }
    void Refresh()
    {
        syncing = true;
        try
        {
            automatic.Header = compact ? null : UiText.Resolve("Capacité maximale automatique", "Automatic maximum capacity");
            automatic.OnContent = UiText.Resolve("Automatique", "Automatic"); automatic.OffContent = UiText.Resolve("Personnalisée", "Custom");
            tokens.Header = compact ? null : UiText.Resolve("Limite pour ce modèle (tokens)", "Limit for this model (tokens)");
            contextLabel.Text = UiText.Resolve("Contexte", "Context");
            var value = provider == null ? null : ModelContexts.For(provider);
            automatic.IsEnabled = provider != null && provider.Model.Length > 0;
            automatic.IsOn = value?.Automatic ?? true;
            tokens.Maximum = Math.Min(value?.MaximumTokens ?? (provider == null ? 10_000_000 : ModelContexts.Ceiling(provider)), provider == null ? 10_000_000 : ModelContexts.Ceiling(provider));
            tokens.Value = value?.Limit ?? ModelContexts.DefaultContextLimit; tokens.IsEnabled = automatic.IsEnabled && !automatic.IsOn;
            model.Text = provider == null || provider.Model.Length == 0 ? UiText.Resolve("Sélectionnez un modèle.", "Select a model.")
                : UiText.Resolve("Modèle : ", "Model: ") + provider.Model;
            source.Text = value?.MaximumTokens is { } maximum
                ? string.Format(UiText.Resolve("Maximum communiqué : {0:N0} tokens.", "Reported maximum: {0:N0} tokens."), maximum)
                : string.Format(UiText.Resolve("Maximum non communiqué. Valeur de repli estimée : {0:N0} tokens, modifiable en mode personnalisé.",
                    "Maximum not reported. Estimated fallback: {0:N0} tokens; change it in custom mode."), provider == null ? ModelContexts.DefaultContextLimit : ModelContexts.Fallback(provider));
            if (value?.Source == "API input") source.Text += " " + UiText.Resolve("Le fournisseur indique une limite de tokens d’entrée.", "The provider reports an input token limit.");
            if (value?.Source == "GGUF") source.Text += " " + UiText.Resolve("Fenêtre déclarée dans les métadonnées GGUF. La mémoire disponible peut imposer une limite plus basse.", "Window declared in GGUF metadata. Available memory may require a lower limit.");
            if (value?.MaximumTokens > tokens.Maximum) source.Text += " " + UiText.Resolve("La limite prise en charge par le moteur est inférieure.", "The engine's supported limit is lower.");
            ToolTipService.SetToolTip(details, source.Text);
            ToolTipService.SetToolTip(tokens, model.Text + "\n" + source.Text);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(details, UiText.Resolve("Informations sur le contexte", "Context information"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tokens, UiText.Resolve("Limite pour ce modèle (tokens)", "Limit for this model (tokens)"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(automatic, UiText.Resolve("Capacité maximale automatique", "Automatic maximum capacity"));
        }
        finally { syncing = false; }
    }
}
