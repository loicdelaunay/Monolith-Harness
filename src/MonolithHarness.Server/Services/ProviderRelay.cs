using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using MonolithHarness.Core;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

/// <summary>
/// Relays authorized OpenAI-compatible calls to the upstream provider, adding the secret on the server side.
/// Clients (and cloud workers, with a run-scoped token) only ever hold a Monolith session, never the provider key.
/// </summary>
public sealed class ProviderRelay(ProviderService providers, AccessService access, SettingsService settings, IDbContextFactory<ServerDb> factory, AuditService audit, IHttpClientFactory clients, RunService runs)
{
    public const string ClientName = "provider-relay";
    static readonly HashSet<string> PostPaths = ["chat/completions", "embeddings"];
    readonly ConcurrentDictionary<Guid, long> calls = new();

    public IReadOnlyDictionary<Guid, long> Calls => calls;

    public async Task HandleAsync(Guid providerId, string path, HttpContext http)
    {
        var user = http.User;
        var userId = user.RequireUserId();
        var isRun = user.SessionKind() == SessionKinds.Run;
        path = path.Trim('/');
        if (!settings.FeatureEnabled(Features.Providers)) throw new ApiException(403, "feature_disabled", "The providers feature is disabled on this server.");
        if (isRun) await EnsureRunActive(user);
        else access.Require(user, Permissions.ProvidersUse);

        await using var db = await factory.CreateDbContextAsync();
        var provider = await db.Providers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == providerId) ?? throw ApiException.NotFound("Provider not found.");
        if (!provider.IsEnabled) throw ApiException.Forbidden("provider_not_allowed", "This provider is disabled.");
        if (!isRun && !provider.AllowLocalUse) throw ApiException.Forbidden("local_use_not_allowed", "This provider can only be used by cloud runs.");
        var models = ProviderService.ParseModels(provider.ModelsJson);

        if (HttpMethods.IsGet(http.Request.Method) && path == "models")
        {
            await providers.EnsureAccessAsync(userId, provider.Id);
            await http.Response.WriteAsJsonAsync(new { @object = "list", data = models.Select(x => new { id = x.Id, @object = "model", owned_by = provider.Name }) });
            return;
        }
        if (!HttpMethods.IsPost(http.Request.Method) || !PostPaths.Contains(path)) throw ApiException.NotFound("This relay path is not available.");

        var limit = settings.Int(SettingKeys.RelayMaxRequestMb) * 1024L * 1024L;
        if (http.Request.ContentLength > limit) throw new ApiException(413, "request_too_large", "The request is too large.");
        using var buffer = new MemoryStream();
        await CopyLimited(http.Request.Body, buffer, limit, http.RequestAborted);
        var body = buffer.ToArray();
        string model;
        try { model = JsonDocument.Parse(body).RootElement.GetProperty("model").GetString() ?? ""; }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw ApiException.BadRequest("invalid_request", "The request body must be JSON with a 'model'."); }
        if (!models.Any(x => x.Id == model)) throw ApiException.Forbidden("model_not_allowed", "This model is not offered by the provider.");
        await providers.EnsureAccessAsync(userId, provider.Id);

        // A call of a local conversation takes its place in the queue like a cloud request (cloud runs were admitted when they started).
        RelaySlot? slot = !isRun && path == "chat/completions" ? await runs.AcquireRelayAsync(userId, provider.Id, model, http.RequestAborted) : null;
        var failed = true;
        try
        {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, slot?.Cancelled ?? CancellationToken.None);
        var cancel = linked.Token;
        calls.AddOrUpdate(providerId, 1, (_, count) => count + 1);
        var nativeVertex = VertexAi.UsesApiKey(provider.BaseUrl);
        if (nativeVertex && path != "chat/completions")
            throw ApiException.BadRequest("unsupported_vertex_path", "Vertex API key connections support Gemini chat completions.");
        var payload = nativeVertex ? JsonNode.Parse(body)!.AsObject() : null;
        var vertex = new Provider { Kind = "vertex", BaseUrl = provider.BaseUrl, Model = model };
        using var request = nativeVertex ? VertexApiKey.CreateRequest(vertex, payload!)
            : new HttpRequestMessage(HttpMethod.Post, provider.BaseUrl + "/" + path) { Content = new ByteArrayContent(body) };
        request.Content!.Headers.ContentType = new("application/json");
        var secret = providers.Secret(provider);
        if (nativeVertex) secret = await VertexAi.AuthorizeAsync(request, vertex, cancel, secret);
        else if (secret.Length > 0) request.Headers.Authorization = new("Bearer", secret);
        else if (VertexAi.IsEndpoint(provider.BaseUrl)) await VertexAi.AuthorizeAsync(request, vertex, cancel);
        HttpResponseMessage upstream;
        try { upstream = await clients.CreateClient(ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel); }
        catch (HttpRequestException ex)
        {
            await audit.WriteAsync("provider.relay_failed", provider.Name, ex.Message, false);
            throw new ApiException(502, "provider_unreachable", "The provider could not be reached.");
        }
        using (upstream)
        {
            if (nativeVertex)
            {
                if (!upstream.IsSuccessStatusCode)
                {
                    http.Response.StatusCode = (int)upstream.StatusCode;
                    var detail = await VertexApiKey.ReadErrorAsync(upstream, secret, cancel);
                    await http.Response.WriteAsJsonAsync(new { error = new { message = detail, type = "vertex_error" } }, cancel);
                    return;
                }
                using var reader = new StreamReader(await upstream.Content.ReadAsStreamAsync(cancel));
                var lines = VertexApiKey.OpenAiLinesAsync(reader, model, cancel);
                if (payload!["stream"]?.GetValue<bool>() == true)
                {
                    http.Response.ContentType = "text/event-stream";
                    await foreach (var line in lines.WithCancellation(cancel))
                    {
                        await http.Response.WriteAsync(line + "\n", cancel);
                        if (line.Length == 0) await http.Response.Body.FlushAsync(cancel);
                    }
                }
                else
                {
                    var completion = await ChatEngine.ParseLinesAsync(lines, _ => { }, cancel);
                    var choices = new JsonArray(new JsonObject { ["index"] = 0, ["message"] = completion.Message,
                        ["finish_reason"] = completion.Termination?.ProviderReason ?? "stop" });
                    await http.Response.WriteAsJsonAsync(new JsonObject { ["object"] = "chat.completion", ["model"] = model,
                        ["id"] = "chatcmpl-vertex-" + Guid.NewGuid().ToString("N"), ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        ["choices"] = choices, ["usage"] = new JsonObject { ["prompt_tokens"] = completion.InputTokens, ["completion_tokens"] = completion.OutputTokens } }, cancel);
                }
                failed = false;
                return;
            }
            http.Response.StatusCode = (int)upstream.StatusCode;
            if (upstream.Content.Headers.ContentType is { } type) http.Response.ContentType = type.ToString();
            await using var stream = await upstream.Content.ReadAsStreamAsync(cancel);
            var chunk = new byte[8192];
            int read;
            // Flush every chunk so streamed (SSE) tokens reach the client immediately.
            while ((read = await stream.ReadAsync(chunk, cancel)) > 0)
            {
                await http.Response.Body.WriteAsync(chunk.AsMemory(0, read), cancel);
                await http.Response.Body.FlushAsync(cancel);
            }
            failed = !upstream.IsSuccessStatusCode;
        }
        }
        catch (OperationCanceledException) when (slot is { } cancelled && cancelled.Cancelled.IsCancellationRequested && !http.RequestAborted.IsCancellationRequested)
        {
            // An administrator cancelled the call from the queue: before the first byte it is reported as such, after it the stream just stops.
            if (!http.Response.HasStarted) throw new ApiException(409, "cancelled", "The request was cancelled by an administrator.");
        }
        finally { if (slot != null) await runs.ReleaseRelayAsync(slot, failed); }
    }

    async Task EnsureRunActive(System.Security.Claims.ClaimsPrincipal user)
    {
        if (!Guid.TryParse(user.FindFirst(ClaimNames.Run)?.Value, out var runId)) throw ApiException.Forbidden();
        await using var db = await factory.CreateDbContextAsync();
        var state = await db.Runs.AsNoTracking().Where(x => x.Id == runId).Select(x => x.State).SingleOrDefaultAsync();
        if (state is not (RunStates.Running or RunStates.AwaitingApproval)) throw ApiException.Forbidden("run_not_active", "This run is no longer active.");
    }

    static async Task CopyLimited(Stream source, Stream target, long limit, CancellationToken ct)
    {
        var chunk = new byte[16384];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > limit) throw new ApiException(413, "request_too_large", "The request is too large.");
            target.Write(chunk, 0, read);
        }
    }
}
