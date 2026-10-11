using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MonolithHarness.Server.Services;

static class ProjectChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var server = await TestServer.StartAsync();
        var admin = await server.LoginAsync();
        string Code((int Status, JsonElement Body) r) => r.Body.ValueKind == JsonValueKind.Object && r.Body.TryGetProperty("code", out var c) ? c.GetString()! : "";
        var disabled = await server.Get("/api/projects", admin);
        check(disabled.Status == 403 && Code(disabled) == "feature_disabled", "Cloud désactivé: les projets sont inaccessibles");
        await server.EnableAsync(admin, "cloud");

        var (_, olga) = await server.NewUserAsync(admin, "olga@example.com");
        var (_, pat) = await server.NewUserAsync(admin, "pat@example.com");
        var (_, quinn) = await server.NewUserAsync(admin, "quinn@example.com");
        var (_, guest) = await server.NewUserAsync(admin, "guest@example.com", "Guest");
        check(Code(await server.Get("/api/projects", guest)) == "missing_permission", "Sans cloud.use, pas de projets");

        // ---- Lifecycle and isolation ----
        var created = await server.Post("/api/projects", new { name = "  Atelier  " }, olga);
        check(created.Status == 201 && created.Body.GetProperty("name").GetString() == "Atelier" && created.Body.GetProperty("access").GetString() == "owner", "Création d’un projet cloud");
        var project = created.Body.GetProperty("id").GetGuid();
        var other = (await server.Post("/api/projects", new { name = "Autre" }, pat)).Body.GetProperty("id").GetGuid();
        check(Code(await server.Post("/api/projects", new { name = "" }, olga)) == "invalid_name", "Nom de projet obligatoire");
        var paths = server.Paths;
        check(Directory.Exists(paths.ProjectFiles(project)) && File.Exists(paths.ProjectDatabase(project)) && paths.ProjectRoot(project) != paths.ProjectRoot(other), "Un dossier de fichiers et une base moteur par projet");
        check((await server.Get("/api/projects", olga)).Body.GetArrayLength() == 1 && (await server.Get("/api/projects", pat)).Body.GetArrayLength() == 1, "Chacun ne voit que ses projets");
        check((await server.Get($"/api/projects/{project}/files", pat)).Status == 404 && (await server.Get($"/api/projects/{project}/files", admin)).Status == 404,
            "Le projet d’un autre est introuvable, administrateur compris");
        check((await server.Get($"/api/projects/{project}/chats", pat)).Status == 404 && (await server.Delete($"/api/projects/{project}", pat)).Status == 404, "…pour les conversations et la suppression aussi");

        // ---- Files ----
        async Task<int> Upload(string token, string path, byte[] content)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/projects/{project}/files/content?path={Uri.EscapeDataString(path)}") { Content = new ByteArrayContent(content) };
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            return (int)(await server.Http.SendAsync(request)).StatusCode;
        }
        check(await Upload(olga, "src/a.txt", Encoding.UTF8.GetBytes("bonjour")) == 204, "Dépôt d’un fichier dans un sous-dossier");
        var listing = (await server.Get($"/api/projects/{project}/files?path=src", olga)).Body;
        check(listing.GetArrayLength() == 1 && listing[0].GetProperty("name").GetString() == "a.txt" && listing[0].GetProperty("size").GetInt64() == 7, "Liste des fichiers");
        var download = await server.Http.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/projects/{project}/files/content?path=src/a.txt") { Headers = { { "Authorization", "Bearer " + olga } } });
        check(await download.Content.ReadAsStringAsync() == "bonjour" && download.Content.Headers.ContentDisposition?.DispositionType == "attachment" && download.Content.Headers.ContentType?.MediaType == "application/octet-stream",
            "Téléchargement en pièce jointe (jamais interprété par le navigateur)");
        check(File.ReadAllText(Path.Combine(paths.ProjectFiles(project), "src", "a.txt")) == "bonjour", "Le fichier vit dans le workspace du projet");
        check(Code(await server.Get($"/api/projects/{project}/files/content?path=src/missing.txt", olga)) == "not_found", "Fichier absent: 404");

        foreach (var attack in new[] { "../x.txt", "..\\x.txt", "src/../../x.txt", "src/..", "C:\\Windows\\win.ini", "src/con.txt", "NUL", "a:stream", "dossier./f", "trail ", "src\\..\\..\\y" })
            check(await Upload(olga, attack, [1]) == 400 && !File.Exists(Path.Combine(paths.ProjectRoot(project), "x.txt")), $"Chemin refusé: {attack}");
        check(!Directory.EnumerateFiles(paths.Projects, "x.txt", SearchOption.AllDirectories).Any() && !File.Exists(Path.Combine(paths.Workspace, "x.txt")), "Aucun fichier n’a échappé au projet");

        // Links planted in the project (for example by a tool) never lead outside of it.
        var outside = Path.Combine(Path.GetTempPath(), "mh-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "TOP SECRET");
        var link = Path.Combine(paths.ProjectFiles(project), "portal");
        if (TryLink(link, outside))
        {
            check(Code(await server.Get($"/api/projects/{project}/files/content?path=portal/secret.txt", olga)) == "invalid_path", "Lecture à travers un lien refusée");
            check(await Upload(olga, "portal/new.txt", [1]) == 400 && !File.Exists(Path.Combine(outside, "new.txt")), "Écriture à travers un lien refusée");
            check(!(await server.Get($"/api/projects/{project}/files", olga)).Body.EnumerateArray().Any(x => x.GetProperty("name").GetString() == "portal"), "Les liens n’apparaissent pas dans les listes");
            check(!(await server.Get("/api/projects", olga)).Body.ToString().Contains("TOP SECRET"), "Le contenu externe n’est pas compté");
            Directory.Delete(link);
        }
        else check(true, "Liens symboliques non testables sur cette machine (ignoré)");

        // ---- Roles inside a project ----
        check(Code(await server.Put($"/api/projects/{project}/members", new { members = new[] { new { email = "ghost@example.com", access = "read" } } }, olga)) == "unknown_user", "Membre inconnu refusé");
        check((await server.Put($"/api/projects/{project}/members", new { members = new[] { new { email = "quinn@example.com", access = "read" }, new { email = "pat@example.com", access = "run" } } }, olga)).Status == 204, "Le propriétaire gère les membres");
        check((await server.Get($"/api/projects/{project}/files?path=src", quinn)).Status == 200 && (await server.Get("/api/projects", quinn)).Body[0].GetProperty("access").GetString() == "read", "Un membre en lecture voit les fichiers");
        check(await Upload(quinn, "q.txt", [1]) == 403 && (await server.Delete($"/api/projects/{project}/files?path=src/a.txt", quinn)).Status == 403, "…sans pouvoir écrire ni supprimer");
        check(await Upload(pat, "p.txt", [1]) == 204, "Un membre « run » écrit des fichiers");
        check(Code(await server.Put($"/api/projects/{project}/members", new { members = Array.Empty<object>() }, pat)) == "project_access_denied", "Seul le propriétaire gère les membres");
        check(Code(await server.SendAsync(HttpMethod.Patch, $"/api/projects/{project}", new { name = "x" }, pat)) == "project_access_denied" && (await server.Delete($"/api/projects/{project}", pat)).Status == 403, "…renomme ou supprime");
        await server.Put($"/api/projects/{project}/members", new { members = new[] { new { email = "pat@example.com", access = "run" } } }, olga);
        check((await server.Get($"/api/projects/{project}/files", quinn)).Status == 404, "Un membre retiré perd l’accès");

        // ---- Quota ----
        await server.SetAsync(admin, "execution.projectDiskMb", 16);
        check(await Upload(olga, "big1.bin", new byte[10 * 1024 * 1024]) == 204, "Fichier de 10 Mo sous le quota de 16 Mo");
        using (var request = new HttpRequestMessage(HttpMethod.Put, $"/api/projects/{project}/files/content?path=big2.bin") { Content = new ByteArrayContent(new byte[10 * 1024 * 1024]) })
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + olga);
            var response = await server.Http.SendAsync(request);
            check((int)response.StatusCode == 413 && (await response.Content.ReadAsStringAsync()).Contains("quota_exceeded"), "Le quota disque du projet est appliqué");
        }
        check(!File.Exists(Path.Combine(paths.ProjectFiles(project), "big2.bin")) && !Directory.EnumerateFiles(paths.ProjectFiles(project), "*.upload-*").Any(), "Aucun fichier partiel après un dépôt refusé");
        check(await Upload(olga, "big1.bin", new byte[2 * 1024 * 1024]) == 204, "Remplacer un fichier libère son ancienne taille");

        // ---- Delete files ----
        check((await server.Delete($"/api/projects/{project}/files?path=big1.bin", olga)).Status == 204 && !File.Exists(Path.Combine(paths.ProjectFiles(project), "big1.bin")), "Suppression d’un fichier");
        check((await server.Delete($"/api/projects/{project}/files?path=src", olga)).Status == 204 && !Directory.Exists(Path.Combine(paths.ProjectFiles(project), "src")), "Suppression d’un dossier");
        check(Code(await server.Delete($"/api/projects/{project}/files?path=", olga)) == "invalid_path", "La racine du projet ne se supprime pas");

        // ---- Conversations ----
        var chat = (await server.Post($"/api/projects/{project}/chats", new { title = "Première" }, olga)).Body;
        check(chat.GetProperty("title").GetString() == "Première", "Création d’une conversation");
        var chatId = chat.GetProperty("id").GetInt32();
        check((await server.Get($"/api/projects/{project}/chats", olga)).Body.GetArrayLength() >= 2, "Liste des conversations (celle du projet initial incluse)");
        check((await server.Get($"/api/projects/{project}/chats/{chatId}/messages", olga)).Body.GetArrayLength() == 0, "Messages d’une conversation neuve");
        check((await server.Get($"/api/projects/{project}/chats/9999/messages", olga)).Status == 404, "Conversation inconnue: 404");
        check(Code(await server.Post($"/api/projects/{project}/chats/{chatId}/requests", new { text = "Bonjour", providerId = Guid.NewGuid(), model = "m" }, olga)) == "provider_not_allowed", "Requête sans provider autorisé refusée");

        // ---- Deletion ----
        check((await server.Delete($"/api/projects/{project}", olga)).Status == 204 && !Directory.Exists(paths.ProjectRoot(project)), "La suppression retire le projet et tout son dossier");
        check((await server.Get("/api/projects", olga)).Body.GetArrayLength() == 0 && Directory.Exists(paths.ProjectRoot(other)), "…sans toucher aux autres projets");
        Directory.Delete(outside, true);
        check(File.Exists(Path.Combine(paths.ProjectFiles(other), "..", "engine", "database.sqlite")), "Les projets voisins gardent leur base");
    }

    static bool TryLink(string link, string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
                process.WaitForExit();
                return process.ExitCode == 0 && Directory.Exists(link);
            }
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception) { return false; }
    }
}
