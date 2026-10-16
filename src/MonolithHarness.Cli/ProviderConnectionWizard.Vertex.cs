using MonolithHarness.Core;

namespace MonolithHarness.Cli;

public sealed partial class ProviderConnectionWizard
{
    async Task<bool> ConfigureVertexAsync(Provider draft, CancellationToken ct)
    {
        var mode = await prompt(new("2/4 · Authentification Vertex AI / Authentication", Choices: [
            new(VertexAi.Adc, "Google Cloud ADC", "Connexion gcloud ; jetons renouvelés automatiquement / gcloud login; automatic token refresh"),
            new(VertexAi.ApiKey, "Clé API Google Cloud · projet / project", "Clé autorisée pour Vertex AI avec les droits IAM du projet / Authorized key with project IAM permissions"),
            new(VertexAi.Express, "Clé API Google Cloud · Express", "Clé Vertex AI Express, sans projet ni région à saisir / Express key; no project or location required") ]));
        if (mode == null) return false;
        if (mode == VertexAi.Express) { draft.BaseUrl = VertexAi.Endpoint("", authentication: mode); return true; }
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var project = await prompt(new("2/4 · Projet Google Cloud / Google Cloud project",
                "Identifiant du projet associé à votre facturation, API Vertex AI activée et droits IAM requis. / Billing project ID; enable Vertex AI and grant IAM access." +
                (mode == VertexAi.Adc ? "\nADC : gcloud auth application-default login" : "")));
            if (project == null) return false;
            var region = await prompt(new("2/4 · Région / Location", "global ou région compatible avec le modèle. / global or a location supported by the model.", Initial: "global"));
            if (region == null) return false;
            try { draft.BaseUrl = VertexAi.Endpoint(project, region, mode); return true; }
            catch (ArgumentException error) { progress?.Invoke(error.Message); }
        }
    }
}
