using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MonolithHarness.Core;

namespace MonolithHarness.App;

internal sealed class VertexProviderFields : UserControl
{
    readonly ComboBox authentication = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly string[] modes = [VertexAi.Adc, VertexAi.ApiKey, VertexAi.Express];
    readonly TextBox project = new() { MaxLength = 64, PlaceholderText = "mon-projet-google-cloud" };
    readonly ComboBox region = new() { IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemsSource = new[] { "global", "us-central1", "europe-west1", "europe-west4", "europe-west9" } };
    readonly TextBlock information = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Foreground = FluentDesign.Secondary };
    readonly HyperlinkButton help = new() { Padding = new(0) };
    bool refreshing;
    public bool UsesApiKey => Mode != VertexAi.Adc;
    string Mode => modes[Math.Clamp(authentication.SelectedIndex, 0, modes.Length - 1)];
    public event Action<string>? EndpointChanged;
    public VertexProviderFields()
    {
        var panel = new StackPanel { Spacing = 10 };
        foreach (var control in new UIElement[] { authentication, project, region, information, help }) panel.Children.Add(control);
        Content = panel;
        authentication.SelectionChanged += (_, _) => { if (!refreshing) { Describe(); Changed(); } };
        project.TextChanged += (_, _) => Changed();
        region.SelectionChanged += (_, _) => { if (refreshing) return; if (region.SelectedItem is string value) region.Text = value; Changed(); };
        region.TextSubmitted += (_, e) => { region.Text = e.Text; Changed(); };
        region.LostFocus += (_, _) => Changed();
    }
    public void Bind(string url)
    {
        refreshing = true;
        try
        {
            VertexAi.TryRead(url, out var value);
            authentication.Header = UiText.Resolve("Authentification", "Authentication");
            authentication.ItemsSource = new[] { "Google Cloud ADC", UiText.Resolve("Clé API Google Cloud · projet", "Google Cloud API key · project"),
                UiText.Resolve("Clé API Google Cloud · Express", "Google Cloud API key · Express") };
            authentication.SelectedIndex = Array.IndexOf(modes, value?.Authentication ?? VertexAi.Adc);
            project.Header = UiText.Resolve("Identifiant du projet Google Cloud", "Google Cloud project ID");
            region.Header = UiText.Resolve("Région", "Location");
            project.Text = value?.Project is { } id && id != "YOUR_PROJECT_ID" ? id : "";
            region.SelectedItem = value?.Location ?? "global"; region.Text = value?.Location ?? "global";
            Describe();
        }
        finally { refreshing = false; }
    }
    void Describe()
    {
        project.Visibility = region.Visibility = Mode == VertexAi.Express ? Visibility.Collapsed : Visibility.Visible;
        information.Text = Mode switch
        {
            VertexAi.ApiKey => UiText.Resolve("Clé Google Cloud autorisée pour Vertex AI et ce projet (droits IAM requis). Saisissez-la dans le champ masqué ci-dessous. Aucun ADC nécessaire.",
                "A Google Cloud key authorized for Vertex AI and this project (IAM permissions required). Enter it in the masked field below. No ADC required."),
            VertexAi.Express => UiText.Resolve("Clé créée pour Vertex AI Express. Le projet est identifié par la clé ; la région est globale. Aucun ADC nécessaire. Saisissez le modèle Gemini manuellement : Express ne propose pas de catalogue.",
                "A key created for Vertex AI Express. The key identifies the project; location is global. No ADC required. Enter the Gemini model manually: Express has no catalogue."),
            _ => UiText.Resolve("Authentification Google Cloud ADC. Les jetons sont renouvelés automatiquement.\nPremière connexion : gcloud auth application-default login",
                "Google Cloud ADC. Tokens refresh automatically.\nFirst sign-in: gcloud auth application-default login")
        };
        information.Text += UiText.Resolve("\nL’utilisation des crédits dépend du compte de facturation et de l’éligibilité définie par Google.",
            "\nCredit use depends on the billing account and eligibility determined by Google.");
        help.Content = UiText.Resolve("Configurer l’accès Google Cloud", "Set up Google Cloud access");
        help.NavigateUri = new Uri(UsesApiKey ? VertexAi.ApiKeyHelp : VertexAi.AuthenticationHelp);
    }
    void Changed()
    {
        if (refreshing) return;
        string url;
        try { url = VertexAi.Endpoint(project.Text, region.Text, Mode); }
        catch (ArgumentException) { url = ""; }
        EndpointChanged?.Invoke(url);
    }
    public void FocusProject() => (Mode == VertexAi.Express ? authentication as Control : project).Focus(FocusState.Programmatic);
}
