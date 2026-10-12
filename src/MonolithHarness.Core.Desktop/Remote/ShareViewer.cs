using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonolithHarness.Core.Remote;

public sealed record SharedToolCall(string Id, string Name, string Arguments);

/// <summary>
/// A message of a shared conversation as the host shows it: text, reasoning, tool calls and results, timings and (for user messages)
/// the sender. <see cref="Streaming"/> messages are still being written.
/// </summary>
public sealed record SharedMessage(string Key, string Role, string Content, bool Streaming, string Reasoning = "", string State = "complete", double Seconds = 0,
    DateTime? CompletedUtc = null, int? CachedInputTokens = null, IReadOnlyList<SharedToolCall>? ToolCalls = null, string? ToolCallId = null, ServerAuthor? Author = null)
{
    /// <summary>The same message as the local conversation model, so that a window can draw it with the code that draws a local chat.</summary>
    public Message ToMessage(int chatId, int index)
    {
        var wire = new JsonObject { ["reasoning_content"] = Reasoning.Length > 0 ? Reasoning : null, ["tool_call_id"] = ToolCallId };
        if (Author != null) wire["author"] = new JsonObject { ["id"] = Author.Id.ToString(), ["email"] = Author.Email, ["name"] = Author.Name };
        if (ToolCalls is { Count: > 0 })
            wire["tool_calls"] = new JsonArray(ToolCalls.Select(call => (JsonNode?)new JsonObject
            {
                ["id"] = call.Id, ["type"] = "function", ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Arguments }
            }).ToArray());
        return new Message
        {
            Id = int.TryParse(Key, out var id) ? id : index + 1, ChatId = chatId, Role = Role, Content = Content, State = Streaming ? "interrupted" : State, Seconds = Seconds,
            CompletedUtc = CompletedUtc, CachedInputTokens = CachedInputTokens, WireJson = wire.Count == 0 ? "" : wire.ToJsonString()
        };
    }
}

/// <summary>
/// Follows a conversation somebody shares live: the host device forwards what must be displayed (a snapshot, then messages, tool
/// activity and streamed text with its reasoning) and the server relays it. Nothing is stored here: the content lives as long as this
/// viewer and the host are connected. A viewer can read and comment, nothing else.
/// </summary>
public sealed class ShareViewer : IDisposable
{
    readonly ServerConnection connection;
    readonly object gate = new();
    readonly List<SharedMessage> messages = [];
    readonly List<SharedComment> comments = [];
    readonly Dictionary<string, SharedToolCall> runningTools = [];
    IDisposable? watch;
    bool disposed;

    ShareViewer(ServerConnection connection, ServerShare share)
    {
        this.connection = connection;
        Share = share;
        Title = share.Title;
    }

    public ServerShare Share { get; }
    public string Title { get; private set; }
    /// <summary>The host's device is connected: what is shown is current.</summary>
    public bool Online { get; private set; }
    /// <summary>People following besides the host, as the server counts them.</summary>
    public int Viewers { get; private set; }
    /// <summary>Why the conversation ended (revoked by the host, closed), or null while it goes on.</summary>
    public string? ClosedReason { get; private set; }

    public IReadOnlyList<SharedMessage> Messages { get { lock (gate) return [.. messages]; } }
    public IReadOnlyList<SharedComment> Comments { get { lock (gate) return [.. comments]; } }
    /// <summary>Tools the host started whose result has not arrived yet.</summary>
    public IReadOnlyList<SharedToolCall> RunningTools { get { lock (gate) return [.. runningTools.Values]; } }

    /// <summary>Raised, off the UI thread, whenever the content changed (streamed text arrives at most about twice a second).</summary>
    public event Action? Changed;

    public static async Task<ShareViewer> OpenAsync(ServerConnection connection, ServerShare share)
    {
        var viewer = new ShareViewer(connection, share);
        // The server replays the current state on subscription, then sends live events.
        viewer.watch = await connection.WatchAsync("share:" + share.Id, viewer.OnEventAsync);
        return viewer;
    }

    public Task CommentAsync(string text, CancellationToken ct = default) => connection.Api.CommentShareAsync(Share.Id, text, ct);

    Task OnEventAsync(ServerEvent evt)
    {
        lock (gate)
        {
            if (disposed) return Task.CompletedTask;
            switch (evt.Name)
            {
                case "bridge":
                    Online = evt.Data.ValueKind == JsonValueKind.Object && evt.Data.TryGetProperty("online", out var online) && online.ValueKind == JsonValueKind.True;
                    break;
                case "viewers":
                    Viewers = evt.Data.ValueKind == JsonValueKind.Object && evt.Data.TryGetProperty("count", out var count) && count.TryGetInt32(out var people) ? people : 0;
                    break;
                case "title":
                    if (Text(evt.Data, "title") is { Length: > 0 } renamed) Title = renamed;
                    break;
                case "snapshot":
                    messages.Clear(); runningTools.Clear();
                    if (evt.Data.ValueKind == JsonValueKind.Object)
                    {
                        if (Text(evt.Data, "title") is { Length: > 0 } title) Title = title;
                        if (evt.Data.TryGetProperty("messages", out var list) && list.ValueKind == JsonValueKind.Array)
                            foreach (var item in list.EnumerateArray()) Upsert(item, streaming: false);
                    }
                    break;
                case "message":
                    // Engine events nest the message: {chatId, message:{id, role, content, ...}}; tool arguments come beside it.
                    Upsert(evt.Data.ValueKind == JsonValueKind.Object && evt.Data.TryGetProperty("message", out var inner) && inner.ValueKind == JsonValueKind.Object ? inner : evt.Data, streaming: false);
                    break;
                case "stream":
                    Upsert(evt.Data, streaming: true);
                    break;
                case "tool":
                    if (evt.Data.ValueKind == JsonValueKind.Object && Text(evt.Data, "id") is { Length: > 0 } toolId)
                        runningTools[toolId] = new SharedToolCall(toolId, Text(evt.Data, "name"), Text(evt.Data, "arguments"));
                    break;
                case "comment":
                    comments.Add(new SharedComment(Text(evt.Data, "author"), Text(evt.Data, "text"), evt.Data.ValueKind == JsonValueKind.Object && evt.Data.TryGetProperty("time", out var time) && time.TryGetDateTime(out var at) ? at : DateTime.UtcNow));
                    break;
                case "closed":
                    ClosedReason = Text(evt.Data, "reason") == "revoked" ? "L'hôte a mis fin au partage." : "Le partage est terminé.";
                    break;
                default: return Task.CompletedTask;
            }
        }
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    void Upsert(JsonElement element, bool streaming)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        var key = element.TryGetProperty("id", out var id) ? id.ToString() : element.TryGetProperty("messageId", out var messageId) ? messageId.ToString() : Guid.NewGuid().ToString("N");
        var role = streaming ? "assistant" : Text(element, "role") is { Length: > 0 } r ? r : "assistant";
        var content = streaming ? Text(element, "text") : Text(element, "content");
        var reasoning = Text(element, "reasoning");
        // Empty placeholders are not worth showing; a tool result and a message with reasoning or tool calls always are.
        if (!streaming && content.Length == 0 && reasoning.Length == 0 && !element.TryGetProperty("toolCalls", out _)) return;
        List<SharedToolCall>? calls = null;
        if (element.TryGetProperty("toolCalls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
            calls = [.. toolCalls.EnumerateArray().Select(x => new SharedToolCall(Text(x, "id"), Text(x, "name"), Text(x, "arguments")))];
        var callId = Text(element, "toolCallId") is { Length: > 0 } linked ? linked : null;
        ServerAuthor? author = null;
        if (element.TryGetProperty("author", out var who) && who.ValueKind == JsonValueKind.Object && Text(who, "email") is { Length: > 0 } email)
            author = new ServerAuthor(Guid.TryParse(Text(who, "id"), out var authorId) ? authorId : Guid.Empty, email, Text(who, "name"));
        var item = new SharedMessage(key, role, content, streaming, reasoning, Text(element, "state") is { Length: > 0 } state ? state : "complete",
            element.TryGetProperty("seconds", out var seconds) && seconds.ValueKind == JsonValueKind.Number && seconds.TryGetDouble(out var duration) ? duration : 0,
            element.TryGetProperty("completedUtc", out var completed) && completed.ValueKind == JsonValueKind.String && completed.TryGetDateTime(out var at) ? at : null,
            element.TryGetProperty("cachedInputTokens", out var cached) && cached.ValueKind == JsonValueKind.Number && cached.TryGetInt32(out var cachedTokens) ? cachedTokens : null, calls, callId, author);
        var index = messages.FindIndex(x => x.Key == key);
        if (index >= 0) messages[index] = item; else messages.Add(item);
        // Its result arrived: the running card is complete.
        if (callId != null) runningTools.Remove(callId);
        if (!streaming && role == "assistant") runningTools.Clear();
    }

    static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; }
        watch?.Dispose();
    }
}

public sealed record SharedComment(string Author, string Text, DateTime TimeUtc);
