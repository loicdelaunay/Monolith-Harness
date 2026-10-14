using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed class ToolAuthorizationWindow : Window
{
    public Grid Panel { get; } = new() { Padding = new(24), RowSpacing = 14 };
    readonly StackPanel entries = new() { Spacing = 12 };
    static string L(string french, string english) => UiText.Resolve(french, english);
    public ToolAuthorizationWindow(string toolName, IReadOnlyList<ToolApprovalEvidence> evidence, ElementTheme theme)
    {
        Title = L("Autorisation de l’outil", "Tool authorization") + " · " + toolName;
        Panel.RequestedTheme = theme; Panel.Background = FluentDesign.Resource("SolidBackgroundFillColorBaseBrush");
        Panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Panel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        Panel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel { Spacing = 6 };
        heading.Children.Add(Text(L("Autorisation de l’outil", "Tool authorization") + " · " + toolName, 22));
        heading.Children.Add(Text(L("Décisions enregistrées au moment de l’appel. Une autorisation ne garantit pas le succès de l’outil.",
            "Decisions recorded at call time. Authorization does not guarantee tool execution success."), 12));
        heading.Children.Add(Text(L("Vert : validateur automatique · Bleu : décision manuelle ou règle · Orange : Tout autoriser ou analyse à vérifier · Rouge : refus ou risque élevé.",
            "Green: automatic validator · Blue: manual decision or rule · Orange: allow everything or review needed · Red: denied or high risk."), 12));
        Panel.Children.Add(heading);
        var scroll = new ScrollViewer { Content = entries, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); Panel.Children.Add(scroll);
        var close = new Button { Content = L("Fermer", "Close"), HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        close.Click += (_, _) => Close(); Grid.SetRow(close, 2); Panel.Children.Add(close);
        Update(evidence); Content = Panel; FluentDesign.WindowChrome(this);
        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 720, Height = 600 });
    }
    static TextBlock Text(string value, double size = 13) => new()
    { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Foreground = FluentDesign.Primary };
    public static string Method(string method) => method switch
    {
        "manual-once" => L("Autorisation manuelle · une fois", "Manual approval · once"),
        "manual-always" => L("Autorisation manuelle · toujours", "Manual approval · always"),
        "manual-denied" => L("Refus manuel", "Manually denied"),
        "saved-grant" => L("Autorisation permanente mémorisée", "Saved permission grant"),
        "project-rule" => L("Règle de permission du projet", "Project permission rule"),
        "command-validator" => L("Validation automatique de la commande", "Automatic command validation"),
        "global-mode" => L("Mode d’autorisation global", "Global permission mode"),
        _ => L("Origine non renseignée", "Origin not recorded")
    };
    static string Mode(string mode) => mode switch
    { "deny" => L("Tout refuser", "Deny all"), "ask" => L("Demander", "Ask"), "full" => L("Tout autoriser", "Allow everything"), "allow" => L("Automatique avec validateur", "Automatic with validator"), _ => mode };
    public static string Summary(IReadOnlyList<ToolApprovalEvidence> evidence) => evidence.Count == 0 ? "" :
        (evidence.Any(x => !x.Allowed) ? L("Refusé", "Denied") : L("Autorisé", "Allowed")) + " · " + Method(evidence.Last().Method);
    public static Brush Color(IReadOnlyList<ToolApprovalEvidence> evidence)
    {
        if (evidence.Any(x => !x.Allowed || x.Classification == "risky")) return FluentDesign.Adapt(255, 110, 110);
        if (evidence.Any(x => x.Mode == PermissionModes.Full || x.Classification == "review")) return FluentDesign.Adapt(245, 180, 75);
        if (evidence.Any(x => x.Method == "command-validator")) return FluentDesign.Adapt(100, 220, 140);
        return FluentDesign.Adapt(90, 195, 245);
    }
    public void Update(IReadOnlyList<ToolApprovalEvidence> evidence)
    {
        entries.Children.Clear();
        foreach (var entry in evidence)
        {
            var content = new StackPanel { Spacing = 8 };
            var outcome = Text(entry.Allowed ? L("Autorisé", "Allowed") : L("Refusé", "Denied"), 17);
            outcome.Foreground = Color([entry]); content.Children.Add(outcome);
            void Field(string title, string? value) { if (!string.IsNullOrWhiteSpace(value)) content.Children.Add(Text(title + " : " + value)); }
            Field(L("Action", "Action"), entry.Action); Field(L("Validation", "Approval"), Method(entry.Method));
            Field(L("Mode global au moment de la décision", "Global mode at decision time"), Mode(entry.Mode));
            Field(L("Règle du projet", "Project rule"), entry.ProjectRule); Field(L("Portée", "Scope"), entry.Scope);
            Field(L("Date et heure de la décision", "Decision date and time"), entry.TimeUtc.ToLocalTime().ToString("G"));
            Field(L("Validateur", "Validator"), entry.Validator); Field(L("Classification du risque", "Risk classification"), entry.Classification);
            if (entry.Score is { } score) Field(L("Score", "Score"), score.ToString("0.###"));
            Field(L("Détail de l’analyse", "Analysis details"), entry.Reason);
            if (entry.Mode == PermissionModes.Full) content.Children.Add(Text(L("Mode Tout autoriser : aucune analyse de risque requise par ce mode.",
                "Allow everything mode: this mode does not require risk analysis."), 12));
            entries.Children.Add(FluentDesign.Surface(content, 14));
        }
    }
}
