using System.Text.Json;

namespace MonolithHarness.Core;

public sealed class AgentRole
{
    public string Name { get; set; } = "";
    public string Instruction { get; set; } = "";
}

public sealed class ConversationAgents
{
    public bool AutomaticCount { get; set; } = true;
    public bool AutomaticRoles { get; set; } = true;
    public int Count { get; set; } = 2;
    public List<AgentRole> Roles { get; set; } = [];
    public const int MaximumTeamSize = 512;
    // Team size applies to each wave. The runtime separately enforces the shared run budget.
    public int? BatchSize => AutomaticCount ? AutomaticRoles ? null : Roles.Count : Count;
    public static ConversationAgents Read(string? json)
    {
        try { var value = JsonSerializer.Deserialize<ConversationAgents>(json ?? "{}"); value?.Validate(); return value ?? new(); }
        catch { return new(); }
    }
    public void Validate()
    {
        if (Count is < 1 or > MaximumTeamSize || Roles == null || Roles.Count > MaximumTeamSize) throw new ArgumentException("Nombre d’agents invalide / Invalid agent count.");
        if (Roles.Any(r => r == null || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 80 || string.IsNullOrWhiteSpace(r.Instruction) || r.Instruction.Length > 4000) || Roles.Select(r => r.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Roles.Count)
            throw new ArgumentException("Chaque rôle doit avoir un nom unique et une consigne / Each role needs a unique name and instructions.");
        if (!AutomaticRoles && (Roles.Count == 0 || !AutomaticCount && Count > Roles.Count))
            throw new ArgumentException("Ajoutez un rôle pour chaque agent demandé / Add a role for every requested agent.");
    }
    public string Json() { Validate(); return JsonSerializer.Serialize(this); }
    public string Instructions => "\nSUBAGENT CONFIGURATION: " +
        (AutomaticCount ? "Choose a useful team size within the shared run budget. " : $"The user selected {Count} agents per wave. ") +
        (AutomaticRoles ? "Choose distinct roles appropriate to the task." : "Use only these named roles and follow their instructions: " + JsonSerializer.Serialize(Roles));
}

public sealed class AgentPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Mode { get; set; } = "auto";
    public ConversationAgents Options { get; set; } = new();
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Mode is not ("auto" or "forced" or "disabled") || Options == null) throw new ArgumentException("Preset invalide / Invalid preset.");
        Options.Validate();
    }
    public override string ToString() => Name;
}
