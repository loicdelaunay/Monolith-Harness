using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
namespace MonolithHarness.Core;

public static class DesktopComposition
{
    // Preserve existing GUI/CLI construction sites while binding platform services once.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification="Desktop composition root for legacy hosts.")]
    [ModuleInitializer]
    public static void Initialize() => ChatEngineServices.Runtime = new DesktopChatRuntime();
    public static PlatformCapabilities Capabilities { get; } = new(true, true, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), true);
}
sealed class DesktopChatRuntime : IChatEngineRuntime
{
    public bool IsAcpKind(string kind) => AcpProviders.Find(kind) != null;
    public IReadOnlyList<LocalModelInfo> LocalModels(Provider provider) => LocalProviderSettings.Read(provider.LocalModelsJson).Models
        .Select(x => new LocalModelInfo(x.Id, x.Purpose, x.ContextTokens)).ToArray();
    public async Task<List<string>> ModelsAsync(Provider provider, CancellationToken ct)
    {
        if (provider.IsAcp) return await new AcpEngine().ModelsAsync(provider, ct);
        var config = LocalProviderSettings.Read(provider.LocalModelsJson);
        foreach (var model in config.Models.Where(x => x.Purpose == "chat" && x.ContextTokens == null))
            model.ContextTokens = await Task.Run(() => GgufContext.Read(model.FullPath, ct), ct);
        provider.LocalModelsJson = config.Json(); ModelContexts.MergeLocal(provider);
        return ProviderModels.Available(provider);
    }
    public async Task<IChatEndpoint> StartLocalAsync(Provider provider, CancellationToken ct) => await LocalModelRuntime.ChatAsync(provider, ct);
    public Task<Completion> PromptExternalAsync(Provider provider, JsonArray messages, Action<GenerationUpdate> update,
        CancellationToken ct, string? effort, AcpRunOptions? options) => new AcpEngine().PromptAsync(provider, messages, update, ct, effort, options);
    public Task<Completion> TrackAsync(Provider provider, int estimate, Func<Action<GenerationUpdate>, Task<Completion>> request,
        Action<GenerationUpdate> update) => TokenConsumption.TrackAsync(provider, estimate, request, update);
}
public sealed class DesktopSecretVault : ISecretVault
{
    public byte[] Protect(string secret) => KeyVault.Encrypt(secret);
    public string Unprotect(byte[] secret) => KeyVault.Decrypt(secret);
}
