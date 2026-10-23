namespace MonolithHarness.Core;

public static class McpAccess
{
    public static bool Enabled(Chat chat) => ConversationAgents.Read(chat.AgentOptionsJson).McpEnabled;
    public static bool Allowed(Chat chat) => Enabled(chat) && chat.AllowOutsideResources && !chat.SandboxEnabled &&
        (ConversationModes.IsChat(chat.InteractionMode) || !AgentPolicy.ReadOnly(chat.ExecutionMode));
    public static bool IsTool(string tool) => tool.StartsWith("mcp_", StringComparison.Ordinal);
}
