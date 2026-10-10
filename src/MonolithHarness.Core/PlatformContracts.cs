using System.Text.Json.Nodes;
namespace MonolithHarness.Core;

public interface IRetrySettings
{
    bool RetryEnabled { get; }
    int RetryCount { get; }
    int RetryDelaySeconds { get; }
}
public sealed record RetrySettings(bool RetryEnabled = true, int RetryCount = 3, int RetryDelaySeconds = 5) : IRetrySettings;
public sealed record LocalModelInfo(string Id = "", string Purpose = "chat", int? ContextTokens = null)
{
    public static IReadOnlyList<LocalModelInfo> Read(Provider provider)
    {
        try {
            var config=System.Text.Json.JsonSerializer.Deserialize<Catalog>(provider.LocalModelsJson);
            return config?.Models?.Where(x=>x!=null && !string.IsNullOrWhiteSpace(x.Id)).ToArray()??[];
        } catch(System.Text.Json.JsonException) {return [];}
    }
    sealed class Catalog { public List<LocalModelInfo>? Models { get; set; } }
}
public interface IChatEndpoint : IDisposable { string Url { get; } string Key { get; } }
public interface IChatEngineRuntime
{
    bool IsAcpKind(string kind);
    IReadOnlyList<LocalModelInfo> LocalModels(Provider provider);
    Task<List<string>> ModelsAsync(Provider provider, CancellationToken ct);
    Task<IChatEndpoint> StartLocalAsync(Provider provider, CancellationToken ct);
    Task<Completion> PromptExternalAsync(Provider provider, JsonArray messages, Action<GenerationUpdate> update,
        CancellationToken ct, string? effort, AcpRunOptions? options);
    Task<Completion> TrackAsync(Provider provider, int estimate, Func<Action<GenerationUpdate>, Task<Completion>> request,
        Action<GenerationUpdate> update);
}
/// <summary>The desktop composition root supplies process/usage services. Mobile stays API-only.</summary>
public static class ChatEngineServices
{
    public static IChatEngineRuntime Runtime { get; set; } = new ApiOnlyRuntime();
}
public sealed class ApiOnlyRuntime : IChatEngineRuntime
{
    public bool IsAcpKind(string kind) => kind.Equals("chatgpt-acp", StringComparison.OrdinalIgnoreCase) || kind.Equals("antigravity-acp", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<LocalModelInfo> LocalModels(Provider provider) => [];
    static PlatformNotSupportedException Unsupported() => new("Ce fournisseur nécessite les services desktop. Sélectionnez un fournisseur API.");
    public Task<List<string>> ModelsAsync(Provider provider, CancellationToken ct) => throw Unsupported();
    public Task<IChatEndpoint> StartLocalAsync(Provider provider, CancellationToken ct) => throw Unsupported();
    public Task<Completion> PromptExternalAsync(Provider provider, JsonArray messages, Action<GenerationUpdate> update,
        CancellationToken ct, string? effort, AcpRunOptions? options) => throw Unsupported();
    public Task<Completion> TrackAsync(Provider provider, int estimate, Func<Action<GenerationUpdate>, Task<Completion>> request,
        Action<GenerationUpdate> update) => request(update);
}
public static class PathIdentity
{
    public static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
public interface ISecretVault { byte[] Protect(string secret); string Unprotect(byte[] protectedSecret); }
public interface IConversationStore
{
    Task<List<Project>> ProjectsAsync(CancellationToken ct = default);
    Task<List<Chat>> ChatsAsync(int projectId, CancellationToken ct = default);
    Task<List<Message>> MessagesAsync(int chatId, CancellationToken ct = default);
    Task<List<Provider>> ProvidersAsync(CancellationToken ct = default);
    Task<Project> CreateProjectAsync(string name, CancellationToken ct = default);
    Task<Chat> CreateChatAsync(int projectId, CancellationToken ct = default);
    Task SaveProviderAsync(Provider provider, CancellationToken ct = default);
    Task SaveMessageAsync(Message message, CancellationToken ct = default);
}
public sealed record PlatformCapabilities(bool LocalCommands, bool LocalModels, bool DesktopControl, bool ExternalAgents);
