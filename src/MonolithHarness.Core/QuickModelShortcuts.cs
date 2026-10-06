namespace MonolithHarness.Core;

public sealed class QuickModelLevel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public int ProviderId { get; set; }
    public string Model { get; set; } = "";
    public string ThinkingLevel { get; set; } = "auto";
    public string InteractionMode { get; set; } = "agent";
}

public static class QuickModelShortcuts
{
    public const int MaximumLevels = 10;
    public static readonly string[] ThinkingLevels = ["auto", "low", "medium", "high", "none"];

    public static void Normalize(FeatureSettings settings)
    {
        settings.QuickModelSelectionMode = settings.QuickModelSelectionMode == "advanced" ? "advanced" : "simple";
        settings.QuickModelLevels = (settings.QuickModelLevels ?? []).Where(x => x != null).Take(MaximumLevels).ToList();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var level in settings.QuickModelLevels)
        {
            if (string.IsNullOrWhiteSpace(level.Id) || level.Id.Length > 64 || !ids.Add(level.Id))
            { level.Id = Guid.NewGuid().ToString("N"); ids.Add(level.Id); }
            level.Name = (level.Name ?? "").Trim();
            if (level.Name.Length > 60) level.Name = level.Name[..60];
            level.Model = (level.Model ?? "").Trim();
            if (level.Model.Length > 300) level.Model = level.Model[..300];
            level.ProviderId = Math.Max(0, level.ProviderId);
            level.ThinkingLevel = ThinkingLevels.Contains(level.ThinkingLevel) ? level.ThinkingLevel : "auto";
            level.InteractionMode = ConversationModes.Normalize(level.InteractionMode);
        }
        if (settings.QuickModelLevels.Count == 0) settings.QuickModelLevelsEnabled = false;
    }

    public static void Validate(IReadOnlyList<QuickModelLevel> levels, bool enabled)
    {
        if (levels == null || levels.Count > MaximumLevels || enabled && levels.Count == 0)
            throw new ArgumentException("Configurez de 1 à 10 niveaux / Configure between 1 and 10 levels.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var level in levels)
            if (level == null || string.IsNullOrWhiteSpace(level.Id) || level.Id.Length > 64 || !ids.Add(level.Id)
                || level.Name == null || level.Name.Length > 60 || level.Name.Any(char.IsControl)
                || level.Model == null || level.Model.Length > 300 || level.Model.Any(char.IsControl)
                || level.ProviderId < 0 || enabled && (level.ProviderId == 0 || string.IsNullOrWhiteSpace(level.Model))
                || !ThinkingLevels.Contains(level.ThinkingLevel) || level.InteractionMode is not ("chat" or "agent"))
                throw new ArgumentException("Niveau de modèle invalide / Invalid model level.");
    }

    public static bool Available(QuickModelLevel level, Provider? provider) => provider != null
        && provider.Id == level.ProviderId && ProviderModels.CanChat(provider) && ProviderModels.Visible(provider).Contains(level.Model);

    public static bool Matches(QuickModelLevel level, Provider? provider, AppState state, Chat? chat) => Available(level, provider)
        && level.Model == provider!.Model && level.InteractionMode == ConversationModes.Normalize(chat?.InteractionMode)
        && level.ThinkingLevel.Equals(ConversationModes.EffectiveThinking(chat, state.ThinkingLevel, state.ChatThinkingLevel), StringComparison.OrdinalIgnoreCase);

    // Validate the whole combination before changing any of the current settings.
    public static void Apply(QuickModelLevel level, Provider provider, AppState state, Chat chat)
    {
        Validate([level], true);
        if (!Available(level, provider)) throw new ArgumentException("Ce modèle n’est plus activé pour ce fournisseur / This model is no longer enabled for this provider.");
        ModelContexts.Select(provider, level.Model);
        state.ProviderId = provider.Id;
        chat.InteractionMode = level.InteractionMode;
        if (ConversationModes.IsChat(level.InteractionMode)) state.ChatThinkingLevel = level.ThinkingLevel;
        else state.ThinkingLevel = level.ThinkingLevel;
    }
}
