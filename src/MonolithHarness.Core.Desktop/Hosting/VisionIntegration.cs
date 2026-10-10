using MonolithHarness.Core;

namespace MonolithHarness.Core.Hosting;

public sealed partial class HarnessService
{
    VisionBridge VisionFor(ConversationSession run) => run.Vision ??= new(run, http, Decrypt, Approve);
}
