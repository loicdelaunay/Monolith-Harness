using MonolithHarness.Core;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    VisionBridge VisionFor(ConversationRun run) => run.Vision ??= new(run, http,
        (secret, _) => Task.FromResult(KeyVault.Decrypt(secret)),
        (scope, title, details, ct) => RequestAccessAsync(scope, title, details, title, ct));
}
