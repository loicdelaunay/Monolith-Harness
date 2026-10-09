using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    (StackPanel Panel, Action<FeatureSettings> Save) BuildInformationSettings()
    {
        var initial = FeatureSettings.Read(state.FeaturesJson).Information;
        var panel = new StackPanel { Spacing = 10, Margin = new(0, 8, 0, 0) };
        panel.Children.Add(Label(WorkflowText(
            "Les catégories activées sont communiquées au modèle de la conversation. Les informations décrivent le PC hôte ; une sandbox possède son propre environnement de commandes. Choisissez uniquement les catégories utiles.",
            "Enabled categories are shared with the conversation model. Information describes the host computer; a sandbox has its own command environment. Select the categories you need."), 12));
        var categories = new StackPanel { Spacing = 8 };
        var toggles = new Dictionary<string, ToggleSwitch>();
        foreach (var option in InformationSettings.Options)
        {
            var toggle = new ToggleSwitch { IsOn = option.Read(initial), OnContent = T("Activé"), OffContent = T("Désactivé") };
            toggles.Add(option.Id, toggle);
            var row = FluentDesign.Setting(WorkflowText(option.French, option.English),
                WorkflowText(option.FrenchDescription, option.EnglishDescription), toggle);
            (option.Id == "automatic" ? panel : categories).Children.Add(row);
        }
        var clock = new ComboBox { Header = WorkflowText("Horloge de référence", "Reference clock"),
            ItemsSource = new[] { WorkflowText("Fuseau horaire du PC", "Computer time zone"), "UTC" },
            SelectedIndex = initial.Clock == "utc" ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        void RefreshClock() => clock.IsEnabled = toggles["date_time"].IsOn || toggles["time_zone"].IsOn;
        toggles["date_time"].Toggled += (_, _) => RefreshClock();
        toggles["time_zone"].Toggled += (_, _) => RefreshClock();
        RefreshClock(); panel.Children.Add(clock);
        panel.Children.Add(new Expander { Header = WorkflowText("Informations transmises", "Shared information"), IsExpanded = true,
            Content = categories, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        var notes = new TextBox { Header = WorkflowText("Informations complémentaires", "Additional context"),
            PlaceholderText = WorkflowText("Ex. outils habituels, particularités de ce PC…", "E.g. usual tools, particularities of this computer…"),
            Text = initial.AdditionalContext, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000, MinHeight = 78, MaxHeight = 160 };
        panel.Children.Add(notes);
        InformationSettings Read()
        {
            var settings = new InformationSettings { Clock = clock.SelectedIndex == 1 ? "utc" : "local", AdditionalContext = notes.Text };
            foreach (var option in InformationSettings.Options) option.Write(settings, toggles[option.Id].IsOn);
            return settings;
        }
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140, MaxHeight = 320,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") };
        var refresh = new Button { Content = WorkflowText("Voir les informations sélectionnées", "Preview selected information") };
        var previewArea = new Expander { Header = WorkflowText("Aperçu local", "Local preview"), Content = preview,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        CancellationTokenSource? previewRequest = null;
        panel.Unloaded += (_, _) => previewRequest?.Cancel();
        refresh.Click += async (_, _) => await Guard(async () =>
        {
            using var cancellation = new CancellationTokenSource(); previewRequest = cancellation;
            refresh.IsEnabled = false;
            var settings = Read();
            var context = new InformationSkill.Context(project, chat, state.Language, provider?.Name, provider?.Model);
            try
            {
                var snapshot = await Task.Run(() => InformationSkill.Capture(settings, context, ct: cancellation.Token), cancellation.Token);
                if (cancellation.IsCancellationRequested) return;
                preview.Text = snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                previewArea.IsExpanded = true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally { if (ReferenceEquals(previewRequest, cancellation)) previewRequest = null; refresh.IsEnabled = true; }
        });
        panel.Children.Add(refresh); panel.Children.Add(previewArea);
        panel.Children.Add(Label(WorkflowText(
            "Les modèles directs peuvent actualiser ces informations avec get_information. Les agents OpenCode / ACP reçoivent le contexte automatique au prochain envoi ; cet outil n’est pas ajouté à leurs outils natifs.",
            "Direct models can refresh this information with get_information. OpenCode / ACP agents receive automatic context on the next message; this tool is not added to their native tools."), 12));
        return (panel, settings => settings.Information = Read());
    }
}
