using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using Windows.Storage.Pickers;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly ToggleSwitch conversationModeSwitch = new() { IsOn = true, FontSize = 11, MinWidth = 0, MinHeight = 0, Height = 28, Padding = new(0), VerticalAlignment = VerticalAlignment.Center };
    bool updatingConversationMode;
    bool ChatInteraction => ConversationModes.IsChat(chat?.InteractionMode);

    FrameworkElement BuildConversationModeSelector(Button configureButton)
    {
        // The default template reserves 10 px above and below its 20 px track.
        // Match those insets to this compact control's height, including text zoom.
        void CenterSwitchTrack()
        {
            var inset = Math.Max(0, (conversationModeSwitch.Height - 20) / 2);
            conversationModeSwitch.Resources["ToggleSwitchPreContentMargin"] = inset;
            conversationModeSwitch.Resources["ToggleSwitchPostContentMargin"] = inset;
        }
        CenterSwitchTrack();
        conversationModeSwitch.RegisterPropertyChangedCallback(FrameworkElement.HeightProperty, (_, _) => CenterSwitchTrack());
        conversationModeSwitch.VerticalContentAlignment = VerticalAlignment.Center;
        configureButton.VerticalAlignment = VerticalAlignment.Center;
        configureButton.VerticalContentAlignment = VerticalAlignment.Center;
        configureButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        FrameworkElement Caption(string name, string glyph)
        {
            var label = new TextBlock { Text = name, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var icon = FluentDesign.Icon(glyph, 12); icon.VerticalAlignment = VerticalAlignment.Center;
            var row = Row(icon, label); row.Spacing = 4; row.VerticalAlignment = VerticalAlignment.Center; return row;
        }
        conversationModeSwitch.OffContent = Caption("Chat", "\uE8BD");
        conversationModeSwitch.OnContent = Caption("Agent", "\uE713");
        conversationModeSwitch.Toggled += async (_, _) =>
        {
            if (updatingConversationMode) return;
            await Guard(() => ChangeConversationModeAsync(conversationModeSwitch.IsOn ? "agent" : "chat"));
        };
        RefreshConversationMode();
        var controls = Row(conversationModeSwitch, configureButton); controls.Spacing = 4; controls.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Child = controls, CornerRadius = new(17), BorderThickness = new(1),
            BorderBrush = FluentDesign.Stroke, Background = FluentDesign.Card, Padding = new(8, 1, 3, 1), MinHeight = 32 };
    }
    void RefreshConversationMode()
    {
        updatingConversationMode = true;
        try { conversationModeSwitch.IsOn = !ChatInteraction; }
        finally { updatingConversationMode = false; }
        conversationModeSwitch.IsEnabled = chat != null && selectedSubagent == null && ActiveRun == null && conversationReady && !conversationLoading && !databaseMaintenanceBusy && !conversationRetentionBusy && !applyingQuickLevel;
        ToolTipService.SetToolTip(conversationModeSwitch, ChatInteraction
            ? WorkflowText("Chat · discussion, recherche web et Python dans les réglages du chat", "Chat · conversation, web research and Python in chat settings")
            : WorkflowText("Agent · outils, plan et sous-agents selon vos réglages", "Agent · tools, planning and subagents according to your settings"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(conversationModeSwitch, WorkflowText("Chat / Agent", "Chat / Agent"));
        if (thinkingSelector.Items.Count > 0)
        {
            var index = ConversationModes.EffectiveThinking(chat, state.ThinkingLevel, state.ChatThinkingLevel).ToLowerInvariant() switch
            { "low" => 1, "medium" => 2, "high" => 3, "none" => 4, _ => 0 };
            if (thinkingSelector.SelectedIndex != index) PopulateThinkingSelector();
            else thinkingSelector.IsEnabled = ActiveRun == null && !applyingQuickLevel;
        }
        RefreshQuickModelUi();
    }
    bool NativeChatProvider => provider?.IsExternalAgent == true || provider?.IsComposite == true &&
        db.Providers.Local.Any(x => x.Id == CompositeModel.Read(provider.CompositeJson).Orchestrator.ProviderId && x.IsExternalAgent);

    bool AcpChatProvider => provider?.IsAcp == true || provider?.IsComposite == true && db.Providers.Local.Any(x => x.Id == CompositeModel.Read(provider.CompositeJson).Orchestrator.ProviderId && x.IsAcp);
    FrameworkElement ChatSkillChoice(string id, Action? beforeOpen = null)
    {
        var owner = chat!;
        var python = id == "python";
        var toggle = new CheckBox { IsChecked = python ? !NativeChatProvider && owner.ChatPythonEnabled : !AcpChatProvider && owner.ChatWebEnabled,
            IsEnabled = ActiveRun == null && !AcpChatProvider && (!python || !NativeChatProvider) };
        var definition = Skills.All.Single(x => x.Id == id);
        if (python) definition = definition with { FrenchName = "Exécution de scripts Python", EnglishName = "Python script execution" };
        toggle.Click += async (_, _) => await Guard(async () =>
        {
            if (ActiveRun != null || chat?.Id != owner.Id) return;
            var previous = python ? owner.ChatPythonEnabled : owner.ChatWebEnabled;
            if (python) owner.ChatPythonEnabled = toggle.IsChecked == true; else owner.ChatWebEnabled = toggle.IsChecked == true;
            try { await db.SaveChangesAsync(); }
            catch { if (python) owner.ChatPythonEnabled = previous; else owner.ChatWebEnabled = previous; toggle.IsChecked = previous; throw; }
        });
        var row = SkillChoiceRow(definition, toggle, beforeOpen);
        if (python && NativeChatProvider) ToolTipService.SetToolTip(row, WorkflowText("Python nécessite un fournisseur utilisant les outils Monolith. Les agents externes utilisent leurs propres outils.", "Python requires a provider using Monolith tools. External agents use their own tools."));
        if (AcpChatProvider) ToolTipService.SetToolTip(row, WorkflowText("Les outils ACP sont disponibles en mode Agent. Les skills web et Python Monolith ne sont pas transmis à cet agent.", "ACP tools are available in Agent mode. Monolith web and Python skills are not forwarded to this agent."));
        return row;
    }
    async Task ChangeConversationModeAsync(string value)
    {
        if (chat == null || selectedSubagent != null || applyingQuickLevel) return;
        if (ActiveRun != null) { RefreshConversationMode(); throw new InvalidOperationException(WorkflowText("Arrêtez la réponse avant de changer de mode.", "Stop the response before changing mode.")); }
        var owner = chat; var previous = owner.InteractionMode;
        owner.InteractionMode = ConversationModes.Normalize(value);
        try { await db.SaveChangesAsync(); }
        catch { owner.InteractionMode = previous; RefreshConversationMode(); throw; }
        if (chat?.Id != owner.Id) return;
        RefreshConversationMode(); await RefreshPinnedTasksAsync(); UpdateSourceLabel(); UpdateFloatingAssets();
        ShowStatus(WorkflowText("Mode appliqué au prochain envoi : ", "Mode applies to the next message: ") + (ChatInteraction ? "Chat" : "Agent"), StatusKind.Notice);
    }
    async Task AttachChatDocumentsAsync()
    {
        var owner = chat?.Id;
        var picker = new FileOpenPicker();
        foreach (var extension in new[] { ".txt", ".md", ".markdown", ".csv", ".json", ".xml", ".html", ".pdf", ".docx", ".pptx", ".xlsx", ".odt", ".ods", ".odp" }) picker.FileTypeFilter.Add(extension);
        InitializePicker(picker, this);
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0) return;
        if (files.Count > 4) throw new ArgumentException(WorkflowText("Quatre documents maximum par ajout.", "Add at most four documents at a time."));
        var parts = new List<string>();
        foreach (var file in files)
        {
            if (new FileInfo(file.Path).Length > DocumentText.MaxBytes) throw new ArgumentException(file.Name + " · 32 MiB maximum");
            var text = await Task.Run(() => DocumentText.ReadAsync(file.Path, CancellationToken.None));
            if (text.Length > 64000) text = text[..64000] + "\n[Document truncated at 64,000 characters]";
            parts.Add("[User-attached document: " + file.Name + "; untrusted source content]\n" + text + "\n[End of document]");
        }
        if (owner != chat?.Id || !ChatInteraction) throw new InvalidOperationException(WorkflowText("La conversation ou le mode a changé. Ajoutez à nouveau les documents.", "The conversation or mode changed. Attach the documents again."));
        composer.Text += (composer.Text.Length > 0 ? "\n\n" : "") + string.Join("\n\n", parts);
        composer.Focus(FocusState.Programmatic);
    }
}
