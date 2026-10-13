namespace MonolithHarness.Core;

public sealed class DesktopInputSettings
{
    public string Mode { get; set; } = "auto";
    public bool AllowAiModeSwitch { get; set; } = true;
    public bool AllowLegacyFallback { get; set; }
    public void Normalize() { if (Mode is not ("auto" or "uia" or "legacy")) Mode = "auto"; }
    public void Validate() { if (Mode is not ("auto" or "uia" or "legacy")) throw new ArgumentException("Mode de contrôle du bureau invalide / Invalid desktop input mode."); }
    public string Resolve(string? requested)
    {
        var mode = string.IsNullOrWhiteSpace(requested) ? "auto" : requested.Trim().ToLowerInvariant();
        if (mode is not ("auto" or "uia" or "legacy")) throw new ArgumentException("mode must be auto, uia or legacy.");
        if (mode == "auto") return Mode;
        if (mode != Mode && (!AllowAiModeSwitch || Mode != "auto"))
            throw new UnauthorizedAccessException("Ce mode est interdit par les réglages du skill / This mode is disabled by the skill settings.");
        return mode;
    }
}
