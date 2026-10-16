using MonolithHarness.Core;
using System.Text.Json.Nodes;

static class ToolImageHistoryChecks
{
    public static void Run(Action<bool, string> check)
    {
        var callIds = new[] { "capture-1", "capture-2", "capture-3" };
        var calls = new JsonArray(callIds.Select(id => (JsonNode)new JsonObject
        {
            ["id"] = id,
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = "asset_capture", ["arguments"] = "{}" }
        }).ToArray());
        var history = new List<Message>
        {
            new() { Role = "assistant", WireJson = new JsonObject { ["role"] = "assistant", ["tool_calls"] = calls }.ToJsonString() }
        };
        history.AddRange(callIds.Select(id => new Message
        {
            Role = "tool",
            WireJson = new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = "captured" }.ToJsonString(),
            Attachments = [new Attachment { Mime = "image/png", Data = [1, 2, 3] }]
        }));
        history.Add(new Message { Role = "assistant", Content = "next answer" });

        var wire = new JsonArray();
        ChatEngine.AppendHistoryWithToolImages(wire, history, tool =>
            ChatEngine.ToWire(new Message { Role = "user", Content = "Tool screenshot", Attachments = tool.Attachments }));

        check(wire.Count == 8 && Enumerable.Range(1, 3).All(i => wire[i]?["role"]?.GetValue<string>() == "tool"
                && wire[i]?["tool_call_id"]?.GetValue<string>() == callIds[i - 1]),
            "All three tool results immediately follow their assistant tool calls");
        check(Enumerable.Range(4, 3).All(i => wire[i]?["role"]?.GetValue<string>() == "user"
                && wire[i]?["content"]?[1]?["image_url"]?["url"]?.GetValue<string>().StartsWith("data:image/png;base64,") == true)
                && wire[7]?["role"]?.GetValue<string>() == "assistant",
            "Captured images remain available after the complete tool-result batch");

        wire = new JsonArray();
        ChatEngine.AppendHistoryWithToolImages(wire, history.Take(2), tool =>
            ChatEngine.ToWire(new Message { Role = "user", Content = "Tool screenshot", Attachments = tool.Attachments }));
        check(wire.Count == 3 && wire[1]?["role"]?.GetValue<string>() == "tool" && wire[2]?["role"]?.GetValue<string>() == "user",
            "A single capture at the end of history still supplies its image");

        VertexNative(check);
    }

    static void VertexNative(Action<bool, string> check)
    {
        Message Call(params (string Id, string Name)[] items) => new()
        {
            Role = "assistant",
            WireJson = new JsonObject
            {
                ["role"] = "assistant",
                ["tool_calls"] = new JsonArray(items.Select(x => (JsonNode)new JsonObject
                {
                    ["id"] = x.Id, ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = x.Name, ["arguments"] = "{}" }
                }).ToArray())
            }.ToJsonString()
        };
        Message Result(string id, string name, bool image) => new()
        {
            Role = "tool", Content = name + "\n{\"ok\":true}",
            WireJson = new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = "{\"ok\":true}" }.ToJsonString(),
            Attachments = image ? [new Attachment { Mime = "image/png", Data = [1, 2, 3] }] : []
        };
        JsonObject Native(string model, IEnumerable<Message> history)
        {
            var wire = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = "system" });
            ChatEngine.AppendHistoryWithToolImages(wire, history, ChatEngine.ToolImageWire);
            var provider = new Provider { Kind = "vertex", Model = model,
                BaseUrl = "https://aiplatform.googleapis.com/v1/projects/test-project/locations/global" };
            using var request = VertexApiKey.CreateRequest(provider, new JsonObject { ["model"] = model, ["messages"] = wire });
            return JsonNode.Parse(request.Content!.ReadAsStringAsync().Result)!.AsObject();
        }

        // Conversation 77: a screenshot ended the history, and replaying it as a user turn was refused by Gemini 4.
        var screenshot = new List<Message> { new() { Role = "user", Content = "Améliore l'UI" },
            Call(("call-shot", "desktop_screenshot")), Result("call-shot", "desktop_screenshot", true) };
        var contents = Native("google/gemini-4-argon", screenshot)["contents"]!.AsArray();
        var last = contents[^1]!;
        var response = last["parts"]?[0]?["functionResponse"];
        check(contents.Count == 3 && last["role"]?.GetValue<string>() == "user" && last["parts"]!.AsArray().Count == 1
                && response?["parts"]?[0]?["inlineData"]?["mimeType"]?.GetValue<string>() == "image/png"
                && response["parts"]![0]!["inlineData"]!["data"]?.GetValue<string>() == "AQID",
            "Gemini 3+ receives a tool screenshot inside its functionResponse, not as a trailing user turn");

        screenshot.Add(new Message { Role = "user", Content = "continue" });
        contents = Native("google/gemini-4-argon", screenshot)["contents"]!.AsArray();
        check(contents.Count == 4 && contents[^1]!["role"]?.GetValue<string>() == "user"
                && contents[^1]!["parts"]!.AsArray().Single()?["text"]?.GetValue<string>() == "continue"
                && contents[^2]!["parts"]!.AsArray().Single()?["functionResponse"]?["parts"]?.AsArray().Count == 1,
            "A later user message gets its own turn after the screenshot response");

        var batch = new List<Message> { new() { Role = "user", Content = "Inspecte" },
            Call(("call-apps", "desktop_applications"), ("call-shot", "desktop_screenshot")),
            Result("call-apps", "desktop_applications", false), Result("call-shot", "desktop_screenshot", true) };
        var parts = Native("google/gemini-4-argon", batch)["contents"]!.AsArray()[^1]!["parts"]!.AsArray();
        check(parts.Count == 2 && parts[0]?["functionResponse"]?["parts"] == null
                && parts[1]?["functionResponse"]?["name"]?.GetValue<string>() == "desktop_screenshot"
                && parts[1]!["functionResponse"]!["parts"]?.AsArray().Count == 1,
            "Each tool image is attached to the function response that produced it");

        contents = Native("google/gemini-2.5-flash", batch)["contents"]!.AsArray();
        check(contents[^2]!["parts"]!.AsArray().Count == 2 && contents[^2]!["parts"]!.AsArray().All(x => x?["functionResponse"]?["parts"] == null)
                && contents[^1]!["parts"]?[1]?["inlineData"]?["mimeType"]?.GetValue<string>() == "image/png",
            "Gemini 2.x keeps tool images as a separate user turn");
        check(contents.All(c => c!["parts"]!.AsArray().All(x => x?["functionResponse"] != null)
                || c["parts"]!.AsArray().All(x => x?["functionResponse"] == null)),
            "Function responses never share a turn with other parts");
    }
}
