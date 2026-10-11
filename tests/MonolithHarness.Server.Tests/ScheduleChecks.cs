using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;
using MonolithHarness.Server.Services;
using MonolithHarness.Server.Workers;

static class ScheduleChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var fake = new FakeExecutor();
        await using var server = await TestServer.StartAsync(builder => builder.Services.AddSingleton<IRunExecutor>(fake));
        fake.Runs = server.Service<RunService>();
        var admin = await server.LoginAsync();
        var factory = server.Service<IDbContextFactory<ServerDb>>();
        var schedules = server.Service<ScheduleService>();
        string Code((int Status, JsonElement Body) r) => r.Body.ValueKind == JsonValueKind.Object && r.Body.TryGetProperty("code", out var x) ? x.GetString()! : "";

        var provider = (await server.Post("/api/admin/providers", new { name = "P", baseUrl = "http://127.0.0.1:9/v1", models = new[] { new { id = "m" } } }, admin)).Body.GetProperty("id").GetGuid();
        var userRole = await server.RoleId("User", admin);
        await server.Put($"/api/admin/providers/{provider}/access", new { entries = new[] { new { kind = "role", id = userRole, name = "" } } }, admin);
        var (_, token) = await server.NewUserAsync(admin, "sam@example.com");
        var (_, other) = await server.NewUserAsync(admin, "tess@example.com");
        check(Code(await server.Get("/api/schedules", token)) == "feature_disabled", "Tâches planifiées désactivées par défaut");
        await server.EnableAsync(admin, "cloud", "schedules");
        var project = (await server.Post("/api/projects", new { name = "Planifié" }, token)).Body.GetProperty("id").GetGuid();

        object Body(string name = "Rapport", string cron = "0 9 * * 1-5", string zone = "Europe/Paris", string policy = "skip") =>
            new { name, projectId = project, instruction = "Fais le rapport", cron, timeZoneId = zone, providerId = provider, model = "m", missedPolicy = policy };

        // ---- Validation ----
        check(Code(await server.Post("/api/schedules", Body(cron: "* * *"), token)) == "invalid_cron", "CRON à trois champs refusé");
        check(Code(await server.Post("/api/schedules", Body(cron: "0 0 31 2 *"), token)) == "invalid_cron", "CRON sans occurrence refusé");
        check(Code(await server.Post("/api/schedules", Body(zone: "Mars/Phobos"), token)) == "invalid_timezone", "Fuseau inconnu refusé");
        check(Code(await server.Post("/api/schedules", Body(cron: "*/1 * * * *"), token)) == "too_frequent", "Intervalle inférieur au minimum refusé");
        check(Code(await server.Post("/api/schedules", Body(policy: "replay"), token)) == "invalid_policy", "Politique inconnue refusée");
        check(Code(await server.Post("/api/schedules", new { name = "x", projectId = Guid.NewGuid(), instruction = "y", cron = "0 9 * * *", timeZoneId = "UTC", providerId = provider, model = "m" }, token)) == "not_found", "Projet inconnu refusé");
        check((await server.Post("/api/schedules", Body(), other)).Status == 404, "Le projet d’un autre n’est pas planifiable");
        check(Code(await server.Post("/api/schedules", new { name = "x", projectId = project, instruction = "y", cron = "0 9 * * *", timeZoneId = "UTC", providerId = provider, model = "unknown" }, token)) == "model_not_allowed", "Modèle hors catalogue refusé");

        var created = await server.Post("/api/schedules", Body(), token);
        var id = created.Body.GetProperty("id").GetGuid();
        var next = created.Body.GetProperty("nextRunUtc").GetDateTime();
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(next, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"));
        check(created.Status == 201 && local.Hour == 9 && local.Minute == 0 && local.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday, "La prochaine occurrence respecte le fuseau (09:00 à Paris, en semaine)");
        check((await server.Get("/api/schedules", other)).Body.GetArrayLength() == 0 && (await server.Put($"/api/schedules/{id}", new { name = "pirate" }, other)).Status == 404, "Les tâches d’un autre sont invisibles");

        async Task Due(Guid schedule, TimeSpan ago, string? policy = null, string? cron = null)
        {
            await using var db = await factory.CreateDbContextAsync();
            var row = await db.Schedules.SingleAsync(x => x.Id == schedule);
            row.NextRunUtc = DateTime.UtcNow - ago;
            row.Enabled = true;
            if (policy != null) row.MissedPolicy = policy;
            if (cron != null) row.Cron = cron;
            await db.SaveChangesAsync();
        }
        async Task<int> RunsOf(Guid schedule) { await using var db = await factory.CreateDbContextAsync(); return await db.Runs.CountAsync(x => x.ScheduleId == schedule); }
        async Task<ScheduleOccurrence[]> Occurrences(Guid schedule) { await using var db = await factory.CreateDbContextAsync(); return await db.Occurrences.Where(x => x.ScheduleId == schedule).OrderBy(x => x.OccurrenceUtc).ToArrayAsync(); }
        async Task<CloudSchedule> Row(Guid schedule) { await using var db = await factory.CreateDbContextAsync(); return await db.Schedules.AsNoTracking().SingleAsync(x => x.Id == schedule); }
        async Task WaitUntil(Func<Task<bool>> condition, int ms = 10000) { for (var waited = 0; waited < ms && !await condition(); waited += 50) await Task.Delay(50); }

        // ---- A due occurrence runs exactly once, even with concurrent executors ----
        await Due(id, TimeSpan.FromSeconds(5));
        await Task.WhenAll(schedules.TickAsync(DateTime.UtcNow), schedules.TickAsync(DateTime.UtcNow), schedules.TickAsync(DateTime.UtcNow));
        check(await RunsOf(id) == 1 && (await Occurrences(id)).Length == 1, "Trois exécuteurs simultanés ne lancent l’occurrence qu’une fois");
        var afterTick = await Row(id);
        check(afterTick.NextRunUtc > DateTime.UtcNow && afterTick.LastRunId != null, "La prochaine échéance est avancée dans le même enregistrement");
        var firstRun = afterTick.LastRunId!.Value;
        await WaitUntil(async () => fake.Started.Contains(firstRun));
        check(fake.Started.Contains(firstRun), "La requête planifiée entre dans la file et démarre");
        var chats = (await server.Get($"/api/projects/{project}/chats", token)).Body.EnumerateArray().Select(x => x.GetProperty("title").GetString()).ToList();
        check(chats.Any(x => x!.StartsWith("Rapport — ")), "Chaque occurrence a sa conversation");

        // ---- No overlap with itself ----
        await Due(id, TimeSpan.FromSeconds(5));
        await schedules.TickAsync(DateTime.UtcNow);
        var occurrences = await Occurrences(id);
        check(await RunsOf(id) == 1 && occurrences.Any(x => x.Outcome == "skipped"), "Une tâche encore en cours ne chevauche pas sa prochaine occurrence");
        fake.Release(firstRun);
        await WaitUntil(async () => (await Row(id)).LastResult.StartsWith("completed"));
        check((await Row(id)).LastResult.StartsWith("completed"), "Le résultat de la dernière exécution est enregistré sur la tâche");

        // ---- Server stopped for a while ----
        await Due(id, TimeSpan.FromHours(3), policy: "skip");
        await schedules.TickAsync(DateTime.UtcNow);
        var missed = await Row(id);
        check(await RunsOf(id) == 1 && missed.LastResult.Contains("Missed") && (await Occurrences(id)).Any(x => x.Outcome == "missed") && missed.NextRunUtc > DateTime.UtcNow, "Politique « skip »: une occurrence manquée est ignorée et signalée");
        await Due(id, TimeSpan.FromDays(2), policy: "run-once", cron: "*/5 * * * *");
        await schedules.TickAsync(DateTime.UtcNow);
        check(await RunsOf(id) == 2, "Politique « run-once »: deux jours d’arriéré donnent une seule exécution");
        var secondRun = (await Row(id)).LastRunId!.Value;
        fake.Release(secondRun);
        await WaitUntil(async () => (await Row(id)).LastResult.StartsWith("completed"));

        // ---- Manual run ----
        check((await server.Post($"/api/schedules/{id}/run", null, token)).Status == 202 && await RunsOf(id) == 3, "Lancer une tâche à la demande");
        fake.Release((await Row(id)).LastRunId!.Value);
        await WaitUntil(async () => (await Row(id)).LastResult.StartsWith("completed"));

        // ---- Limits ----
        await server.SetAsync(admin, "schedules.maxPerUser", 2);
        check((await server.Post("/api/schedules", Body("Deuxième"), token)).Status == 201 && Code(await server.Post("/api/schedules", Body("Troisième"), token)) == "too_many_schedules", "Nombre de tâches limité par utilisateur");

        // ---- Features and permissions are checked at each occurrence ----
        await server.Put("/api/admin/features/schedules", new { enabled = false }, admin);
        await Due(id, TimeSpan.FromSeconds(5));
        await schedules.TickAsync(DateTime.UtcNow);
        check(await RunsOf(id) == 3, "Fonctionnalité désactivée: aucune exécution planifiée");
        await server.Put("/api/admin/features/schedules", new { enabled = true }, admin);

        var restrictedRole = (await server.Post("/api/admin/roles", new { name = "Sans planning", permissions = new[] { Permissions.CloudUse, Permissions.ProvidersUse } }, admin)).Body.GetProperty("id").GetGuid();
        var (restrictedId, restrictedToken) = await server.NewUserAsync(admin, "uma@example.com", "User");
        var restrictedProject = (await server.Post("/api/projects", new { name = "R" }, restrictedToken)).Body.GetProperty("id").GetGuid();
        var restrictedSchedule = (await server.Post("/api/schedules", new { name = "Uma", projectId = restrictedProject, instruction = "y", cron = "0 9 * * *", timeZoneId = "UTC", providerId = provider, model = "m" }, restrictedToken)).Body.GetProperty("id").GetGuid();
        await server.SendAsync(HttpMethod.Patch, $"/api/admin/users/{restrictedId}", new { roleIds = new[] { restrictedRole } }, admin);
        await Due(restrictedSchedule, TimeSpan.FromSeconds(5));
        await schedules.TickAsync(DateTime.UtcNow);
        var revoked = await Row(restrictedSchedule);
        check(await RunsOf(restrictedSchedule) == 0 && !revoked.Enabled && revoked.LastResult.Contains("permissions"), "Permission retirée: la tâche ne s’exécute plus et se désactive");

        // ---- Persistence ----
        await server.RestartAsync();
        var token2 = await server.LoginAsync("sam@example.com", "password-1234");
        var listed = (await server.Get("/api/schedules", token2)).Body;
        check(listed.GetArrayLength() == 2 && listed.EnumerateArray().All(x => x.GetProperty("nextRunUtc").ValueKind == JsonValueKind.String), "Les tâches et leurs échéances survivent à un redémarrage");
        check((await server.Delete($"/api/schedules/{id}", token2)).Status == 204 && (await server.Get("/api/schedules", token2)).Body.GetArrayLength() == 1, "Suppression d’une tâche");
    }
}
