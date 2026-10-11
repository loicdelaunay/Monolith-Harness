using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using MonolithHarness.Server;
using MonolithHarness.Server.Services;
using MonolithHarness.Server.Workers;

/// <summary>An in-process server on a free loopback port with its own data folder.</summary>
sealed class TestServer : IAsyncDisposable
{
    public const string AdminEmail = "admin@admin.com";
    public const string AdminPassword = "admin1234";
    public ServerPaths Paths { get; }
    public WebApplication App { get; private set; } = null!;
    public HttpClient Http { get; private set; } = null!;
    public string Base { get; private set; } = "";
    readonly Action<WebApplicationBuilder>? configure;
    readonly bool keepData;

    TestServer(ServerPaths paths, Action<WebApplicationBuilder>? configure, bool keepData)
    {
        Paths = paths;
        this.configure = configure;
        this.keepData = keepData;
    }

    public static async Task<TestServer> StartAsync(Action<WebApplicationBuilder>? configure = null, string? data = null)
    {
        var folder = data ?? Path.Combine(Path.GetTempPath(), "mh-server-test-" + Guid.NewGuid().ToString("N"));
        var server = new TestServer(new ServerPaths(folder), configure, data != null);
        await server.BootAsync();
        return server;
    }

    async Task BootAsync()
    {
        App = await ServerApp.BuildAsync(Paths, new ServerOptions { Port = 0 }, configure);
        // Workers are this very server, launched from its build output.
        App.Services.GetRequiredService<WorkerLauncher>().Use("dotnet", ServerDll());
        await App.StartAsync();
        Base = App.Services.GetRequiredService<ServerEndpoints>().LoopbackBase;
        Http = new HttpClient { BaseAddress = new Uri(Base), Timeout = TimeSpan.FromSeconds(60) };
    }

    /// <summary>Stops the server but keeps the data folder, then starts again on it (restart behaviour).</summary>
    public async Task RestartAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        Http.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await BootAsync();
    }

    static string ServerDll()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "MonolithHarness.Server", "bin", "Debug", "net10.0", "MonolithHarness.Server.dll");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Build src/MonolithHarness.Server first.");
    }

    public T Service<T>() where T : notnull => App.Services.GetRequiredService<T>();

    // ---- HTTP ----

    public async Task<(int Status, JsonElement Body)> SendAsync(HttpMethod method, string path, object? body = null, string? token = null, Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        if (token != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        foreach (var (key, value) in headers ?? []) request.Headers.TryAddWithoutValidation(key, value);
        using var response = await Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        var json = text.Length > 0 && response.Content.Headers.ContentType?.MediaType == "application/json" ? JsonDocument.Parse(text).RootElement.Clone() : default;
        return ((int)response.StatusCode, json);
    }

    public Task<(int Status, JsonElement Body)> Get(string path, string? token = null) => SendAsync(HttpMethod.Get, path, null, token);
    public Task<(int Status, JsonElement Body)> Post(string path, object? body = null, string? token = null) => SendAsync(HttpMethod.Post, path, body ?? new { }, token);
    public Task<(int Status, JsonElement Body)> Put(string path, object? body = null, string? token = null) => SendAsync(HttpMethod.Put, path, body ?? new { }, token);
    public Task<(int Status, JsonElement Body)> Delete(string path, string? token = null) => SendAsync(HttpMethod.Delete, path, null, token);

    public async Task<string> LoginAsync(string email = AdminEmail, string password = AdminPassword)
    {
        var (status, body) = await Post("/api/auth/login", new { email, password, label = "test" });
        if (status != 200) throw new Exception($"Login of {email} failed with {status}: {body}");
        return body.GetProperty("token").GetString()!;
    }

    public async Task<Guid> RoleId(string name, string admin)
    {
        var (_, roles) = await Get("/api/admin/roles", admin);
        return roles.EnumerateArray().First(x => x.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();
    }

    /// <summary>Creates an account with the given role and returns its token.</summary>
    public async Task<(Guid Id, string Token)> NewUserAsync(string admin, string email, string role = "User", string password = "password-1234")
    {
        var (status, body) = await Post("/api/admin/users", new { email, password, displayName = email, roleIds = new[] { await RoleId(role, admin) } }, admin);
        if (status != 201) throw new Exception($"User creation failed: {status} {body}");
        return (body.GetProperty("id").GetGuid(), await LoginAsync(email, password));
    }

    public async Task EnableAsync(string admin, params string[] features)
    {
        foreach (var feature in features) await Put($"/api/admin/features/{feature}", new { enabled = true }, admin);
    }

    public Task SetAsync(string admin, string key, object value) => Put($"/api/admin/settings/{key}", new { value = value.ToString()!.ToLowerInvariant() }, admin);

    public async Task<WsClient> ConnectAsync(string? token = null, bool authenticateWithMessage = false)
    {
        var socket = new ClientWebSocket();
        if (token != null && !authenticateWithMessage) socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        await socket.ConnectAsync(new Uri(Base.Replace("http", "ws") + "/ws"), CancellationToken.None);
        var client = new WsClient(socket);
        if (token != null && authenticateWithMessage) await client.SendAsync(new { type = "auth", token });
        return client;
    }

    public async ValueTask DisposeAsync()
    {
        try { await App.StopAsync(); } catch (Exception) { }
        await App.DisposeAsync();
        Http.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (!keepData)
            try { Directory.Delete(Paths.Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>A WebSocket client that collects every frame for later inspection.</summary>
sealed class WsClient : IAsyncDisposable
{
    readonly ClientWebSocket socket;
    readonly Channel<JsonElement> frames = Channel.CreateUnbounded<JsonElement>();
    readonly List<JsonElement> seen = [];
    readonly Task reader;
    public WebSocketCloseStatus? CloseStatus => socket.CloseStatus;
    public WebSocketState State => socket.State;
    public IReadOnlyList<JsonElement> Seen { get { lock (seen) return seen.ToList(); } }

    public WsClient(ClientWebSocket socket)
    {
        this.socket = socket;
        reader = Task.Run(ReadAsync);
    }

    async Task ReadAsync()
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do { result = await socket.ReceiveAsync(buffer, CancellationToken.None); message.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
                if (result.MessageType == WebSocketMessageType.Close) break;
                var frame = JsonDocument.Parse(message.ToArray()).RootElement.Clone();
                lock (seen) seen.Add(frame);
                frames.Writer.TryWrite(frame);
            }
        }
        catch (Exception) { }
        finally { frames.Writer.TryComplete(); }
    }

    public Task SendAsync(object frame) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame)), WebSocketMessageType.Text, true, CancellationToken.None);

    /// <summary>Waits for a frame matching the predicate (frames already received count too).</summary>
    public async Task<JsonElement?> WaitAsync(Func<JsonElement, bool> match, int milliseconds = 15000)
    {
        foreach (var frame in Seen) if (match(frame)) return frame;
        using var timeout = new CancellationTokenSource(milliseconds);
        try
        {
            await foreach (var frame in frames.Reader.ReadAllAsync(timeout.Token))
                if (match(frame)) return frame;
        }
        catch (OperationCanceledException) { }
        return null;
    }

    public Task<JsonElement?> WaitTypeAsync(string type, int milliseconds = 15000) => WaitAsync(x => Str(x, "type") == type, milliseconds);
    public Task<JsonElement?> WaitEventAsync(string topic, string name, int milliseconds = 15000) => WaitAsync(x => Str(x, "type") == "event" && Str(x, "topic") == topic && Str(x, "name") == name, milliseconds);
    public Task<JsonElement?> WaitErrorAsync(string code, int milliseconds = 8000) => WaitAsync(x => Str(x, "type") == "error" && Str(x, "code") == code, milliseconds);
    public int Count(Func<JsonElement, bool> match) => Seen.Count(match);

    public async Task<bool> WaitClosedAsync(int milliseconds = 10000)
    {
        var finished = await Task.WhenAny(reader, Task.Delay(milliseconds));
        return finished == reader;
    }

    public static string Str(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    public async ValueTask DisposeAsync()
    {
        try { if (socket.State == WebSocketState.Open) await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch (Exception) { }
        socket.Dispose();
    }
}
