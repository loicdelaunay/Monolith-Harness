namespace MonolithHarness.Core;

/// <summary>Conversation resource boundary, independent of automatic/saved permission grants.</summary>
public static class ResourceAccessPolicy
{
    public const string Denied = "Accès hors pièces jointes désactivé pour cette conversation / Access outside conversation attachments is disabled.";

    // Do not parse shell strings as a filesystem sandbox. Unconstrained executors and unknown
    // tools are unavailable in restricted conversations; sandbox commands stay in private copies.
    public static bool Allowed(Chat chat, string tool) => chat.AllowOutsideResources
        || chat.SandboxEnabled && SandboxWorkspace.Allowed(tool)
        || InformationSkill.Handles(tool) || WebHttpTools.Handles(tool) || MemoryTools.Handles(tool) || FileIndexTools.Handles(tool)
        || tool is "list_sources" or "read_source" or "write_source" or "edit_source"
            or "glob_sources" or "grep_sources" or "patch_sources" or FileProposals.Tool
            or "rag_index" or "rag_search" or "rag_sources" or "rag_read"
            or "list_images" or "analyze_image" or "load_skill" or "read_skill_resource" or "skill_locations"
            or "delegate_tasks" or "todowrite" or "question";

    public static void Demand(Chat chat, string tool)
    {
        if (!Allowed(chat, tool)) throw new UnauthorizedAccessException(Denied + " · " + tool
            + "\nLe terminal local, Python, les outils du bureau, le navigateur et MCP ne garantissent pas cette limite. Utilisez la sandbox pour exécuter du code isolé / Use sandbox mode for isolated commands.");
    }

    public static void DemandProvider(Chat chat, Provider provider)
    {
        if (!chat.AllowOutsideResources && provider.IsExternalAgent)
            throw new UnauthorizedAccessException(Denied + "\nLes agents OpenCode / ACP ne permettent pas de garantir ce périmètre. Choisissez un fournisseur direct ou réactivez l’accès extérieur / Choose a direct provider or enable outside access.");
    }

    public static string Prompt(Chat chat) => chat.AllowOutsideResources ? "" :
        "\nATTACHED RESOURCE BOUNDARY: Work only with this conversation's attached files/folders and its automatic workspace. Absolute outside paths and parent traversal are denied; permissions, saved grants and full-access mode cannot override this. Local terminal/Python, desktop, browser, MCP and external agents are unavailable because they cannot enforce this boundary. In sandbox mode, only isolated command tools are available. Use scoped file tools; do not seek alternate routes to outside files. Application-managed skills, memory, images and HTTP artifacts are available through their scoped tools.";
}
