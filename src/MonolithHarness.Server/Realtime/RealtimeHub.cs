using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Realtime;

public sealed record RealtimeMessage(string Topic, string Name, JsonElement Data, DateTime TimeUtc);

/// <summary>One WebSocket client. Writes go through a bounded outbox so a slow reader can never block the server.</summary>
public sealed class RealtimeConnection(WebSocket socket, ClaimsPrincipal? user, int outboxSize = 2048)
{
    readonly Channel<string> outbox = Channel.CreateBounded<string>(new BoundedChannelOptions(outboxSize) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    public Guid Id { get; } = Guid.NewGuid();
    public WebSocket Socket { get; } = socket;
    public ClaimsPrincipal? User { get; set; } = user;
    public HashSet<string> Topics { get; } = [];
    public bool Overflowed { get; private set; }
    public Guid? UserId => User.UserId();
    public Guid? SessionId => User.SessionId();

    public bool Enqueue(string json)
    {
        if (outbox.Writer.TryWrite(json)) return true;
        Overflowed = true;
        return false;
    }

    public void Complete() => outbox.Writer.TryComplete();

    public async Task PumpAsync(CancellationToken ct)
    {
        await foreach (var json in outbox.Reader.ReadAllAsync(ct))
        {
            if (Socket.State != WebSocketState.Open) return;
            await Socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, ct);
        }
    }
}

/// <summary>
/// Topic-based publish/subscribe shared by WebSocket clients and the in-process Blazor UI.
/// Topics: <c>user:{id}</c> (implicit), <c>run:{id}</c>, <c>project:{id}</c>, <c>share:{id}</c>, <c>queue</c>, <c>server</c>.
/// </summary>
public sealed class RealtimeHub
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly ConcurrentDictionary<Guid, RealtimeConnection> connections = new();
    readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, RealtimeConnection>> topics = new();
    readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Action<RealtimeMessage>>> listeners = new();

    public int ConnectionCount => connections.Count;

    /// <summary>Raised (with the topic) whenever a connection joins or leaves a topic, including when it disconnects.</summary>
    public event Action<string>? MembershipChanged;

    /// <summary>The connections currently subscribed to a topic.</summary>
    public IReadOnlyList<RealtimeConnection> Subscribers(string topic) => topics.TryGetValue(topic, out var set) ? [.. set.Values] : [];

    public static string UserTopic(Guid id) => "user:" + id;
    public static string RunTopic(Guid id) => "run:" + id;
    public static string ProjectTopic(Guid id) => "project:" + id;
    public static string ShareTopic(Guid id) => "share:" + id;
    public const string QueueTopic = "queue";
    public const string ServerTopic = "server";

    public void Register(RealtimeConnection connection)
    {
        connections[connection.Id] = connection;
        if (connection.UserId is { } user) Subscribe(connection, UserTopic(user));
        Subscribe(connection, ServerTopic);
    }

    public void Remove(RealtimeConnection connection)
    {
        connections.TryRemove(connection.Id, out _);
        foreach (var topic in connection.Topics.ToList()) Unsubscribe(connection, topic);
        connection.Complete();
    }

    public void Subscribe(RealtimeConnection connection, string topic)
    {
        lock (connection.Topics) connection.Topics.Add(topic);
        topics.GetOrAdd(topic, _ => new()).TryAdd(connection.Id, connection);
        RaiseMembership(topic);
    }

    void RaiseMembership(string topic)
    {
        try { MembershipChanged?.Invoke(topic); }
        catch (Exception) { /* an observer must never break a subscription */ }
    }

    public void Unsubscribe(RealtimeConnection connection, string topic)
    {
        lock (connection.Topics) connection.Topics.Remove(topic);
        if (topics.TryGetValue(topic, out var set)) { set.TryRemove(connection.Id, out _); if (set.IsEmpty) topics.TryRemove(topic, out _); }
        RaiseMembership(topic);
    }

    /// <summary>Removes subscribers of a topic (revoked share, removed viewer, ...). The filter keeps the others.</summary>
    public void Unsubscribe(string topic, Func<RealtimeConnection, bool> select)
    {
        if (!topics.TryGetValue(topic, out var set)) return;
        foreach (var connection in set.Values.Where(select).ToList()) Unsubscribe(connection, topic);
    }

    public IDisposable Listen(string topic, Action<RealtimeMessage> callback)
    {
        var id = Guid.NewGuid();
        listeners.GetOrAdd(topic, _ => new())[id] = callback;
        return new Listener(() => { if (listeners.TryGetValue(topic, out var set)) set.TryRemove(id, out _); });
    }

    sealed class Listener(Action dispose) : IDisposable { public void Dispose() => dispose(); }

    public void Publish(string topic, string name, object? data)
    {
        var element = JsonSerializer.SerializeToElement(data, Json);
        var message = new RealtimeMessage(topic, name, element, DateTime.UtcNow);
        if (listeners.TryGetValue(topic, out var local))
            foreach (var callback in local.Values) { try { callback(message); } catch { /* a faulty UI listener must not affect others */ } }
        if (!topics.TryGetValue(topic, out var set) || set.IsEmpty) return;
        var json = Serialize(message);
        foreach (var connection in set.Values) Deliver(connection, json);
    }

    public void PublishToUser(Guid user, string name, object? data) => Publish(UserTopic(user), name, data);

    /// <summary>Sends to a single connection (replay on subscribe, acknowledgements, errors).</summary>
    public void Send(RealtimeConnection connection, object frame) => Deliver(connection, JsonSerializer.Serialize(frame, Json));

    public void SendEvent(RealtimeConnection connection, RealtimeMessage message) => Deliver(connection, Serialize(message));

    static string Serialize(RealtimeMessage message) => JsonSerializer.Serialize(new { type = "event", topic = message.Topic, name = message.Name, data = message.Data, time = message.TimeUtc }, Json);

    void Deliver(RealtimeConnection connection, string json)
    {
        if (connection.Enqueue(json)) return;
        // A client that cannot keep up is disconnected; it can reconnect and resubscribe.
        _ = CloseAsync(connection, WebSocketCloseStatus.PolicyViolation, "Slow consumer");
    }

    public void CloseSession(Guid sessionId)
    {
        foreach (var connection in connections.Values.Where(x => x.SessionId == sessionId).ToList())
            _ = CloseAsync(connection, WebSocketCloseStatus.PolicyViolation, "Session revoked");
    }

    public void CloseUser(Guid userId, Guid? exceptSession = null)
    {
        foreach (var connection in connections.Values.Where(x => x.UserId == userId && x.SessionId != exceptSession).ToList())
            _ = CloseAsync(connection, WebSocketCloseStatus.PolicyViolation, "Session revoked");
    }

    public Task CloseAllAsync() => Task.WhenAll(connections.Values.Select(x => CloseAsync(x, WebSocketCloseStatus.EndpointUnavailable, "Server stopping")));

    static async Task CloseAsync(RealtimeConnection connection, WebSocketCloseStatus status, string reason)
    {
        try
        {
            if (connection.Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await connection.Socket.CloseOutputAsync(status, reason, CancellationToken.None);
        }
        catch { /* already gone */ }
        try { connection.Socket.Abort(); } catch { }
    }
}
