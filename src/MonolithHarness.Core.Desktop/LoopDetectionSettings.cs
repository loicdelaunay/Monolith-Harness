namespace MonolithHarness.Core;

/// <summary>Detects consecutive identical calls, independently of model retries and agent step limits.</summary>
public sealed class LoopDetectionSettings
{
    public bool Enabled { get; set; } = true;
    public int RepeatedCallThreshold { get; set; } = 3;
    public string Action { get; set; } = "ask";
    public void Validate()
    {
        if (RepeatedCallThreshold is < 2 or > 50 || Action is not ("ask" or "stop"))
            throw new ArgumentException("Réglages de détection de boucle invalides / Invalid loop detection settings.");
    }
}
