using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MonolithHarness.Core;
using Windows.System;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    sealed record SlashChoice(string Name, string Arguments, string Description, bool IsSkill = false)
    { public override string ToString() => "/" + Name + " " + Arguments + "  ·  " + Description; }
    readonly ListView slashChoices = new() { MaxHeight = 210, SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
    readonly Border slashSuggestions = new() { Background = FluentDesign.Card, BorderBrush = FluentDesign.Stroke, BorderThickness = new(1), CornerRadius = new(8), Visibility = Visibility.Collapsed };
    bool slashDismissed, completingSlash, slashRefreshQueued, slashShowingHelp;
    SkillInvocation.CompletionToken? currentSlashToken;
    string slashDraft = "";
    SlashChoice[] CommandSlashCatalog() =>
    [
        new("chat", "", WorkflowText("Discussion, web et Python en option", "Conversation, web and optional Python")),
        new("agent", "", WorkflowText("Outils, plan et sous-agents", "Tools, planning and subagents")),
        new("agents", "[off|auto|on|nombre]", WorkflowText("Sous-agents et rôles de la conversation", "Conversation subagents and roles")),
        new("proposition", "", WorkflowText("Préparer des fichiers pour revue", "Prepare files for review")),
        new("proposals", "", WorkflowText("Réviser les fichiers proposés", "Review proposed files")),
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
    SlashChoice[] SlashCatalog() => [.. CommandSlashCatalog(), .. Skills.Available(project?.GetSourceFolders(), project?.Id ?? 0)
        .Where(s => !CommandSlashCatalog().Any(c => c.Name.Equals(s.Id, StringComparison.OrdinalIgnoreCase)))
        .Select(s => new SlashChoice(s.Id, "", WorkflowText("Skill · ", "Skill · ") + WorkflowText(s.FrenchName, s.EnglishName)
            + (ChatInteraction && s.Id is not ("web" or "python") ? WorkflowText(" · Mode Agent", " · Agent mode") : "")
            + (!Skills.Enabled(state.EnabledSkills, s.Id) ? WorkflowText(" · Désactivé", " · Disabled") : ""), IsSkill: true))];
    UIElement BuildSlashSuggestions()
    {
        slashSuggestions.Child = slashChoices;
        composer.TextChanged += (_, _) =>
        {
            if (completingSlash) return;
            slashDismissed = slashShowingHelp = false; ScheduleSlashRefresh();
        };
        composer.SelectionChanged += (_, _) => { if (!completingSlash) ScheduleSlashRefresh(); };
        composer.GotFocus += (_, _) => { if (!completingSlash) { slashDismissed = false; ScheduleSlashRefresh(); } };
        slashChoices.ItemClick += (_, e) => { if (e.ClickedItem is SlashChoice choice) CompleteSlash(choice); };
        return slashSuggestions;
    }
    void ScheduleSlashRefresh()
    {
        // TextChanged can precede the native editor's caret update, especially after paste.
        // Coalesce both events and read the final caret once they have finished.
        if (slashRefreshQueued) return;
        slashRefreshQueued = true;
        if (!DispatcherQueue.TryEnqueue(() => { slashRefreshQueued = false; RefreshSlashSuggestions(); })) slashRefreshQueued = false;
    }
    void RefreshSlashSuggestions(bool all = false)
    {
        var text = composer.Text;
        var token = SkillInvocation.CompletionAt(text, composer.SelectionStart, composer.SelectionLength);
        if (all) slashShowingHelp = true;
        if (slashShowingHelp && token == null) token = SkillInvocation.CompletionAt(text, text.TrimEnd().Length);
        if (slashDismissed || !slashShowingHelp && token == null)
        { currentSlashToken = null; slashSuggestions.Visibility = Visibility.Collapsed; return; }
        var selected = (slashChoices.SelectedItem as SlashChoice)?.Name;
        var items = SlashCatalog()
            .Where(x => slashShowingHelp || token?.AtMessageStart == true || x.IsSkill)
            .Where(x => !ChatInteraction || x.IsSkill || x.Name is not ("agents" or "plan" or "proposition" or "proposals" or "goal"))
            .Where(x => slashShowingHelp || x.Name.StartsWith(token!.Prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        currentSlashToken = token; slashDraft = text;
        slashChoices.ItemsSource = items;
        var selectedIndex = Array.FindIndex(items, item => item.Name == selected);
        slashChoices.SelectedIndex = selectedIndex >= 0 ? selectedIndex : items.Length > 0 ? 0 : -1;
        slashSuggestions.Visibility = items.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    void CompleteSlash(SlashChoice choice, bool afterKey = false)
    {
        var text = composer.Text;
        // Clicking a suggestion moves focus; preserve the token captured while editing.
        var token = text == slashDraft ? currentSlashToken : SkillInvocation.CompletionAt(text, composer.SelectionStart, composer.SelectionLength);
        if (token == null) return;
        var end = token.Start + token.Length;
        var replacement = "/" + choice.Name;
        if (end == text.Length || !char.IsWhiteSpace(text[end])) replacement += " ";
        var caret = token.Start + replacement.Length;
        if (end < text.Length && text[end] == ' ') caret++;
        var completed = text[..token.Start] + replacement + text[end..];
        var originalCaret = composer.SelectionStart;
        void Apply()
        {
            // Ignore unrelated edits queued after this key. Some native editors still
            // insert the completion key's newline before handing control back to Uno.
            var current = composer.Text;
            if (current != text && !(afterKey && new[] { "\r", "\n", "\r\n", "\t" }
                .Any(key => current == text.Insert(Math.Clamp(originalCaret, 0, text.Length), key)))) return;
            completingSlash = true;
            try
            {
                composer.Text = completed;
                composer.Select(caret, 0);
                if (composer.FocusState == FocusState.Unfocused) composer.Focus(FocusState.Programmatic);
            }
            finally { completingSlash = false; }
            slashDismissed = true; slashShowingHelp = false; currentSlashToken = null;
            slashSuggestions.Visibility = Visibility.Collapsed;
        }
        if (afterKey) DispatcherQueue.TryEnqueue(Apply);
        else Apply();
    }
    bool HandleSlashKey(KeyRoutedEventArgs e)
    {
        // Finish pending caret updates before deciding whether Enter completes or sends.
        RefreshSlashSuggestions();
        if (slashSuggestions.Visibility != Visibility.Visible) return false;
        if (e.Key == VirtualKey.Escape) { slashDismissed = true; slashShowingHelp = false; slashSuggestions.Visibility = Visibility.Collapsed; e.Handled = true; return true; }
        if (e.Key is VirtualKey.Up or VirtualKey.Down)
        {
            var count = slashChoices.Items.Count;
            if (count > 0) slashChoices.SelectedIndex = (Math.Max(0, slashChoices.SelectedIndex) + (e.Key == VirtualKey.Down ? 1 : count - 1)) % count;
            if (slashChoices.SelectedItem != null) slashChoices.ScrollIntoView(slashChoices.SelectedItem);
            e.Handled = true; return true;
        }
        var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (e.Key == VirtualKey.Enter && !shift && slashChoices.SelectedItem is SlashChoice partial
            && (partial.IsSkill || !composer.Text.Trim().Equals("/" + partial.Name, StringComparison.OrdinalIgnoreCase)))
        { e.Handled = true; CompleteSlash(partial, afterKey: true); return true; }
        if (e.Key == VirtualKey.Tab && slashChoices.SelectedItem is SlashChoice choice) { e.Handled = true; CompleteSlash(choice, afterKey: true); return true; }
        return false;
    }
    async Task<bool> ExecuteSlashCommandAsync()
    {
        var input = composer.Text.Trim();
        if (!input.StartsWith('/') || input.Contains('\n') || input.Contains('\r')) return false;
        var separator = input.IndexOfAny([' ', '\t']);
        var name = (separator < 0 ? input[1..] : input[1..separator]).ToLowerInvariant();
        var argument = separator < 0 ? "" : input[(separator + 1)..].Trim();
        if (SkillInvocation.Requested(input, project?.GetSourceFolders(), project?.Id ?? 0).Any(s => s.Id.Equals(name.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}'), StringComparison.OrdinalIgnoreCase)))
        { slashSuggestions.Visibility = Visibility.Collapsed; return false; }
        if (!CommandSlashCatalog().Any(x => x.Name == name))
        {
            ShowStatus(WorkflowText("Commande inconnue. Tapez /help ou utilisez Tab pour compléter.", "Unknown command. Type /help or use Tab to autocomplete."), StatusKind.Notice);
            return true;
        }
        if (name == "help") { RefreshSlashSuggestions(all: true); return true; }
        if (chat == null && name is "chat" or "agent" or "agents" or "plan" or "proposition" or "proposals" or "goal") return true;
        if (ChatInteraction && name is "agents" or "plan" or "proposition" or "proposals" or "goal")
            throw new InvalidOperationException(WorkflowText("Passez en Agent pour configurer le plan et les sous-agents.", "Switch to Agent to configure planning and subagents."));
        var owner = chat;
        switch (name)
        {
            case "chat": case "agent":
                await ChangeConversationModeAsync(name); composer.Text = ""; return true;
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
            case "proposals": composer.Text = ""; await ReviewFileProposalsAsync(); return true;
            case "proposition": if (owner != null) owner.ExecutionMode = "propose"; break;
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
                if (argument.Length == 0) { composer.Text = ""; OpenModelOptions(chooseModel: true); return true; }
                if (provider == null || !ProviderModels.Visible(provider).Contains(argument)) throw new ArgumentException(WorkflowText("Modèle indisponible chez ce fournisseur.", "Model unavailable from this provider."));
                provider.Model = argument; PopulateModelSelector(); UpdateProvider(); break;
        }
        await db.SaveChangesAsync(); composer.Text = ""; RefreshModelActivity(); RefreshGenerationControls();
        ShowStatus(WorkflowText("Réglage enregistré · ", "Setting saved · ") + "/" + name, StatusKind.Notice);
        return true;
    }
}
