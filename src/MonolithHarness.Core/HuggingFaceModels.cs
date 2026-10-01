using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public sealed record HubModel(string Id, string Purpose, string Family, bool Gated, string License, string Revision)
{
    public override string ToString() => Id;
}
public sealed record HubModelFile(string Name, long Bytes, string Sha256)
{
    public override string ToString() => $"{Name} · {MachineCapabilities.Size(Bytes)}";
}
public sealed record LocalTransferProgress(long Bytes, long? Total, string Detail)
{
    public double? Percent => Total > 0 ? Math.Min(100, 100d * Bytes / Total.Value) : null;
}

public sealed class HuggingFaceModels
{
    static readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
    public async Task<List<HubModel>> SearchAsync(string query, string purpose, string token, CancellationToken ct)
    {
        var filter = purpose == "chat" ? "gguf" : "text-to-image";
        var path = "https://huggingface.co/api/models?search=" + Uri.EscapeDataString(query.Trim()) + "&filter=" + filter + "&sort=downloads&direction=-1&limit=40&full=true";
        var node = await ReadAsync(path, token, ct);
        return (node as JsonArray ?? []).OfType<JsonObject>().Where(x => purpose != "chat" || x["pipeline_tag"]?.ToString() is not ("text-to-image" or "image-to-image" or "text-to-video"))
            .Select(x => new HubModel(x["id"]?.ToString() ?? "", purpose,
            purpose == "image" ? LocalModelCompatibility.ImageFamily(x["id"]?.ToString() ?? "", x) : "", x["gated"]?.ToString() is not (null or "False" or "false"),
            x["cardData"]?["license"]?.ToString() ?? "à consulter sur le dépôt", x["sha"]?.ToString() ?? "main")).Where(x => x.Id.Length > 0).ToList();
    }
    public async Task<(HubModel Model, List<HubModelFile> Files)> FilesAsync(HubModel model, string token, CancellationToken ct)
    {
        var info = (await ReadAsync("https://huggingface.co/api/models/" + RepositoryPath(model.Id) + "?blobs=true", token, ct)).AsObject();
        var family = model.Purpose == "image" ? LocalModelCompatibility.ImageFamily(model.Id, info) : "";
        model = model with { Family = family, Revision = info["sha"]?.ToString() ?? "main" };
        var files = (info["siblings"] as JsonArray ?? []).OfType<JsonObject>().Select(x => new HubModelFile(x["rfilename"]?.ToString() ?? "",
            long.TryParse((x["size"] ?? x["lfs"]?["size"])?.ToString(), out var size) ? size : 0,
            (x["lfs"]?["sha256"] ?? x["lfs"]?["oid"])?.ToString() ?? ""))
            .Where(x => x.Name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) || model.Purpose == "image" && x.Name.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return (model, files);
    }
    public async Task<LocalModel> DownloadAsync(HubModel model, HubModelFile file, LocalProviderSettings settings, string token,
        IProgress<LocalTransferProgress>? progress, CancellationToken ct)
    {
        var repositoryPath = RepositoryPath(model.Id);
        var folder = Path.Combine(settings.ModelDirectory, model.Id.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(folder); SandboxWorkspace.AssertNoLinks(folder);
        var destination = Path.Combine(folder, Path.GetFileName(file.Name));
        if (File.Exists(destination)) throw new IOException("Ce fichier existe déjà. Importez-le ou choisissez un autre dossier.");
        var drive = new DriveInfo(Path.GetPathRoot(folder)!);
        if (file.Bytes > 0 && drive.AvailableFreeSpace < file.Bytes + 512L * 1024 * 1024) throw new IOException("Espace disque insuffisant pour ce téléchargement.");
        var uri = "https://huggingface.co/" + repositoryPath + "/resolve/" + Uri.EscapeDataString(model.Revision) + "/" + string.Join('/', file.Name.Split('/').Select(Uri.EscapeDataString));
        await LocalDownload.CopyAsync(http, new Uri(uri), destination, file.Bytes, file.Sha256, token, progress, ct);
        return new() { Id = Path.GetFileNameWithoutExtension(file.Name), Path = LocalProviderSettings.StoredPath(destination), Purpose = model.Purpose, Family = model.Family, Bytes = new FileInfo(destination).Length, Repository = model.Id };
    }
    static string RepositoryPath(string id)
    {
        var pieces = id.Split('/');
        if (pieces.Length != 2 || pieces.Any(x => string.IsNullOrWhiteSpace(x) || x is "." or ".." || x.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))))
            throw new ArgumentException("Identifiant Hugging Face invalide.");
        return string.Join('/', pieces.Select(Uri.EscapeDataString));
    }
    static async Task<JsonNode> ReadAsync(string url, string token, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new("Bearer", token);
        using var response = await http.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Hugging Face : HTTP {(int)response.StatusCode}. Pour un dépôt privé ou protégé, renseignez un jeton de lecture et acceptez ses conditions sur Hugging Face.");
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token)) ?? throw new IOException("Réponse Hugging Face vide.");
    }
}

internal static class LocalDownload
{
    public static async Task CopyAsync(HttpClient http, Uri uri, string destination, long expectedBytes, string expectedHash, string token,
        IProgress<LocalTransferProgress>? progress, CancellationToken ct)
    {
        var partial = destination + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            // HttpClient clears Authorization on cross-host redirects. Never add it to signed CDN URLs.
            if (!string.IsNullOrEmpty(token) && uri.Host == "huggingface.co") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd("MonolithHarness/1.41.0");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Téléchargement : HTTP {(int)response.StatusCode}. Vérifiez l’accès au dépôt.");
            var length = response.Content.Headers.ContentLength ?? (expectedBytes > 0 ? expectedBytes : (long?)null);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
            {
                var buffer = new byte[128 * 1024]; long received = 0; var last = Environment.TickCount64;
                while (true)
                {
                    var count = await input.ReadAsync(buffer, ct); if (count == 0) break;
                    received += count; digest.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), ct);
                    if (Environment.TickCount64 - last > 150) { progress?.Report(new(received, length, "Téléchargement")); last = Environment.TickCount64; }
                }
                if (expectedBytes > 0 && received != expectedBytes || length > 0 && received != length) throw new IOException("Téléchargement incomplet ; le fichier n’a pas été importé.");
                var hash = Convert.ToHexStringLower(digest.GetHashAndReset());
                if (!string.IsNullOrEmpty(expectedHash) && !hash.Equals(expectedHash.Replace("sha256:", ""), StringComparison.OrdinalIgnoreCase)) throw new IOException("Empreinte SHA-256 incorrecte ; le fichier n’a pas été importé.");
                progress?.Report(new(received, length, string.IsNullOrEmpty(expectedHash) ? "Téléchargé · empreinte distante indisponible" : "SHA-256 vérifié"));
            }
            ct.ThrowIfCancellationRequested(); File.Move(partial, destination, false);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}
