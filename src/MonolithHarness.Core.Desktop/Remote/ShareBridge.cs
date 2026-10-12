using System.Collections.Concurrent;
using System.Text.Json;

namespace MonolithHarness.Core.Remote;

public sealed record ShareComment(Guid ShareId, string Author, string Text, DateTime TimeUtc);

/// <summary>
/// The "local shared" mode: the conversation and its tools stay on this device, which forwards what must be displayed to the server so
/// that authorized people follow it live. Viewers can only read and comment; comments come back here. After a dropped connection the
/// bridge attaches again and sends a fresh snapshot.
/// </summary>
public sealed class ShareBridge
{
    sealed class Active(Func<Task<object>> snapshot, Func<ShareComment, Task> onComment, Func<int, Task>? onViewers)
    {
        public Func<Task<object>> Snapshot { get; } = snapshot;
        public Func<ShareComment, Task> OnComment { get; } = onComment;
        public Func<int, Task>? OnViewers { get; } = onViewers;
        public DateTime LastStream { get; set; }
        public int Viewers { get; set; }
    }

    // Streamed text is cumulative: about twice a second is smooth enough for a viewer and light enough for long answers.
    static readonly TimeSpan StreamInterval = TimeSpan.FromMilliseconds(400);
    readonly ServerConnection connection;
    readonly ConcurrentDictionary<Guid, Active> active = new();
    readonly Dictionary<Guid, IDisposable> watches = [];
    readonly SemaphoreSlim gate = new(1, 1);

    public ShareBridge(ServerConnection connection)
    {
        this.connection = connection;
        connection.RealtimeConnected += ReattachAsync;
    }

    public bool IsActive(Guid share) => active.ContainsKey(share);

    /// <summary>People following this device's conversation right now, besides its owner, as the server counts them.</summary>
    public int Viewers(Guid share) => active.TryGetValue(share, out var state) ? state.Viewers : 0;

    /// <summary>Declares the share on the server, attaches this device as its bridge and sends the first snapshot.</summary>
    public async Task<ServerShare> StartAsync(string title, string clientConversationId, Func<Task<object>> snapshot, Func<ShareComment, Task> onComment, CancellationToken ct = default, bool isPublic = false, Func<int, Task>? onViewers = null)
    {
        var share = await connection.Api.CreateShareAsync(title, clientConversationId, ct, isPublic);
        await AttachAsync(share.Id, snapshot, onComment, onViewers);
        return share;
    }

    /// <summary>Resumes sharing of an existing share (after a restart of the application).</summary>
    public async Task AttachAsync(Guid share, Func<Task<object>> snapshot, Func<ShareComment, Task> onComment, Func<int, Task>? onViewers = null)
    {
        await gate.WaitAsync();
        try
        {
            active[share] = new Active(snapshot, onComment, onViewers);
            if (!watches.ContainsKey(share))
                watches[share] = await connection.WatchAsync("share:" + share, evt => OnShareEventAsync(share, evt), subscribe: false);
        }
        finally { gate.Release(); }
        await SendAttachAsync(share);
    }

    async Task ReattachAsync()
    {
        foreach (var share in active.Keys.ToList())
            try { await SendAttachAsync(share); }
            catch (ServerException) { }
    }

    async Task SendAttachAsync(Guid share)
    {
        if (!active.TryGetValue(share, out var state)) return;
        var socket = connection.Realtime();
        if (!await socket.WaitConnectedAsync(TimeSpan.FromSeconds(10))) throw new ServerException("not_connected", "Connexion temps réel indisponible / Real-time connection unavailable.");
        await socket.SendAsync(new { type = "bridge.attach", shareId = share });
        await socket.SendAsync(new { type = "bridge.snapshot", shareId = share, data = await state.Snapshot() });
    }

    /// <summary>Forwards a display event of the conversation. Streaming updates are throttled; they are cumulative, so nothing is lost.</summary>
    public async Task ForwardAsync(Guid share, string name, object data)
    {
        if (!active.TryGetValue(share, out var state)) return;
        var socket = connection.Realtime();
        if (!socket.IsConnected) return;
        if (name == "stream")
        {
            var now = DateTime.UtcNow;
            if (now - state.LastStream < StreamInterval) return;
            state.LastStream = now;
        }
        try { await socket.SendAsync(new { type = "bridge.event", shareId = share, name, data }); }
        catch (ServerException) { /* reattached with a snapshot on reconnection */ }
    }

    /// <summary>The host ends the sharing for everybody.</summary>
    public async Task StopAsync(Guid share, CancellationToken ct = default)
    {
        await Detach(share);
        await connection.Api.RevokeShareAsync(share, ct);
    }

    /// <summary>Stops forwarding without revoking (the application is closing).</summary>
    public async Task Detach(Guid share)
    {
        await gate.WaitAsync();
        try
        {
            active.TryRemove(share, out _);
            if (watches.Remove(share, out var watch)) watch.Dispose();
        }
        finally { gate.Release(); }
    }

    async Task OnShareEventAsync(Guid share, ServerEvent evt)
    {
        if (!active.TryGetValue(share, out var state) || evt.Data.ValueKind != JsonValueKind.Object) return;
        if (evt.Name == "viewers")
        {
            state.Viewers = evt.Data.TryGetProperty("count", out var count) && count.TryGetInt32(out var people) ? people : 0;
            if (state.OnViewers is { } notify) await notify(state.Viewers);
            return;
        }
        if (evt.Name != "comment") return;
        string Text(string name) => evt.Data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        await state.OnComment(new ShareComment(share, Text("author"), Text("text"), DateTime.UtcNow));
    }
}
