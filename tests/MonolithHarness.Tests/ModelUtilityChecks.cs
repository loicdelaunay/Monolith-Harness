using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using MonolithHarness.Core;

internal static class ModelUtilityChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var source = "salu comment est ce que tu va";
        var doc = ProofreadingDocument.Parse(source, """
            {"edits":[{"start":0,"length":4,"original":"salu","replacement":"Salut,","reason":"Salutation"},
            {"start":27,"length":2,"original":"va","replacement":"vas ?","reason":"Accord"}]}
            """);
        check(doc.Corrections[1].Start == 27, "Proofreader anchors exact fragments");
        doc.Apply(doc.Corrections[0]);
        check(doc.Corrections[0].Start == 29 && doc.Text.EndsWith("va"), "Applying one correction shifts following offsets");
        check(doc.ApplyAll() == "Salut, comment est ce que tu vas ?", "Apply all preserves source outside corrections");
        doc = ProofreadingDocument.Parse("😀 salu", """{"edits":[{"start":2,"length":4,"original":"salu","replacement":"salut","reason":""}]}""");
        check(doc.Corrections[0].Start == 3 && doc.ApplyAll() == "😀 salut", "Unique fragments repair model Unicode offsets");
        doc = ProofreadingDocument.Parse("Bonjour", """{"edits":[{"start":7,"length":0,"original":"","replacement":" !","reason":"Ponctuation"}]}""");
        check(doc.ApplyAll() == "Bonjour !", "Punctuation insertion is supported");
        doc = ProofreadingDocument.Parse("salu", "```json\n{\"edits\":[{\"start\":0,\"length\":4,\"original\":\"salu\",\"replacement\":\"salut\",\"reason\":\"\"}]}\n```");
        doc.Ignore(doc.Corrections[0]);
        check(doc.ApplyAll() == "salu", "Ignored corrections preserve text");
        static bool Invalid(string text, string json) { try { ProofreadingDocument.Parse(text, json); return false; } catch (FormatException) { return true; } }
        check(Invalid("salu salu", """{"edits":[{"start":3,"length":4,"original":"salu","replacement":"salut","reason":""}]}"""), "Ambiguous source fragments cannot replace arbitrary occurrences");
        check(Invalid("abc", """{"edits":[{"start":0,"length":2,"original":"ab","replacement":"A","reason":""},{"start":1,"length":2,"original":"bc","replacement":"B","reason":""}]}"""), "Overlapping edits are rejected");
        check(Invalid("abc", """{"edits":[{"start":8,"length":0,"original":"","replacement":"!","reason":""}]}"""), "Out-of-bounds insertion is rejected");
        check(Invalid("abc", "No mistakes") && Invalid("abc", "{\"edits\":[null]}"), "Malformed model results surface as errors");
        check(ProofreadingDocument.Parse("Correct.", "{\"edits\":[]}").Corrections.Count == 0, "An explicit empty edit list means no mistakes");
        var messages = ModelUtilityPrompts.Translate("Bonjour", "French", "English").Messages();
        var measured = ModelToolClient.Measure(new(new JsonObject { ["content"] = "Hello", ["reasoning_content"] = "Thinking" }, 12, 7, 99), messages, 2, .4);
        check(measured.TotalTokens == 19 && measured.TokensPerSecond == 3.5 && !measured.Estimated && measured.FirstTokenSeconds == .4, "Metrics use provider tokens and external end-to-end elapsed time");
        var estimated = ModelToolClient.Measure(new(new JsonObject { ["content"] = "1234", ["reasoning_content"] = "5678" }, null, null, 0), messages, 2, null);
        check(estimated.Estimated && estimated.OutputTokens == 2 && estimated.InputTokens > 0, "Missing usage is explicitly estimated including reasoning");
        var mixed = ModelToolClient.Measure(new(new JsonObject { ["content"] = "Hello" }, 9, null, 0), messages, 0, null);
        check(!mixed.InputEstimated && mixed.OutputEstimated && mixed.TokensPerSecond == 0, "Mixed usage provenance and zero duration stay honest");
        foreach (var item in ModelBenchmark.Cases.Where(x => x.ExpectedJson != null))
        {
            var answer = ModelUtilityPrompts.ParseObject(item.ExpectedJson!);
            if (item.AcceptedPatches != null) answer["patch"] = item.AcceptedPatches[0];
            check(item.Grade(answer.ToJsonString()) == true && item.Grade("{\"incorrect\":true}") == false, "Benchmark grades " + item.Id);
            if (item.AcceptedPatches != null)
            { answer["patch"] = "wrong"; check(item.Grade(answer.ToJsonString()) == false, "Correct outputs alone do not pass bug fix " + item.Id); }
        }
        check(ModelBenchmark.Cases[0].Grade("A short answer") == null && ModelBenchmark.Cases.Count == 7, "Throughput is not incorrectly counted as a logic score");

        var requests = new List<JsonObject>();
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject());
            check(request.Headers.Authorization?.Parameter == "fixture-secret", "Tools use the selected provider credentials");
            var payload = "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\n" +
                "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":2}}\n\ndata: [DONE]\n\n";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "text/event-stream") };
        }));
        var client = new ModelToolClient(http);
        var provider = new Provider { Model = "selected-model", BaseUrl = "https://fixture.invalid/v1" };
        var result = await client.RunAsync(provider, "fixture-secret", ModelUtilityPrompts.Translate("Bonjour", "French", "English"), _ => { }, default);
        check(result.Text == "Hello" && result.TotalTokens == 12 && result.FirstTokenSeconds != null, "Streaming client returns text, usage and first-token latency");
        check(requests[0]["model"]!.GetValue<string>() == "selected-model" && requests[0]["tools"] == null && requests[0]["messages"]!.AsArray().Count == 2, "Dedicated requests use no chat history or agent tools");
        using var canceledHttp = new HttpClient(new Handler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); throw new Exception("unreachable"); }));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        bool canceled = false;
        try { await new ModelToolClient(canceledHttp).RunAsync(provider, "", new("system", "input"), _ => { }, cancellation.Token); }
        catch (OperationCanceledException) { canceled = true; }
        check(canceled, "Cancel stops an active provider request");
        var posted = false; var removed = false; JsonObject? openCodePayload = null;
        var utilityDirectory = Path.Combine(PortableStorage.Temporary, "model-tools");
        var directoryExisted = Directory.Exists(utilityDirectory);
        using var openCodeHttp = new HttpClient(new Handler(async (request, ct) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (request.Method == HttpMethod.Delete)
            { removed = path == "/session/utility-session"; body = "true"; }
            else if (path == "/session") body = "{\"id\":\"utility-session\"}";
            else if (path.EndsWith("/prompt_async"))
            { openCodePayload = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject(); posted = true; body = "{}"; }
            else if (path == "/experimental/tool/ids") body = "[\"bash\",\"read\",\"external_mcp\"]";
            else if (path.EndsWith("/message")) body = !posted ? "[]" : """
                [{"info":{"id":"answer","role":"assistant","time":{"completed":1},"tokens":{"input":12,"output":3}},
                  "parts":[{"type":"text","text":"Translated text"}]}]
                """;
            else throw new Exception("Unexpected OpenCode tool route: " + path);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }));
        try
        {
            var openCodeProvider = new Provider { Kind = "opencode", BaseUrl = "http://localhost:4096", Model = "provider/model", OpenCodeTools = true };
            var openCodeResult = await new ModelToolClient(openCodeHttp).RunAsync(openCodeProvider, "", new("Translate", "Source"), _ => { }, default);
            check(openCodeResult.Text == "Translated text" && removed, "OpenCode tools create and remove their own isolated session");
            check(openCodeProvider.OpenCodeTools && openCodePayload?["tools"] is JsonObject disabled && disabled.All(p => p.Value!.GetValue<bool>() == false), "OpenCode native and MCP tools are denied without mutating the configured provider");
            check(openCodePayload?["model"]?["providerID"]?.GetValue<string>() == "provider" && openCodePayload?["model"]?["modelID"]?.GetValue<string>() == "model", "OpenCode uses the selected model reference");
        }
        finally
        {
            if (!directoryExisted && Directory.Exists(utilityDirectory) && !Directory.EnumerateFileSystemEntries(utilityDirectory).Any()) Directory.Delete(utilityDirectory);
        }
        var report = new ModelBenchmarkReport(ModelBenchmark.SuiteVersion, "test", DateTimeOffset.UtcNow, "fixture", "model",
            [new("a", "logic", "test", "prompt", measured, true, null, "expected", "reason"), new("b", "bug", "test", "prompt", estimated, false, null, "expected", "reason")], true);
        check(report.Estimated && report.InputTokens == measured.InputTokens + estimated.InputTokens && report.Passed == 1 && report.Graded == 2 && report.TokensPerSecond == 2.25, "Benchmark aggregates tokens with a weighted throughput");
        check(!report.Json().Contains("fixture-secret"), "Benchmark export contains results without provider credentials");
    }

    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
}
