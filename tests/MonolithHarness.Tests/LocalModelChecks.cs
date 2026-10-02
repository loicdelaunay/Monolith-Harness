using System.Net;
using System.Security.Cryptography;
using System.Text;
using MonolithHarness.Core;

static class LocalModelChecks
{
    sealed class Fixture(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
    }
    static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    public static async Task Run(Action<bool, string> check)
    {
        var requests = new List<(string Path, string? Token)>();
        var bytes = "GGUF\u0003\0\0\0fixture weights"u8.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var http = new HttpClient(new Fixture((request, ct) =>
        {
            requests.Add((request.RequestUri!.PathAndQuery, request.Headers.Authorization?.Parameter));
            if (request.RequestUri.AbsolutePath == "/api/models") return Task.FromResult(Json("""
                [{"id":"author/Model-GGUF","downloads":123456,"likes":89,"lastModified":"2026-09-30T12:00:00Z","gated":false,"sha":"search-revision","cardData":{"license":["apache-2.0","mit"],"base_model":["original/Model"],"description":"A local chat model"}},
                 {"id":"author/Image-GGUF","pipeline_tag":"text-to-image"}]
                """));
            if (request.RequestUri.AbsolutePath.StartsWith("/api/models/")) return Task.FromResult(Json($$$"""
                {"sha":"pinned-revision","gated":"manual","config":{"model_type":"llama"},"cardData":{"license":"mit"},"siblings":[
                  {"rfilename":"weights/Model-Q4_K_M.gguf","lfs":{"size":{{{bytes.Length}}},"sha256":"{{{hash}}}"}},
                  {"rfilename":"Model-mmproj.gguf","size":12},{"rfilename":"README.md","size":100},{"rfilename":"unsafe.ckpt","size":50}]}
                """));
            if (request.RequestUri.AbsolutePath.Contains("/raw/")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("---\r\nlicense: mit\r\n---\r\n# Model card\r\n\r\nLocal model details.") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }));
        var catalog = new HuggingFaceModels(http);
        var found = await catalog.SearchAsync("model with spaces", "chat", "fixture-token", default);
        check(found.Count == 1 && found[0].Id == "author/Model-GGUF", "Local models: chat search filters image repositories");
        check(requests[0].Path.Contains("search=model%20with%20spaces") && requests[0].Path.Contains("filter=gguf"), "Local models: search parameters are escaped and match the selected format");
        var model = found[0];
        check(model.Downloads == 123456 && model.Likes == 89 && model.LastModified?.Year == 2026 && !model.Gated, "Local models: public repository statistics and dates are parsed");
        check(model.License == "apache-2.0, mit" && model.BaseModel == "original/Model" && model.Description == "A local chat model", "Local models: licence and base-model arrays retain useful metadata");
        var response = await catalog.FilesAsync(model, "fixture-token", default);
        check(response.Model.Revision == "pinned-revision" && response.Model.Gated && response.Model.License == "mit" && response.Model.Architecture == "llama", "Local models: file details refresh pinned revision, gated access and architecture");
        check(response.Model.Downloads == model.Downloads && response.Files.Count == 2 && response.Files.Any(x => x.Name.Contains("mmproj")), "Local models: sparse file metadata preserves statistics and exposes unsupported components");
        var file = response.Files.Single(x => x.Name.Contains("Q4_K_M"));
        check(file.Bytes == bytes.Length && file.Sha256 == hash && file.Name.StartsWith("weights/"), "Local models: nested file paths keep their size and SHA-256");
        var readme = await catalog.ReadmeAsync(response.Model, "fixture-token", default);
        check(readme == "# Model card\n\nLocal model details." && requests[^1].Path.Contains("/raw/pinned-revision/README.md"), "Local models: model cards use the pinned revision and hide YAML metadata");
        check(requests.All(x => x.Token == "fixture-token"), "Local models: private repository requests carry the read token");
        using var absentHttp = new HttpClient(new Fixture((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        check(await new HuggingFaceModels(absentHttp).ReadmeAsync(model, "", default) == "", "Local models: repositories without a README still support file browsing");
        using var largeHttp = new HttpClient(new Fixture((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[512 * 1024 + 1]) })));
        try { await new HuggingFaceModels(largeHttp).ReadmeAsync(model, "", default); throw new Exception("Accepted an oversized model card"); }
        catch (IOException) { check(true, "Local models: oversized model cards have a bounded memory limit"); }
        var directory = Path.Combine(Path.GetTempPath(), "monolith-local-model-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new LocalProviderSettings { Directory = directory };
            var imported = await catalog.DownloadAsync(response.Model, file, settings, "fixture-token", null, default);
            check(File.Exists(imported.FullPath) && (await File.ReadAllBytesAsync(imported.FullPath)).SequenceEqual(bytes) && imported.Repository == model.Id, "Local models: verified downloads import into the chosen directory");
            check(requests[^1].Path.Contains("/resolve/pinned-revision/weights/Model-Q4_K_M.gguf"), "Local models: downloads resolve the selected file at the inspected revision");
            await LocalModelImport.InspectAsync(imported.FullPath, "chat", default);
            try { await catalog.DownloadAsync(response.Model, file, settings, "", null, default); throw new Exception("Overwrote existing model"); }
            catch (IOException) { check((await File.ReadAllBytesAsync(imported.FullPath)).SequenceEqual(bytes), "Local models: existing model files cannot be overwritten"); }
            try { await catalog.DownloadAsync(response.Model, file with { Name = "bad-hash.gguf", Sha256 = new string('0', 64) }, settings, "", null, default); throw new Exception("Accepted invalid hash"); }
            catch (IOException) { check(!Directory.EnumerateFiles(directory, "bad-hash*", SearchOption.AllDirectories).Any(), "Local models: a bad checksum leaves no file or partial transfer"); }
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { await catalog.ReadmeAsync(model, "", cancellation.Token); throw new Exception("Ignored cancellation"); }
            catch (OperationCanceledException) { check(true, "Local models: cancelled model-card requests stop immediately"); }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
