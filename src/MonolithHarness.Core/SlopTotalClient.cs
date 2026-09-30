using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

/// <summary>Native client for SlopTotal. Scores and calibration always come from the detection service.</summary>
public sealed class SlopTotalClient : IDisposable
{
    public const string DefaultEndpoint = "http://127.0.0.1:8786";
    public const int MaxDocumentBytes = 10 * 1024 * 1024;
    const int MaxResponseBytes = 8 * 1024 * 1024;
    readonly HttpClient http;
    public Uri Endpoint { get; }
    public sealed record Update(JsonObject? Engine = null, int? QueuePosition = null);

    public SlopTotalClient(string endpoint)
    {
        Endpoint = NormalizeEndpoint(endpoint);
        http = new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = Endpoint, Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MonolithHarness/" + GitHubUpdates.CurrentVersion);
    }

    public static Uri NormalizeEndpoint(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || !Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Adresse du service invalide : utilisez une URL HTTP ou HTTPS sans identifiants, paramètres ni fragment. / Invalid service address.");
        return uri;
    }

    public async Task<JsonObject> HealthAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(12));
        return await GetAsync("health", timeout.Token);
    }

    public async Task<JsonArray> EnginesAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync("api/engines", HttpCompletionOption.ResponseHeadersRead, ct);
        return await ReadAsync(response, ct) as JsonArray ?? throw new IOException("Invalid SlopTotal engine catalog.");
    }

    public async Task<JsonObject> AnalyzeAsync(string text, string url, IProgress<Update>? progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) && text.Trim().Length < 50) throw new ArgumentException("50 caractères minimum. / At least 50 characters.");
        if (text.Length > 100_000) throw new ArgumentException("100 000 caractères maximum. / Maximum 100,000 characters.");
        if (!string.IsNullOrWhiteSpace(url)) ValidateUrl(url);
        var start = await PostAsync("api/web/analyze", new JsonObject { ["text"] = text, ["url"] = url }, ct);
        start = await WaitQueueAsync(start, progress, ct);
        var id = String(start, "report_id");
        if (id.Length is 0 or > 12 || !id.All(char.IsAsciiLetterOrDigit)) throw new IOException("Invalid SlopTotal report identifier.");

        using var response = await http.GetAsync("api/stream/" + id, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) await ReadAsync(response, ct);
        if (response.Content.Headers.ContentType?.MediaType != "text/event-stream") throw new IOException("SlopTotal did not return a result stream.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder(); bool done = false; int receivedBytes = 0;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            receivedBytes += Encoding.UTF8.GetByteCount(line);
            if (receivedBytes > MaxResponseBytes || data.Length > 128_000) throw new IOException("SlopTotal stream exceeded its size limit.");
            if (line.StartsWith("data:", StringComparison.Ordinal)) data.AppendLine(line[5..].TrimStart());
            if (line.Length != 0 || data.Length == 0) continue;
            var packet = JsonNode.Parse(data.ToString()) as JsonObject ?? throw new IOException("Invalid SlopTotal event."); data.Clear();
            if (packet["done"]?.GetValue<bool>() == true) { done = true; break; }
            if (packet.ContainsKey("engine_name")) progress?.Report(new(packet));
        }
        if (!done) throw new IOException("Le flux de résultats a été interrompu. / The result stream was interrupted.");
        return await GetAsync("api/report/" + id, ct);
    }

    public Task<JsonObject> ParagraphsAsync(string text, string url, CancellationToken ct) =>
        PostAsync("api/paragraph-score", new JsonObject { ["text"] = text, ["url"] = url }, ct);

    public Task<JsonObject> SiteAsync(string url, CancellationToken ct)
    {
        ValidateUrl(url);
        return PostAsync("api/scan/site", new JsonObject { ["url"] = url, ["include_text"] = true }, ct);
    }

    public async Task<string> ExtractAsync(string path, CancellationToken ct)
    {
        if (!new[] { ".txt", ".md", ".pdf", ".docx" }.Contains(Path.GetExtension(path).ToLowerInvariant())) throw new ArgumentException("TXT, MD, PDF ou DOCX uniquement. / TXT, MD, PDF or DOCX only.");
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaxDocumentBytes) throw new ArgumentException("Document : 10 Mo maximum. / Document: maximum 10 MB.");
        using var form = new MultipartFormDataContent(); form.Add(new StreamContent(file), "file", Path.GetFileName(path));
        using var response = await http.PostAsync("api/extract", form, ct);
        var result = await ReadAsync(response, ct) as JsonObject ?? throw new IOException("Invalid extraction response.");
        var text = String(result, "text");
        if (text.Length is < 50 or > 100_000) throw new IOException("Le texte extrait doit contenir entre 50 et 100 000 caractères. / Extracted text must contain 50–100,000 characters.");
        return text;
    }

    async Task<JsonObject> WaitQueueAsync(JsonObject response, IProgress<Update>? progress, CancellationToken ct)
    {
        var ticket = String(response, "ticket_id");
        while (String(response, "status") is "queued" or "running" or "processing")
        {
            if (ticket.Length is 0 or > 128) throw new IOException("Invalid SlopTotal queue ticket.");
            progress?.Report(new(QueuePosition: (int)Number(response, "position")));
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            response = await GetAsync("api/queue/ticket/" + Uri.EscapeDataString(ticket), ct);
        }
        if (!string.IsNullOrWhiteSpace(String(response, "error"))) throw new IOException(String(response, "error"));
        return response["result"] as JsonObject ?? response;
    }

    async Task<JsonObject> GetAsync(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
        return await ReadAsync(response, ct) as JsonObject ?? throw new IOException("Invalid SlopTotal response.");
    }
    async Task<JsonObject> PostAsync(string path, JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        return await ReadAsync(response, ct) as JsonObject ?? throw new IOException("Invalid SlopTotal response.");
    }
    static async Task<JsonNode> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct); using var buffer = new MemoryStream(); var bytes = new byte[8192];
        while (await stream.ReadAsync(bytes, ct) is var read && read > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) throw new IOException("SlopTotal response exceeded its size limit.");
            buffer.Write(bytes, 0, read);
        }
        JsonNode? node = null;
        try { node = JsonNode.Parse(buffer.ToArray()); } catch (System.Text.Json.JsonException) { }
        if (!response.IsSuccessStatusCode)
        {
            var error = node is JsonObject obj ? String(obj, "error", String(obj, "detail")) : "";
            if (error.Length > 600) error = error[..600];
            throw new HttpRequestException($"SlopTotal · HTTP {(int)response.StatusCode}" + (error.Length > 0 ? " · " + error : ""), null, response.StatusCode);
        }
        return node ?? throw new IOException("Le service ne renvoie pas de JSON SlopTotal. / The service did not return SlopTotal JSON.");
    }
    static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || uri.UserInfo.Length > 0)
            throw new ArgumentException("Utilisez une URL HTTP ou HTTPS complète. / Use a complete HTTP or HTTPS URL.");
    }
    public static string String(JsonObject obj, string key, string fallback = "") => obj[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : fallback;
    public static double Number(JsonObject obj, string key) => obj[key] is JsonValue value && value.TryGetValue<double>(out var number) && double.IsFinite(number) ? number : 0;
    public static bool EngineFailed(JsonObject obj) => String(obj, "verdict") == "error" || new[] { "engine error:", "model loading failed", "failed to load", "unavailable", "error loading" }
        .Any(fragment => String(obj, "details").Contains(fragment, StringComparison.OrdinalIgnoreCase));
    public void Dispose() => http.Dispose();
}
