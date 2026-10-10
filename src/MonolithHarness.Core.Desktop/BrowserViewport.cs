using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public static class BrowserViewport
{
    public const int MobileWidth = 390;
    public static string Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch
    { "desktop" => "desktop", "mobile" => "mobile", _ => throw new ArgumentException("mode must be desktop or mobile / Le mode doit être desktop ou mobile.") };
    public static void AddDefinition(JsonArray definitions) => definitions.Add(new JsonObject
    {
        ["type"] = "function", ["function"] = new JsonObject
        {
            ["name"] = "browser_viewport",
            ["description"] = "Switch the current conversation browser tab between desktop (fills the pane) and mobile (390-pixel responsive width; height fits the pane). Use mobile to inspect small-screen layout and desktop to restore it. Does not navigate, reload or emulate a mobile OS, touch input or user agent. Returns the actual viewport. Requires browser access and Web research; unavailable in Chat mode or Chrome MCP mode.",
            ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject
                { ["mode"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("desktop", "mobile") } },
                ["required"] = new JsonArray("mode"), ["additionalProperties"] = false }
        }
    });
}
