using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Services;

public sealed record ProviderModel(string Id, int ContextLimit = 128000, bool SupportsImages = false);

public sealed record AccessEntry(string Kind, Guid Id, string Name);

/// <summary>Outcome of testing a provider: reachability, status, latency and the models it publishes.</summary>
public sealed record ProviderProbe(bool Ok, int Status, long Milliseconds, string Message, IReadOnlyList<ProviderModel> Models);

public sealed record ProviderView(Guid Id, string Name, string BaseUrl, bool HasSecret, IReadOnlyList<ProviderModel> Models, bool IsEnabled, bool AllowLocalUse, IReadOnlyList<AccessEntry> Access);

/// <summary>What a client receives: models and relay path, never the upstream address or the secret.</summary>
public sealed record CatalogProvider(Guid Id, string Name, IReadOnlyList<ProviderModel> Models, bool AllowLocalUse, string RelayPath);

/// <summary>Administrator-managed catalog of shared providers. Secrets stay on the server, encrypted with Data Protection.</summary>
public sealed class ProviderService(IDbContextFactory<ServerDb> factory, IDataProtectionProvider protection, AccessService access, SettingsService settings, AuditService audit,
    IHttpClientFactory clients, SyncSignals signals)
{
    readonly IDataProtector protector = protection.CreateProtector("MonolithHarness.Server.ProviderSecrets.v1");
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<ProviderModel> ParseModels(string json) =>
        JsonSerializer.Deserialize<List<ProviderModel>>(json, Json) ?? [];

    // ---- Administration ----

    public async Task<List<ProviderView>> ListAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        var providers = await db.Providers.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        var entries = await db.ProviderAccesses.AsNoTracking().ToListAsync();
        var names = await SubjectNames(db);
        return providers.Select(x => ToView(x, entries.Where(e => e.ProviderId == x.Id), names)).ToList();
    }

    public async Task<ProviderView> CreateAsync(string? name, string? baseUrl, string? secret, IEnumerable<ProviderModel>? models, bool enabled, bool allowLocalUse)
    {
        var provider = new SharedProvider { IsEnabled = enabled, AllowLocalUse = allowLocalUse };
        Apply(provider, name, baseUrl, models);
        if (!string.IsNullOrEmpty(secret)) provider.ProtectedSecret = protector.Protect(System.Text.Encoding.UTF8.GetBytes(secret));
        ValidateVertexKey(provider);
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Providers.AnyAsync(x => x.Name == provider.Name)) throw ApiException.Conflict("provider_exists", "A provider with this name already exists.");
        db.Providers.Add(provider);
        await db.SaveChangesAsync();
        await audit.WriteAsync("provider.created", provider.Name, provider.BaseUrl);
        signals.Providers();
        return (await ListAsync()).First(x => x.Id == provider.Id);
    }

    /// <summary><paramref name="secret"/>: null keeps the stored key, an empty string removes it.</summary>
    public async Task<ProviderView> UpdateAsync(Guid id, string? name, string? baseUrl, string? secret, IEnumerable<ProviderModel>? models, bool? enabled, bool? allowLocalUse)
    {
        await using var db = await factory.CreateDbContextAsync();
        var provider = await db.Providers.SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Provider not found.");
        Apply(provider, name ?? provider.Name, baseUrl ?? provider.BaseUrl, models ?? ParseModels(provider.ModelsJson));
        if (secret != null) provider.ProtectedSecret = secret.Length == 0 ? [] : protector.Protect(System.Text.Encoding.UTF8.GetBytes(secret));
        ValidateVertexKey(provider);
        if (enabled != null) provider.IsEnabled = enabled.Value;
        if (allowLocalUse != null) provider.AllowLocalUse = allowLocalUse.Value;
        if (await db.Providers.AnyAsync(x => x.Name == provider.Name && x.Id != id)) throw ApiException.Conflict("provider_exists", "A provider with this name already exists.");
        await db.SaveChangesAsync();
        await audit.WriteAsync("provider.updated", provider.Name, $"enabled={provider.IsEnabled}; localUse={provider.AllowLocalUse}; secretChanged={secret != null}");
        signals.Providers();
        return (await ListAsync()).First(x => x.Id == id);
    }

    public async Task DeleteAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        var provider = await db.Providers.SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Provider not found.");
        db.Providers.Remove(provider);
        await db.SaveChangesAsync();
        await audit.WriteAsync("provider.deleted", provider.Name);
        signals.Providers();
    }

    public async Task SetAccessAsync(Guid id, IEnumerable<AccessEntry> entries)
    {
        await using var db = await factory.CreateDbContextAsync();
        var provider = await db.Providers.SingleOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Provider not found.");
        var wanted = entries.Where(x => x.Kind is SubjectKinds.User or SubjectKinds.Role).DistinctBy(x => (x.Kind, x.Id)).ToList();
        foreach (var entry in wanted)
            if (entry.Kind == SubjectKinds.User ? !await db.Users.AnyAsync(x => x.Id == entry.Id) : !await db.Roles.AnyAsync(x => x.Id == entry.Id))
                throw ApiException.BadRequest("unknown_subject", "A user or role of the access list does not exist.");
        await db.ProviderAccesses.Where(x => x.ProviderId == id).ExecuteDeleteAsync();
        db.ProviderAccesses.AddRange(wanted.Select(x => new ProviderAccess { ProviderId = id, SubjectKind = x.Kind, SubjectId = x.Id }));
        await db.SaveChangesAsync();
        await audit.WriteAsync("provider.access_changed", provider.Name, $"{wanted.Count} entries");
        signals.Providers();
    }

    // ---- Test and detection ----

    /// <summary>
    /// Calls the provider's model list (<c>GET {base}/models</c>) with the given or stored key. Used by the administration dialog to
    /// check the address and the key and to propose the models the provider offers. The key never leaves the server.
    /// </summary>
    /// <param name="secret">null: use the stored key of <paramref name="existing"/>; otherwise exactly this key (empty: none).</param>
    public async Task<ProviderProbe> ProbeAsync(string? baseUrl, string? secret, Guid? existing, CancellationToken ct = default)
    {
        if (!Uri.TryCreate((baseUrl ?? "").Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query))
            throw ApiException.BadRequest("invalid_provider", "The base URL must be an absolute http(s) address without credentials or query.");
        var key = secret ?? "";
        if (secret == null && existing is { } id)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            if (await db.Providers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) is { } stored) key = Secret(stored);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var clock = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Get, uri.ToString().TrimEnd('/') + "/models");
        request.Headers.Accept.ParseAdd("application/json");
        if (key.Length > 0 && !MonolithHarness.Core.VertexAi.UsesApiKey(uri.ToString())) request.Headers.Authorization = new("Bearer", key);
        try
        {
            if (MonolithHarness.Core.VertexAi.IsEndpoint(uri.ToString()) && (key.Length == 0 || MonolithHarness.Core.VertexAi.UsesApiKey(uri.ToString())))
            {
                var target = new MonolithHarness.Core.Provider { Kind = "vertex", BaseUrl = uri.ToString().TrimEnd('/') };
                var ids = await MonolithHarness.Core.VertexAi.ModelsAsync(clients.CreateClient(ProviderRelay.ClientName), target, timeout.Token, key);
                var found = ids.Select(id => new ProviderModel(id, MonolithHarness.Core.ModelContexts.For(target, id).Limit, true)).ToList();
                return new(true, 200, clock.ElapsedMilliseconds, $"Catalogue Google chargé · {found.Count} modèles. Droits d’inférence et éligibilité des crédits non vérifiés.", found);
            }
            using var response = await clients.CreateClient(ProviderRelay.ClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
                return new(false, status, clock.ElapsedMilliseconds, status switch
                {
                    401 or 403 => $"Clé d’API refusée par le provider (HTTP {status}).",
                    404 => "Liste des modèles introuvable (HTTP 404): l’adresse est peut-être incorrecte, ou le provider ne publie pas ses modèles. Saisissez-les à la main.",
                    >= 300 and < 400 => $"Le provider redirige (HTTP {status}): utilisez l’adresse finale de l’API.",
                    _ => $"Le provider a répondu HTTP {status}."
                }, []);
            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var limited = new MemoryStream();
            var chunk = new byte[16384];
            int read;
            while ((read = await body.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (limited.Length + read > 8 * 1024 * 1024) return new(false, status, clock.ElapsedMilliseconds, "La liste des modèles est trop volumineuse.", []);
                limited.Write(chunk, 0, read);
            }
            var models = ParseCatalog(limited.ToArray());
            var elapsed = clock.ElapsedMilliseconds;
            return models.Count == 0
                ? new(true, status, elapsed, $"Provider joignable en {elapsed} ms, mais aucun modèle n’a été reconnu dans sa réponse. Saisissez-les à la main.", [])
                : new(true, status, elapsed, $"Provider joignable en {elapsed} ms · {models.Count} modèle(s) détecté(s).", models);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, 0, clock.ElapsedMilliseconds, "Le provider n’a pas répondu dans les 15 secondes.", []); }
        catch (HttpRequestException ex) { return new(false, 0, clock.ElapsedMilliseconds, "Provider injoignable: " + (ex.InnerException?.Message ?? ex.Message), []); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return new(false, 0, clock.ElapsedMilliseconds, ex.Message, []); }
    }

    static readonly string[] ContextKeys = ["context_length", "context_window", "max_context_length", "max_model_len", "context_size", "n_ctx_train", "max_input_tokens"];

    /// <summary>
    /// Reads the model list of an OpenAI-compatible provider ({"data":[{"id":...}]}, also {"models":[...]} or a bare array) and, when
    /// the provider publishes them, the context length and image support of each model.
    /// </summary>
    public static List<ProviderModel> ParseCatalog(byte[] json)
    {
        var models = new List<ProviderModel>();
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var list = root.ValueKind == JsonValueKind.Array ? root
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array ? data
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("models", out var named) && named.ValueKind == JsonValueKind.Array ? named
                : default;
            if (list.ValueKind != JsonValueKind.Array) return models;
            foreach (var item in list.EnumerateArray())
            {
                string? id = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (item.ValueKind == JsonValueKind.Object)
                    foreach (var name in new[] { "id", "name", "model" })
                        if (id == null && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) id = value.GetString();
                if (string.IsNullOrWhiteSpace(id) || id.Length > 200) continue;
                models.Add(new ProviderModel(id.Trim(), ContextOf(item), VisionOf(item)));
            }
        }
        catch (JsonException) { }
        return models.DistinctBy(x => x.Id).OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Take(500).ToList();
    }

    static int ContextOf(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return 128000;
        foreach (var holder in new[] { item, Child(item, "top_provider"), Child(item, "meta"), Child(item, "capabilities") })
            foreach (var key in ContextKeys)
                if (holder.ValueKind == JsonValueKind.Object && holder.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var tokens) && tokens >= 1024)
                    return tokens;
        return 128000;
    }

    static bool VisionOf(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return false;
        var architecture = Child(item, "architecture");
        foreach (var modalities in new[] { Child(item, "input_modalities"), Child(item, "modalities"), Child(architecture, "input_modalities") })
            if (modalities.ValueKind == JsonValueKind.Array && modalities.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString()!.Equals("image", StringComparison.OrdinalIgnoreCase)))
                return true;
        foreach (var flags in new[] { Child(item, "capabilities"), item })
            foreach (var name in new[] { "vision", "supports_vision", "image_input" })
                if (flags.ValueKind == JsonValueKind.Object && flags.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True) return true;
        return false;
    }

    static JsonElement Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? value : default;

    // ---- Use ----

    /// <summary>Providers the user may call, with their models. Empty when the feature is off or the user lacks the permission.</summary>
    public async Task<List<CatalogProvider>> CatalogAsync(ClaimsPrincipal user)
    {
        if (!access.Can(user, Permissions.ProvidersUse)) return [];
        var userId = user.RequireUserId();
        await using var db = await factory.CreateDbContextAsync();
        var allowed = await AccessibleIds(db, userId);
        var providers = await db.Providers.AsNoTracking().Where(x => x.IsEnabled && allowed.Contains(x.Id)).OrderBy(x => x.Name).ToListAsync();
        return providers.Select(x => new CatalogProvider(x.Id, x.Name, ParseModels(x.ModelsJson), x.AllowLocalUse, $"/api/providers/{x.Id}/relay")).ToList();
    }

    /// <summary>The provider must be enabled and granted to the user (directly or through a role).</summary>
    public async Task<SharedProvider> EnsureAccessAsync(Guid userId, Guid providerId)
    {
        if (!settings.FeatureEnabled(Features.Providers)) throw new ApiException(403, "feature_disabled", "The providers feature is disabled on this server.");
        await using var db = await factory.CreateDbContextAsync();
        var provider = await db.Providers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == providerId);
        if (provider == null || !provider.IsEnabled || !(await AccessibleIds(db, userId)).Contains(providerId))
            throw ApiException.Forbidden("provider_not_allowed", "This provider is not available to you.");
        return provider;
    }

    /// <summary>Resolves a provider and model for a user, or explains why they cannot be used.</summary>
    public async Task<(SharedProvider Provider, ProviderModel Model)> ResolveAsync(Guid userId, Guid providerId, string model)
    {
        var provider = await EnsureAccessAsync(userId, providerId);
        var selected = ParseModels(provider.ModelsJson).FirstOrDefault(x => x.Id == model)
            ?? throw ApiException.BadRequest("model_not_allowed", "This model is not offered by the provider.");
        return (provider, selected);
    }

    public string Secret(SharedProvider provider) =>
        provider.ProtectedSecret.Length == 0 ? "" : System.Text.Encoding.UTF8.GetString(protector.Unprotect(provider.ProtectedSecret));

    static async Task<HashSet<Guid>> AccessibleIds(ServerDb db, Guid userId)
    {
        var roleIds = await db.UserRoles.Where(x => x.UserId == userId).Select(x => x.RoleId).ToListAsync();
        var ids = await db.ProviderAccesses.Where(x => (x.SubjectKind == SubjectKinds.User && x.SubjectId == userId) || (x.SubjectKind == SubjectKinds.Role && roleIds.Contains(x.SubjectId)))
            .Select(x => x.ProviderId).ToListAsync();
        return ids.ToHashSet();
    }

    static async Task<Dictionary<Guid, string>> SubjectNames(ServerDb db)
    {
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Email);
        foreach (var role in await db.Roles.AsNoTracking().ToListAsync()) names[role.Id] = role.Name;
        return names;
    }

    static ProviderView ToView(SharedProvider provider, IEnumerable<ProviderAccess> entries, Dictionary<Guid, string> names) =>
        new(provider.Id, provider.Name, provider.BaseUrl, provider.ProtectedSecret.Length > 0, ParseModels(provider.ModelsJson), provider.IsEnabled, provider.AllowLocalUse,
            entries.Select(x => new AccessEntry(x.SubjectKind, x.SubjectId, names.GetValueOrDefault(x.SubjectId, "?"))).OrderBy(x => x.Kind).ThenBy(x => x.Name).ToList());

    void ValidateVertexKey(SharedProvider provider)
    {
        if (!MonolithHarness.Core.VertexAi.UsesApiKey(provider.BaseUrl)) return;
        try { MonolithHarness.Core.VertexAi.RequireApiKey(Secret(provider)); }
        catch (ArgumentException error) { throw ApiException.BadRequest("invalid_vertex_key", error.Message); }
    }
    static void Apply(SharedProvider provider, string? name, string? baseUrl, IEnumerable<ProviderModel>? models)
    {
        name = (name ?? "").Trim();
        if (name.Length is 0 or > 80) throw ApiException.BadRequest("invalid_provider", "A provider name has 1 to 80 characters.");
        if (!Uri.TryCreate((baseUrl ?? "").Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query))
            throw ApiException.BadRequest("invalid_provider", "The base URL must be an absolute http(s) address without credentials or query.");
        var list = (models ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Id)).Select(x => x with { Id = x.Id.Trim(), ContextLimit = Math.Clamp(x.ContextLimit, 1024, 10_000_000) }).DistinctBy(x => x.Id).ToList();
        if (MonolithHarness.Core.VertexAi.IsEndpoint(uri.ToString()))
        {
            try { MonolithHarness.Core.VertexAi.Read(uri.ToString()); }
            catch (ArgumentException ex) { throw ApiException.BadRequest("invalid_vertex_project", ex.Message); }
            list = list.Select(x => x with { Id = MonolithHarness.Core.VertexAi.NormalizeModel(x.Id) }).DistinctBy(x => x.Id).ToList();
        }
        provider.Name = name;
        provider.BaseUrl = uri.ToString().TrimEnd('/');
        provider.ModelsJson = JsonSerializer.Serialize(list, Json);
    }
}
