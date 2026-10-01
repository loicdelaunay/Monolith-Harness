using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    (StackPanel Panel, StackPanel Logs, Action<FeatureSettings> Save, Func<bool> Validate) BuildConversationPreferences()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        var panel = new StackPanel { Spacing = 12 };
        var enabled = new CheckBox { IsChecked = config.AutoNameConversations };
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Nommer automatiquement les nouvelles conversations", "Automatically name new conversations"), WorkflowText("Le modèle choisi reçoit un extrait textuel et peut consommer des tokens. Le nommage manuel reste accessible par clic droit.", "The selected model receives a text excerpt and may consume tokens. Manual naming remains available in the context menu."), enabled));
        var timing = new ComboBox { Header = WorkflowText("Quand nommer la conversation", "When to name the conversation"), ItemsSource = new[] { WorkflowText("Dès le premier message de l’utilisateur", "After the first user message"), WorkflowText("Après la fin de la première réponse", "After the first response finishes") }, SelectedIndex = config.AutoNamingTiming == "first-message" ? 0 : 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        timing.IsEnabled = enabled.IsChecked == true;
        enabled.Checked += (_, _) => timing.IsEnabled = true;
        enabled.Unchecked += (_, _) => timing.IsEnabled = false;
        panel.Children.Add(timing);
        var providers = db.Providers.Local.Where(p => !p.IsComposite).ToList();
        var provider = new ComboBox { Header = WorkflowText("Fournisseur de nommage", "Naming provider"), ItemsSource = providers, SelectedItem = providers.FirstOrDefault(p => p.Id == config.NamingProviderId), HorizontalAlignment = HorizontalAlignment.Stretch };
        var model = new ComboBox { Header = WorkflowText("Modèle de nommage", "Naming model"), IsEditable = true, Text = config.NamingModel, HorizontalAlignment = HorizontalAlignment.Stretch };
        SettingsModelPicker.Connect(provider, model, config.NamingModel);
        panel.Children.Add(provider); panel.Children.Add(model);
        var error = Label("", 12); error.Foreground = FluentDesign.Secondary; panel.Children.Add(error);
        var logPanel = new StackPanel { Spacing = 12 };
        var logs = new CheckBox { IsChecked = config.LogsEnabled };
        logPanel.Children.Add(FluentDesign.Setting(WorkflowText("Activer les logs", "Enable logs"), WorkflowText("Diagnostics locaux sans prompts, réponses, clés API ni arguments d’outils.", "Local diagnostics without prompts, responses, API keys or tool arguments."), logs));
        var level = new ComboBox { Header = WorkflowText("Sévérité minimum", "Minimum severity"), ItemsSource = Enum.GetNames<AppLogLevel>(), SelectedItem = config.LogLevel, HorizontalAlignment = HorizontalAlignment.Stretch };
        var days = new NumberBox { Header = WorkflowText("Conservation en jours", "Retention in days"), Minimum = 1, Maximum = 365, Value = config.LogRetentionDays };
        logPanel.Children.Add(level); logPanel.Children.Add(days); logPanel.Children.Add(Label(AppLog.DirectoryPath, 11));
        logs.Checked += (_, _) => level.IsEnabled = days.IsEnabled = true;
        logs.Unchecked += (_, _) => level.IsEnabled = days.IsEnabled = false;
        level.IsEnabled = days.IsEnabled = logs.IsChecked == true;
        return (panel, logPanel, settings => {
            settings.AutoNamingTiming = timing.SelectedIndex == 0 ? "first-message" : "first-response";
            settings.AutoNameConversations = enabled.IsChecked == true; settings.NamingProviderId = (provider.SelectedItem as Provider)?.Id ?? 0; settings.NamingModel = SettingsModelPicker.Read(model);
            settings.LogsEnabled = logs.IsChecked == true; settings.LogLevel = level.SelectedItem as string ?? "Information";
            settings.LogRetentionDays = double.IsFinite(days.Value) ? Math.Clamp((int)days.Value, 1, 365) : 7;
        }, () => {
            var valid = enabled.IsChecked != true || provider.SelectedItem is Provider && !string.IsNullOrWhiteSpace(SettingsModelPicker.Read(model));
            error.Text = valid ? "" : WorkflowText("Choisissez le fournisseur et le modèle de nommage.", "Choose the naming provider and model."); return valid;
        });
    }
}
