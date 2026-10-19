using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    async Task SaveActionSuggestionAsync(ConversationRun run, ActionSuggestion proposal, CancellationToken ct)
    {
        if (!Skills.Enabled(RunSkills(run), ActionSuggestions.SkillId)) throw new UnauthorizedAccessException("Skill désactivé / Skill disabled.");
        var message = new Message { ChatId = run.Chat.Id, Role = "interaction", State = "ui",
            Content = ActionSuggestions.Summary(proposal), WireJson = ActionSuggestions.Wire(proposal, run.SelectedProviderId) };
        run.Db.Messages.Add(message); await run.Db.SaveChangesAsync(ct);
        RenderActionSuggestion(message, run.Messages); ScrollRunToBottom(run);
    }

    void RenderActionSuggestion(Message message, StackPanel target)
    {
        var data = ActionSuggestions.Read(message);
        if (data == null) { AddMessage("info", message.Content, [], target); return; }
        var card = new Border { Padding = new(16), CornerRadius = new(12), BorderThickness = new(1),
            BorderBrush = FluentDesign.Stroke, Background = FluentDesign.Resource("AssistantMessageFillBrush") };
        target.Children.Add(card);
        void Render()
        {
            var value = ActionSuggestions.Read(message)?.Suggestion;
            if (value == null) return;
            var panel = new StackPanel { Spacing = 10 };
            var title = new Grid { ColumnSpacing = 8 };
            title.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); title.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); title.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            title.Children.Add(FluentDesign.Icon("\uE945", 18));
            var heading = new TextBlock { Text = value.Title, TextWrapping = TextWrapping.Wrap, FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = FluentDesign.Primary };
            Grid.SetColumn(heading, 1); title.Children.Add(heading);
            var dismiss = new Button { Content = FluentDesign.Icon("\uE711", 11), Padding = new(6), MinWidth = 28, MinHeight = 28,
                Background = FluentDesign.Resource("TransparentBrush"), BorderThickness = new(0), Visibility = value.Selected.Length == 0 ? Visibility.Visible : Visibility.Collapsed };
            AutomationProperties.SetName(dismiss, WorkflowText("Ignorer la proposition", "Dismiss suggestion"));
            Grid.SetColumn(dismiss, 2); title.Children.Add(dismiss); panel.Children.Add(title);
            if (value.Description.Length > 0) panel.Children.Add(new TextBlock { Text = value.Description, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = FluentDesign.Secondary });
            var buttons = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            var list = new List<Button>();
            foreach (var action in value.Actions)
            {
                var copy = new StackPanel { Spacing = 4 };
                var label = new Grid { ColumnSpacing = 8 };
                label.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); label.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                label.Children.Add(FluentDesign.Icon(action.Kind switch { "launch_agent" => "\uE768", "new_chat" => "\uE8F2", "enqueue" => "\uE8FD", _ => "\uE72A" }, 16));
                var actionLabel = new TextBlock { Text = action.Label, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = FluentDesign.Primary };
                Grid.SetColumn(actionLabel, 1); label.Children.Add(actionLabel);
                copy.Children.Add(label);
                if (action.Description.Length > 0) copy.Children.Add(new TextBlock { Text = action.Description, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = FluentDesign.Secondary });
                var button = new Button { Content = copy, Padding = new(12), MinHeight = 58,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    IsEnabled = value.Selected.Length == 0 && Skills.Enabled(state.EnabledSkills, ActionSuggestions.SkillId),
                    BorderBrush = value.Selected == action.Id ? FluentDesign.Resource("AccentFillColorDefaultBrush") : FluentDesign.Stroke,
                    BorderThickness = new(1), CornerRadius = new(8) };
                AutomationProperties.SetName(button, action.Label);
                ToolTipService.SetToolTip(button, WorkflowText("Consigne envoyée après votre clic :\n", "Prompt sent after your click:\n") + action.Prompt[..Math.Min(500, action.Prompt.Length)]);
                button.Click += async (_, _) => await Guard(async () => {
                    foreach (var b in list) b.IsEnabled = false; dismiss.IsEnabled = false;
                    try { await SelectSuggestedActionAsync(message, action.Id); }
                    finally { Render(); }
                });
                list.Add(button); buttons.Children.Add(button);
            }
            void Arrange() {
                int cols = buttons.ActualWidth >= 850 ? 4 : buttons.ActualWidth >= 480 ? 2 : 1;
                if (buttons.ColumnDefinitions.Count == cols && buttons.RowDefinitions.Count > 0) return;
                buttons.ColumnDefinitions.Clear(); buttons.RowDefinitions.Clear();
                for (int i = 0; i < cols; i++) buttons.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                for (int i = 0; i < (list.Count + cols - 1) / cols; i++) buttons.RowDefinitions.Add(new() { Height = GridLength.Auto });
                for (int i = 0; i < list.Count; i++) { Grid.SetColumn(list[i], i % cols); Grid.SetRow(list[i], i / cols); }
            }
            Arrange(); buttons.SizeChanged += (_, _) => Arrange(); panel.Children.Add(buttons);
            var prompts = new StackPanel { Spacing = 10 };
            foreach (var action in value.Actions)
            {
                prompts.Children.Add(new TextBlock { Text = action.Label, Foreground = FluentDesign.Primary, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                prompts.Children.Add(new TextBlock { Text = action.Prompt, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Foreground = FluentDesign.Secondary, FontSize = 12 });
            }
            panel.Children.Add(new Expander { Header = WorkflowText("Consignes des actions", "Action prompts"), IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new ScrollViewer { Content = prompts, MaxHeight = 200, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            panel.Children.Add(new TextBlock { FontSize = 12, Foreground = FluentDesign.Secondary, TextWrapping = TextWrapping.Wrap,
                Text = value.Selected == "dismissed" ? WorkflowText("Proposition ignorée", "Suggestion dismissed") : value.Selected.Length > 0
                    ? WorkflowText("Choisi : ", "Selected: ") + value.Actions.Single(a => a.Id == value.Selected).Label
                    : !Skills.Enabled(state.EnabledSkills, ActionSuggestions.SkillId) ? WorkflowText("Skill Propositions d’actions désactivé", "Action suggestions skill disabled")
                    : WorkflowText("Aucune action ne démarre sans votre clic. Les autorisations habituelles s’appliquent.", "No action starts without your click. Normal tool permissions apply.") });
            dismiss.Click += async (_, _) => await Guard(async () => { dismiss.IsEnabled = false; try { await SelectSuggestedActionAsync(message, "dismissed"); } finally { Render(); } });
            card.Child = panel;
        }
        Render();
    }

    async Task SelectSuggestedActionAsync(Message view, string choice)
    {
        if (databaseMaintenanceBusy || conversationRetentionBusy) throw new InvalidOperationException(WorkflowText("Attendez la fin de l’entretien.", "Wait for maintenance to finish."));
        await using var store = new HarnessDb();
        var saved = await store.Messages.AsNoTracking().SingleAsync(m => m.Id == view.Id && m.ChatId == view.ChatId);
        var (proposal, providerId) = ActionSuggestions.Read(saved) ?? throw new InvalidOperationException("Proposition invalide / Invalid suggestion");
        if (proposal.Selected.Length > 0) { view.Content = saved.Content; view.WireJson = saved.WireJson; throw new InvalidOperationException(WorkflowText("Cette proposition a déjà été traitée.", "This suggestion has already been handled.")); }
        if (!Skills.Enabled(state.EnabledSkills, ActionSuggestions.SkillId) && choice != "dismissed") throw new UnauthorizedAccessException(WorkflowText("Le skill Propositions d’actions est désactivé.", "The Action suggestions skill is disabled."));
        var selected = choice == "dismissed" ? null : proposal.Actions.Single(a => a.Id == choice);
        var owner = await store.Chats.AsNoTracking().SingleAsync(c => c.Id == saved.ChatId);
        if (selected != null && ConversationModes.IsChat(owner.InteractionMode)) throw new InvalidOperationException(WorkflowText("Passez en mode Agent pour utiliser ces actions.", "Switch to Agent mode to use these actions."));
        var project = await store.Projects.AsNoTracking().SingleAsync(p => p.Id == owner.ProjectId);
        if (selected != null)
        {
            var sourceProvider = await store.Providers.AsNoTracking().SingleOrDefaultAsync(p => p.Id == providerId) ?? throw new InvalidOperationException(WorkflowText("Le fournisseur d’origine a été supprimé.", "The original provider has been removed."));
            if (selected.Kind == "launch_agent" && (sourceProvider.IsExternalAgent || sourceProvider.IsComposite)) throw new InvalidOperationException(WorkflowText("Choisissez un fournisseur direct pour lancer cet agent.", "Choose a direct provider to launch this agent."));
            ResourceAccessPolicy.DemandProvider(owner, sourceProvider);
        }
        var result = proposal with { Selected = choice };
        var content = ActionSuggestions.Summary(result); var wire = ActionSuggestions.Wire(result, providerId);
        var destination = owner;
        await using (var transaction = await store.Database.BeginTransactionAsync())
        {
            // Claim and enqueue atomically. Reopening or double-clicking cannot replay the action.
            int claimed = await store.Messages.Where(m => m.Id == saved.Id && m.ChatId == saved.ChatId && m.WireJson == saved.WireJson)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.Content, content).SetProperty(m => m.WireJson, wire));
            if (claimed != 1) throw new InvalidOperationException(WorkflowText("Proposition déjà traitée. Rouvrez la conversation.", "Suggestion already handled. Reopen the conversation."));
            if (selected != null)
            {
                if (selected.Kind is "new_chat" or "launch_agent")
                {
                    destination = new Chat { ProjectId = owner.ProjectId, Title = selected.Label, InteractionMode = owner.InteractionMode,
                        ExecutionMode = owner.ExecutionMode, OrchestrationMode = owner.OrchestrationMode, AgentOptionsJson = owner.AgentOptionsJson,
                        SandboxEnabled = owner.SandboxEnabled, ResourcePathsJson = ProjectResources.Serialize(ProjectResources.For(owner, project, store.FilePath)), AllowOutsideResources = owner.AllowOutsideResources,
                        ChatWebEnabled = owner.ChatWebEnabled, ChatPythonEnabled = owner.ChatPythonEnabled };
                    if (selected.Kind == "launch_agent")
                    {
                        // A human click explicitly requests one agent. Preserve execution/resource restrictions.
                        var agents = ConversationAgents.Read(owner.AgentOptionsJson); agents.AutomaticCount = false; agents.Count = 1; agents.AutomaticRoles = true;
                        destination.AgentOptionsJson = System.Text.Json.JsonSerializer.Serialize(agents); destination.OrchestrationMode = "forced";
                    }
                    store.Chats.Add(destination); await store.SaveChangesAsync();
                }
                long last = await store.PendingInputs.Where(i => i.ChatId == destination.Id).Select(i => (long?)(i.SortOrder == 0 ? i.Id : i.SortOrder)).MaxAsync() ?? 0;
                store.PendingInputs.Add(new PendingInput { ChatId = destination.Id, ProviderId = providerId, Text = selected.Prompt, SortOrder = last + 1 });
                await store.SaveChangesAsync();
            }
            await transaction.CommitAsync();
        }
        view.Content = content; view.WireJson = wire;
        if (conversationRuns.TryGetValue(saved.ChatId, out var current))
        {
            var tracked = current.Db.Messages.Local.FirstOrDefault(m => m.Id == saved.Id);
            if (tracked != null) { tracked.Content = content; tracked.WireJson = wire; }
        }
        if (conversationHistory.TryGetValue(saved.ChatId, out var history))
            foreach (var item in history.Where(m => m.Id == saved.Id)) { item.Content = content; item.WireJson = wire; }
        if (selected == null) return;
        await RefreshInboxAsync();
        if (destination.Id != owner.Id)
        {
            SaveConversationDraft(); state.ProjectId = destination.ProjectId; state.ChatId = destination.Id;
            loading = true;
            try { projects.SelectedItem = (projects.ItemsSource as IEnumerable<Project>)?.FirstOrDefault(p => p.Id == destination.ProjectId) ?? project; }
            finally { loading = false; }
            await db.SaveChangesAsync();
            await SelectProject();
        }
        ShowStatus(selected.Kind == "enqueue" ? WorkflowText("Tâche ajoutée à la file.", "Task queued.") : WorkflowText("Action sélectionnée · démarrage…", "Action selected · starting…"));
        if (selected.Kind != "enqueue" && !conversationRuns.ContainsKey(destination.Id))
        {
            // Keep the click handler short: execution has its own error handling and queue persistence.
            _ = Guard(() => RunNextQueuedAsync(destination.Id, chat?.Id == destination.Id ? messages : CreateMessagePanel()));
        }
    }
}
