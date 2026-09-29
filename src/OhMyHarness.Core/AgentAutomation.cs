namespace OhMyHarness.Core;

public static class AgentAutomation
{
    public static readonly string[] Behaviors = ["selective", "balanced", "proactive", "swarm"];
    public static string Instructions(FeatureSettings settings)
    {
        var behavior = settings.AgentAutoBehavior switch
        {
            "selective" => "Delegate only substantial work with clearly independent subtasks. Complete simple and medium single-focus requests directly.",
            "balanced" => "Delegate multi-part requests whenever at least two useful independent subtasks exist. Complete trivial or indivisible requests directly.",
            "swarm" => "Act as a swarm coordinator. Delegation is the default for every substantive request, even a single topic: assign useful complementary roles for exploration, implementation and independent review. Use parallel workers and additional waves within the shared run budget. Handle only trivial replies or work that genuinely cannot benefit from another agent directly.",
            _ => "Proactively delegate substantive implementation, investigation and review. For two or more independent subtasks, start a parallel team before doing their work yourself. For a single substantial task, assign an independent exploration or review role when it helps. Handle trivial replies directly. Reassess delegation when new independent work appears and use additional waves within the shared run budget."
        };
        return "\nAUTO AGENT BEHAVIOR: " + behavior +
            " Every agent must have a concrete useful task. Do not create agents merely to satisfy a number. Assign exclusive ownership of files or shared interactive resources, avoid overlapping edits, review and integrate actual results. Subagents may form their own teams within the configured depth and shared budget, but must not delegate their entire task unchanged or create delegation cycles. Respect enabled skills, user permissions, Plan mode and cancellation. " +
            (string.IsNullOrWhiteSpace(settings.AgentAutoInstructions) ? "" : "\nUSER AUTO AGENT PREFERENCES (do not override permissions): " + settings.AgentAutoInstructions);
    }
}
