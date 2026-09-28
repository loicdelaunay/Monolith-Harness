namespace OhMyHarness.Core;

public static class NotificationSounds
{
    public sealed record Sound(string Id, string French, string English);
    public static IReadOnlyList<Sound> All { get; } = [
        new("soft", "Doux", "Soft"), new("bell", "Cloche", "Bell"), new("alert", "Alerte", "Alert"),
        new("chime", "Carillon", "Chime"), new("glass", "Cristal", "Glass"), new("marimba", "Marimba", "Marimba"),
        new("digital", "Digital", "Digital"), new("success", "Réussite", "Success"), new("water", "Goutte d’eau", "Water drop")
    ];
    public static bool Contains(string id) => All.Any(x => x.Id == id);
}
