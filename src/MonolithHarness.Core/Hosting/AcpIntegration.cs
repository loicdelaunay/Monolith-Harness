namespace MonolithHarness.Core.Hosting;

public sealed partial class HarnessService
{
    AcpRunOptions AcpOptions(ConversationSession run)
    {
        var directory = run.Project.GetSourceFolders().FirstOrDefault(Directory.Exists) ??
            Path.Combine(PortableStorage.Folder("AcpWorkspaces", Path.GetDirectoryName(database)!), "chat-" + run.Chat.Id);
        return new(directory, run.Chat.ExecutionMode, run.Provider.OpenCodeTools && PermissionModes.Normalize(hostOptions?.PermissionModeOverride ?? run.Options.PermissionMode) != PermissionModes.Deny,
            (request, ct) => Approve("acp|" + run.Provider.Id + "|" + directory + "|" + request.Kind + "|" + request.Title + "|" + string.Join('|', request.Paths),
                run.Chat.Title + " · " + run.Provider.Name + " · " + request.Title, string.Join('\n', request.Paths) + "\n" + request.Details, ct));
    }
}
