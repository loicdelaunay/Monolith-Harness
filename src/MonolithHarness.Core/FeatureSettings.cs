using System.Text.Json;

namespace MonolithHarness.Core;

public sealed class FeatureSettings
{
    public int ImageGenerationProviderId { get; set; }
    public string ImageGenerationModel { get; set; } = "";
    public int ImageGenerationWidth { get; set; } = 1024;
    public int ImageGenerationHeight { get; set; } = 1024;
    public int ImageGenerationSteps { get; set; } = 28;
    // Older profiles skip onboarding; newly created profiles explicitly opt in.
    public bool WelcomeCompleted { get; set; } = true;
    // Cutoff captured by the usage migration; newer interrupted messages aren't legacy history.
    public int ConsumptionLegacyMessageId { get; set; }
    public int AiDetectorProviderId { get; set; }
    public string AiDetectorModel { get; set; } = "";
    public string AiDetectorEndpoint { get; set; } = SlopTotalClient.DefaultEndpoint;
    public string Theme { get; set; } = "fluent-dark";
    public List<AppearanceTheme> CustomThemes { get; set; } = [];
    public string ResponseStyle { get; set; } = "default";
    public string CliTheme { get; set; } = "shared";
    public string CliFont { get; set; } = "";
    public int CliFontSize { get; set; } = 14;
    public bool GuiCheckUpdates { get; set; } = true;
    public AutomaticUpdateMode? GuiUpdateMode { get; set; }
    public string GuiUpdateVersion { get; set; } = GitHubUpdates.Latest;
    [System.Text.Json.Serialization.JsonIgnore]
    public AutomaticUpdateMode EffectiveGuiUpdateMode => GuiUpdateMode is { } mode && Enum.IsDefined(mode)
        ? mode : GuiCheckUpdates ? AutomaticUpdateMode.Notify : AutomaticUpdateMode.Disabled;
    public bool CliCheckUpdates { get; set; } = true;
    public string ApplicationName { get; set; } = BrandingAssets.DefaultName;
    public bool ApplicationNameTwoLines { get; set; } = true;
    public string LogoPath { get; set; } = "";
    public bool ThemeLogosEnabled { get; set; }
    public string LightLogoPath { get; set; } = "";
    public string DarkLogoPath { get; set; } = "";
    public Dictionary<int, string> ChatGoals { get; set; } = [];
    public string LogoForTheme(bool dark) => ThemeLogosEnabled && !string.IsNullOrWhiteSpace(dark ? DarkLogoPath : LightLogoPath)
        ? dark ? DarkLogoPath : LightLogoPath : LogoPath;
    public string GoalInstructions(int chatId) => ChatGoals.TryGetValue(chatId, out var goal) && !string.IsNullOrWhiteSpace(goal)
        ? "\nUSER GOAL FOR THIS CONVERSATION: " + goal + "\nWork towards this goal, follow subsequent user instructions, and report when it is achieved.\n" : "";
    public int FontZoomPercent { get; set; } = 100;
    public string ChatMessageDensity { get; set; } = "normal";
    public string RenderingGpuPreference { get; set; } = "auto";
    public bool ShowAttentionSection { get; set; } = true;
    public bool RenderMermaid { get; set; } = true;
    public bool RenderMath { get; set; } = true;
    public bool AutoArchiveConversations { get; set; }
    public int AutoArchiveDays { get; set; } = 30;
    public bool AutoDeleteConversations { get; set; }
    public int AutoDeleteDays { get; set; } = 45;
    public bool AutoCleanArtifacts { get; set; } = true;
    public int ArtifactRetentionDays { get; set; } = 7;
    public string WebHttpResponseMode { get; set; } = "smart";
    public string InterfaceFont { get; set; } = "";
    public string UserMessageFont { get; set; } = "";
    public string AssistantMessageFont { get; set; } = "";
    public bool RetryEnabled { get; set; } = true;
    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public CompactionSettings Compaction { get; set; } = new();
    public bool NotificationBell { get; set; } = true;
    public bool NotificationOs { get; set; }
    public bool NotificationSound { get; set; } = true;
    public bool NotifyCompleted { get; set; } = true;
    public bool NotifyActionRequired { get; set; } = true;
    public string NotificationTone { get; set; } = "soft";
    public int NotificationVolume { get; set; } = 50;
    public List<AgentPreset> AgentPresets { get; set; } = [];
    public string AgentAutoBehavior { get; set; } = "proactive";
    public string AgentAutoInstructions { get; set; } = "";
    public int AgentMaxTotal { get; set; } = 64;
    public int AgentParallelism { get; set; } = 8;
    public int AgentMaxDepth { get; set; } = 3;
    public int AgentMaxSteps { get; set; } = 200;
    public bool ShowComposerSpeed { get; set; } = true;
    public bool ShowComposerContext { get; set; } = true;
    public bool QuickModelLevelsEnabled { get; set; }
    public List<QuickModelLevel> QuickModelLevels { get; set; } = [];
    public string QuickModelSelectionMode { get; set; } = "simple";
    public bool AutoFocusTool { get; set; }
    public string BrowserMode { get; set; } = "embedded";
    public string ChromePath { get; set; } = "";
    public string CommandGuardMode { get; set; } = "lancet";
    public int CommandGuardProviderId { get; set; }
    public string CommandGuardModel { get; set; } = "";
    public string RagMode { get; set; } = "local";
    public int RagProviderId { get; set; }
    public string RagModel { get; set; } = "text-embedding-3-small";
    public int RagMaxFiles { get; set; } = 500;
    public int RagTopK { get; set; } = 5;
    public int VisionProviderId { get; set; }
    public string VisionModel { get; set; } = "";
    public string VisionInstruction { get; set; } = "";
    public bool VisionComponents { get; set; }
    public string AutoNamingTiming { get; set; } = "first-response";
    public bool AutoNameConversations { get; set; }
    public int NamingProviderId { get; set; }
    public string NamingModel { get; set; } = "";
    public bool LogsEnabled { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
    public int LogRetentionDays { get; set; } = 7;
    public void FilterBrowser(System.Text.Json.Nodes.JsonArray tools)
    {
        if (BrowserMode == "embedded") return;
        for (int i=tools.Count-1; i>=0; i--)
        {
            var name=tools[i]?["function"]?["name"]?.GetValue<string>() ?? "";
            if(name.StartsWith("browser_") || name is "browse" or "read_page" or "inspect_dom" or "open_local_file") tools.RemoveAt(i);
        }
    }
    public static FeatureSettings Read(string json)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<FeatureSettings>(json) ?? new();
            // Carry the former combined switch into each missing independent preference.
            if (json.Contains("\"ShowComposerMetrics\"", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("ShowComposerMetrics", out var previous)
                    && previous.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    if (!document.RootElement.TryGetProperty("ShowComposerSpeed", out _)) settings.ShowComposerSpeed = previous.GetBoolean();
                    if (!document.RootElement.TryGetProperty("ShowComposerContext", out _)) settings.ShowComposerContext = previous.GetBoolean();
                }
            }
            settings.ApplicationName = BrandingAssets.DisplayName(settings.ApplicationName);
            settings.Compaction ??= new();
            settings.Compaction.Normalize();
            settings.WebHttpResponseMode = settings.WebHttpResponseMode?.Trim().ToLowerInvariant() switch {
                "legacy" or "full" => "legacy", _ => "smart" };
            if (settings.ArtifactRetentionDays is < 1 or > 3650) settings.ArtifactRetentionDays = 7;
            if (settings.GuiUpdateMode is { } mode && !Enum.IsDefined(mode)) settings.GuiUpdateMode = null;
            settings.GuiUpdateVersion = GitHubUpdates.NormalizeTargetVersion(settings.GuiUpdateVersion);
            settings.CommandGuardMode = settings.CommandGuardMode == "model" ? "model" : "lancet";
            settings.CommandGuardModel ??= "";
            QuickModelShortcuts.Normalize(settings);
            return settings;
        }
        catch { return new(); }
    }
    public string Json()
    {
        if (CommandGuardMode is not ("lancet" or "model") || CommandGuardProviderId < 0 || CommandGuardModel == null || CommandGuardModel.Length > 300)
            throw new ArgumentException("Réglages du validateur de commandes invalides / Invalid command validator settings.");
        WebHttpResponseMode = WebHttpTools.NormalizeResponseMode(WebHttpResponseMode);
        if (ArtifactRetentionDays is < 1 or > 3650)
            throw new ArgumentException("Réglages HTTP ou de nettoyage des artefacts invalides / Invalid HTTP or artifact cleanup settings.");
        if (GuiUpdateMode is { } mode && !Enum.IsDefined(mode)) throw new ArgumentException("Invalid automatic update mode.");
        // Keep older versions aware of the user's choice when reading the same portable profile.
        GuiCheckUpdates = EffectiveGuiUpdateMode != AutomaticUpdateMode.Disabled;
        GuiUpdateVersion = GitHubUpdates.NormalizeTargetVersion(GuiUpdateVersion);
        if (ImageGenerationProviderId < 0 || ImageGenerationModel == null || ImageGenerationModel.Length > 300 ||
            ImageGenerationWidth is < 256 or > 2048 || ImageGenerationHeight is < 256 or > 2048 || ImageGenerationWidth % 64 != 0 || ImageGenerationHeight % 64 != 0 || ImageGenerationSteps is < 1 or > 100)
            throw new ArgumentException("Réglages de génération d’image invalides : dimensions multiples de 64 entre 256 et 2048, de 1 à 100 étapes.");
        if (Compaction == null) throw new ArgumentException("Réglages de compactage requis / Compaction settings required.");
        Compaction.Validate();
        if (ChatGoals == null || ChatGoals.Any(x => x.Key <= 0 || x.Value == null || x.Value.Length > 4000))
            throw new ArgumentException("Objectif de conversation invalide / Invalid conversation goal.");
        if (CustomThemes == null || CustomThemes.Count > 50 || CustomThemes.Any(x => !AppearanceThemes.IsValidCustom(x)) || CustomThemes.Select(x => x.Id).Distinct().Count() != CustomThemes.Count)
            throw new ArgumentException("Thèmes personnalisés invalides / Invalid custom themes.");
        if (ChatMessageDensity is not ("compact" or "normal" or "spacious") || RenderingGpuPreference is not ("auto" or "high-performance" or "power-saving") || AutoNamingTiming is not ("first-message" or "first-response"))
            throw new ArgumentException("Densité, GPU ou moment du nommage invalide / Invalid density, GPU or naming timing.");
        if (AutoArchiveDays is < 1 or > 3650 || AutoDeleteDays is < 1 or > 3650 || AutoArchiveConversations && AutoDeleteConversations && AutoDeleteDays <= AutoArchiveDays)
            throw new ArgumentException("La suppression doit survenir après l’archivage, avec des délais de 1 à 3650 jours / Deletion must follow archiving, with delays from 1 to 3650 days.");
        QuickModelShortcuts.Validate(QuickModelLevels, QuickModelLevelsEnabled);
        if (QuickModelSelectionMode is not ("simple" or "advanced")) throw new ArgumentException("Invalid model selection mode.");
        ResponseStyle = ResponseStyles.Get(ResponseStyle).Id;
        if (new[] { InterfaceFont, UserMessageFont, AssistantMessageFont }.Any(font => font == null || font.Length > 100 || font.Any(char.IsControl)))
            throw new ArgumentException("Police d’interface invalide / Invalid interface font.");
        if (RetryCount is < 0 or > 10 || RetryDelaySeconds is < 1 or > 300 || NotificationVolume is < 0 or > 100 || !NotificationSounds.Contains(NotificationTone))
            throw new ArgumentException("Réglages de retry ou notification invalides / Invalid retry or notification settings.");
        if (AgentPresets == null || AgentPresets.Count > 30) throw new ArgumentException("30 presets maximum.");
        foreach (var preset in AgentPresets) preset.Validate();
        if (!AgentAutomation.Behaviors.Contains(AgentAutoBehavior) || AgentAutoInstructions == null || AgentAutoInstructions.Length > 4000 ||
            AgentMaxTotal is < 1 or > ConversationAgents.MaximumTeamSize || AgentParallelism is < 1 or > 64 || AgentMaxDepth is < 1 or > 8 || AgentMaxSteps is < 8 or > 2000)
            throw new ArgumentException("Réglages du mode Auto des agents invalides / Invalid agent Auto settings.");
        if (CliFont.Length > 100 || CliFont.Any(char.IsControl) || CliFontSize is < 8 or > 36) throw new ArgumentException("Police CLI invalide / Invalid CLI font.");
        if (VisionInstruction.Length > 8000 || NamingModel.Length > 200 || NamingProviderId < 0 || LogRetentionDays is < 1 or > 365 || !Enum.TryParse<AppLogLevel>(LogLevel, out _))
            throw new ArgumentException("Réglages vision, nommage ou logs invalides.");
        if (BrowserMode is not ("embedded" or "chrome" or "disabled") || RagMode is not ("local" or "api") || RagMaxFiles is < 1 or > 2000 || RagTopK is < 1 or > 20 || RagModel.Length > 200 || VisionProviderId < 0 || VisionModel.Length > 200)
            throw new ArgumentException("Réglages navigateur/RAG invalides.");
        return JsonSerializer.Serialize(this);
    }
    public McpServer ChromeServer(int chatId) => new()
    {
        Id = int.MaxValue, Name = "Chrome DevTools", Enabled = BrowserMode == "chrome", Command = OperatingSystem.IsWindows() ? "npx.cmd" : "npx",
        ArgumentsJson = JsonSerializer.Serialize(new[] { "-y", "chrome-devtools-mcp@latest", "--no-usage-statistics", "--user-data-dir=" + Path.Combine(PortableStorage.Folder("Chrome"), "chat-" + chatId) }
            .Concat(string.IsNullOrWhiteSpace(ChromePath) ? [] : new[] { "--executable-path=" + ChromePath }))
    };
}

public sealed class RagChunk
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Model { get; set; } = "";
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Text { get; set; } = "";
    public byte[] Vector { get; set; } = [];
}
