using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MonolithHarness.Server.Data;
using MonolithHarness.Server.Security;

static class AccountChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var server = await TestServer.StartAsync();
        var (infoStatus, info) = await server.Get("/api/server");
        check(infoStatus == 200 && info.GetProperty("apiVersion").GetInt32() == 1, "Identité du serveur accessible sans connexion");
        check(!info.GetProperty("features").GetProperty("cloud").GetBoolean() && info.GetProperty("features").GetProperty("sharing").GetBoolean(),
            "Fonctionnalités: cloud désactivé par défaut, partage activé");
        check((await server.Get("/api/auth/me")).Status == 401 && (await server.Get("/api/admin/users")).Status == 401, "Les API protégées refusent les appels anonymes");
        check((await server.Get("/api/auth/me", "mhs_invalid")).Status == 401, "Un jeton inconnu est refusé");

        // ---- First start: the specified administrator, once ----
        var (status, login) = await server.Post("/api/auth/login", new { email = "ADMIN@admin.com ", password = "admin1234", label = "tests" });
        check(status == 200, "Le compte administrateur initial se connecte (e-mail normalisé)");
        var token = login.GetProperty("token").GetString()!;
        check(token.StartsWith("mhs_") && login.GetProperty("passwordChangeSuggested").GetBoolean() && !login.GetProperty("passwordChangeRequired").GetBoolean(),
            "Le changement du mot de passe initial est proposé, pas imposé par défaut");
        var permissions = login.GetProperty("user").GetProperty("permissions").EnumerateArray().Select(x => x.GetString()).ToList();
        check(permissions.Contains(Permissions.UsersManage) && permissions.Count == Permissions.All.Count, "L’administrateur détient toutes les permissions");

        var factory = server.Service<IDbContextFactory<ServerDb>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var admin = await db.Users.SingleAsync(x => x.Email == TestServer.AdminEmail);
            check(admin.PasswordHash.StartsWith("pbkdf2-sha512$210000$") && !admin.PasswordHash.Contains("admin1234"), "Mot de passe haché avec PBKDF2 salé");
            check(!await db.Sessions.AnyAsync(x => x.TokenHash == token), "Le jeton de session n’est pas stocké en clair");
        }

        // ---- Failures and enumeration ----
        var wrong = await server.Post("/api/auth/login", new { email = TestServer.AdminEmail, password = "nope-nope" });
        var unknown = await server.Post("/api/auth/login", new { email = "ghost@example.com", password = "nope-nope" });
        check(wrong.Status == 401 && unknown.Status == 401 && wrong.Body.GetProperty("code").GetString() == unknown.Body.GetProperty("code").GetString()
            && wrong.Body.GetProperty("message").GetString() == unknown.Body.GetProperty("message").GetString(), "Mauvais mot de passe et compte inconnu donnent la même réponse");

        // ---- Throttling ----
        var (alice, aliceToken) = await server.NewUserAsync(token, "alice@example.com");
        for (var i = 0; i < 5; i++) await server.Post("/api/auth/login", new { email = "alice@example.com", password = "wrong-password" });
        var blocked = await server.Post("/api/auth/login", new { email = "alice@example.com", password = "password-1234" });
        check(blocked.Status == 429 && blocked.Body.GetProperty("code").GetString() == "too_many_attempts", "Après 5 échecs la connexion est bloquée, même avec le bon mot de passe");
        var ghostBlocked = 0;
        for (var i = 0; i < 6; i++) if ((await server.Post("/api/auth/login", new { email = "nobody@example.com", password = "wrong-password" })).Status == 429) ghostBlocked++;
        check(ghostBlocked >= 1, "Un compte inexistant est limité de la même manière");
        check((await server.Post($"/api/admin/users/{alice}/unlock", null, token)).Status == 204 && (await server.Post("/api/auth/login", new { email = "alice@example.com", password = "password-1234" })).Status == 200,
            "L’administrateur débloque un compte");

        // ---- Password change ----
        var aliceSecond = await server.LoginAsync("alice@example.com", "password-1234");
        var me = (await server.Get("/api/auth/me", aliceToken)).Body;
        check(me.GetProperty("passwordChangePending").GetBoolean(), "Un compte créé par l’administrateur propose de changer son mot de passe");
        check((await server.Post("/api/auth/password", new { currentPassword = "bad-current", newPassword = "brand-new-pass" }, aliceToken)).Status == 401, "Changement refusé si le mot de passe actuel est faux");
        check((await server.Post("/api/auth/password", new { currentPassword = "password-1234", newPassword = "short" }, aliceToken)).Body.GetProperty("code").GetString() == "weak_password", "Mot de passe trop court refusé");
        check((await server.Post("/api/auth/password", new { currentPassword = "password-1234", newPassword = "brand-new-pass" }, aliceToken)).Status == 204, "Changement de mot de passe accepté");
        check(!(await server.Get("/api/auth/me", aliceToken)).Body.GetProperty("passwordChangePending").GetBoolean(), "La proposition de changement disparaît");
        check((await server.Get("/api/auth/me", aliceToken)).Status == 200 && (await server.Get("/api/auth/me", aliceSecond)).Status == 401, "Le changement révoque les autres connexions seulement");
        check((await server.Post("/api/auth/login", new { email = "alice@example.com", password = "password-1234" })).Status == 401, "L’ancien mot de passe ne fonctionne plus");

        // ---- Enforced change (policy) ----
        check((await server.Post("/api/auth/password", new { currentPassword = "admin1234", newPassword = "the-admin-secret" }, token)).Status == 204, "L’administrateur change son mot de passe initial");
        await server.SetAsync(token, SettingKeysTest.EnforcePasswordChange, true);
        var (_, bobToken) = await server.NewUserAsync(token, "bob@example.com");
        var enforced = await server.Get("/api/projects", bobToken);
        check(enforced.Status == 403 && enforced.Body.GetProperty("code").GetString() == "password_change_required", "Politique stricte: l’API reste fermée tant que le mot de passe n’est pas changé");
        check((await server.Get("/api/auth/me", bobToken)).Status == 200, "…sauf les points d’authentification");
        await server.Post("/api/auth/password", new { currentPassword = "password-1234", newPassword = "bobs-new-password" }, bobToken);
        var restored = await server.Get("/api/projects", bobToken);
        check(restored.Body.GetProperty("code").GetString() != "password_change_required", "…puis l’accès est rétabli");
        await server.SetAsync(token, SettingKeysTest.EnforcePasswordChange, false);

        // ---- Recovery ----
        var recovery = (await server.Post($"/api/admin/users/{alice}/recovery", null, token)).Body;
        var recoveryToken = recovery.GetProperty("token").GetString()!;
        check(recoveryToken.StartsWith("mhr_"), "L’administrateur émet un code de récupération à usage unique");
        check((await server.Post("/api/auth/recover", new { email = "alice@example.com", token = "mhr_wrong", newPassword = "recovered-pass-1" })).Status == 400, "Un faux code est refusé");
        check((await server.Post("/api/auth/recover", new { email = "alice@example.com", token = recoveryToken, newPassword = "short" })).Status == 400, "Un mot de passe faible n’est pas accepté…");
        check((await server.Post("/api/auth/recover", new { email = "alice@example.com", token = recoveryToken, newPassword = "recovered-pass-1" })).Status == 204, "…et le code n’est pas consommé par cet échec");
        check((await server.Get("/api/auth/me", aliceToken)).Status == 401, "La récupération révoque toutes les connexions");
        check((await server.Post("/api/auth/recover", new { email = "alice@example.com", token = recoveryToken, newPassword = "recovered-pass-2" })).Status == 400, "Un code ne sert qu’une fois");
        check((await server.Post("/api/auth/login", new { email = "alice@example.com", password = "recovered-pass-1" })).Status == 200, "Connexion avec le mot de passe récupéré");

        // ---- Sessions ----
        var logout = await server.LoginAsync("alice@example.com", "recovered-pass-1");
        check((await server.Post("/api/auth/logout", null, logout)).Status == 204 && (await server.Get("/api/auth/me", logout)).Status == 401, "La déconnexion révoque le jeton");
        var first = await server.LoginAsync("alice@example.com", "recovered-pass-1");
        var second = await server.LoginAsync("alice@example.com", "recovered-pass-1");
        var sessions = (await server.Get("/api/auth/sessions", first)).Body;
        check(sessions.GetArrayLength() >= 2 && sessions.EnumerateArray().Any(x => x.GetProperty("current").GetBoolean()), "Liste des connexions actives");
        await server.Post("/api/auth/sessions/revoke-others", null, first);
        check((await server.Get("/api/auth/me", first)).Status == 200 && (await server.Get("/api/auth/me", second)).Status == 401, "Révocation des autres connexions");
        await using (var db = await factory.CreateDbContextAsync())
            await db.Sessions.Where(x => x.TokenHash == PasswordHasher.HashToken(first)).ExecuteUpdateAsync(set => set.SetProperty(x => x.ExpiresUtc, DateTime.UtcNow.AddMinutes(-1)));
        check((await server.Get("/api/auth/me", first)).Status == 401, "Une session expirée est refusée");

        // ---- Cookie sessions: no cross-site writes ----
        var cookieToken = await server.LoginAsync("alice@example.com", "recovered-pass-1");
        var asCookie = new Dictionary<string, string> { ["Cookie"] = $"{SessionAuth.Cookie}={cookieToken}" };
        check((await server.SendAsync(HttpMethod.Get, "/api/auth/me", null, null, asCookie)).Status == 200, "Le cookie de session authentifie les lectures");
        var cross = new Dictionary<string, string>(asCookie) { ["Origin"] = "http://evil.example" };
        check((await server.SendAsync(HttpMethod.Post, "/api/auth/sessions/revoke-others", new { }, null, cross)).Status == 401, "Une écriture inter-sites avec le cookie est refusée");
        check((await server.SendAsync(HttpMethod.Post, "/api/auth/sessions/revoke-others", new { }, null, asCookie)).Status == 200, "La même écriture sans origine étrangère est acceptée");

        // ---- Audit ----
        var audit = (await server.Get("/api/admin/audit?take=500", token)).Body.EnumerateArray().Select(x => x.GetProperty("action").GetString()).ToList();
        check(audit.Contains("auth.login") && audit.Contains("auth.login_failed") && audit.Contains("auth.password_changed") && audit.Contains("server.initialized"), "Le journal d’audit trace connexions, échecs et changements");

        // ---- Restart: credentials are never reset ----
        await server.RestartAsync();
        check((await server.Post("/api/auth/login", new { email = TestServer.AdminEmail, password = "admin1234" })).Status == 401, "Un redémarrage ne réinitialise pas le mot de passe de l’administrateur");
        var adminAgain = await server.LoginAsync(TestServer.AdminEmail, "the-admin-secret");
        check((await server.Get("/api/admin/users", adminAgain)).Body.GetArrayLength() == 3, "Aucun compte recréé au redémarrage");
        var (second2, _) = await server.NewUserAsync(adminAgain, "root2@example.com", "Administrator");
        check((await server.Delete("/api/admin/users/" + (await server.Get("/api/auth/me", adminAgain)).Body.GetProperty("id").GetGuid(), (await server.LoginAsync("root2@example.com", "password-1234")))).Status == 204,
            "Le compte administrateur initial peut être supprimé s’il reste un autre administrateur");
        await server.RestartAsync();
        check((await server.Post("/api/auth/login", new { email = TestServer.AdminEmail, password = "admin1234" })).Status == 401
            && (await server.Post("/api/auth/login", new { email = TestServer.AdminEmail, password = "the-admin-secret" })).Status == 401, "Un compte administrateur supprimé n’est jamais recréé");
        _ = second2;
    }
}

static class SettingKeysTest
{
    public const string EnforcePasswordChange = "security.enforcePasswordChange";
}
