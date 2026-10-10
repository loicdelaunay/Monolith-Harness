namespace MonolithHarness.Core;

public sealed class CompactionSettings
{
    public static readonly string[] Strengths = ["gentle", "balanced", "strong", "custom"];
    public static readonly string[] Strategies = ["recent", "full", "tools"];
    public bool AutoEnabled { get; set; } = true;
    public string Strength { get; set; } = "balanced";
    public int TriggerPercent { get; set; } = 90;
    public int TargetPercent { get; set; } = 60;
    public string Strategy { get; set; } = "recent";
    public int RecentTurns { get; set; } = 2;
    public int CustomRetentionPercent { get; set; } = 20;
    public int CustomMaxSummaryTokens { get; set; } = 2000;
    public string CustomInstruction { get; set; } = "";
    public double Retention => (Strength switch { "gentle" => 40, "strong" => 10, "custom" => CustomRetentionPercent, _ => 20 }) / 100d;
    public int SummaryLimit => Strength switch { "gentle" => 4000, "strong" => 1000, "custom" => CustomMaxSummaryTokens, _ => 2000 };
    public bool ShouldCompact(int tokens, int limit) => AutoEnabled && limit > 0 && tokens >= Math.Ceiling(limit * TriggerPercent / 100d);
    public void Validate()
    {
        if (!Strengths.Contains(Strength) || !Strategies.Contains(Strategy) || TriggerPercent is < 10 or > 99 ||
            TargetPercent is < 5 or > 90 || TargetPercent >= TriggerPercent || RecentTurns is < 0 or > 10 ||
            CustomRetentionPercent is < 5 or > 60 || CustomMaxSummaryTokens is < 128 or > 32000 ||
            CustomInstruction == null || CustomInstruction.Length > 8000)
            throw new ArgumentException("Compactage invalide : la cible doit être inférieure au seuil / Invalid compaction settings: target must be below trigger.");
    }
    public void Normalize()
    {
        if (!Strengths.Contains(Strength)) Strength = "balanced";
        if (!Strategies.Contains(Strategy)) Strategy = "recent";
        TriggerPercent = Math.Clamp(TriggerPercent, 10, 99);
        TargetPercent = Math.Clamp(TargetPercent, 5, Math.Min(90, TriggerPercent - 1));
        RecentTurns = Math.Clamp(RecentTurns, 0, 10);
        CustomRetentionPercent = Math.Clamp(CustomRetentionPercent, 5, 60);
        CustomMaxSummaryTokens = Math.Clamp(CustomMaxSummaryTokens, 128, 32000);
        CustomInstruction ??= "";
        if (CustomInstruction.Length > 8000) CustomInstruction = CustomInstruction[..8000];
    }
}
