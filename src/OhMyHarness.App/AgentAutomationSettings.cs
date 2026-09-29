using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    (StackPanel Panel, Action<FeatureSettings> Save) BuildAgentAutomationSettings()
    {
        var config = FeatureSettings.Read(state.FeaturesJson);
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(Label(WorkflowText("Comportement des agents en mode Auto", "Agent behavior in Auto mode"), 22));
        panel.Children.Add(Label(WorkflowText("Avant chaque demande, Auto évalue les tâches qui peuvent être déléguées. Ces réglages s’appliquent à toutes les conversations en mode Auto. Le mode Désactivé ne lance aucun agent ; le mode Forcé lance l’équipe choisie.", "Before every request, Auto evaluates which tasks can be delegated. These settings apply to all conversations in Auto mode. Disabled starts no agents; Forced starts the selected team."), 13));
        var behavior = new ComboBox
        {
            Header = WorkflowText("Insistance de la délégation", "Delegation insistence"), HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { WorkflowText("Sélectif", "Selective"), WorkflowText("Équilibré", "Balanced"), WorkflowText("Proactif", "Proactive"), "Swarm" },
            SelectedIndex = Math.Max(0, Array.IndexOf(AgentAutomation.Behaviors, config.AgentAutoBehavior))
        };
        var explanation = Label("", 13);
        void Describe() => explanation.Text = behavior.SelectedIndex switch
        {
            0 => WorkflowText("Délègue surtout les demandes importantes avec plusieurs tâches indépendantes. Les demandes simples et moyennes restent directes.", "Delegates mainly substantial requests with several independent tasks. Simple and medium requests remain direct."),
            1 => WorkflowText("Délègue dès qu’au moins deux tâches utiles peuvent avancer indépendamment. Les demandes simples restent directes.", "Delegates when at least two useful tasks can progress independently. Simple requests remain direct."),
            3 => WorkflowText("Privilégie une équipe pour chaque demande substantielle, avec des rôles complémentaires d’exploration, réalisation et revue. Réutilise des vagues d’agents tant que cela aide à terminer la demande.", "Prefers a team for every substantive request, with complementary exploration, implementation and review roles. Uses additional waves while they help complete the request."),
            _ => WorkflowText("Recherche activement les occasions de déléguer. Lance une équipe sur les demandes comportant plusieurs tâches et propose un rôle complémentaire sur une tâche importante. Réévalue les besoins au fil du travail.", "Actively looks for delegation opportunities. Starts a team for multi-part requests and assigns a complementary role for a substantial single task. Reassesses as work progresses.")
        };
        behavior.SelectionChanged += (_, _) => Describe(); Describe();
        panel.Children.Add(behavior); panel.Children.Add(FluentDesign.Surface(explanation, 14));
        var instruction = new TextBox
        {
            Header = WorkflowText("Consignes complémentaires du mode Auto", "Additional Auto instructions"), Text = config.AgentAutoInstructions,
            PlaceholderText = WorkflowText("Exemple : déléguer l’exploration et la revue ; réserver les modifications à un seul agent par fichier.", "Example: delegate exploration and review; assign only one writer per file."),
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 220, MaxLength = 4000
        };
        panel.Children.Add(instruction);
        panel.Children.Add(Label(WorkflowText("Avec OpenCode, l’équipe initiale peut utiliser les outils natifs autorisés, mais la délégation imbriquée native reste gérée séparément : les sous-agents OpenCode ne créent pas de descendants hors du budget de l’application.", "With OpenCode, the initial team can use authorized native tools. Native nested delegation is handled separately: OpenCode subagents do not create descendants outside the application budget."), 12));
        var limits = new StackPanel { Spacing = 12 };
        NumberBox Number(string fr, string en, int value, int minimum, int maximum) => new()
        {
            Header = WorkflowText(fr, en), Value = value, Minimum = minimum, Maximum = maximum,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var total = Number("Agents au total par demande (descendants inclus)", "Total agents per request (including descendants)", config.AgentMaxTotal, 1, ConversationAgents.MaximumTeamSize);
        var parallel = Number("Requêtes de modèles en parallèle", "Parallel model requests", config.AgentParallelism, 1, 64);
        var depth = Number("Profondeur de délégation", "Delegation depth", config.AgentMaxDepth, 1, 8);
        var steps = Number("Étapes maximum par agent", "Maximum steps per agent", config.AgentMaxSteps, 8, 2000);
        limits.Children.Add(Label(WorkflowText("Le budget est partagé par toute l’équipe et toutes les vagues d’une demande. Les agents héritent des outils activés et des autorisations du chat. Le contexte est compacté pour poursuivre les travaux longs. Arrêter la conversation arrête toute l’équipe. Augmenter ces valeurs augmente les requêtes et peut augmenter leur coût.", "The budget is shared by the whole team and all waves of a request. Agents inherit enabled tools and chat permissions. Context is compacted for long work. Stopping the conversation stops the whole team. Higher values increase requests and may increase their cost."), 13));
        limits.Children.Add(total); limits.Children.Add(parallel); limits.Children.Add(depth); limits.Children.Add(steps);
        panel.Children.Add(new Expander { Header = WorkflowText("Capacités et garde-fous du swarm", "Swarm capacity and safeguards"), Content = limits, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        int Value(NumberBox field, int fallback) => double.IsFinite(field.Value) ? (int)Math.Clamp(field.Value, field.Minimum, field.Maximum) : fallback;
        return (panel, settings =>
        {
            settings.AgentAutoBehavior = AgentAutomation.Behaviors[Math.Clamp(behavior.SelectedIndex, 0, 3)];
            settings.AgentAutoInstructions = instruction.Text.Trim();
            settings.AgentMaxTotal = Value(total, 64); settings.AgentParallelism = Value(parallel, 8);
            settings.AgentMaxDepth = Value(depth, 3); settings.AgentMaxSteps = Value(steps, 200);
        });
    }
}
