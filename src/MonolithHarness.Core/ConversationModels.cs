namespace MonolithHarness.Core;

public sealed class Project
{
    public string Icon { get; set; } = "folder";
    public string Color { get; set; } = "";
    public bool IsInbox { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "Mon projet";
    public string SourceFolder { get; set; } = "";
    public string PermissionProfileJson { get; set; } = "";
    public List<Chat> Chats { get; set; } = [];
    public override string ToString() => Name;

    public List<string> GetSourceFolders() =>
        string.IsNullOrWhiteSpace(SourceFolder)
            ? []
            : SourceFolder.Split(['|', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(PathIdentity.Comparer)
                .ToList();

    public void SetSourceFolders(IEnumerable<string> folders)
    {
        SourceFolder = string.Join('|', folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).Distinct(PathIdentity.Comparer));
    }
}
public sealed class Chat
{
    public bool IsPinned { get; set; }
    public DateTime? UpdatedUtc { get; set; } = DateTime.UtcNow;
    public string AgentOptionsJson { get; set; } = "{}";
    public bool IsFavorite { get; set; }
    public bool IsArchived { get; set; }
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Title { get; set; } = "Nouvelle conversation";
    public string InteractionMode { get; set; } = "agent";
    public bool ChatWebEnabled { get; set; } = true;
    public bool ChatPythonEnabled { get; set; }
    public string ExecutionMode { get; set; } = "execute";
    public string OrchestrationMode { get; set; } = "disabled";
    public bool SandboxEnabled { get; set; }
    public string ResourcePathsJson { get; set; } = "";
    public bool AllowOutsideResources { get; set; } = true;
    public bool TodoDismissed { get; set; }
    public List<Message> Messages { get; set; } = [];
    public override string ToString() => Title;
}
public sealed class Message
{
    public DateTime? CompletedUtc { get; set; }
    public int Id { get; set; }
    public int ChatId { get; set; }
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public string CompatibilityNotice { get; set; } = "";
    public string WireJson { get; set; } = "";
    public string State { get; set; } = "complete";
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CachedInputTokens { get; set; }
    public double Seconds { get; set; }
    public List<Attachment> Attachments { get; set; } = [];
}
public sealed class Attachment
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public string Name { get; set; } = "";
    public string Mime { get; set; } = "image/png";
    public byte[] Data { get; set; } = [];
}
public sealed class Provider
{
    public string LocalModelsJson { get; set; } = "{}";
    public bool IsLocal => Kind.Equals("local", StringComparison.OrdinalIgnoreCase);
    public string DetectedModelsJson { get; set; } = "[]";
    public string SelectedModelsJson { get; set; } = "";
    public string CompositeJson { get; set; } = "";
    public bool IsComposite => Kind.Equals("composite", StringComparison.OrdinalIgnoreCase);
    public int Id { get; set; }
    public string Name { get; set; } = "OpenAI";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4.1-mini";
    public byte[] ProtectedKey { get; set; } = [];
    int legacyContextLimit = ModelContexts.DefaultContextLimit;
    public int ContextLimit
    {
        get => ModelContexts.For(this).Limit;
        set => ModelContexts.SetOverride(this, value);
    }
    // Serialized after ContextLimit so snapshots restore automatic/manual state after the compatibility setter.
    public string ModelContextsJson { get; set; } = "{}";
    internal void StoreContextLimit(int value) => legacyContextLimit = value;
    public bool SupportsImages { get; set; } = true;
    public string Kind { get; set; } = "openai";
    public string Username { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public bool AutoStart { get; set; }
    public bool OpenCodeTools { get; set; }
    public bool BypassFreeLimitation { get; set; }
    public string AcpArgumentsJson { get; set; } = "[]";
    public bool IsAcp => ChatEngineServices.Runtime.IsAcpKind(Kind);
    public bool IsExternalAgent => IsOpenCode || IsAcp;
    public bool IsOpenCode => Kind.Equals("opencode", StringComparison.OrdinalIgnoreCase);
    public override string ToString() => Id > 0 ? $"{Name} · #{Id}" : Name;
}
public sealed class ExternalChatSession
{
    public int Id { get; set; }
    public int ChatId { get; set; }
    public int ProviderId { get; set; }
    public string SessionId { get; set; } = "";
}
public sealed class AppState
{
    public int Id { get; set; } = 1;
    public int? ProjectId { get; set; }
    public int? ChatId { get; set; }
    public int ProviderId { get; set; } = 1;
    public string FeaturesJson { get; set; } = "{}";
    public string BrowserUrl { get; set; } = "https://www.bing.com";
    public string Language { get; set; } = "fr";
    public string EnabledSkills { get; set; } = "sources,web";
    public string ThinkingLevel { get; set; } = "auto";
    public string ChatThinkingLevel { get; set; } = "none";
    public string PermissionMode { get; set; } = PermissionModes.Ask;
    public bool AutoContinue { get; set; }
    public bool ShowReasoningDetails { get; set; } = true;
}
public static class PermissionModes
{
    public const string Deny = "deny";
    public const string Ask = "ask";
    public const string Allow = "allow";
    public const string Full = "full";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Deny => Deny,
        Allow => Allow,
        Full => Full,
        _ => Ask
    };

    // null means that the regular per-scope grant/dialog flow must continue.
    public static bool? AutomaticDecision(string? value) => Normalize(value) switch
    {
        Deny => false,
        Allow or Full => true,
        _ => null
    };
}
public sealed class PromptTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public override string ToString() => Name;
}
public sealed class PermissionGrant
{
    public int Id { get; set; }
    public string Scope { get; set; } = "";
    public string Name { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;
}
