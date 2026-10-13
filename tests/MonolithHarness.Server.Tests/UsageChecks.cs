using System.Text.Json;

/// <summary>Speed and consumption statistics shared by the clients: idempotent uploads, common and per-user views, privacy rules.</summary>
static class UsageChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        var (aliceId, alice) = await server.NewUserAsync(admin, "usage-a@example.com");
        var (bobId, bob) = await server.NewUserAsync(admin, "usage-b@example.com");
        var now = DateTime.UtcNow;

        object Sample(string id, double minutesAgo, string model, long input, long output, double seconds, string status = "complete", bool? viaServer = null) =>
            new { id, completedUtc = now.AddMinutes(-minutesAgo), provider = "Perso", model, activity = "chat", inputTokens = input, outputTokens = output, cachedInputTokens = input / 2, seconds, status, estimated = false, viaServer = viaServer ?? false };

        var (status, accepted) = await server.Post("/api/usage", new
        {
            deviceId = "pc-1",
            samples = new[] { Sample("1", 30, "model-a", 1000, 200, 10), Sample("2", 20, "model-a", 2000, 600, 20), Sample("3", 10, "model-b", 500, 100, 4, viaServer: true), Sample("4", 5, "model-a", 100, 50, 2, "cancelled") }
        }, alice);
        check(status == 200 && accepted.GetProperty("accepted").GetInt32() == 4 && accepted.GetProperty("duplicates").GetInt32() == 0, "Un utilisateur partage ses mesures");
        (_, accepted) = await server.Post("/api/usage", new { deviceId = "pc-1", samples = new[] { Sample("1", 30, "model-a", 1000, 200, 10), Sample("2", 20, "model-a", 2000, 600, 20) } }, alice);
        check(accepted.GetProperty("accepted").GetInt32() == 0 && accepted.GetProperty("duplicates").GetInt32() == 2, "Renvoyer les mêmes mesures ne les compte pas deux fois");
        (_, accepted) = await server.Post("/api/usage", new { deviceId = "pc-2", samples = new[] { Sample("1", 30, "model-a", 1000, 200, 10) } }, alice);
        check(accepted.GetProperty("accepted").GetInt32() == 1, "Le même numéro venant d’un autre appareil est une autre mesure");
        (_, accepted) = await server.Post("/api/usage", new
        {
            deviceId = "pc-1",
            samples = new object[]
            {
                new { id = "", completedUtc = now, provider = "p", model = "m", inputTokens = 1, outputTokens = 1, seconds = 1.0 },
                new { id = "neg", completedUtc = now, provider = "p", model = "m", inputTokens = -5, outputTokens = 1, seconds = 1.0 },
                new { id = "future", completedUtc = now.AddDays(3), provider = "p", model = "m", inputTokens = 1, outputTokens = 1, seconds = 1.0 },
                new { id = "old", completedUtc = now.AddDays(-4000), provider = "p", model = "m", inputTokens = 1, outputTokens = 1, seconds = 1.0 },
                new { id = "slow", completedUtc = now, provider = "p", model = "m", inputTokens = 1, outputTokens = 1, seconds = 1e9 },
            }
        }, alice);
        check(accepted.GetProperty("accepted").GetInt32() == 0 && accepted.GetProperty("rejected").GetInt32() == 5, "Les mesures incohérentes (sans numéro, négatives, futures, trop anciennes, durée absurde) sont rejetées");
        check((await server.Post("/api/usage", new { deviceId = "x", samples = Enumerable.Range(0, 1001).Select(i => Sample("b" + i, 1, "m", 1, 1, 1)).ToArray() }, alice)).Status == 400, "Un lot de plus de 1000 mesures est refusé");
        check((await server.Post("/api/usage", new { deviceId = "x", samples = new[] { Sample("a", 1, "m", 1, 1, 1) } })).Status == 401, "Partager sans être connecté est refusé");

        // ---- My own figures ----
        var (_, mine) = await server.Get("/api/usage/summary?scope=me&period=24h", alice);
        var totals = mine.GetProperty("totals");
        check(totals.GetProperty("calls").GetInt32() == 5 && totals.GetProperty("inputTokens").GetInt64() == 4600 && totals.GetProperty("outputTokens").GetInt64() == 1150, "Mon résumé totalise les requêtes et les jetons");
        // Rated calls: 1 (200/10), 2 (600/20), 3 (100/4) and the second device's 1 (200/10); the cancelled one does not count for speed.
        var rated = totals.GetProperty("ratedCalls").GetInt32();
        var average = totals.GetProperty("averageSpeed").GetDouble();
        check(rated == 4 && Math.Abs(average - (200 + 600 + 100 + 200) / (10 + 20 + 4 + 10.0)) < 0.001, $"La vitesse moyenne est pondérée par la durée et ignore les appels interrompus ({average:0.##} jetons/s)");
        check(Math.Abs(totals.GetProperty("minSpeed").GetDouble() - 20) < 0.001 && Math.Abs(totals.GetProperty("maxSpeed").GetDouble() - 30) < 0.001, "Les vitesses minimale et maximale sont celles d’un appel");
        var models = mine.GetProperty("models").EnumerateArray().ToList();
        check(models.Count == 2 && models[0].GetProperty("model").GetString() == "model-a" && models[0].GetProperty("calls").GetInt32() == 4, "Le détail par modèle regroupe les appels");
        check(mine.GetProperty("timeline").GetArrayLength() == 24 && mine.GetProperty("timeline").EnumerateArray().Sum(x => x.GetProperty("calls").GetInt32()) == 5, "La courbe de 24 h a 24 points qui totalisent les requêtes");
        foreach (var (period, count) in new[] { ("7d", 7), ("30d", 30), ("1y", 12) })
        {
            var (_, view) = await server.Get($"/api/usage/summary?scope=me&period={period}", alice);
            check(view.GetProperty("timeline").GetArrayLength() == count, $"La courbe « {period} » a {count} points");
        }
        check(mine.GetProperty("users").ValueKind == JsonValueKind.Null, "Ma vue personnelle ne liste pas d’autres utilisateurs");

        // ---- Privacy: others' figures need the right ----
        await server.Post("/api/usage", new { deviceId = "b", samples = new[] { Sample("1", 15, "model-b", 800, 400, 8) } }, bob);
        check((await server.Get("/api/usage/summary?scope=all", alice)).Status == 403, "La vue commune est refusée sans le droit usage.viewAll");
        check((await server.Get($"/api/usage/summary?scope=user&userId={bobId}", alice)).Status == 403, "Les chiffres d’un autre utilisateur sont refusés sans le droit");
        check((await server.Get($"/api/usage/summary?scope=user&userId={aliceId}", alice)).Status == 200, "Chacun peut demander ses propres chiffres par identifiant");
        check((await server.Get("/api/usage/summary?scope=nothing", alice)).Status == 400, "Une portée inconnue est refusée");
        var (_, common) = await server.Get("/api/usage/summary?scope=all&period=24h", admin);
        var users = common.GetProperty("users").EnumerateArray().ToList();
        check(common.GetProperty("totals").GetProperty("calls").GetInt32() == 6 && common.GetProperty("totals").GetProperty("users").GetInt32() == 2, "La vue commune additionne tous les utilisateurs");
        check(users.Count == 2 && users.Any(x => x.GetProperty("email").GetString() == "usage-b@example.com" && x.GetProperty("calls").GetInt32() == 1), "…et détaille chaque utilisateur");
        var (_, onlyBob) = await server.Get($"/api/usage/summary?scope=user&userId={bobId}&period=24h", admin);
        check(onlyBob.GetProperty("totals").GetProperty("calls").GetInt32() == 1 && onlyBob.GetProperty("userId").GetGuid() == bobId, "La vue par utilisateur ne montre que cet utilisateur");

        // A role can be given the right to look.
        var (_, status0) = await server.Get("/api/usage/status", alice);
        check(status0.GetProperty("samples").GetInt32() == 5 && status0.GetProperty("enabled").GetBoolean(), "Le statut indique combien de mesures j’ai partagées");

        // ---- Erasing ----
        var (_, erased) = await server.Delete("/api/usage", alice);
        check(erased.GetProperty("deleted").GetInt32() == 5, "Un utilisateur efface ses mesures");
        check((await server.Delete($"/api/usage?userId={bobId}", alice)).Status == 403, "Il ne peut pas effacer celles d’un autre");
        (_, erased) = await server.Delete($"/api/usage?userId={bobId}", admin);
        check(erased.GetProperty("deleted").GetInt32() == 1, "Un administrateur efface celles d’un compte");
        var (_, empty) = await server.Get("/api/usage/summary?scope=all", admin);
        check(empty.GetProperty("totals").GetProperty("calls").GetInt32() == 0 && empty.GetProperty("totals").GetProperty("averageSpeed").ValueKind == JsonValueKind.Null, "Sans mesure, les totaux sont vides");

        // ---- Account removal and feature switch ----
        await server.Post("/api/usage", new { deviceId = "b", samples = new[] { Sample("9", 1, "model-b", 10, 10, 1) } }, bob);
        await server.Delete($"/api/admin/users/{bobId}", admin);
        var (_, afterDelete) = await server.Get("/api/usage/summary?scope=all", admin);
        check(afterDelete.GetProperty("totals").GetProperty("calls").GetInt32() == 0, "Supprimer un compte supprime ses mesures");
        await server.Put("/api/admin/features/usage", new { enabled = false }, admin);
        var (disabled, body) = await server.Post("/api/usage", new { deviceId = "a", samples = new[] { Sample("1", 1, "m", 1, 1, 1) } }, alice);
        check(disabled == 403 && body.GetProperty("code").GetString() == "feature_disabled", "Fonctionnalité désactivée: le serveur n’accepte plus de mesures");
        check((await server.Get("/api/usage/summary", alice)).Status == 403, "…ni ne les affiche");
    }
}
