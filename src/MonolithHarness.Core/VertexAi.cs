using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;

namespace MonolithHarness.Core;

/// <summary>Vertex configuration and authentication. API keys use the native Gemini API; OAuth uses OpenAI compatibility.</summary>
public static class VertexAi
{
    public const string DefaultBaseUrl = "https://aiplatform.googleapis.com/v1/projects/YOUR_PROJECT_ID/locations/global/endpoints/openapi";
    public const string AuthenticationHelp = "https://cloud.google.com/docs/authentication/set-up-adc-local-dev-environment";
    static readonly SemaphoreSlim credentialGate = new(1, 1);
    static GoogleCredential? credential;
    public const string Adc = "adc", ApiKey = "api-key", Express = "express";
    public const string ApiKeyHelp = "https://cloud.google.com/vertex-ai/generative-ai/docs/start/api-keys";
    public sealed record Configuration(string Project, string Location, string Host, string Authentication = Adc)
    {
        public bool UsesApiKey => Authentication != Adc;
    }
    public static bool UsesApiKey(string? url) => TryRead(url, out var config) && config!.UsesApiKey;
    public static void RequireApiKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Vertex AI : saisissez une clé API Google Cloud. / Enter a Google Cloud API key.");
        // Google credentials are opaque: Express keys can include punctuation, notably a dot.
        // Only reject characters that cannot safely form an HTTP header value.
        if (key.Trim().Any(c => c <= 0x20 || c >= 0x7f))
            throw new ArgumentException("Vertex AI : la clé contient un espace ou un caractère non autorisé. Copiez uniquement la clé. / Copy only the key, without spaces or invalid characters.");
    }

    public static string Endpoint(string project, string location = "global", string authentication = Adc)
    {
        if (authentication == Express) return "https://aiplatform.googleapis.com/v1";
        if (authentication is not (Adc or ApiKey)) throw new ArgumentException("Unknown Vertex authentication mode.");
        project = project.Trim(); location = location.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(project, @"^(?:[a-z][a-z0-9-]{4,28}[a-z0-9]|[0-9]{6,20})$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Vertex AI : identifiant de projet Google Cloud requis (pas son nom affiché). / Google Cloud project ID required (not its display name).");
        if (location != "global" && !Regex.IsMatch(location, @"^[a-z]+-[a-z]+[0-9]+$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Vertex AI : région invalide. Utilisez global ou une région telle que europe-west1. / Invalid location; use global or a region such as europe-west1.");
        var host = location == "global" ? "aiplatform.googleapis.com" : location + "-aiplatform.googleapis.com";
        return $"https://{host}/v1/projects/{project}/locations/{location}" + (authentication == Adc ? "/endpoints/openapi" : "");
    }
    public static bool IsEndpoint(string? value) => TryRead(value, out _);
    public static bool TryRead(string? value, out Configuration? configuration)
    {
        configuration = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !Trusted(uri) || uri.Query.Length > 0 || uri.Fragment.Length > 0) return false;
        if (uri.AbsolutePath.TrimEnd('/') is "/v1" or "/v1beta1" && uri.Host == "aiplatform.googleapis.com")
        { configuration = new("", "global", uri.Host, Express); return true; }
        var match = Regex.Match(uri.AbsolutePath.TrimEnd('/'), @"^/v1(?:beta1)?/projects/([^/]+)/locations/([^/]+)(/endpoints/openapi)?$", RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        configuration = new(Uri.UnescapeDataString(match.Groups[1].Value), match.Groups[2].Value, uri.Host, match.Groups[3].Success ? Adc : ApiKey); return true;
    }
    public static Configuration Read(string value)
    {
        if (!TryRead(value, out var config)) throw new ArgumentException("Vertex AI : configurez le projet et la région. / Configure the project and location.");
        var expected = new Uri(Endpoint(config!.Project, config.Location, config.Authentication));
        if (config.Host != expected.Host) throw new ArgumentException("Vertex AI : l’hôte doit correspondre à la région. / The host must match the location.");
        return config;
    }
    static bool Trusted(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        Regex.IsMatch(uri.Host, @"^(?:[a-z]+-[a-z]+[0-9]+-)?aiplatform\.googleapis\.com$", RegexOptions.CultureInvariant);

    public static string NormalizeModel(string model)
    {
        model = model.Trim();
        if (model.StartsWith("publishers/google/models/", StringComparison.Ordinal)) model = model[25..];
        else if (model.StartsWith("models/", StringComparison.Ordinal)) model = model[7..];
        return model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) ? "google/" + model : model;
    }
    internal static Uri NativeEndpoint(Provider provider, string model, string operation)
    {
        var config = Read(provider.BaseUrl);
        model = NormalizeModel(model);
        if (!model.StartsWith("google/gemini-", StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(model[7..], @"^[a-zA-Z0-9._-]+$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Vertex AI : choisissez un identifiant Gemini, par exemple google/gemini-2.5-flash. / Choose a Gemini model ID.");
        if (operation is not ("countTokens" or "streamGenerateContent")) throw new ArgumentException("Unsupported Vertex operation.");
        var root = provider.BaseUrl.TrimEnd('/');
        if (!config.UsesApiKey) root = root[..^"/endpoints/openapi".Length];
        return new Uri(root + "/publishers/google/models/" + model[7..] + ":" + operation +
            (operation == "streamGenerateContent" ? "?alt=sse" : ""));
    }
    /// <summary>Checks the selected model's token-count endpoint without generating a response or importing a catalogue.</summary>
    public static async Task ProbeAsync(HttpClient http, Provider provider, string key, CancellationToken ct)
    {
        if (ChatEngine.ProviderGuard?.Invoke(provider) is { } refused) throw new InvalidOperationException(refused);
        using var request = new HttpRequestMessage(HttpMethod.Post, NativeEndpoint(provider, provider.Model, "countTokens"))
        {
            Content = new StringContent(new JsonObject { ["contents"] = new JsonArray(new JsonObject {
                ["role"] = "user", ["parts"] = new JsonArray(new JsonObject { ["text"] = "Monolith connection check" }) }) }.ToJsonString(),
                System.Text.Encoding.UTF8, "application/json")
        };
        var secret = await AuthorizeAsync(request, provider, ct, key);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Vertex AI : HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {await ProviderErrorDetails.ReadAsync(response, secret, ct)}", null, response.StatusCode);
        var result = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        if (result?["totalTokens"] is not JsonValue count || !count.TryGetValue<int>(out var tokens) || tokens <= 0)
            throw new IOException("Vertex AI : réponse de vérification inattendue. / Unexpected connection check response.");
    }
    public static async Task<string> AuthorizeAsync(HttpRequestMessage request, Provider provider, CancellationToken ct, string key = "")
    {
        var config = Read(provider.BaseUrl);
        if (request.RequestUri is not { } target || !Trusted(target) || target.Host != config.Host)
            throw new ArgumentException("Vertex AI : destination Google Cloud non autorisée. / Invalid Google Cloud destination.");
        if (config.UsesApiKey)
        {
            RequireApiKey(key);
            request.Headers.Add("x-goog-api-key", key.Trim());
            return key.Trim();
        }
        GoogleCredential current;
        await credentialGate.WaitAsync(ct);
        try
        {
            credential ??= (await GoogleCredential.GetApplicationDefaultAsync(ct)).CreateScoped("https://www.googleapis.com/auth/cloud-platform");
            current = credential;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            throw new InvalidOperationException("Vertex AI : configurez les identifiants Google Cloud avec gcloud auth application-default login, ou GOOGLE_APPLICATION_CREDENTIALS. / Configure Google Cloud Application Default Credentials.");
        }
        finally { credentialGate.Release(); }
        string token;
        try { token = await ((ITokenAccess)current).GetAccessTokenForRequestAsync(cancellationToken: ct); }
        catch (TokenResponseException)
        {
            throw new InvalidOperationException("Vertex AI : connexion Google Cloud à renouveler. Relancez gcloud auth application-default login puis redémarrez l’application. / Renew Google Cloud sign-in, then restart the application.");
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // User ADC may have a different quota project. Bill the project explicitly selected by the user.
        request.Headers.TryAddWithoutValidation("x-goog-user-project", config.Project);
        return token;
    }
    public static async Task<List<string>> ModelsAsync(HttpClient http, Provider provider, CancellationToken ct, string key = "")
    {
        var config = Read(provider.BaseUrl); var metadata = new List<ModelContextMetadata>(); var models = new List<string>();
        if (config.UsesApiKey) RequireApiKey(key);
        if (config.Authentication == Express)
            throw new InvalidOperationException("Vertex AI Express ne fournit pas de catalogue de modèles. Saisissez un identifiant Gemini manuellement, par exemple google/gemini-2.5-flash. / Express has no model catalogue; enter a Gemini model ID manually, e.g. google/gemini-2.5-flash.");
        var seen = new HashSet<string>(StringComparer.Ordinal); string page = "";
        for (var n = 0; n < 20; n++)
        {
            var url = $"https://{config.Host}/v1beta1/publishers/google/models?pageSize=1000" + (page.Length == 0 ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var token = await AuthorizeAsync(request, provider, ct, key);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Vertex AI · catalogue : HTTP {(int)response.StatusCode}. {await ProviderErrorDetails.ReadAsync(response, token, ct)}", null, response.StatusCode);
            var data = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            if (data?["publisherModels"] is JsonArray entries)
                foreach (var item in entries.OfType<JsonObject>())
                {
                    var id = NormalizeModel(item["name"]?.ToString() ?? "");
                    if (!id.StartsWith("google/gemini-", StringComparison.OrdinalIgnoreCase) ||
                        new[] { "embedding", "-image", "-tts", "-live", "native-audio", "robotics" }.Any(x => id.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
                    models.Add(id); metadata.Add(ModelContexts.FromApi(id, item));
                }
            page = data?["nextPageToken"]?.ToString() ?? "";
            if (page.Length == 0) { ModelContexts.Merge(provider, metadata); return ProviderModels.Normalize(models); }
            if (!seen.Add(page)) throw new InvalidOperationException("Vertex AI : pagination du catalogue répétée. / Repeated catalogue page.");
        }
        throw new InvalidOperationException("Vertex AI : catalogue trop long. Saisissez un identifiant de modèle manuellement. / Catalogue too long; enter a model ID manually.");
    }
}
