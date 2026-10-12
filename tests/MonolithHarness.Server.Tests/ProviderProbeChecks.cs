using System.Text.Json;
using MonolithHarness.Server.Services;

/// <summary>The administration dialog tests a provider and detects its models.</summary>
static class ProviderProbeChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var upstream = await FakeProvider.StartAsync();
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        var (_, user) = await server.NewUserAsync(admin, "plain@example.com");
        string ErrorCode((int Status, JsonElement Body) r) => r.Body.ValueKind == JsonValueKind.Object && r.Body.TryGetProperty("code", out var x) ? x.GetString() ?? "" : "";
        string Other(string folder) => upstream.BaseUrl[..^3] + folder;

        // ---- Detection ----
        var probe = await server.Post("/api/admin/providers/probe", new { baseUrl = upstream.BaseUrl, secret = "sk-typed" }, admin);
        check(probe.Status == 200 && probe.Body.GetProperty("ok").GetBoolean() && probe.Body.GetProperty("models").GetArrayLength() == 1
            && probe.Body.GetProperty("models")[0].GetProperty("id").GetString() == "fake-model" && probe.Body.GetProperty("message").GetString()!.Contains("1 modèle"), "La sonde joint le provider et liste ses modèles");
        check(upstream.ModelListAuthorizations.TryPeek(out var firstAuthorization) && firstAuthorization == "Bearer sk-typed", "La clé saisie dans le dialogue est utilisée pour l’appel");
        check(!probe.Body.ToString().Contains("sk-typed"), "La réponse de la sonde ne renvoie jamais la clé");

        var rich = (await server.Post("/api/admin/providers/probe", new { baseUrl = Other("/rich") }, admin)).Body.GetProperty("models").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!, x => x);
        check(rich.Count == 3 && rich["vision-model"].GetProperty("contextLimit").GetInt32() == 200000 && rich["vision-model"].GetProperty("supportsImages").GetBoolean(), "Le contexte et la lecture d’images publiés par le provider sont repris");
        check(rich["text-model"].GetProperty("contextLimit").GetInt32() == 32768 && !rich["text-model"].GetProperty("supportsImages").GetBoolean() && rich["plain-model"].GetProperty("contextLimit").GetInt32() == 128000,
            "Sans métadonnées, un modèle reçoit des valeurs par défaut raisonnables (128000, sans images)");
        var names = (await server.Post("/api/admin/providers/probe", new { baseUrl = Other("/names") }, admin)).Body.GetProperty("models").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
        check(names.SequenceEqual(["llama3:8b", "qwen2.5:7b"]), "Les catalogues qui publient des noms (type Ollama) sont compris");

        // ---- Diagnostics ----
        var denied = (await server.Post("/api/admin/providers/probe", new { baseUrl = Other("/denied"), secret = "bad" }, admin)).Body;
        check(!denied.GetProperty("ok").GetBoolean() && denied.GetProperty("status").GetInt32() == 401 && denied.GetProperty("message").GetString()!.Contains("refusée"), "Une clé refusée est signalée clairement");
        var empty = (await server.Post("/api/admin/providers/probe", new { baseUrl = Other("/empty") }, admin)).Body;
        check(empty.GetProperty("ok").GetBoolean() && empty.GetProperty("models").GetArrayLength() == 0 && empty.GetProperty("message").GetString()!.Contains("aucun modèle"), "Un provider joignable sans liste exploitable invite à saisir les modèles à la main");
        var missing = (await server.Post("/api/admin/providers/probe", new { baseUrl = Other("/inexistant") }, admin)).Body;
        check(!missing.GetProperty("ok").GetBoolean() && missing.GetProperty("status").GetInt32() == 404, "Une adresse sans liste de modèles (404) est expliquée");
        var down = (await server.Post("/api/admin/providers/probe", new { baseUrl = "http://127.0.0.1:9/v1" }, admin)).Body;
        check(!down.GetProperty("ok").GetBoolean() && down.GetProperty("status").GetInt32() == 0 && down.GetProperty("message").GetString()!.Contains("injoignable"), "Un provider injoignable est signalé sans erreur serveur");
        check(ErrorCode(await server.Post("/api/admin/providers/probe", new { baseUrl = "ftp://x" }, admin)) == "invalid_provider", "Une adresse non http(s) est refusée");
        check(ErrorCode(await server.Post("/api/admin/providers/probe", new { baseUrl = "http://u:p@host/v1" }, admin)) == "invalid_provider", "Une adresse avec identifiants est refusée");
        check(ErrorCode(await server.Post("/api/admin/providers/probe", new { baseUrl = upstream.BaseUrl }, user)) == "missing_permission", "Tester un provider exige providers.manage");
        check((await server.Post("/api/admin/providers/probe", new { baseUrl = upstream.BaseUrl })).Status == 401, "…et d’être connecté");

        // ---- Stored key ----
        var created = await server.Post("/api/admin/providers", new { name = "Stocké", baseUrl = upstream.BaseUrl, secret = "sk-stored", models = new[] { new { id = "fake-model" } } }, admin);
        var id = created.Body.GetProperty("id").GetGuid();
        while (upstream.ModelListAuthorizations.TryDequeue(out _)) { }
        await server.Post("/api/admin/providers/probe", new { baseUrl = upstream.BaseUrl, providerId = id }, admin);
        check(upstream.ModelListAuthorizations.TryDequeue(out var stored) && stored == "Bearer sk-stored", "Sans clé saisie, la sonde utilise la clé enregistrée du provider modifié, qui ne quitte pas le serveur");
        await server.Post("/api/admin/providers/probe", new { baseUrl = upstream.BaseUrl, providerId = id, secret = "" }, admin);
        check(upstream.ModelListAuthorizations.TryDequeue(out var none) && none == "", "Une clé vide demandée explicitement supprime l’en-tête d’autorisation");
        check(ProviderService.ParseCatalog("""[ "a", {"id":"b"}, {"model":"c"}, 3 ]"""u8.ToArray()).Select(x => x.Id).SequenceEqual(["a", "b", "c"]) && ProviderService.ParseCatalog("not json"u8.ToArray()).Count == 0,
            "Les formats de catalogue courants sont lus; une réponse invalide ne casse rien");
    }
}
