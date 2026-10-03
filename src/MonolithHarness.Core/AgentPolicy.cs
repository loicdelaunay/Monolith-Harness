using System.Text.Json.Nodes;

namespace MonolithHarness.Core;

public static class AgentPolicy
{
    public static string Mode(string? value) => value is "plan" or "chat" ? value : "execute";
    public static string Orchestration(string? value) => value is "auto" or "forced" ? value : "disabled";
    public static bool ReadOnly(string mode) => Mode(mode) is "plan" or "chat";
    // An allow-list is deliberate: new tools, MCP and arbitrary shell commands cannot silently bypass Plan.
    public static bool Allowed(string mode, string tool) => Mode(mode) != "chat" && (!ReadOnly(mode) || tool is
        "asset_inspect" or "asset_capture" or "asset_guides" or "browser_tabs" or "memory_search" or "memory_read" or
        "list_images" or "analyze_image" or "rag_search" or "rag_sources" or "rag_read" or "list_sources" or "read_source" or "glob_sources" or "grep_sources" or "git_changes" or
        "read_page" or "inspect_dom" or "python_info" or "keyboard_keys" or "desktop_applications" or "desktop_screens" or "desktop_screenshot" or "browser_screenshot" or
        "load_skill" or "read_skill_resource" or "skill_locations" or "delegate_tasks" or "todowrite" or "question" or "list_terminals" or "read_terminal" or "wait_terminal" or "file_index_read" || GitTools.IsReadOnly(tool));
    public static void Demand(string mode, string tool)
    {
        if (!Allowed(mode, tool)) throw new UnauthorizedAccessException(Mode(mode) == "chat"
            ? $"Mode Chat : outil '{tool}' indisponible. Passez en Agent pour utiliser les outils / Chat mode has no tools."
            : $"Mode Plan : outil '{tool}' interdit. Passez en Exécution au prochain envoi / Plan mode forbids this tool.");
    }
    public static bool Allowed(Chat chat, string tool) => ConversationModes.IsChat(chat.InteractionMode)
        ? ConversationModes.ToolAllowed(chat, tool) : Allowed(chat.ExecutionMode, tool);
    public static void Demand(Chat chat, string tool)
    {
        if (!ConversationModes.IsChat(chat.InteractionMode)) { Demand(chat.ExecutionMode, tool); return; }
        if (!Allowed(chat, tool)) throw new UnauthorizedAccessException($"Mode Chat : outil '{tool}' indisponible ou skill désactivé / Tool unavailable or skill disabled in Chat mode.");
    }
    public static void Filter(JsonArray definitions, Chat chat)
    {
        for (var i = definitions.Count - 1; i >= 0; i--)
            if (!Allowed(chat, definitions[i]?["function"]?["name"]?.GetValue<string>() ?? "")) definitions.RemoveAt(i);
    }
    public static void Filter(JsonArray definitions, string mode)
    {
        for (var i = definitions.Count - 1; i >= 0; i--)
            if (!Allowed(mode, definitions[i]?["function"]?["name"]?.GetValue<string>() ?? "")) definitions.RemoveAt(i);
    }
    public static string Prompt(string mode, string orchestration) =>
        Mode(mode) == "chat" ? "\nCHAT MODE: Respond conversationally. Only enabled web research and Python skills are available; no project tools, autonomous goals or delegation." :
        (ReadOnly(mode)
            ? "\nPLAN MODE: Analyze, inspect and propose a plan. All writes, shell commands, navigation/interaction and external MCP calls are technically disabled. Do not attempt alternate tools to bypass this. User must switch to Execution for implementation."
            : "\nEXECUTION MODE: Implement the user request within the enabled tools and permission rules.") +
        (Orchestration(orchestration) == "disabled" ? "\nDelegation disabled: do the work yourself." :
            "\nCoordinate independent subtasks with delegate_tasks. Use parallel teams and additional waves within the shared run budget; subagents may delegate independent portions of their task within the configured depth. Review and integrate actual results. Assign exclusive file ownership and avoid overlapping writes or shared browser/desktop interactions. Do not delegate the entire task unchanged or form delegation cycles.");
}

public sealed record OpenCodeRunPolicy(string Mode, string Orchestration, bool ChatWebEnabled = false)
{
    public bool AllowsChatWeb(string action) => Mode == "chat" && ChatWebEnabled && action is "webfetch" or "websearch";
}
