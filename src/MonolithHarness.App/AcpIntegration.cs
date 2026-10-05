using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    string AcpSystemPrompt(ConversationRun run) => AcpProviders.SystemPrompt(run.Options.Language, run.Chat.ExecutionMode);
    AcpRunOptions AcpOptions(ConversationRun run)
    {
        var directory = run.Project.GetSourceFolders().FirstOrDefault(Directory.Exists) ??
            Path.Combine(PortableStorage.Folder("AcpWorkspaces"), "chat-" + run.Chat.Id);
        return new(directory, run.Chat.ExecutionMode, run.Provider.OpenCodeTools && PermissionModes.Normalize(state.PermissionMode) != PermissionModes.Deny,
            (request, ct) => RequestAccessAsync("acp|" + run.Provider.Id + "|" + directory + "|" + request.Kind + "|" + request.Title + "|" + string.Join('|', request.Paths),
                WorkflowText("Autorisation d’outil ACP", "ACP tool permission"), request.Title + "\n" + string.Join('\n', request.Paths) + "\n" + request.Details,
                run.Provider.Name + " · " + request.Title, ct));
    }
}
