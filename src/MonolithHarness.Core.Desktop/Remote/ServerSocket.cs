using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MonolithHarness.Core.Remote;

/// <summary>An event pushed by the server: <c>{type:"event", topic, name, data}</c>.</summary>
public sealed record ServerEvent(string Topic, string Name, JsonElement Data);

/// <summary>
/// The server's WebSocket: authenticates with the session token, resubscribes after every reconnection and delivers events to a
/// single handler. A dropped connection is retried with a growing delay; <see cref="StopAsync"/> ends it.
/// </summary>
public sealed class ServerSocket : IAsyncDisposable
{
    readonly Uri address;
    readonly Func<string?> token;
    readonly Func<ServerEvent, Task> onEvent;
    readonly Action<string>? onFrame;
    readonly HashSet<string> topics = [];
    readonly SemaphoreSlim sendLock = new(1, 1);
    readonly object gate = new();
    CancellationTokenSource? lifetime;
    Task? loop;
    ClientWebSocket? socket;
    TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ServerSocket(Uri baseUri, Func<string?> token, Func<ServerEvent, Task> onEvent, Action<string>? onFrame = null)
    {
        address = ServerUrl.WebSocket(baseUri);
        this.token = token;
        this.onEvent = onEvent;
        this.onFrame = onFrame;
    }

    public bool IsConnected => socket is { State: WebSocketState.Open };

    /// <summary>Raised after each (re)connection, once the topics were subscribed again.</summary>
    public event Func<Task>? Connected;
    public event Action? Disconnected;

    public void Start()
    {
        lock (gate)
        {
            if (loop != null) return;
            lifetime = new CancellationTokenSource();
            loop = Task.Run(() => RunAsync(lifetime.Token));
        }
    }

    public async Task<bool> WaitConnectedAsync(TimeSpan timeout)
    {
        Task wait;
        lock (gate) wait = connected.Task;
        return await Task.WhenAny(wait, Task.Delay(timeout)) == wait;
    }

    public async Task SubscribeAsync(string topic)
    {
        lock (gate) topics.Add(topic);
        if (IsConnected) await SendAsync(new { type = "subscribe", topic });
    }

    public async Task UnsubscribeAsync(string topic)
    {
        lock (gate) topics.Remove(topic);
        if (IsConnected) await SendAsync(new { type = "unsubscribe", topic });
    }

    public async Task SendAsync(object frame)
    {
        var current = socket;
        if (current is not { State: WebSocketState.Open }) throw new ServerException("not_connected", "Connexion temps réel indisponible / Real-time connection unavailable.");
        await sendLock.WaitAsync();
        try { await current.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame, ServerApi.Json)), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch (WebSocketException ex) { throw new ServerException("not_connected", ex.Message); }
        finally { sendLock.Release(); }
    }

    async Task RunAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            using var current = new ClientWebSocket();
            try
            {
                if (token() is { Length: > 0 } value) current.Options.SetRequestHeader("Authorization", "Bearer " + value);
                current.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await current.ConnectAsync(address, ct);
                socket = current;
                string[] known;
                lock (gate) known = [.. topics];
                foreach (var topic in known) await SendAsync(new { type = "subscribe", topic });
                lock (gate) connected.TrySetResult();
                delay = TimeSpan.FromSeconds(1);
                if (Connected is { } handler) await handler();
                await ReadAsync(current, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is WebSocketException or IOException or HttpRequestException or ServerException) { }
            finally
            {
                socket = null;
                lock (gate) if (connected.Task.IsCompleted) connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Disconnected?.Invoke();
            }
            try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
            delay = TimeSpan.FromSeconds(Math.Min(30, delay.TotalSeconds * 2));
        }
    }

    async Task ReadAsync(ClientWebSocket current, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        while (current.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await current.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) return;
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            JsonDocument document;
            try { document = JsonDocument.Parse(message.ToArray()); }
            catch (JsonException) { continue; }
            using (document)
            {
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : "";
                if (type == "event" && root.TryGetProperty("topic", out var topic) && root.TryGetProperty("name", out var name))
                {
                    var data = root.TryGetProperty("data", out var d) ? d.Clone() : default;
                    try { await onEvent(new ServerEvent(topic.GetString() ?? "", name.GetString() ?? "", data)); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { onFrame?.Invoke("handler: " + ex.Message); }
                }
                else onFrame?.Invoke(root.GetRawText());
            }
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? source;
        Task? running;
        lock (gate) { source = lifetime; running = loop; lifetime = null; loop = null; }
        if (source == null) return;
        source.Cancel();
        try { if (socket is { State: WebSocketState.Open } open) await open.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)); } catch (Exception) { }
        try { if (running != null) await running.WaitAsync(TimeSpan.FromSeconds(3)); } catch (Exception) { }
        source.Dispose();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
