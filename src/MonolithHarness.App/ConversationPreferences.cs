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
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Nommer automatiquement les nouvelles conversations", "Automatically name new conversations"), WorkflowText("Après la première réponse. Le modèle choisi reçoit un extrait textuel et peut consommer des tokens. Le nommage reste accessible par clic droit quand cette option est désactivée.", "After the first response. The selected model receives a text excerpt and may consume tokens. Manual AI naming stays available in the context menu."), enabled));
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
            settings.AutoNameConversations = enabled.IsChecked == true; settings.NamingProviderId = (provider.SelectedItem as Provider)?.Id ?? 0; settings.NamingModel = SettingsModelPicker.Read(model);
            settings.LogsEnabled = logs.IsChecked == true; settings.LogLevel = level.SelectedItem as string ?? "Information";
            settings.LogRetentionDays = double.IsFinite(days.Value) ? Math.Clamp((int)days.Value, 1, 365) : 7;
        }, () => {
            var valid = enabled.IsChecked != true || provider.SelectedItem is Provider && !string.IsNullOrWhiteSpace(SettingsModelPicker.Read(model));
            error.Text = valid ? "" : WorkflowText("Choisissez le fournisseur et le modèle de nommage.", "Choose the naming provider and model."); return valid;
        });
    }
}
