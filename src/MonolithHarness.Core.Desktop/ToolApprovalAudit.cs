namespace MonolithHarness.Core;

/// <summary>Async scopes isolate simultaneous chats and nested tools, including approvals made after awaits.</summary>
public sealed class ToolApprovalAudit : IDisposable
{
    static readonly AsyncLocal<ToolApprovalAudit?> current = new();
    readonly ToolApprovalAudit? parent;
    readonly List<ToolApprovalEvidence> entries = [];
    readonly Action<ToolApprovalEvidence[]>? changed;
    readonly object gate = new();
    ToolApprovalAudit(Action<ToolApprovalEvidence[]>? changed)
    { parent = current.Value; this.changed = changed; current.Value = this; }
    public static ToolApprovalAudit Begin(Action<ToolApprovalEvidence[]>? changed = null) => new(changed);
    public ToolApprovalEvidence[] Snapshot() { lock (gate) return entries.ToArray(); }
    public static bool Decision(bool allowed, string method, string mode, string profile, string scope, string action, CommandGuardResult? risk = null)
    {
        var active = current.Value;
        if (active == null) return allowed;
        var evidence = new ToolApprovalEvidence(allowed, method, string.IsNullOrEmpty(mode) ? "" : PermissionModes.Normalize(mode), ToolAuthorization.Clean(scope, 2048),
            ToolAuthorization.Clean(action, 300), ProjectResources.Decision(profile, scope), DateTimeOffset.UtcNow,
            risk == null ? null : ToolAuthorization.Clean(risk.Validator, 200), risk?.Classification, risk?.Score,
            risk == null ? null : ToolAuthorization.Clean(string.Join(" · ", new[] { risk.Reason, risk.Explanation }.Where(s => !string.IsNullOrWhiteSpace(s)))));
        lock (active.gate)
        {
            if (active.entries.Count >= 128) active.entries.RemoveAt(0);
            active.entries.Add(evidence);
        }
        try { active.changed?.Invoke(active.Snapshot()); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { AppLog.Write(AppLogLevel.Warning, "authorization.display_failed", ex); }
        return allowed;
    }
    public void Dispose() => current.Value = parent;
}
