using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>A scripted OpenAI-compatible upstream: records what it receives and streams server-sent events.</summary>
sealed class FakeProvider : IAsyncDisposable
{
    public sealed record Received(string Path, string Authorization, JsonObject? Body);

    WebApplication app = null!;
    public string BaseUrl { get; private set; } = "";
    public ConcurrentQueue<Received> Requests { get; } = new();
    /// <summary>Decides the reply to each chat completion; the default answers with a short text.</summary>
    public Func<JsonObject, Reply> Handler { get; set; } = _ => Reply.Text("OK");

    public sealed record Reply(string? Content, string? ToolName = null, string? ToolArguments = null, int Status = 200, int DelayMs = 0)
    {
        public static Reply Text(string text) => new(text);
        public static Reply Tool(string name, object arguments) => new(null, name, JsonSerializer.Serialize(arguments));
    }

    public static async Task<FakeProvider> StartAsync()
    {
        var provider = new FakeProvider();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        provider.app = builder.Build();
        provider.app.MapPost("/v1/chat/completions", provider.Complete);
        provider.app.MapGet("/v1/models", () => Results.Json(new { data = new[] { new { id = "fake-model" } } }));
        await provider.app.StartAsync();
        provider.BaseUrl = provider.app.Urls.First().TrimEnd('/') + "/v1";
        return provider;
    }

    async Task Complete(HttpContext http)
    {
        using var reader = new StreamReader(http.Request.Body);
        var text = await reader.ReadToEndAsync();
        var body = JsonNode.Parse(text) as JsonObject ?? new JsonObject();
        Requests.Enqueue(new Received("chat/completions", http.Request.Headers.Authorization.ToString(), body));
        var reply = Handler(body);
        if (reply.DelayMs > 0) await Task.Delay(reply.DelayMs, http.RequestAborted);
        http.Response.StatusCode = reply.Status;
        if (reply.Status != 200) { await http.Response.WriteAsJsonAsync(new { error = new { message = "scripted failure" } }); return; }
        http.Response.ContentType = "text/event-stream";
        async Task Chunk(object value)
        {
            await http.Response.WriteAsync("data: " + JsonSerializer.Serialize(value) + "\n\n");
            await http.Response.Body.FlushAsync();
        }
        if (reply.ToolName != null)
        {
            await Chunk(new { choices = new[] { new { delta = new { tool_calls = new[] { new { index = 0, id = "call_1", type = "function", function = new { name = reply.ToolName, arguments = reply.ToolArguments } } } } } } });
            await Chunk(new { choices = new[] { new { delta = new { }, finish_reason = "tool_calls" } } });
        }
        else
        {
            var half = reply.Content!.Length / 2;
            await Chunk(new { choices = new[] { new { delta = new { content = reply.Content[..half] } } } });
            await Task.Delay(150);
            await Chunk(new { choices = new[] { new { delta = new { content = reply.Content[half..] } } } });
            await Chunk(new { choices = new[] { new { delta = new { }, finish_reason = "stop" } } });
        }
        await Chunk(new { choices = Array.Empty<object>(), usage = new { prompt_tokens = 12, completion_tokens = 3 } });
        await http.Response.WriteAsync("data: [DONE]\n\n");
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
