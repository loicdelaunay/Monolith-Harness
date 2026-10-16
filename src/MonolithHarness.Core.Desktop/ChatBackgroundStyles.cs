namespace MonolithHarness.Core;

public sealed record ChatBackgroundStyle(string Id, string French, string English);

public static class ChatBackgroundStyles
{
    public const string Disabled = "none";
    public const int DefaultOpacityPercent = 22;
    public static IReadOnlyList<ChatBackgroundStyle> All { get; } = [
        new(Disabled, "Désactivé", "Disabled"),
        new("code", "Code flottant", "Floating code"),
        new("aurora", "Aurore", "Aurora"),
        new("constellation", "Constellation", "Constellation"),
        new("waves", "Ondulations", "Ripples"),
        new("particles", "Particules", "Particles"),
        new("grid", "Grille lumineuse", "Luminous grid")
    ];
    public static bool IsValid(string? value) => All.Any(style => style.Id == value);
    public static int NormalizeOpacity(int value) => Math.Clamp(value, 0, 100);
    public static string? NormalizeColor(string? value) => AppearanceThemes.IsColor(value?.Trim()) ? value!.Trim().ToUpperInvariant() : null;
    public static string Normalize(string? value) => IsValid(value) ? value! : Disabled;
}
