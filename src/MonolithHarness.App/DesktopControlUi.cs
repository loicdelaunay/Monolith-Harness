using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using MonolithHarness.Core.Hosting;
using System.Text.Json.Nodes;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    (StackPanel Panel, Action<FeatureSettings> Save) BuildDesktopInputSettings(string tool)
    {
        var initial = DesktopControlTools.Settings(FeatureSettings.Read(state.FeaturesJson), tool);
        var panel = new StackPanel { Spacing = 10 };
        var mode = new ComboBox { Header = WorkflowText("Mode de contrôle", "Control mode"),
            ItemsSource = new[] { WorkflowText("Automatique", "Automatic"), WorkflowText("UI Automation / API (Windows)", "UI Automation / API (Windows)"), "Legacy" },
            SelectedIndex = initial.Mode == "uia" ? 1 : initial.Mode == "legacy" ? 2 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var switching = new ToggleSwitch { IsOn = initial.AllowAiModeSwitch };
        var fallback = new ToggleSwitch { IsOn = initial.AllowLegacyFallback };
        var explanation = Label("", 12);
        panel.Children.Add(mode);
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Autoriser l’IA à choisir le mode", "Allow AI to choose a mode"),
            WorkflowText("En Automatique, le modèle peut demander UI Automation ou Legacy pour un appel. Un mode fixe reste imposé.", "In Automatic mode, the model may request UI Automation or Legacy per call. A fixed mode remains enforced."), switching));
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Repli automatique vers Legacy", "Automatic fallback to Legacy"),
            WorkflowText("Si aucun pattern compatible n’est disponible sur Windows, proposer puis exécuter le legacy selon les autorisations. Ce mode utilise votre focus et votre curseur. Aucun repli après une erreur d’exécution ou un délai dépassé.", "When no compatible Windows pattern is available, request approval for Legacy. It uses your focus and cursor. Never fall back after an execution error or timeout."), fallback));
        panel.Children.Add(explanation);
        void Refresh()
        {
            switching.IsEnabled = mode.SelectedIndex == 0;
            fallback.IsEnabled = mode.SelectedIndex == 0 && OperatingSystem.IsWindows();
            explanation.Text = WorkflowText(
                "Windows : UI Automation inspecte les contrôles et utilise leurs actions natives. Les outils DOM/API déjà disponibles sont privilégiés pour le navigateur. Certaines applications peuvent changer le focus. macOS : legacy avec permission Accessibilité. Linux : contrôle du bureau indisponible.",
                "Windows: UI Automation inspects controls and uses native actions. Existing DOM/API tools are preferred for the browser. Some apps may change focus. macOS: legacy with Accessibility permission. Linux: desktop input unavailable.");
        }
        mode.SelectionChanged += (_, _) => Refresh(); Refresh();
        return (panel, settings => {
            var saved = new DesktopInputSettings { Mode = mode.SelectedIndex == 1 ? "uia" : mode.SelectedIndex == 2 ? "legacy" : "auto", AllowAiModeSwitch = switching.IsOn, AllowLegacyFallback = fallback.IsOn };
            if (tool == "desktop_keyboard") settings.KeyboardInput = saved; else settings.MouseInput = saved;
        });
    }
    async Task<FeatureSettings> ReadDesktopInputSettingsAsync(ConversationRun run, string? tool, CancellationToken ct)
    {
        var snapshot = await ReadStoreAsync(store => store.States.AsNoTracking().Single(), ct);
        var skills = ConversationModes.EffectiveSkills(run.Chat, snapshot.EnabledSkills, run.Provider.IsOpenCode);
        if (tool == null ? !DesktopControlTools.Enabled(skills) : !Skills.Enabled(skills, tool == "desktop_mouse" ? "mouse_control" : "keyboard_control"))
            throw new UnauthorizedAccessException(T("Outil non autorisé."));
        return FeatureSettings.Read(snapshot.FeaturesJson);
    }
    Task<string> ExecuteDesktopInputAsync(string tool, JsonObject original, ConversationRun run, CancellationToken ct)
    {
        var args = original.DeepClone().AsObject();
        args["keys"] ??= (args["key"] ?? args["shortcut"])?.DeepClone();
        args["delta_y"] ??= (args["deltaY"] ?? args["delta"])?.DeepClone();
        args["click_count"] ??= args["clickCount"]?.DeepClone();
        return DesktopControl.ExecuteAsync(tool, args, token => ReadDesktopInputSettingsAsync(run, tool, token),
            (mode, details, token) => RequestAccessAsync("desktop|" + (tool == "desktop_mouse" ? "mouse" : "keyboard") + "|" + mode,
                WorkflowText("Contrôle du bureau · ", "Desktop control · ") + (mode == "uia" ? "UI Automation" : "Legacy"), details,
                WorkflowText("Contrôle du bureau", "Desktop control"), token), ct);
    }
    Task<string> InspectDesktopUiAsync(JsonObject args, ConversationRun run, CancellationToken ct) =>
        DesktopControl.InspectAsync(args, token => ReadDesktopInputSettingsAsync(run, null, token),
            (mode, details, token) => RequestAccessAsync("desktop|ui", WorkflowText("Inspecter les contrôles Windows", "Inspect Windows UI controls"), details,
                WorkflowText("Contrôles accessibles transmis au modèle", "Accessible controls sent to the model"), token), ct);
}
