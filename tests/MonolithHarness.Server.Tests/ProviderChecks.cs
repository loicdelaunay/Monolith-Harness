using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;
using MonolithHarness.Server.Services;

static class ProviderChecks
{
    const string UpstreamSecret = "sk-upstream-secret";

    public static async Task Run(Action<bool, string> check)
    {
        await using var upstream = await FakeProvider.StartAsync();
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        string Code((int Status, JsonElement Body) r) => r.Body.ValueKind == JsonValueKind.Object && r.Body.TryGetProperty("code", out var c) ? c.GetString()! : "";

        // ---- Catalog administration ----
        var created = await server.Post("/api/admin/providers", new
        {
            name = "Fake", baseUrl = upstream.BaseUrl, secret = UpstreamSecret, isEnabled = true, allowLocalUse = true,
            models = new[] { new { id = "fake-model", contextLimit = 32000, supportsImages = false } }
        }, admin);
        check(created.Status == 201, "L’administrateur ajoute un provider au catalogue");
        var providerId = created.Body.GetProperty("id").GetGuid();
        check(created.Body.GetProperty("hasSecret").GetBoolean() && !created.Body.ToString().Contains(UpstreamSecret), "La réponse de création ne contient pas la clé");
        check(!(await server.Get("/api/admin/providers", admin)).Body.ToString().Contains(UpstreamSecret), "La liste d’administration ne renvoie jamais la clé");
        await using (var db = await server.Service<IDbContextFactory<ServerDb>>().CreateDbContextAsync())
        {
            var stored = (await db.Providers.SingleAsync()).ProtectedSecret;
            check(stored.Length > 0 && Encoding.UTF8.GetString(stored).IndexOf(UpstreamSecret, StringComparison.Ordinal) < 0 && Convert.ToHexString(stored).IndexOf(Convert.ToHexString(Encoding.UTF8.GetBytes(UpstreamSecret)), StringComparison.Ordinal) < 0,
                "La clé est chiffrée en base (Data Protection)");
        }
        check(Code(await server.Post("/api/admin/providers", new { name = "Fake", baseUrl = upstream.BaseUrl }, admin)) == "provider_exists", "Nom de provider unique");
        check(Code(await server.Post("/api/admin/providers", new { name = "Bad", baseUrl = "ftp://x" }, admin)) == "invalid_provider", "Adresse non http(s) refusée");
        check(Code(await server.Post("/api/admin/providers", new { name = "Bad2", baseUrl = "http://user:pw@host/v1" }, admin)) == "invalid_provider", "Adresse avec identifiants refusée");

        // ---- Access: users, roles, permission ----
        var (dan, danToken) = await server.NewUserAsync(admin, "dan@example.com");
        var (_, erinToken) = await server.NewUserAsync(admin, "erin@example.com", "Guest");
        check((await server.Get("/api/providers", danToken)).Body.GetArrayLength() == 0, "Sans autorisation explicite, le catalogue est vide");
        check(Code(await server.Get("/api/admin/providers", danToken)) == "missing_permission", "Gérer le catalogue exige providers.manage");
        await server.Put($"/api/admin/providers/{providerId}/access", new { entries = new[] { new { kind = "user", id = dan, name = "" } } }, admin);
        var catalog = (await server.Get("/api/providers", danToken)).Body;
        check(catalog.GetArrayLength() == 1 && catalog[0].GetProperty("models")[0].GetProperty("id").GetString() == "fake-model", "L’utilisateur autorisé reçoit le provider et ses modèles");
        var catalogText = catalog.ToString();
        check(!catalogText.Contains(UpstreamSecret) && !catalogText.Contains(upstream.BaseUrl) && catalog[0].GetProperty("relayPath").GetString() == $"/api/providers/{providerId}/relay", "Le catalogue ne révèle ni clé ni adresse amont");

        var guestRole = await server.RoleId("Guest", admin);
        await server.Put($"/api/admin/providers/{providerId}/access", new { entries = new[] { new { kind = "role", id = guestRole, name = "" } } }, admin);
        check((await server.Get("/api/providers", erinToken)).Body.GetArrayLength() == 0, "Un accès par rôle ne suffit pas sans la permission providers.use");
        var analysts = await server.Post("/api/admin/roles", new { name = "Analystes", permissions = new[] { Permissions.ProvidersUse } }, admin);
        var analystsId = analysts.Body.GetProperty("id").GetGuid();
        var (_, frankToken) = await server.NewUserAsync(admin, "frank@example.com", "Analystes");
        await server.Put($"/api/admin/providers/{providerId}/access", new { entries = new[] { new { kind = "role", id = analystsId, name = "" } } }, admin);
        check((await server.Get("/api/providers", frankToken)).Body.GetArrayLength() == 1 && (await server.Get("/api/providers", danToken)).Body.GetArrayLength() == 0, "Accès par rôle: seuls les membres du rôle voient le provider");
        await server.Put($"/api/admin/providers/{providerId}/access", new { entries = new[] { new { kind = "user", id = dan, name = "" }, new { kind = "role", id = analystsId, name = "" } } }, admin);

        // ---- Relay ----
        var models = await server.Get($"/api/providers/{providerId}/relay/models", danToken);
        check(models.Status == 200 && models.Body.GetProperty("data")[0].GetProperty("id").GetString() == "fake-model", "Le relais liste les modèles autorisés");
        var request = new { model = "fake-model", stream = true, messages = new[] { new { role = "user", content = "Salut" } } };
        var stopwatch = Stopwatch.StartNew();
        using var message = new HttpRequestMessage(HttpMethod.Post, $"/api/providers/{providerId}/relay/chat/completions") { Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json") };
        message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + danToken);
        using var response = await server.Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        var firstChunk = new byte[4096];
        var firstRead = await stream.ReadAsync(firstChunk);
        var firstAt = stopwatch.ElapsedMilliseconds;
        var rest = await new StreamReader(stream).ReadToEndAsync();
        var total = stopwatch.ElapsedMilliseconds;
        var body = Encoding.UTF8.GetString(firstChunk, 0, firstRead) + rest;
        check(response.StatusCode == System.Net.HttpStatusCode.OK && response.Content.Headers.ContentType?.MediaType == "text/event-stream" && body.Contains("[DONE]") && body.Contains("\"content\":\"O\"") && body.Contains("\"content\":\"K\""), "Le relais transmet la réponse en flux (SSE)");
        check(total - firstAt >= 100, "…au fil de l’eau, sans attendre la fin de la réponse");
        var seen = upstream.Requests.Last();
        check(seen.Authorization == "Bearer " + UpstreamSecret && !seen.Authorization.Contains("mhs_"), "Le serveur ajoute la clé amont; le jeton du client n’est jamais transmis");
        check(Code(await server.Post($"/api/providers/{providerId}/relay/chat/completions", new { model = "gpt-secret", messages = Array.Empty<object>() }, danToken)) == "model_not_allowed", "Un modèle hors catalogue est refusé");
        check(Code(await server.Post($"/api/providers/{providerId}/relay/chat/completions", new { messages = Array.Empty<object>() }, danToken)) == "invalid_request", "Une requête sans modèle est refusée");
        check((await server.Post($"/api/providers/{providerId}/relay/files", new { model = "fake-model" }, danToken)).Status == 404, "Seuls chat/completions, embeddings et models sont relayés");
        check(Code(await server.Post($"/api/providers/{providerId}/relay/chat/completions", request, erinToken)) == "missing_permission", "Sans providers.use, le relais est fermé");
        var (_, georgeToken) = await server.NewUserAsync(admin, "george@example.com");
        check(Code(await server.Post($"/api/providers/{providerId}/relay/chat/completions", request, georgeToken)) == "provider_not_allowed", "Sans accès au provider, le relais est fermé");
        check((await server.Post($"/api/providers/{providerId}/relay/chat/completions", request)).Status == 401, "Le relais exige une authentification");

        await server.SetAsync(admin, SettingKeys.RelayMaxRequestMb, 1);
        var huge = new string('x', 2 * 1024 * 1024);
        check((await server.Post($"/api/providers/{providerId}/relay/chat/completions", new { model = "fake-model", messages = new[] { new { role = "user", content = huge } } }, danToken)).Status == 413, "Une requête trop volumineuse est refusée");

        upstream.Handler = _ => new FakeProvider.Reply(null, Status: 500);
        check((await server.Post($"/api/providers/{providerId}/relay/chat/completions", request, danToken)).Status == 500, "Les erreurs du provider sont transmises telles quelles");
        upstream.Handler = _ => FakeProvider.Reply.Text("OK");

        // ---- Local use is optional ----
        await server.Put($"/api/admin/providers/{providerId}", new { allowLocalUse = false }, admin);
        check(Code(await server.Post($"/api/providers/{providerId}/relay/chat/completions", request, danToken)) == "local_use_not_allowed", "Utilisation locale désactivable par provider");
        await server.Put($"/api/admin/providers/{providerId}", new { allowLocalUse = true }, admin);

        // ---- Secrets: keep, replace, remove ----
        await server.Put($"/api/admin/providers/{providerId}", new { name = "Fake renamed" }, admin);
        await server.Post($"/api/providers/{providerId}/relay/chat/completions", request, danToken);
        check(upstream.Requests.Last().Authorization == "Bearer " + UpstreamSecret, "Modifier un provider sans 'secret' conserve la clé");
        await server.Put($"/api/admin/providers/{providerId}", new { secret = "sk-rotated" }, admin);
        await server.Post($"/api/providers/{providerId}/relay/chat/completions", request, danToken);
        check(upstream.Requests.Last().Authorization == "Bearer sk-rotated", "La clé peut être remplacée");
        await server.Put($"/api/admin/providers/{providerId}", new { secret = "" }, admin);
        await server.Post($"/api/providers/{providerId}/relay/chat/completions", request, danToken);
        check(upstream.Requests.Last().Authorization == "", "…ou supprimée");

        // ---- Disabling ----
        await server.Put($"/api/admin/providers/{providerId}", new { isEnabled = false }, admin);
        check(Code(await server.Post($"/api/providers/{providerId}/relay/chat/completions", request, danToken)) == "provider_not_allowed" && (await server.Get("/api/providers", danToken)).Body.GetArrayLength() == 0, "Un provider désactivé disparaît du catalogue et du relais");
        await server.Put($"/api/admin/providers/{providerId}", new { isEnabled = true }, admin);
        await server.Put("/api/admin/features/providers", new { enabled = false }, admin);
        check(Code(await server.Post($"/api/providers/{providerId}/relay/chat/completions", request, danToken)) == "feature_disabled" && (await server.Get("/api/providers", danToken)).Body.GetArrayLength() == 0, "Désactiver la fonctionnalité ferme le catalogue et le relais");
        await server.Put("/api/admin/features/providers", new { enabled = true }, admin);

        // ---- Run-scoped tokens only open the relay of a live run ----
        var sessions = server.Service<SessionService>();
        var (runToken, _) = await sessions.CreateAsync(dan, SessionKinds.Run, "run", "local", "worker", TimeSpan.FromMinutes(5), Guid.NewGuid());
        check((await server.Get("/api/auth/me", runToken)).Status is 401 or 403 && (await server.Get("/api/providers", runToken)).Status is 401 or 403, "Un jeton de run n’ouvre aucune autre API");
        check(Code(await server.Post($"/api/providers/{providerId}/relay/chat/completions", request, runToken)) == "run_not_active", "Un jeton de run sans exécution active est refusé par le relais");
        await server.Put($"/api/admin/providers/{providerId}", new { secret = UpstreamSecret }, admin);

        // ---- Deletion ----
        check((await server.Delete($"/api/admin/providers/{providerId}", admin)).Status == 204 && (await server.Get("/api/admin/providers", admin)).Body.GetArrayLength() == 0, "Suppression d’un provider");
    }
}
