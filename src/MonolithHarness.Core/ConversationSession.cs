using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace MonolithHarness.Core;

/// <summary>A generation's captured inputs and private persistence context, independent of UI selection.</summary>
public class ConversationSession : IDisposable
{
    public int PendingInputId { get; set; }
    public int SelectedProviderId { get; }
    public CompositeModel? Composite { get; }
    public Dictionary<string,Provider> AgentProviders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public volatile ConversationExport.Progress? ExportProgress;
    public WorkflowTools? Workflow { get; set; }
    public VisionBridge? Vision { get; set; }
    public WebHttpTools WebHttp { get; }
    public ToolLoopGuard LoopGuard { get; } = new();
    public SandboxWorkspace? Sandbox { get; private set; }
    public string? SandboxEngine { get; private set; }
    public async Task PrepareSandboxAsync(CancellationToken ct)
    {
        if (!Chat.SandboxEnabled) return;
        if (Provider.IsOpenCode || AgentProviders.Values.Any(x=>x.IsOpenCode)) throw new InvalidOperationException("Sandbox : OpenCode n'est pas encore isolé. Choisissez un fournisseur OpenAI compatible ou DeepSeek pour l’orchestrateur et les sous-agents / OpenCode is not supported in sandbox mode.");
        SandboxEngine = await SandboxContainer.CheckAsync(ct);
        Sandbox = await SandboxWorkspace.OpenAsync(Db.Database.GetDbConnection().DataSource, Chat.Id, Project.GetSourceFolders(), ct);
        Project.SetSourceFolders(Sandbox.WorkRoots);
        Cancellation.CancelAfter(TimeSpan.FromMinutes(30));
    }
    public Chat Chat { get; }
    public Project Project { get; }
    // OpenCode sessions remain bound to their original directory when the interaction mode changes.
    // This routing context is never used as model tool access in Chat mode.
    public Project OpenCodeProject { get; }
    public Provider Provider { get; }
    public AppState Options { get; }
    public string Prompt { get; }
    public List<Attachment> Images { get; }
    public HarnessDb Db { get; }
    public CancellationTokenSource Cancellation { get; }

    public ConversationSession(Chat chat, Project project, Provider provider, AppState options,
        string prompt, IEnumerable<Attachment> images, string? databasePath = null, IEnumerable<Provider>? availableProviders = null)
    {
        Chat = new Chat { Id = chat.Id, ProjectId = chat.ProjectId, Title = chat.Title, IsFavorite = chat.IsFavorite, IsPinned = chat.IsPinned, IsArchived = chat.IsArchived, UpdatedUtc = chat.UpdatedUtc, AgentOptionsJson = chat.AgentOptionsJson,
            InteractionMode = ConversationModes.Normalize(chat.InteractionMode),
            ChatWebEnabled = chat.ChatWebEnabled, ChatPythonEnabled = chat.ChatPythonEnabled,
            SandboxEnabled = !ConversationModes.IsChat(chat.InteractionMode) && chat.SandboxEnabled, ResourcePathsJson = chat.ResourcePathsJson, TodoDismissed = chat.TodoDismissed,
            ExecutionMode = ConversationModes.IsChat(chat.InteractionMode) ? "chat" : AgentPolicy.Mode(chat.ExecutionMode),
            OrchestrationMode = ConversationModes.IsChat(chat.InteractionMode) ? "disabled" : AgentPolicy.Orchestration(chat.OrchestrationMode) };
        Project = new Project { Id = project.Id, Name = project.Name, IsInbox = project.IsInbox, Icon = project.Icon, Color = project.Color, PermissionProfileJson = project.PermissionProfileJson };
        Project.SetSourceFolders(ConversationModes.IsChat(Chat.InteractionMode) ? [] : ProjectResources.For(chat, project, databasePath));
        Provider = JsonSerializer.Deserialize<Provider>(JsonSerializer.Serialize(provider))!;
        SelectedProviderId=provider.Id;
        if(provider.IsComposite)
        {
            var available=availableProviders?.ToList() ?? throw new InvalidOperationException("Fournisseurs du modèle composé requis.");
            Composite=CompositeModel.Read(provider.CompositeJson);
            if (!ConversationModes.IsChat(Chat.InteractionMode)) Composite.Validate(available);
            Provider=CompositeModel.Resolve(Composite.Orchestrator,available);
            if (!ConversationModes.IsChat(Chat.InteractionMode))
            {
                foreach(var agent in Composite.Agents)AgentProviders[agent.Name]=CompositeModel.Resolve(agent,available);
                Chat.OrchestrationMode="forced";
            }
        }
        OpenCodeProject = Provider.IsOpenCode && ConversationModes.IsChat(Chat.InteractionMode)
            ? ProjectResources.Effective(chat, project, databasePath) : Project;
        Options = new AppState { FeaturesJson = options.FeaturesJson, Language = options.Language, EnabledSkills = options.EnabledSkills, ThinkingLevel = ConversationModes.EffectiveThinking(Chat, options.ThinkingLevel), AutoContinue = options.AutoContinue };
        Options.EnabledSkills = ConversationModes.EffectiveSkills(Chat, options.EnabledSkills, Provider.IsOpenCode);
        Prompt = prompt;
        Images = images.Select(x => new Attachment { Name = x.Name, Mime = x.Mime, Data = [.. x.Data] }).ToList();
        Db = new HarnessDb(databasePath);
        WebHttp = new(FeatureSettings.Read(Options.FeaturesJson).WebHttpResponseMode,
            ConversationArtifacts.DirectoryPath(Chat.Id, databasePath));
        Db.Attach(Chat);
        Cancellation = new CancellationTokenSource();
    }

    public void Dispose()
    {
        WebHttp.Dispose();
        Sandbox?.Dispose();
        Cancellation.Dispose();
        Db.Dispose();
        GC.SuppressFinalize(this);
    }
}
