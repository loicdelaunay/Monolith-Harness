using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    (StackPanel Panel, Action<FeatureSettings> Save) BuildLoopDetectionSettings()
    {
        var config = FeatureSettings.Read(state.FeaturesJson).LoopDetection ?? new();
        var panel = new StackPanel { Spacing = 12 };
        var enabled = new ToggleSwitch { IsOn = config.Enabled };
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Détecter les boucles d’appels d’outils", "Detect repeated tool calls"),
            WorkflowText("Surveille les appels consécutifs au même outil avec les mêmes arguments, dans chaque conversation et sous-agent. Les attentes courtes du terminal sont exclues. Les requêtes réseau en retry et les limites d’étapes restent indépendantes.",
                "Watch consecutive calls to the same tool with identical arguments in each conversation and subagent. Short terminal waits are excluded. Network retries and step limits are independent."), enabled));
        var threshold = new NumberBox { Header = WorkflowText("Nombre d’appels identiques avant détection", "Identical calls before detection"),
            Minimum = 2, Maximum = 50, Value = config.RepeatedCallThreshold, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var action = new ComboBox { Header = WorkflowText("Lorsqu’une boucle est détectée", "When a loop is detected"),
            ItemsSource = new[] { WorkflowText("Demander : arrêter ou continuer une fois", "Ask: stop or continue once"), WorkflowText("Arrêter automatiquement", "Stop automatically") },
            SelectedIndex = config.Action == "stop" ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(threshold); panel.Children.Add(action);
        panel.Children.Add(Label(WorkflowText("Par défaut : détection activée, 3 appels identiques, confirmation. Les changements s’appliquent aux prochains envois.",
            "Default: enabled, 3 identical calls, ask for confirmation. Changes apply to subsequent messages."), 12));
        void Refresh() => threshold.IsEnabled = action.IsEnabled = enabled.IsOn;
        enabled.Toggled += (_, _) => Refresh(); Refresh();
        return (panel, settings => settings.LoopDetection = new()
        { Enabled = enabled.IsOn, RepeatedCallThreshold = double.IsFinite(threshold.Value) ? Math.Clamp((int)threshold.Value, 2, 50) : 3, Action = action.SelectedIndex == 1 ? "stop" : "ask" });
    }
}
