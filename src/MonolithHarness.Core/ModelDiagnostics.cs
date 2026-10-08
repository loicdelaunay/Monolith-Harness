using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public sealed record ModelProbeResult(string Role, string Model, string Status, string Detail, double Seconds);
public static class ModelDiagnostics
{
    public static async Task RunAsync(FeatureSettings settings, IReadOnlyList<Provider> providers, HttpClient http,
        Func<Provider, CancellationToken, Task> prepare, bool complete, IProgress<ModelProbeResult> progress, CancellationToken ct)
    {
        async Task Probe(string role, string label, Func<CancellationToken, Task<string>> action)
        {
            ct.ThrowIfCancellationRequested(); var watch = Stopwatch.StartNew();
            progress.Report(new(role, label, "running", "", 0));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try { var detail = await action(timeout.Token); progress.Report(new(role, label, "success", detail, watch.Elapsed.TotalSeconds)); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { progress.Report(new(role, label, "failed", "Délai dépassé / Timed out", watch.Elapsed.TotalSeconds)); }
            catch (Exception ex) when (ex is not OperationCanceledException) { progress.Report(new(role, label, "failed", ex.Message, watch.Elapsed.TotalSeconds)); }
        }
        Provider Select(int id, string model, int depth = 0)
        {
            if (depth > 16) throw new InvalidOperationException("Cycle de modèles composés / Composite model cycle.");
            var source = providers.FirstOrDefault(p => p.Id == id) ?? throw new InvalidOperationException("Fournisseur non configuré / Provider not configured.");
            var copy = JsonSerializer.Deserialize<Provider>(JsonSerializer.Serialize(source))!;
            if (copy.IsComposite)
            {
                var spec = CompositeModel.Read(copy.CompositeJson).Orchestrator;
                return Select(spec.ProviderId, spec.Model, depth + 1);
            }
            if (string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("Modèle non configuré / Model not configured.");
            copy.Model = model; copy.OpenCodeTools = false; return copy;
        }
        async Task<string> ChatProbe(int id, string model, bool naming, CancellationToken token)
        {
            var selected = Select(id, model); await prepare(selected, token);
            var request = new ModelToolRequest(naming ? "Generate only a short title, maximum 70 characters. Do not use tools." : "Reply only with MONOLITH_OK. Do not use tools.",
                naming ? "User: Comment organiser mes fichiers ? Assistant: Créez des dossiers par projet." : "Connection probe.", 60, 2000);
            var response = await new ModelToolClient(http).RunAsync(selected, KeyVault.Decrypt(selected.ProtectedKey), request, _ => { }, token, new() { RetryEnabled = false });
            if (naming && string.IsNullOrWhiteSpace(ConversationNaming.CleanTitle(response.Text))) throw new IOException("Titre vide / Empty title.");
            if (!naming && !response.Text.Trim().Contains("MONOLITH_OK", StringComparison.Ordinal)) throw new IOException("Réponse inattendue / Unexpected response.");
            return naming ? "Titre reçu / Title received" : "Réponse reçue / Response received";
        }
        await Probe("Nommage / Naming", settings.NamingModel, token => ChatProbe(settings.NamingProviderId, settings.NamingModel, true, token));
        await Probe("RAG", settings.RagMode == "api" ? settings.RagModel : LocalEmbeddings.ModelName, async token =>
        {
            float[] vector;
            if (settings.RagMode != "api") vector = await Task.Run(() => LocalEmbeddings.Embed("Un court texte de vérification."), token);
            else
            {
                var selected = Select(settings.RagProviderId, settings.RagModel);
                if (selected.IsExternalAgent || selected.IsComposite) throw new InvalidOperationException("Ce fournisseur ne fournit pas des embeddings / Provider does not expose embeddings.");
                using var request = new HttpRequestMessage(HttpMethod.Post, ChatEngine.Endpoint(selected.BaseUrl, "embeddings"))
                    { Content = JsonContent.Create(new { model = settings.RagModel, input = "Un court texte de vérification.", encoding_format = "float" }) };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", KeyVault.Decrypt(selected.ProtectedKey));
                using var response = await http.SendAsync(request, token); response.EnsureSuccessStatusCode();
                var data = await response.Content.ReadFromJsonAsync<JsonObject>(token);
                vector = data?["data"]?[0]?["embedding"]?.AsArray().Select(v => v!.GetValue<float>()).ToArray() ?? [];
            }
            if (vector.Length == 0 || vector.Any(v => !float.IsFinite(v)) || vector.All(v => v == 0)) throw new IOException("Vecteur invalide / Invalid vector.");
            return vector.Length + " dimensions";
        });
        await Probe("Autorisation / Approval", settings.CommandGuardMode == "model" ? settings.CommandGuardModel : "LANCET", async token =>
        {
            // This command is analyzed as text, never executed.
            var sample = new CommandApproval("echo Monolith validation probe", OperatingSystem.IsWindows() ? "powershell" : "bash", PortableStorage.Root);
            var result = await CommandGuard.CheckAsync(sample, settings,
                (id, _) => Task.FromResult(providers.FirstOrDefault(p => p.Id == id)),
                (p, _) => Task.FromResult(KeyVault.Decrypt(p.ProtectedKey)), http, token);
            if (!result.AllowsAutomatic) throw new IOException("Validation sans décision valide favorable / No valid low-risk decision: " + (result.Reason ?? result.Classification));
            return "Analyse valide, commande non exécutée / Valid analysis, command not executed";
        });
        if (!complete) return;
        foreach (var provider in providers)
            foreach (var model in ProviderModels.Visible(provider).DefaultIfEmpty(provider.Model).Distinct())
                await Probe("Fournisseur / Provider · " + provider.Name + " (#" + provider.Id + ")", model, token => ChatProbe(provider.Id, model, false, token));
    }
}
