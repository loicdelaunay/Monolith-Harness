using MonolithHarness.Server.Security;

namespace MonolithHarness.Server.Realtime;

public static class WebSocketEndpoint
{
    /// <summary>
    /// <c>/ws</c>: authenticated by <c>Authorization: Bearer</c> or the session cookie at the handshake, or by a first
    /// <c>{"type":"auth","token":"..."}</c> message for clients that cannot set headers.
    /// </summary>
    public static void MapRealtime(this IEndpointRouteBuilder app) =>
        app.Map("/ws", async (HttpContext http, RealtimeHub hub, RealtimeGateway gateway) =>
        {
            if (!http.WebSockets.IsWebSocketRequest) { http.Response.StatusCode = StatusCodes.Status400BadRequest; await http.Response.WriteAsJsonAsync(new { code = "websocket_required", message = "Open this address with a WebSocket client." }); return; }
            // Browsers send the cookie on cross-site handshakes: only same-origin pages may use it.
            var usesCookie = !http.Request.Headers.ContainsKey("Authorization") && http.Request.Cookies.ContainsKey(SessionAuth.Cookie);
            if (usesCookie && http.Request.Headers.TryGetValue("Origin", out var origin) && Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri)
                && !uri.Authority.Equals(http.Request.Host.Value, StringComparison.OrdinalIgnoreCase))
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            var connection = new RealtimeConnection(socket, http.User.IsAuthenticated() && http.User.SessionKind() != Data.SessionKinds.Run ? http.User : null);
            await gateway.RunAsync(connection, http.RequestAborted);
        }).WithTags("Real time").WithSummary("WebSocket for streaming replies, run progress, approvals and shared conversations.").ExcludeFromDescription();
}
