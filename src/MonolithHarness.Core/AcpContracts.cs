namespace MonolithHarness.Core;

public sealed record AcpPermission(string Title, string Kind, string Details, IReadOnlyList<string> Paths);
public sealed record AcpRunOptions(string Directory, string Mode = "plan", bool ToolsEnabled = false,
    Func<AcpPermission, CancellationToken, Task<bool>>? Authorize = null);
