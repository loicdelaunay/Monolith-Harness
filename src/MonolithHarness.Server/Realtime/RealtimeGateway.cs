using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MonolithHarness.Server.Security;
using MonolithHarness.Server.Services;

namespace MonolithHarness.Server.Realtime;

/// <summary>Authorizes topic subscriptions and handles the messages of one WebSocket connection.</summary>
public sealed class RealtimeGateway(RealtimeHub hub, ShareService shares, ProjectService projects, RunService runs, AccessService access, SessionService sessions,
    ILogger<RealtimeGateway> log)
{
    public const int MaxMessageBytes = 512 * 1024;
    static readonly TimeSpan AuthenticationDelay = TimeSpan.FromSeconds(15);

    public static async Task<string?> ReadMessageAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (message.Length + result.Count > MaxMessageBytes)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None);
                    return null;
                }
                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    /// <summary>Runs the connection until it closes. The user is known from the handshake or from a first <c>auth</c> message.</summary>
    public async Task RunAsync(RealtimeConnection connection, CancellationToken stop)
    {
        var authenticated = connection.User.IsAuthenticated();
        if (authenticated) hub.Register(connection);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var pump = Task.Run(() => connection.PumpAsync(linked.Token));
        var watchdog = Task.Run(() => WatchSessionAsync(connection, linked));
        try
        {
            if (!authenticated)
                hub.Send(connection, new { type = "hello", authenticate = true, message = "Send {\"type\":\"auth\",\"token\":\"...\"} within 15 seconds." });
            else hub.Send(connection, new { type = "hello", user = connection.User.Email() });
            using var deadline = new CancellationTokenSource(AuthenticationDelay);
            while (!linked.IsCancellationRequested && connection.Socket.State == WebSocketState.Open)
            {
                string? text;
                try
                {
                    using var receive = CancellationTokenSource.CreateLinkedTokenSource(linked.Token, connection.User.IsAuthenticated() ? CancellationToken.None : deadline.Token);
                    text = await ReadMessageAsync(connection.Socket, receive.Token);
                }
                catch (OperationCanceledException) { break; }
                catch (WebSocketException) { break; }
                if (text == null) break;
                await HandleAsync(connection, text);
            }
        }
        finally
        {
            shares.Disconnected(connection);
            hub.Remove(connection);
            linked.Cancel();
            try { await Task.WhenAll(pump, watchdog); } catch (Exception) { }
            if (connection.Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                try { await connection.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye", CancellationToken.None); } catch (WebSocketException) { }
        }
    }

    async Task WatchSessionAsync(RealtimeConnection connection, CancellationTokenSource linked)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(linked.Token))
            {
                if (connection.SessionId is not { } id) continue;
                if (await sessions.RefreshAsync(id) is { } identity) connection.User = identity.ToPrincipal(SessionAuth.Scheme);
                else { linked.Cancel(); return; }
            }
        }
        catch (OperationCanceledException) { }
    }

    async Task HandleAsync(RealtimeConnection connection, string text)
    {
        string type = "";
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
            string? Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            Guid Id(string name) => Guid.TryParse(Text(name), out var id) ? id : throw ApiException.BadRequest("invalid_id", $"'{name}' must be a GUID.");
            var reply = root.TryGetProperty("ref", out var r) ? r.Clone() : (JsonElement?)null;

            if (type == "ping") { hub.Send(connection, new { type = "pong" }); return; }
            if (type == "auth")
            {
                var identity = await sessions.ValidateAsync(Text("token") ?? "") ?? throw ApiException.Unauthorized("invalid_token", "Invalid or expired token.");
                if (identity.Kind == Data.SessionKinds.Run) throw ApiException.Forbidden();
                connection.User = identity.ToPrincipal(SessionAuth.Scheme);
                hub.Register(connection);
                hub.Send(connection, new { type = "authenticated", user = connection.User.Email(), @ref = reply });
                return;
            }
            if (!connection.User.IsAuthenticated()) throw ApiException.Unauthorized();

            switch (type)
            {
                case "subscribe":
                    await SubscribeAsync(connection, Text("topic") ?? "");
                    hub.Send(connection, new { type = "subscribed", topic = Text("topic"), @ref = reply });
                    break;
                case "unsubscribe":
                    if (Text("topic") is { } topic && topic != RealtimeHub.ServerTopic && topic != RealtimeHub.UserTopic(connection.UserId ?? Guid.Empty)) hub.Unsubscribe(connection, topic);
                    hub.Send(connection, new { type = "unsubscribed", topic = Text("topic"), @ref = reply });
                    break;
                case "bridge.attach":
                    await shares.AttachAsync(connection, Id("shareId"));
                    hub.Send(connection, new { type = "bridge.attached", shareId = Text("shareId"), @ref = reply });
                    break;
                case "bridge.snapshot":
                    shares.Snapshot(connection, Id("shareId"), root.GetProperty("data"));
                    break;
                case "bridge.event":
                    shares.Event(connection, Id("shareId"), Text("name") ?? "", root.GetProperty("data"));
                    break;
                case "bridge.detach":
                    shares.Disconnected(connection);
                    hub.Send(connection, new { type = "bridge.detached", @ref = reply });
                    break;
                case "share.comment":
                    await shares.CommentAsync(connection.User, Id("shareId"), Text("text"));
                    break;
                default:
                    throw ApiException.BadRequest("unknown_type", "Unknown message type: " + type);
            }
        }
        catch (ApiException ex) { hub.Send(connection, new { type = "error", code = ex.Code, message = ex.Message, request = type }); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        { hub.Send(connection, new { type = "error", code = "invalid_message", message = "The message is not valid.", request = type }); }
        catch (Exception ex)
        {
            log.LogError(ex, "WebSocket message {Type} failed", type);
            hub.Send(connection, new { type = "error", code = "server_error", message = "Unexpected server error.", request = type });
        }
    }

    async Task SubscribeAsync(RealtimeConnection connection, string topic)
    {
        var user = connection.User!;
        var separator = topic.IndexOf(':');
        var kind = separator < 0 ? topic : topic[..separator];
        Guid id = default;
        if (separator >= 0 && !Guid.TryParse(topic[(separator + 1)..], out id)) throw ApiException.BadRequest("invalid_topic", "Invalid topic.");
        switch (kind)
        {
            case "run":
                access.Require(user, Permissions.CloudUse);
                await runs.GetAsync(user, id); // rights are the ones of reading the request
                hub.Subscribe(connection, topic);
                break;
            case "project":
                await projects.RequireAsync(user, id, Data.ProjectAccess.Read);
                hub.Subscribe(connection, topic);
                break;
            case "share":
                await shares.SubscribeAsync(connection, id);
                break;
            case "queue":
                access.RequireQueueManage(user);
                hub.Subscribe(connection, topic);
                break;
            case "server":
            case "user" when id == connection.UserId:
                break; // implicit
            default:
                throw ApiException.BadRequest("unknown_topic", "Unknown topic.");
        }
    }
}
