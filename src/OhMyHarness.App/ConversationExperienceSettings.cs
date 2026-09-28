using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    (StackPanel Panel, Action<FeatureSettings> Save) BuildRetrySettings()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        var panel = new StackPanel { Spacing = 10 };
        var enabled = new ToggleSwitch { IsOn = config.RetryEnabled };
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Réessayer automatiquement", "Automatic retry"),
            WorkflowText("Relance une requête en cas d’erreur réseau, de saturation ou de flux interrompu. Les erreurs de clé API et les outils déjà exécutés ne sont pas relancés.", "Retry network, rate-limit and interrupted-stream errors. Invalid API keys and completed tools are not retried."), enabled));
        var count = new NumberBox { Header = WorkflowText("Nombre de nouvelles tentatives", "Number of retries"), Minimum = 0, Maximum = 10, Value = config.RetryCount, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var delay = new NumberBox { Header = WorkflowText("Délai entre les tentatives (secondes)", "Delay between retries (seconds)"), Minimum = 1, Maximum = 300, Value = config.RetryDelaySeconds, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        panel.Children.Add(count); panel.Children.Add(delay);
        void Refresh() => count.IsEnabled = delay.IsEnabled = enabled.IsOn;
        enabled.Toggled += (_, _) => Refresh(); Refresh();
        return (panel, settings => { settings.RetryEnabled = enabled.IsOn; settings.RetryCount = double.IsFinite(count.Value) ? Math.Clamp((int)count.Value, 0, 10) : 3; settings.RetryDelaySeconds = double.IsFinite(delay.Value) ? Math.Clamp((int)delay.Value, 1, 300) : 5; });
    }

    (StackPanel Panel, Action<FeatureSettings> Save) BuildNotificationSettings()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        var panel = new StackPanel { Spacing = 14 };
        var completed = new ToggleSwitch { IsOn = config.NotifyCompleted };
        var action = new ToggleSwitch { IsOn = config.NotifyActionRequired };
        var bell = new ToggleSwitch { IsOn = config.NotificationBell };
        var sound = new ToggleSwitch { IsOn = config.NotificationSound };
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Conversation terminée", "Conversation completed"), "", completed));
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Action requise", "Action required"), WorkflowText("Questions et demandes d’autorisation de l’agent.", "Agent questions and approval requests."), action));
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Cloche de notification", "Notification bell"), WorkflowText("Affiche les alertes dans l’application et sur les conversations concernées.", "Show alerts in the app and beside their conversations."), bell));
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Jouer un son", "Play a sound"), "", sound));
        var sounds = NotificationSounds.All.ToList();
        var tone = new ComboBox { Header = WorkflowText("Son", "Sound"), ItemsSource = sounds.Select(x => WorkflowText(x.French, x.English)).ToArray(), SelectedIndex = Math.Max(0, sounds.FindIndex(x => x.Id == config.NotificationTone)), HorizontalAlignment = HorizontalAlignment.Stretch };
        string SelectedTone() => sounds[Math.Clamp(tone.SelectedIndex, 0, sounds.Count - 1)].Id;
        var volume = new Slider { Header = WorkflowText("Volume", "Volume"), Minimum = 0, Maximum = 100, Value = config.NotificationVolume, StepFrequency = 5 };
        panel.Children.Add(tone); panel.Children.Add(volume);
        var preview = Action(WorkflowText("Écouter le son", "Preview sound"), () => NotificationAudio.PlayAsync(SelectedTone(), (int)volume.Value));
        preview.HorizontalAlignment = HorizontalAlignment.Right; panel.Children.Add(preview);
        void Refresh() => tone.IsEnabled = volume.IsEnabled = preview.IsEnabled = sound.IsOn;
        sound.Toggled += (_, _) => Refresh(); Refresh();
        return (panel, settings => {
            settings.NotifyCompleted = completed.IsOn; settings.NotifyActionRequired = action.IsOn;
            settings.NotificationBell = bell.IsOn; settings.NotificationSound = sound.IsOn;
            settings.NotificationTone = SelectedTone();
            settings.NotificationVolume = (int)volume.Value;
        });
    }
}
