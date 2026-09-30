using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    (StackPanel Panel, Action<FeatureSettings> Save, Func<bool> Validate) BuildCompactionSettings()
    {
        var config = FeatureSettings.Read(state.FeaturesJson).Compaction;
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(Label(WorkflowText("Libérer du contexte, garder l’essentiel", "Free context, keep what matters"), 22));
        panel.Children.Add(Label(WorkflowText("Ces réglages s’appliquent aux prochains envois dans la GUI, le CLI et les sous-agents. Le compactage produit un résumé avec le modèle sélectionné et consomme des tokens. Les messages originaux restent consultables dans la conversation.",
            "These settings apply to subsequent requests in the GUI, CLI and subagents. Compaction uses the selected model and consumes tokens. Original messages remain available in the conversation."), 13));
        var automatic = new ToggleSwitch { IsOn = config.AutoEnabled, OnContent = UiText.T("Activé"), OffContent = UiText.T("Désactivé") };
        panel.Children.Add(FluentDesign.Setting(WorkflowText("Compactage automatique", "Automatic compaction"),
            WorkflowText("Déclenche un résumé quand l’utilisation du contexte atteint le seuil choisi. Le compactage manuel reste disponible.",
                "Create a summary when context usage reaches your threshold. Manual compaction remains available."), automatic));
        NumberBox Number(string fr, string en, int value, int minimum, int maximum) => new()
        {
            Header = WorkflowText(fr, en), Value = value, Minimum = minimum, Maximum = maximum,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var trigger = Number("Seuil de déclenchement (% du contexte)", "Trigger threshold (% of context)", config.TriggerPercent, 10, 99);
        var target = Number("Cible après compactage (% du contexte)", "Target after compaction (% of context)", config.TargetPercent, 5, 90);
        var overview = Label("", 13);
        panel.Children.Add(trigger); panel.Children.Add(target); panel.Children.Add(FluentDesign.Surface(overview, 14));
        var strength = new ComboBox { Header = WorkflowText("Force du compactage", "Compaction strength"),
            ItemsSource = new[] { WorkflowText("Léger · plus de détails", "Gentle · more detail"), WorkflowText("Équilibré", "Balanced"),
                WorkflowText("Fort · résumé très concis", "Strong · very concise"), WorkflowText("Personnalisé", "Custom") },
            SelectedIndex = Array.IndexOf(CompactionSettings.Strengths, config.Strength), HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(strength);
        var strengthInfo = Label("", 13); panel.Children.Add(strengthInfo);
        var custom = new StackPanel { Spacing = 12 };
        var retention = Number("Taille souhaitée du résumé (% du texte compacté)", "Desired summary size (% of compacted text)", config.CustomRetentionPercent, 5, 60);
        var summaryBudget = Number("Budget maximal du résumé (tokens estimés)", "Maximum summary budget (estimated tokens)", config.CustomMaxSummaryTokens, 128, 32000);
        var instruction = new TextBox { Header = WorkflowText("Consignes personnalisées", "Custom instructions"), Text = config.CustomInstruction,
            PlaceholderText = WorkflowText("Exemple : conserver les décisions et chemins ; regrouper les résultats d’outils par fichier ; terminer par les tâches restantes.",
                "Example: preserve decisions and paths; group tool results by file; end with remaining tasks."),
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110, MaxHeight = 250, MaxLength = 8000 };
        custom.Children.Add(retention); custom.Children.Add(summaryBudget); custom.Children.Add(instruction);
        custom.Children.Add(Label(WorkflowText("Le budget est réduit si nécessaire pour respecter la cible globale. Les consignes de conservation des demandes et du travail restant restent applicables.",
            "The budget is reduced if needed to meet the overall target. Requirements and unfinished work are always preserved."), 12));
        var customSurface = FluentDesign.Surface(custom, 16); panel.Children.Add(customSurface);
        var strategy = new ComboBox { Header = WorkflowText("Stratégie de compactage", "Compaction strategy"),
            ItemsSource = new[] { WorkflowText("Conserver les échanges récents", "Keep recent exchanges"), WorkflowText("Résumer tout l’historique", "Summarize the entire history"),
                WorkflowText("Alléger d’abord les échanges avec les outils", "Reduce tool exchanges first") },
            SelectedIndex = Array.IndexOf(CompactionSettings.Strategies, config.Strategy), HorizontalAlignment = HorizontalAlignment.Stretch };
        var strategyInfo = Label("", 13);
        var recent = Number("Échanges récents à conserver en priorité", "Recent exchanges to prioritize", config.RecentTurns, 0, 10);
        panel.Children.Add(strategy); panel.Children.Add(strategyInfo); panel.Children.Add(recent);
        panel.Children.Add(Label(WorkflowText("La demande en cours reste intégrale en mode automatique. Si elle dépasse à elle seule la cible, le système conserve l’historique et indique que la cible ne peut pas être atteinte. Les grands historiques sont résumés par morceaux, sans troncature silencieuse.",
            "The current request stays intact in automatic mode. If it alone exceeds the target, history is preserved and the target cannot be met. Large histories are summarized in segments without silent truncation."), 12));
        panel.Children.Add(Label(WorkflowText("Les budgets et la taille finale du contexte sont estimés. Pour les sous-agents OpenCode, le compactage interne des sessions reste géré par OpenCode.",
            "Budgets and final context size are estimated. OpenCode manages its subagent sessions' internal compaction."), 12));
        var error = Label("", 13); error.Foreground = new SolidColorBrush(Microsoft.UI.Colors.IndianRed); panel.Children.Add(error);
        int Value(NumberBox field) => (int)field.Value;
        CompactionSettings Read() => new() { AutoEnabled = automatic.IsOn, TriggerPercent = Value(trigger), TargetPercent = Value(target),
            Strength = CompactionSettings.Strengths[Math.Clamp(strength.SelectedIndex, 0, 3)], Strategy = CompactionSettings.Strategies[Math.Clamp(strategy.SelectedIndex, 0, 2)],
            RecentTurns = Value(recent), CustomRetentionPercent = Value(retention), CustomMaxSummaryTokens = Value(summaryBudget), CustomInstruction = instruction.Text.Trim() };
        void Refresh()
        {
            trigger.IsEnabled = automatic.IsOn;
            customSurface.Visibility = strength.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            strengthInfo.Text = strength.SelectedIndex switch {
                0 => WorkflowText("Conserve davantage d’explications et d’exemples. Résumé visé : 40 % du texte, jusqu’à 4 000 tokens estimés.", "Keep more explanations and examples. Desired summary: 40% of the source, up to 4,000 estimated tokens."),
                2 => WorkflowText("Privilégie des notes courtes : contraintes, décisions, résultats et prochaines actions. Résumé visé : 10 %, jusqu’à 1 000 tokens estimés.", "Favor short notes: constraints, decisions, results and next actions. Desired summary: 10%, up to 1,000 estimated tokens."),
                3 => WorkflowText("Choisissez la taille du résumé, son budget et vos propres consignes.", "Choose summary size, token budget and your own instructions."),
                _ => WorkflowText("Conserve les faits techniques utiles et enlève les répétitions. Résumé visé : 20 %, jusqu’à 2 000 tokens estimés.", "Keep useful technical facts and remove repetition. Desired summary: 20%, up to 2,000 estimated tokens.") };
            strategyInfo.Text = strategy.SelectedIndex switch {
                1 => WorkflowText("Résume tous les échanges disponibles, en gardant la demande en cours intégrale lors du compactage automatique.", "Summarize all available exchanges while keeping the current request intact during automatic compaction."),
                2 => WorkflowText("Résume d’abord les anciens appels et résultats d’outils, puis les autres échanges si nécessaire pour atteindre la cible.", "Summarize older tool calls and results first, then other exchanges if needed to reach the target."),
                _ => WorkflowText("Résume les échanges les plus anciens et préserve les récents en priorité. Certains échanges récents peuvent aussi être résumés si la cible l’exige.", "Summarize oldest exchanges and prioritize recent ones. Some recent exchanges may also be summarized if needed to reach the target.") };
            recent.Visibility = strategy.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible;
            overview.Text = automatic.IsOn
                ? string.Format(WorkflowText("Déclenchement à {0:0} % → cible {1:0} %. La cible automatique comprend les instructions, les outils, le résumé et les échanges conservés.",
                    "Trigger at {0:0}% → target {1:0}%. The automatic target includes instructions, tools, summary and retained exchanges."), trigger.Value, target.Value)
                : string.Format(WorkflowText("Compactage automatique désactivé. Cible de l’historique après compactage manuel : {0:0} % maximum.", "Automatic compaction disabled. Manual history target: at most {0:0}%."), target.Value);
        }
        bool Validate()
        {
            var fields = new[] { trigger, target, recent, retention, summaryBudget };
            if (fields.Any(x => !double.IsFinite(x.Value) || x.Value != Math.Truncate(x.Value) || x.Value < x.Minimum || x.Value > x.Maximum))
            { error.Text = WorkflowText("Saisissez des valeurs entières dans les limites affichées.", "Enter whole numbers within the displayed limits."); return false; }
            if (target.Value >= trigger.Value)
            { error.Text = WorkflowText("La cible après compactage doit être inférieure au seuil de déclenchement.", "The target after compaction must be below the trigger threshold."); return false; }
            error.Text = ""; return true;
        }
        strength.SelectionChanged += (_, _) => Refresh(); strategy.SelectionChanged += (_, _) => Refresh(); automatic.Toggled += (_, _) => Refresh();
        trigger.ValueChanged += (_, _) => Refresh(); target.ValueChanged += (_, _) => Refresh();
        var restore = Action(WorkflowText("Rétablir les réglages de compactage", "Restore compaction defaults"), () => {
            automatic.IsOn = true; trigger.Value = 90; target.Value = 60; strength.SelectedIndex = 1; strategy.SelectedIndex = 0;
            recent.Value = 2; retention.Value = 20; summaryBudget.Value = 2000; instruction.Text = ""; error.Text = ""; Refresh(); return Task.CompletedTask; });
        restore.HorizontalAlignment = HorizontalAlignment.Right; panel.Children.Add(restore);
        Refresh();
        return (panel, settings => settings.Compaction = Read(), Validate);
    }
}
