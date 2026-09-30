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
    }
}
