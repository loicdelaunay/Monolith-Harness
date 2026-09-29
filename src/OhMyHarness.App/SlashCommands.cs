using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using OhMyHarness.Core;
using Windows.System;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    sealed record SlashChoice(string Name, string Arguments, string Description)
    { public override string ToString() => "/" + Name + " " + Arguments + "  ·  " + Description; }
    readonly ListView slashChoices = new() { MaxHeight = 210, SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    readonly Border slashSuggestions = new() { Background = FluentDesign.Card, BorderBrush = FluentDesign.Stroke, BorderThickness = new(1), CornerRadius = new(8), Visibility = Visibility.Collapsed };
    bool slashDismissed;
    SlashChoice[] SlashCatalog() =>
    [
        new("agents", "[off|auto|on|nombre]", WorkflowText("Sous-agents et rôles de la conversation", "Conversation subagents and roles")),
        new("plan", "[on|off]", WorkflowText("Mode plan ou exécution", "Plan or execution mode")),
        new("goal", "[objectif|off]", WorkflowText("Définir l’objectif de la conversation", "Set the conversation goal")),
        new("model", "[nom]", WorkflowText("Choisir le modèle", "Choose the model")),
        new("thinking", "[auto|low|medium|high|none]", WorkflowText("Niveau de réflexion", "Reasoning effort")),
        new("retry", "[on|off]", WorkflowText("Activer les nouvelles tentatives", "Toggle automatic retry")),
        new("theme", "[id]", WorkflowText("Changer de thème", "Change theme")),
        new("settings", "", WorkflowText("Ouvrir les réglages", "Open settings")),
        new("stop", "", WorkflowText("Arrêter cette génération", "Stop this generation")),
        new("help", "", WorkflowText("Afficher les commandes", "Show commands"))
    ];
    UIElement BuildSlashSuggestions()
    {
        slashSuggestions.Child = slashChoices;
        composer.TextChanged += (_, _) => { slashDismissed = false; RefreshSlashSuggestions(); };
        slashChoices.ItemClick += (_, e) => { if (e.ClickedItem is SlashChoice choice) CompleteSlash(choice); };
        return slashSuggestions;
    }
    void RefreshSlashSuggestions(bool all = false)
    {
        var text = composer.Text.TrimStart();
        if (slashDismissed || !all && (!text.StartsWith('/') || text.Any(char.IsWhiteSpace))) { slashSuggestions.Visibility = Visibility.Collapsed; return; }
        var items = SlashCatalog().Where(x => all || x.Name.StartsWith(text[1..], StringComparison.OrdinalIgnoreCase)).ToArray();
        slashChoices.ItemsSource = items;
        slashChoices.SelectedIndex = items.Length > 0 ? 0 : -1;
        slashSuggestions.Visibility = items.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    void CompleteSlash(SlashChoice choice)
    {
        composer.Text = "/" + choice.Name + " "; composer.Select(composer.Text.Length, 0);
        slashSuggestions.Visibility = Visibility.Collapsed; composer.Focus(FocusState.Programmatic);
    }
    bool HandleSlashKey(KeyRoutedEventArgs e)
    {
        if (slashSuggestions.Visibility != Visibility.Visible) return false;
        if (e.Key == VirtualKey.Escape) { slashDismissed = true; slashSuggestions.Visibility = Visibility.Collapsed; e.Handled = true; return true; }
        if (e.Key is VirtualKey.Up or VirtualKey.Down)
        {
            var count = slashChoices.Items.Count;
            if (count > 0) slashChoices.SelectedIndex = (Math.Max(0, slashChoices.SelectedIndex) + (e.Key == VirtualKey.Down ? 1 : count - 1)) % count;
            if (slashChoices.SelectedItem != null) slashChoices.ScrollIntoView(slashChoices.SelectedItem);
            e.Handled = true; return true;
        }
        // Enter executes a complete command; Tab accepts autocomplete without submitting.
        if (e.Key == VirtualKey.Enter && (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) == 0 && slashChoices.SelectedItem is SlashChoice partial && !SlashCatalog().Any(x => composer.Text.Trim().Equals("/" + x.Name, StringComparison.OrdinalIgnoreCase)))
        { CompleteSlash(partial); e.Handled = true; return true; }
        if (e.Key == VirtualKey.Tab && slashChoices.SelectedItem is SlashChoice choice) { CompleteSlash(choice); e.Handled = true; return true; }
        return false;
    }
    async Task<bool> ExecuteSlashCommandAsync()
    {
        var input = composer.Text.Trim();
        if (!input.StartsWith('/') || input.Contains('\n') || input.Contains('\r')) return false;
        var separator = input.IndexOfAny([' ', '\t']);
        var name = (separator < 0 ? input[1..] : input[1..separator]).ToLowerInvariant();
        var argument = separator < 0 ? "" : input[(separator + 1)..].Trim();
        if (!SlashCatalog().Any(x => x.Name == name))
        {
            ShowStatus(WorkflowText("Commande inconnue. Tapez /help ou utilisez Tab pour compléter.", "Unknown command. Type /help or use Tab to autocomplete."), StatusKind.Notice);
            return true;
        }
        if (name == "help") { RefreshSlashSuggestions(all: true); return true; }
        if (chat == null && name is "agents" or "plan" or "goal") return true;
        var owner = chat;
        switch (name)
        {
            case "settings": composer.Text = ""; await Settings(); return true;
            case "stop": generation?.Cancel(); if (owner != null) await terminals.StopChatAsync(owner.Id); break;
            case "agents":
                if (provider?.IsComposite == true) throw new InvalidOperationException(WorkflowText("Les agents sont imposés par le modèle composé.", "Agents are required by the composite model."));
                if (argument.Length == 0) { composer.Text = ""; await ConfigureAgentsAsync(owner!); return true; }
                if (int.TryParse(argument, out var count) && count is >= 1 and <= ConversationAgents.MaximumTeamSize)
                {
                    var agents = ConversationAgents.Read(owner!.AgentOptionsJson); agents.AutomaticCount = false; agents.Count = count;
                    owner.AgentOptionsJson = agents.Json(); owner.OrchestrationMode = "forced";
                }
                else owner!.OrchestrationMode = argument.ToLowerInvariant() switch { "off" or "disabled" => "disabled", "auto" => "auto", "on" or "forced" => "forced", _ => throw new ArgumentException("/agents off|auto|on|nombre") };
                break;
            case "plan":
                owner!.ExecutionMode = argument.ToLowerInvariant() switch { "" => owner.ExecutionMode == "plan" ? "execute" : "plan", "on" or "plan" => "plan", "off" or "execute" => "execute", _ => throw new ArgumentException("/plan on|off") }; break;
            case "goal":
                if (argument.Length == 0)
                {
                    var goal = new TextBox { Text = FeatureSettings.Read(state.FeaturesJson).ChatGoals.GetValueOrDefault(owner!.Id, ""), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000, MinHeight = 120 };
                    var dialog = new ContentDialog { XamlRoot = root.XamlRoot, Title = WorkflowText("Objectif de la conversation", "Conversation goal"), Content = goal, PrimaryButtonText = UiText.T("Enregistrer"), CloseButtonText = UiText.T("Annuler"), DefaultButton = ContentDialogButton.Primary };
                    if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return true;
                    argument = goal.Text.Trim();
                }
                if (argument.Length > 4000) throw new ArgumentException(WorkflowText("Objectif limité à 4 000 caractères.", "Goal is limited to 4,000 characters."));
                var goals = FeatureSettings.Read(state.FeaturesJson);
                if (argument.Equals("off", StringComparison.OrdinalIgnoreCase) || argument.Length == 0) goals.ChatGoals.Remove(owner!.Id); else goals.ChatGoals[owner!.Id] = argument;
                state.FeaturesJson = goals.Json(); break;
            case "thinking":
                if (argument is not ("auto" or "low" or "medium" or "high" or "none")) throw new ArgumentException("/thinking auto|low|medium|high|none");
                state.ThinkingLevel = argument; PopulateThinkingSelector(); break;
            case "retry":
                var retries = FeatureSettings.Read(state.FeaturesJson);
                retries.RetryEnabled = argument.ToLowerInvariant() switch { "" => !retries.RetryEnabled, "on" => true, "off" => false, _ => throw new ArgumentException("/retry on|off") };
                state.FeaturesJson = retries.Json(); break;
            case "theme":
                if (argument.Length == 0) { composer.Text = ""; await Settings(); return true; }
                var appearance = FeatureSettings.Read(state.FeaturesJson);
                var theme = AppearanceThemes.WithCustom(appearance.CustomThemes).FirstOrDefault(x => x.Id.Equals(argument, StringComparison.OrdinalIgnoreCase) || x.French.Equals(argument, StringComparison.OrdinalIgnoreCase) || x.English.Equals(argument, StringComparison.OrdinalIgnoreCase));
                if (theme == null) throw new ArgumentException(WorkflowText("Thème inconnu : ", "Unknown theme: ") + argument);
                appearance.Theme = theme.Id; state.FeaturesJson = appearance.Json(); ApplyAppearance(); break;
            case "model":
                if (argument.Length == 0) { composer.Text = ""; composerInfoExpanded = true; UpdateInfoPanel(); modelSelector.Focus(FocusState.Programmatic); modelSelector.IsDropDownOpen = true; return true; }
                if (provider == null || !ProviderModels.Visible(provider).Contains(argument)) throw new ArgumentException(WorkflowText("Modèle indisponible chez ce fournisseur.", "Model unavailable from this provider."));
                provider.Model = argument; PopulateModelSelector(); UpdateProvider(); break;
        }
        await db.SaveChangesAsync(); composer.Text = ""; RefreshModelActivity();
        ShowStatus(WorkflowText("Réglage enregistré · ", "Setting saved · ") + "/" + name, StatusKind.Notice);
        return true;
    }
}
