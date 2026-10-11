using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;
using MonolithHarness.Server.Services;

static class AdminChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        string Code((int Status, JsonElement Body) r) => r.Body.ValueKind == JsonValueKind.Object && r.Body.TryGetProperty("code", out var c) ? c.GetString()! : "";

        // ---- Accounts ----
        var userRole = await server.RoleId("User", admin);
        var created = await server.Post("/api/admin/users", new { email = "carol@example.com", password = "password-1234", displayName = "Carol", roleIds = new[] { userRole } }, admin);
        check(created.Status == 201 && created.Body.GetProperty("passwordChangePending").GetBoolean(), "Création d’un compte par e-mail et mot de passe");
        check(Code(await server.Post("/api/admin/users", new { email = "CAROL@example.com", password = "password-1234" }, admin)) == "email_taken", "E-mail déjà utilisé refusé (insensible à la casse)");
        check(Code(await server.Post("/api/admin/users", new { email = "not-an-email", password = "password-1234" }, admin)) == "invalid_email", "E-mail invalide refusé");
        check(Code(await server.Post("/api/admin/users", new { email = "dave@example.com", password = "short" }, admin)) == "weak_password", "Mot de passe faible refusé");
        var carolId = created.Body.GetProperty("id").GetGuid();
        var carol = await server.LoginAsync("carol@example.com", "password-1234");
        check((await server.Get("/api/admin/users", carol)).Status == 403, "Un utilisateur sans permission n’accède pas à l’administration");

        // ---- Roles are customizable ----
        var roleCreated = await server.Post("/api/admin/roles", new { name = "Auditeur", description = "Lecture du journal", permissions = new[] { Permissions.AuditRead } }, admin);
        check(roleCreated.Status == 201, "Création d’un rôle personnalisé");
        check(Code(await server.Post("/api/admin/roles", new { name = "Bad", permissions = new[] { "does.not.exist" } }, admin)) == "unknown_permission", "Permission inconnue refusée");
        var auditRole = roleCreated.Body.GetProperty("id").GetGuid();
        check((await server.Get("/api/admin/audit", carol)).Status == 403, "Sans la permission audit.read, le journal est fermé");
        await server.SendAsync(HttpMethod.Patch, $"/api/admin/users/{carolId}", new { roleIds = new[] { userRole, auditRole } }, admin);
        check((await server.Get("/api/admin/audit", carol)).Status == 200, "Un rôle ajouté s’applique immédiatement, sans nouvelle connexion");
        await server.Put($"/api/admin/roles/{auditRole}", new { permissions = Array.Empty<string>() }, admin);
        check((await server.Get("/api/admin/audit", carol)).Status == 403, "Retirer une permission d’un rôle s’applique immédiatement");
        var adminRole = await server.RoleId("Administrator", admin);
        check(Code(await server.Put($"/api/admin/roles/{adminRole}", new { permissions = new[] { Permissions.AuditRead } }, admin)) == "system_role", "Le rôle Administrator reste complet");
        check(Code(await server.Delete($"/api/admin/roles/{adminRole}", admin)) == "system_role", "Le rôle Administrator ne se supprime pas");
        check((await server.Delete($"/api/admin/roles/{auditRole}", admin)).Status == 204, "Un rôle personnalisé se supprime");

        // ---- Features ----
        var cloudAsAdmin = await server.Get("/api/projects", admin);
        check(cloudAsAdmin.Status == 403 && Code(cloudAsAdmin) == "feature_disabled", "Fonctionnalité désactivée: refusée même à l’administrateur");
        var shareDisabled = await server.Put("/api/admin/features/sharing", new { enabled = false }, admin);
        check(shareDisabled.Status == 204 && Code(await server.Post("/api/shares", new { title = "x" }, admin)) == "feature_disabled", "Désactiver le partage bloque ses opérations");
        await server.Put("/api/admin/features/sharing", new { enabled = true }, admin);
        check((await server.Post("/api/shares", new { title = "Visible" }, admin)).Status == 201, "Réactiver la fonctionnalité la rend à nouveau disponible");
        check(Code(await server.Put("/api/admin/features/unknown", new { enabled = true }, admin)) == "unknown_feature", "Fonctionnalité inconnue refusée");
        check(Code(await server.Put("/api/admin/features/cloud", new { enabled = true }, carol)) == "missing_permission" || (await server.Put("/api/admin/features/cloud", new { enabled = true }, carol)).Status == 403, "Régler les fonctionnalités exige settings.manage");

        // ---- Settings ----
        check(Code(await server.Put("/api/admin/settings/queue.maxConcurrentUsers", new { value = "0" }, admin)) == "invalid_setting", "Réglage hors limites refusé");
        check(Code(await server.Put("/api/admin/settings/queue.nothing", new { value = "1" }, admin)) == "unknown_setting", "Réglage inconnu refusé");
        check((await server.Put("/api/admin/settings/queue.maxConcurrentUsers", new { value = "3" }, admin)).Status == 204, "Réglage valide enregistré");
        var settings = (await server.Get("/api/admin/settings", admin)).Body.EnumerateArray().First(x => x.GetProperty("key").GetString() == "queue.maxConcurrentUsers");
        check(settings.GetProperty("value").GetString() == "3", "Réglage relu");
        await server.RestartAsync();
        admin = await server.LoginAsync();
        check((await server.Get("/api/admin/settings", admin)).Body.EnumerateArray().First(x => x.GetProperty("key").GetString() == "queue.maxConcurrentUsers").GetProperty("value").GetString() == "3"
            && (await server.Get("/api/server")).Body.GetProperty("features").GetProperty("sharing").GetBoolean(), "Réglages et fonctionnalités persistent après redémarrage");

        // ---- Last administrator, self deletion, disabled accounts ----
        var me = (await server.Get("/api/auth/me", admin)).Body.GetProperty("id").GetGuid();
        check(Code(await server.SendAsync(HttpMethod.Patch, $"/api/admin/users/{me}", new { isActive = false }, admin)) == "last_administrator", "Le dernier administrateur actif ne peut pas être désactivé");
        check(Code(await server.SendAsync(HttpMethod.Patch, $"/api/admin/users/{me}", new { roleIds = new[] { userRole } }, admin)) == "last_administrator", "…ni retiré du rôle Administrator");
        check(Code(await server.Delete($"/api/admin/users/{me}", admin)) is "self_delete" or "last_administrator", "…ni supprimé");
        carol = await server.LoginAsync("carol@example.com", "password-1234");
        await server.SendAsync(HttpMethod.Patch, $"/api/admin/users/{carolId}", new { isActive = false }, admin);
        check((await server.Get("/api/auth/me", carol)).Status == 401 && (await server.Post("/api/auth/login", new { email = "carol@example.com", password = "password-1234" })).Status == 401,
            "Un compte désactivé perd ses connexions et ne peut plus se connecter");
        await server.SendAsync(HttpMethod.Patch, $"/api/admin/users/{carolId}", new { isActive = true }, admin);

        // ---- Administrator password reset and session revocation ----
        carol = await server.LoginAsync("carol@example.com", "password-1234");
        check((await server.Post($"/api/admin/users/{carolId}/password", new { password = "temporary-pass-9" }, admin)).Status == 204 && (await server.Get("/api/auth/me", carol)).Status == 401,
            "Un mot de passe temporaire émis par l’administrateur révoque les connexions");
        carol = await server.LoginAsync("carol@example.com", "temporary-pass-9");
        check((await server.Get("/api/auth/me", carol)).Body.GetProperty("passwordChangePending").GetBoolean(), "…et propose son remplacement");
        check((await server.Post($"/api/admin/users/{carolId}/revoke-sessions", null, admin)).Body.GetProperty("revoked").GetInt32() >= 1 && (await server.Get("/api/auth/me", carol)).Status == 401, "Révocation des connexions d’un compte");

        // ---- Backup ----
        check((await server.Get("/api/admin/backups", carol = await server.LoginAsync("carol@example.com", "temporary-pass-9"))).Status == 403, "Les sauvegardes exigent backup.manage");
        var backup = await server.Post("/api/admin/backups", null, admin);
        var name = backup.Body.GetProperty("name").GetString()!;
        check(backup.Status == 201 && File.Exists(Path.Combine(server.Paths.Backups, name)), "Sauvegarde cohérente de la base");
        check((await server.Get("/api/admin/backups", admin)).Body.EnumerateArray().Any(x => x.GetProperty("name").GetString() == name), "Sauvegardes listées");
        check((await server.Get("/api/admin/backups/..%2Fdatabase.sqlite", admin)).Status is 400 or 404, "Nom de sauvegarde malveillant refusé");
        var restoreRoot = new ServerPaths(Path.Combine(Path.GetTempPath(), "mh-restore-" + Guid.NewGuid().ToString("N")));
        restoreRoot.Ensure();
        BackupService.Restore(restoreRoot, Path.Combine(server.Paths.Backups, name));
        await using (var db = new ServerDb(ServerDb.Options(restoreRoot.Database)))
            check(await db.Users.AnyAsync(x => x.Email == TestServer.AdminEmail) && await db.Roles.AnyAsync(x => x.Name == "Administrator"), "La sauvegarde se restaure hors ligne avec ses comptes");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(restoreRoot.Root, true); } catch (IOException) { }
        check((await server.Delete("/api/admin/backups/" + name, admin)).Status == 204, "Suppression d’une sauvegarde");

        // ---- API documentation ----
        var openApi = await server.Http.GetStringAsync("/openapi/v1.json");
        check(openApi.Contains("\"/api/auth/login\"") && openApi.Contains("\"bearer\"") && openApi.Contains("/api/admin/users"), "Document OpenAPI avec authentification bearer");
        check((await server.Http.GetAsync("/swagger/index.html")).IsSuccessStatusCode, "Interface Swagger servie");

        // ---- Audit ----
        var actions = (await server.Get("/api/admin/audit?take=500", admin)).Body.EnumerateArray().Select(x => x.GetProperty("action").GetString()).ToList();
        check(new[] { "user.created", "role.created", "feature.enabled", "setting.changed", "backup.created", "user.password_reset" }.All(actions.Contains), "Les opérations d’administration sont journalisées");
    }
}
