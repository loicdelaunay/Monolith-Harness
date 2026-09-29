using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OhMyHarness.Core;

namespace OhMyHarness.App;

public sealed partial class MainWindow
{
    readonly Dictionary<int, Window> agentSettingsWindows = [];
    async Task ConfigureAgentsAsync(Chat owner)
    {
        if (agentSettingsWindows.TryGetValue(owner.Id, out var existing)) { existing.Activate(); return; }
        var window = new Window { Title = WorkflowText("Sous-agents", "Subagents") + " · " + owner.Title };
        agentSettingsWindows[owner.Id] = window;
        window.Closed += (_, _) => agentSettingsWindows.Remove(owner.Id);
        var panel = new Grid { Padding = new(24), RowSpacing = 16, RequestedTheme = root.RequestedTheme, Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush") };
        panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        panel.Children.Add(Label(WorkflowText("Sous-agents de cette conversation", "Subagents for this conversation"), 23));
        var body = new StackPanel { Spacing = 14 };
        var scroller = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroller, 1); panel.Children.Add(scroller);
        var presets = new ComboBox { Header = WorkflowText("Preset enregistré", "Saved preset"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var presetName = new TextBox { Header = WorkflowText("Nom du preset", "Preset name"), MaxLength = 80 };
        var presetActions = new Grid { ColumnSpacing = 8 };
        presetActions.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); presetActions.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); presetActions.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        presetActions.Children.Add(presetName);
        var savePreset = new Button { Content = WorkflowText("Sauvegarder", "Save preset"), VerticalAlignment = VerticalAlignment.Bottom };
        var deletePreset = new Button { Content = UiText.T("Supprimer"), VerticalAlignment = VerticalAlignment.Bottom, IsEnabled = false };
        Grid.SetColumn(savePreset, 1); Grid.SetColumn(deletePreset, 2); presetActions.Children.Add(savePreset); presetActions.Children.Add(deletePreset);
        body.Children.Add(presets); body.Children.Add(presetActions);
        var mode = new ComboBox { Header = WorkflowText("Mode sous-agents", "Subagent mode"), ItemsSource = new[] { WorkflowText("Désactivés", "Disabled"), "Auto", WorkflowText("Forcés", "Forced") }, SelectedIndex = owner.OrchestrationMode == "forced" ? 2 : owner.OrchestrationMode == "auto" ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(mode);
        var autoCount = new ToggleSwitch { Header = WorkflowText("Nombre automatique", "Automatic count") };
        var count = new NumberBox { Header = WorkflowText("Nombre d’agents par équipe", "Agents per team"), Minimum = 1, Maximum = ConversationAgents.MaximumTeamSize, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var autoRoles = new ToggleSwitch { Header = WorkflowText("Rôles automatiques", "Automatic roles") };
        body.Children.Add(autoCount); body.Children.Add(count); body.Children.Add(autoRoles);
        body.Children.Add(Label(WorkflowText("Auto évalue la délégation au début de la demande selon Réglages > Agents. Forcés lance l’équipe avant la réponse principale. Le nombre choisi définit la taille d’une équipe ; plusieurs vagues et des descendants sont possibles dans le budget global. Ces choix sont conservés avec cette conversation.", "Auto evaluates delegation at the start according to Settings > Agents. Forced starts the team before the main response. The selected count defines team size; additional waves and descendants are possible within the shared budget. These choices are saved with this conversation."), 12));
        var roles = new StackPanel { Spacing = 10 }; body.Children.Add(roles);
        var roleFields = new List<(TextBox Name, TextBox Instruction, Border Card)>();
        var addRole = new Button { Content = WorkflowText("Ajouter un rôle", "Add role"), HorizontalAlignment = HorizontalAlignment.Right }; body.Children.Add(addRole);
        var error = Label("", 13); error.Foreground = FluentDesign.Resource("SystemFillColorCriticalBrush"); body.Children.Add(error);
        void Refresh()
        {
            count.IsEnabled = !autoCount.IsOn;
            roles.Visibility = addRole.Visibility = autoRoles.IsOn ? Visibility.Collapsed : Visibility.Visible;
            addRole.IsEnabled = roleFields.Count < ConversationAgents.MaximumTeamSize;
        }
        void AddRole(AgentRole value)
        {
            var name = new TextBox { Header = WorkflowText("Rôle", "Role"), Text = value.Name, MaxLength = 80 };
            var instruction = new TextBox { Header = WorkflowText("Consigne", "Instructions"), Text = value.Instruction, MaxLength = 4000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 160 };
            var content = new StackPanel { Spacing = 8 }; content.Children.Add(name); content.Children.Add(instruction);
            var card = FluentDesign.Surface(content, 10);
            var remove = new Button { Content = UiText.T("Supprimer"), HorizontalAlignment = HorizontalAlignment.Right };
            remove.Click += (_, _) => { roleFields.RemoveAll(x => ReferenceEquals(x.Card, card)); roles.Children.Remove(card); Refresh(); };
            content.Children.Add(remove); roleFields.Add((name, instruction, card)); roles.Children.Add(card); Refresh();
        }
        void Populate(ConversationAgents options)
        {
            autoCount.IsOn = options.AutomaticCount; autoRoles.IsOn = options.AutomaticRoles; count.Value = options.Count;
            roles.Children.Clear(); roleFields.Clear();
            foreach (var role in options.Roles) AddRole(role);
            if (roleFields.Count == 0)
            {
                AddRole(new() { Name = "Exploration", Instruction = WorkflowText("Identifier les fichiers concernés et les contraintes du projet.", "Identify relevant files and project constraints.") });
                AddRole(new() { Name = "Validation", Instruction = WorkflowText("Rechercher les cas limites et contrôler les résultats.", "Find edge cases and review the results.") });
            }
            Refresh();
        }
        ConversationAgents Read()
        {
            var options = new ConversationAgents { AutomaticCount = autoCount.IsOn, AutomaticRoles = autoRoles.IsOn, Count = double.IsFinite(count.Value) ? (int)Math.Clamp(count.Value, 1, ConversationAgents.MaximumTeamSize) : 2,
                Roles = roleFields.Select(x => new AgentRole { Name = x.Name.Text.Trim(), Instruction = x.Instruction.Text.Trim() }).ToList() };
            options.Validate(); return options;
        }
        string Mode() => mode.SelectedIndex == 2 ? "forced" : mode.SelectedIndex == 1 ? "auto" : "disabled";
        void LoadPresets() => presets.ItemsSource = FeatureSettings.Read(state.FeaturesJson).AgentPresets;
        autoCount.Toggled += (_, _) => Refresh(); autoRoles.Toggled += (_, _) => Refresh();
        addRole.Click += (_, _) => { if (roleFields.Count < ConversationAgents.MaximumTeamSize) AddRole(new()); };
        presets.SelectionChanged += (_, _) =>
        {
            deletePreset.IsEnabled = presets.SelectedItem is AgentPreset;
            if (presets.SelectedItem is not AgentPreset selected) return;
            Populate(selected.Options); presetName.Text = selected.Name;
            mode.SelectedIndex = selected.Mode == "forced" ? 2 : selected.Mode == "auto" ? 1 : 0;
        };
        savePreset.Click += async (_, _) =>
        {
            try
            {
                var config = FeatureSettings.Read(state.FeaturesJson);
                var selected = presets.SelectedItem as AgentPreset;
                var preset = new AgentPreset { Name = presetName.Text.Trim(), Mode = Mode(), Options = Read() };
                if (selected != null && selected.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase)) preset.Id = selected.Id;
                if (config.AgentPresets.Any(x => x.Id != preset.Id && x.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException(WorkflowText("Ce nom de preset existe déjà.", "A preset with that name already exists."));
                preset.Validate(); config.AgentPresets.RemoveAll(x => x.Id == preset.Id); config.AgentPresets.Add(preset);
                state.FeaturesJson = config.Json(); await db.SaveChangesAsync(); LoadPresets();
                presets.SelectedItem = (presets.ItemsSource as IEnumerable<AgentPreset>)?.FirstOrDefault(x => x.Id == preset.Id);
                error.Text = "";
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        deletePreset.Click += async (_, _) =>
        {
            try
            {
                if (presets.SelectedItem is not AgentPreset selected) return;
                var config = FeatureSettings.Read(state.FeaturesJson); config.AgentPresets.RemoveAll(x => x.Id == selected.Id);
                state.FeaturesJson = config.Json(); await db.SaveChangesAsync(); LoadPresets(); presetName.Text = ""; error.Text = "";
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        var save = new Button { Content = UiText.T("Enregistrer"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var cancel = new Button { Content = UiText.T("Annuler") }; cancel.Click += (_, _) => window.Close();
        save.Click += async (_, _) =>
        {
            try
            {
                var json = Read().Json(); owner.AgentOptionsJson = json; owner.OrchestrationMode = Mode();
                await db.SaveChangesAsync(); window.Close();
                ShowStatus(WorkflowText("Sous-agents enregistrés pour cette conversation.", "Subagents saved for this conversation."));
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        var actions = Row(cancel, save); actions.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetRow(actions, 2); panel.Children.Add(actions);
        Populate(ConversationAgents.Read(owner.AgentOptionsJson)); LoadPresets();
        window.Content = panel; ObserveTextZoom(panel); FluentDesign.WindowChrome(window); ApplyBrandingIcon(window);
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 760, Height = 820 }); window.Activate();
        await Task.CompletedTask;
    }
}
