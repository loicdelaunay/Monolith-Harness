using System.Text.Json;
using MonolithHarness.Server.Services;

/// <summary>The message of the moment: set by an administrator, seen by everybody at once, kept across restarts.</summary>
static class AnnouncementChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        var (_, user) = await server.NewUserAsync(admin, "reader@example.com");

        var (_, info) = await server.Get("/api/server");
        check(info.GetProperty("announcement").ValueKind == JsonValueKind.Null, "Sans message, /api/server n’en annonce aucun");
        check((await server.Put("/api/admin/announcement", new { text = "Hello", level = "info" }, user)).Status == 403, "Un utilisateur ordinaire ne peut pas publier de message");
        check((await server.Put("/api/admin/announcement", new { text = "Hello", level = "info" })).Status == 401, "Publier sans être connecté est refusé");

        await using var live = await server.ConnectAsync(user);
        await using var anonymousFollower = await server.ConnectAsync(user);
        var (status, published) = await server.Put("/api/admin/announcement", new { text = "Maintenance ce soir à 22 h", level = "warning", expiresMinutes = 120 }, admin);
        check(status == 200 && published.GetProperty("level").GetString() == "warning" && published.GetProperty("text").GetString() == "Maintenance ce soir à 22 h", "L’administrateur publie un message avec son niveau d’alerte");
        var pushed = await live.WaitEventAsync("server", "announcement");
        check(pushed is { } evt && evt.GetProperty("data").GetProperty("level").GetString() == "warning" && evt.GetProperty("data").GetProperty("text").GetString()!.StartsWith("Maintenance"),
            "Les clients connectés reçoivent le message tout de suite (topic server)");
        check(pushed is { } timed && timed.GetProperty("data").GetProperty("expiresUtc").ValueKind == JsonValueKind.String, "La durée d’affichage est annoncée aux clients");
        (_, info) = await server.Get("/api/server");
        check(info.GetProperty("announcement").GetProperty("text").GetString() == "Maintenance ce soir à 22 h", "/api/server (anonyme) renvoie le message pour les clients qui se connectent après");

        foreach (var (level, text, code) in new[] { ("critical", "x", "invalid_level"), ("info", "   ", "empty_announcement"), ("info", new string('x', 501), "announcement_too_long") })
        {
            var (bad, body) = await server.Put("/api/admin/announcement", new { text, level }, admin);
            check(bad == 400 && body.GetProperty("code").GetString() == code, $"Message refusé: {code}");
        }
        check((await server.Put("/api/admin/announcement", new { text = "x", level = "info", expiresMinutes = 0 }, admin)).Status == 400, "Une durée nulle est refusée");

        var (_, flattened) = await server.Put("/api/admin/announcement", new { text = "Ligne 1\r\nLigne 2\t\u0007fin", level = "ERROR" }, admin);
        check(flattened.GetProperty("text").GetString() == "Ligne 1 Ligne 2 fin" && flattened.GetProperty("level").GetString() == "error", "Le message tient sur une ligne, sans caractère de contrôle, et le niveau est normalisé");
        check(flattened.GetProperty("expiresUtc").ValueKind == JsonValueKind.Null, "Sans durée, le message dure jusqu’à son retrait");

        await server.RestartAsync();
        admin = await server.LoginAsync();
        (_, info) = await server.Get("/api/server");
        check(info.GetProperty("announcement").GetProperty("level").GetString() == "error", "Le message survit à un redémarrage du serveur");

        // A message whose time is over disappears by itself.
        await server.Service<SettingsService>().SetTextAsync("announcement.expiresUtc", DateTime.UtcNow.AddMinutes(-1).ToString("O"));
        (_, info) = await server.Get("/api/server");
        check(info.GetProperty("announcement").ValueKind == JsonValueKind.Null && server.Service<AnnouncementService>().Current() == null, "Un message expiré n’est plus annoncé");

        await server.Put("/api/admin/announcement", new { text = "Retour à la normale bientôt", level = "info" }, admin);
        await using var second = await server.ConnectAsync(admin);
        check((await server.Delete("/api/admin/announcement", admin)).Status == 204, "L’administrateur retire le message");
        var withdrawn = await second.WaitEventAsync("server", "announcement");
        check(withdrawn is { } gone && gone.GetProperty("data").ValueKind == JsonValueKind.Null, "Le retrait est annoncé aux clients (données nulles)");
        (_, info) = await server.Get("/api/server");
        check(info.GetProperty("announcement").ValueKind == JsonValueKind.Null, "Après le retrait, /api/server n’annonce plus rien");

        var (_, audit) = await server.Get("/api/admin/audit?action=announcement", admin);
        check(audit.EnumerateArray().Any(x => x.GetProperty("action").GetString() == "announcement.set") && audit.EnumerateArray().Any(x => x.GetProperty("action").GetString() == "announcement.cleared"),
            "Publier et retirer un message sont écrits dans le journal d’audit");
    }
}
