using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core.Remote;

/// <summary>The sender of a message: enough to draw an avatar and a name.</summary>
public sealed record ServerAuthor(Guid Id, string Email, string Name)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Email : Name;
}

/// <summary>
/// What a host sends to the viewers of a shared conversation so that they see what the host sees: every message with its reasoning,
/// tool calls and timings (not the hidden plumbing of the engine, not attachments), the text as it is streamed, and the sender of the
/// user messages. The server relays it as-is and keeps nothing.
/// </summary>
public static class ShareContent
{
    const int MaxToolText = 6_000;
    const int MaxText = 60_000;

    /// <summary>One message as a viewer receives it (camelCase on the wire).</summary>
    public static object Of(Message m, ServerAuthor? author = null)
    {
        string? reasoning = null, toolCallId = null;
        object? toolCalls = null;
        if (!string.IsNullOrEmpty(m.WireJson))
            try
            {
                if (JsonNode.Parse(m.WireJson) is JsonObject wire)
                {
                    reasoning = wire["reasoning_content"]?.GetValue<string>();
                    toolCallId = wire["tool_call_id"]?.GetValue<string>();
                    if (wire["tool_calls"] is JsonArray calls)
                        toolCalls = calls.OfType<JsonObject>().Select(call => new
                        {
                            id = call["id"]?.GetValue<string>() ?? "", name = call["function"]?["name"]?.GetValue<string>() ?? "", arguments = call["function"]?["arguments"]?.GetValue<string>() ?? ""
                        }).ToList();
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        var content = m.Role == "tool" ? Clip(m.Content, MaxToolText) : Clip(m.Content, MaxText);
        return new
        {
            id = m.Id, role = m.Role, content, state = m.State, seconds = m.Seconds, completedUtc = m.CompletedUtc, cachedInputTokens = m.CachedInputTokens,
            reasoning, toolCalls, toolCallId, author = m.Role == "user" ? author : null
        };
    }

    /// <summary>The conversation as it is now, newest messages first until the size budget is used (the server limits a frame to 512 KB).</summary>
    public static object Snapshot(string title, IEnumerable<Message> history, ServerAuthor? author = null, int budgetBytes = 300_000)
    {
        var picked = new List<object>();
        var used = 0;
        foreach (var message in history.Reverse())
        {
            if (message.Role is "compaction" || message.Content.Length == 0 && string.IsNullOrEmpty(message.WireJson)) continue;
            var item = Of(message, author);
            var size = JsonSerializer.Serialize(item, ServerApi.Json).Length;
            if (picked.Count > 0 && used + size > budgetBytes) break;
            picked.Add(item); used += size;
        }
        picked.Reverse();
        return new { title, messages = picked };
    }

    /// <summary>A tool that just started (its card shows as running on the viewers' side until its result arrives).</summary>
    public static object ToolStarted(string id, string name, string arguments) => new { id, name, arguments = Clip(arguments, 4_000) };

    static string Clip(string text, int limit) => text.Length <= limit ? text : text[..limit] + "…";
}
