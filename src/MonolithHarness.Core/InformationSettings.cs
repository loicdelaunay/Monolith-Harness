namespace MonolithHarness.Core;

public sealed class InformationSettings
{
    public bool AutomaticContext { get; set; } = true;
    public bool DateTime { get; set; } = true;
    public bool TimeZone { get; set; } = true;
    public string Clock { get; set; } = "local";
    public bool System { get; set; } = true;
    public bool Locale { get; set; } = true;
    public bool Hardware { get; set; }
    public bool Runtimes { get; set; } = true;
    public bool DevelopmentTools { get; set; }
    public bool EnvironmentVariables { get; set; }
    public bool Paths { get; set; }
    public bool Identity { get; set; }
    public bool Conversation { get; set; } = true;
    public string AdditionalContext { get; set; } = "";
    public void Normalize()
    {
        Clock = Clock == "utc" ? "utc" : "local";
        AdditionalContext ??= "";
        if (AdditionalContext.Length > 4000) AdditionalContext = AdditionalContext[..4000];
    }
    public void Validate()
    {
        if (Clock is not ("local" or "utc") || AdditionalContext == null || AdditionalContext.Length > 4000)
            throw new ArgumentException("Réglages du skill Informations invalides / Invalid Information skill settings.");
    }
    public sealed record Option(string Id, string French, string English, string FrenchDescription, string EnglishDescription,
        Func<InformationSettings, bool> Read, Action<InformationSettings, bool> Write);
    public static IReadOnlyList<Option> Options { get; } =
    [
        new("automatic", "Contexte automatique", "Automatic context", "Ajouter les catégories choisies à chaque envoi. Sinon, le modèle peut les demander avec son outil Informations.", "Add selected categories to each message. Otherwise, the model can request them with its Information tool.", s => s.AutomaticContext, (s,v) => s.AutomaticContext=v),
        new("date_time", "Date et heure", "Date and time", "Date, jour de la semaine et heure actualisés à chaque demande.", "Date, weekday and time refreshed on each request.", s => s.DateTime, (s,v) => s.DateTime=v),
        new("time_zone", "Fuseau horaire", "Time zone", "Fuseau du PC ou UTC et décalage horaire actuel.", "Computer time zone or UTC and current offset.", s => s.TimeZone, (s,v) => s.TimeZone=v),
        new("system", "Système et shell", "System and shell", "Windows, Linux ou macOS, version du système, architectures et shell utilisé par l’application.", "Windows, Linux or macOS, OS version, architectures and the application shell.", s => s.System, (s,v) => s.System=v),
        new("locale", "Langue et formats régionaux", "Language and regional formats", "Langue de l’application, culture du PC et formats des dates et nombres.", "Application language, computer culture and date/number formats.", s => s.Locale, (s,v) => s.Locale=v),
        new("hardware", "Processeur et mémoire", "Processor and memory", "Processeurs logiques accessibles, budget mémoire .NET et mémoire du processus. Ce budget peut être inférieur à la RAM physique.", "Accessible logical processors, .NET memory budget and process memory. This budget can be lower than physical RAM.", s => s.Hardware, (s,v) => s.Hardware=v),
        new("runtimes", "Versions de l’application et des runtimes", "Application and runtime versions", "Version de Monolith Harness, de .NET et du Python embarqué, sans extraire ni lancer Python.", "Monolith Harness, .NET and bundled Python versions, without extracting or starting Python.", s => s.Runtimes, (s,v) => s.Runtimes=v),
        new("development_tools", "Outils de développement disponibles", "Available development tools", "Détecter Git, .NET, Node, Python, Docker et d’autres outils dans les dossiers locaux du PATH, sans exécuter de commande. Les versions ne sont pas vérifiées.", "Detect Git, .NET, Node, Python, Docker and other tools in local PATH directories without running commands. Versions are not checked.", s => s.DevelopmentTools, (s,v) => s.DevelopmentTools=v),
        new("environment", "Environnements de développement", "Development environments", "Variables sélectionnées : VIRTUAL_ENV, CONDA_DEFAULT_ENV, CONDA_PREFIX, DOTNET_ROOT, JAVA_HOME, NODE_ENV, SHELL, TERM, LANG, LC_ALL, MSYSTEM et WSL_DISTRO_NAME. Aucune autre variable n’est lue ou transmise par cette option.", "Selected variables: VIRTUAL_ENV, CONDA_DEFAULT_ENV, CONDA_PREFIX, DOTNET_ROOT, JAVA_HOME, NODE_ENV, SHELL, TERM, LANG, LC_ALL, MSYSTEM and WSL_DISTRO_NAME. This option does not read or transmit other variables.", s => s.EnvironmentVariables, (s,v) => s.EnvironmentVariables=v),
        new("paths", "Chemins locaux", "Local paths", "Dossier de l’application, profil utilisateur, dossier courant et chemins des sources déjà associées à la conversation. N’autorise aucun accès supplémentaire.", "Application directory, user profile, current directory and sources already associated with the conversation. Grants no additional access.", s => s.Paths, (s,v) => s.Paths=v),
        new("identity", "Identifiants du PC", "Computer identity", "Nom du PC et nom du compte utilisateur.", "Computer name and user account name.", s => s.Identity, (s,v) => s.Identity=v),
        new("conversation", "Contexte de la conversation", "Conversation context", "Projet, titre, modèle, fournisseur et mode de travail ; indique si les commandes sont isolées en sandbox.", "Project, title, model, provider and work mode; indicates whether commands are isolated in a sandbox.", s => s.Conversation, (s,v) => s.Conversation=v)
    ];
}
