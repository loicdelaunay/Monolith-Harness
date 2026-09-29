using System.Globalization;

namespace OhMyHarness.App;

public static partial class UiText
{
    static readonly Dictionary<string, (string German, string Spanish)> Translations = ReadTranslations();
    static Dictionary<string, (string German, string Spanish)> ReadTranslations()
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var line in LocaleData.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|');
            if (parts.Length == 3) result[parts[0]] = (parts[1], parts[2]);
        }
        return result;
    }
    public static string Resolve(string french, string english, string? language = null)
    {
        var locale = language ?? Language;
        if (locale == "fr") return french;
        if (locale is not ("de" or "es")) return english;
        var key = english.Trim();
        if (Translations.TryGetValue(key, out var value))
        {
            var translated = locale == "de" ? value.German : value.Spanish;
            return english[..(english.Length - english.TrimStart().Length)] + translated + english[(english.TrimEnd().Length)..];
        }
        // Dynamic labels keep model names, paths, numbers and user text unchanged.
        foreach (var prefix in new[] { "Error: ", "Goal: ", "Setting saved · ", "Provider: ", "Tool: ", "Folder: ", "Duration: ", "Answered at " })
            if (english.StartsWith(prefix, StringComparison.Ordinal) && Translations.TryGetValue(prefix.Trim(), out var translated))
                return (locale == "de" ? translated.German : translated.Spanish) + " " + english[prefix.Length..];
        if (english.StartsWith("No response for ") && english.EndsWith(" seconds"))
        {
            var seconds = english[16..^8];
            return locale == "de" ? $"Seit {seconds} Sekunden keine Antwort" : $"Sin respuesta desde hace {seconds} segundos";
        }
        return english;
    }
    const string LocaleData = """
    General|Allgemein|General
    Appearance|Darstellung|Apariencia
    Providers|Anbieter|Proveedores
    Settings|Einstellungen|Ajustes
    ⚙  Settings|⚙  Einstellungen|⚙  Ajustes
    Skills|Skills|Skills
    Memory|Gedächtnis|Memoria
    Permissions|Berechtigungen|Permisos
    Browser|Browser|Navegador
    Notifications|Benachrichtigungen|Notificaciones
    Reset|Zurücksetzen|Restablecer
    About|Über die Anwendung|Acerca de
    Theme|Design|Tema
    Active theme|Aktives Design|Tema activo
    Customize|Anpassen|Personalizar
    Open editor|Editor öffnen|Abrir editor
    Fonts|Schriftarten|Fuentes
    Interface|Oberfläche|Interfaz
    Your messages and composer|Ihre Nachrichten und Eingabe|Tus mensajes y el editor
    Model replies|Modellantworten|Respuestas del modelo
    Default font|Standardschrift|Fuente predeterminada
    Font size|Schriftgröße|Tamaño de fuente
    Theme-specific logos|Logos je nach Design|Logos según el tema
    Application name and logo|Anwendungsname und Logo|Nombre y logo de la aplicación
    Application display name|Angezeigter Anwendungsname|Nombre visible de la aplicación
    Custom logo|Eigenes Logo|Logo personalizado
    Choose logo…|Logo auswählen…|Elegir logo…
    Default logo|Standardlogo|Logo original
    Reset name and logo|Name und Logo zurücksetzen|Restablecer nombre y logo
    Shared logo|Gemeinsames Logo|Logo común
    ☀ Light theme logo|☀ Logo für helle Designs|☀ Logo de temas claros
    ☾ Dark theme logo|☾ Logo für dunkle Designs|☾ Logo de temas oscuros
    Choose light and dark logos. An empty choice uses the shared logo.|Wählen Sie ein helles und ein dunkles Logo. Ohne Auswahl wird das gemeinsame Logo verwendet.|Elige un logo claro y uno oscuro. Una opción vacía usa el logo común.
    Application language|Sprache der Anwendung|Idioma de la aplicación
    Save|Speichern|Guardar
    Save preset|Preset speichern|Guardar preset
    Cancel|Abbrechen|Cancelar
    Close|Schließen|Cerrar
    Delete|Löschen|Eliminar
    Delete…|Löschen…|Eliminar…
    Edit|Bearbeiten|Editar
    Create|Erstellen|Crear
    Add|Hinzufügen|Añadir
    Remove|Entfernen|Quitar
    Refresh|Aktualisieren|Actualizar
    Copy|Kopieren|Copiar
    Copied!|Kopiert!|¡Copiado!
    Rename|Umbenennen|Renombrar
    Duplicate|Duplizieren|Duplicar
    copy|Kopie|copia
    Export|Exportieren|Exportar
    Import|Importieren|Importar
    Browse…|Durchsuchen…|Examinar…
    Enabled|Aktiviert|Activado
    Disabled|Deaktiviert|Desactivado
    Allowed|Erlaubt|Permitido
    Low|Niedrig|Bajo
    Medium|Mittel|Medio
    High|Hoch|Alto
    Ready|Bereit|Listo
    Confirmation|Bestätigung|Confirmación
    Error:|Fehler:|Error:
    Folder:|Ordner:|Carpeta:
    Provider:|Anbieter:|Proveedor:
    Tool:|Werkzeug:|Herramienta:
    Goal:|Ziel:|Objetivo:
    Setting saved ·|Einstellung gespeichert ·|Ajuste guardado ·
    Duration:|Dauer:|Duración:
    Answered at|Geantwortet um|Respuesta a las
    PROJECTS|PROJEKTE|PROYECTOS
    Projects|Projekte|Proyectos
    + Project|+ Projekt|+ Proyecto
    Manage|Verwalten|Gestionar
    Project name|Projektname|Nombre del proyecto
    Create a project|Projekt erstellen|Crear un proyecto
    Manage project|Projekt verwalten|Gestionar proyecto
    Project icon|Projektsymbol|Icono del proyecto
    Icon|Symbol|Icono
    Color|Farbe|Color
    Default folders · one per line|Standardordner · einer pro Zeile|Carpetas predeterminadas · una por línea
    Add folder|Ordner hinzufügen|Añadir carpeta
    Use project folders|Projektordner verwenden|Usar carpetas del proyecto
    Source folder|Quellordner|Carpeta de origen
    Source files|Quelldateien|Archivos de origen
    Project source folders|Quellordner des Projekts|Carpetas de origen del proyecto
    Link a source folder|Quellordner verknüpfen|Asociar carpeta de origen
    Add source folder|Quellordner hinzufügen|Añadir carpeta de origen
    Unlink source folder|Quellordner trennen|Desasociar carpeta de origen
    Unlink source folders|Quellordner trennen|Desasociar carpetas de origen
    No source folder|Kein Quellordner|Sin carpeta de origen
    Sources|Quellen|Fuentes
    Files|Dateien|Archivos
    Images|Bilder|Imágenes
    Attach images|Bilder anhängen|Adjuntar imágenes
    Attach files|Dateien anhängen|Adjuntar archivos
    Clear files|Anhänge entfernen|Quitar archivos
    + Image|+ Bild|+ Imagen
    Conversation|Unterhaltung|Conversación
    Conversations|Unterhaltungen|Conversaciones
    CONVERSATIONS|UNTERHALTUNGEN|CONVERSACIONES
    New conversation|Neue Unterhaltung|Nueva conversación
    +  New conversation|+  Neue Unterhaltung|+  Nueva conversación
    Create a conversation|Unterhaltung erstellen|Crear una conversación
    Search conversations|Unterhaltungen suchen|Buscar conversaciones
    Search conversations…|Unterhaltungen suchen…|Buscar conversaciones…
    Filter conversations|Unterhaltungen filtern|Filtrar conversaciones
    No conversations yet|Noch keine Unterhaltungen|Todavía no hay conversaciones
    No results|Keine Ergebnisse|Sin resultados
    Pin|Anheften|Fijar
    Unpin|Lösen|Desfijar
    Pinned conversations|Angeheftete Unterhaltungen|Conversaciones fijadas
    Favorites|Favoriten|Favoritos
    Add favorite|Zu Favoriten hinzufügen|Añadir a favoritos
    Remove favorite|Aus Favoriten entfernen|Quitar de favoritos
    Favorite|Favorit|Favorito
    Archive|Archiv|Archivo
    Restore|Wiederherstellen|Restaurar
    Move|Verschieben|Mover
    Message actions|Nachrichtenaktionen|Acciones del mensaje
    Ask a question, explore your sources…|Stellen Sie eine Frage, erkunden Sie Ihre Quellen…|Haz una pregunta, explora tus fuentes…
    Send  ↑|Senden  ↑|Enviar  ↑
    Stop|Stoppen|Detener
    YOU|SIE|TÚ
    ASSISTANT|ASSISTENT|ASISTENTE
    TOOL|WERKZEUG|HERRAMIENTA
    Model|Modell|Modelo
    MODEL|MODELL|MODELO
    Speed|Geschwindigkeit|Velocidad
    SPEED|GESCHWINDIGKEIT|VELOCIDAD
    CONTEXT|KONTEXT|CONTEXTO
    THINKING|DENKEN|RAZONAMIENTO
    No provider|Kein Anbieter|Sin proveedor
    Configure your provider|Anbieter konfigurieren|Configura tu proveedor
    Model, speed and context|Modell, Geschwindigkeit und Kontext|Modelo, velocidad y contexto
    Show model information|Modellinformationen anzeigen|Mostrar información del modelo
    Model thinking…|Das Modell denkt nach…|El modelo está razonando…
    The model is thinking…|Das Modell denkt nach…|El modelo está razonando…
    🧠 Model thinking|🧠 Das Modell denkt nach|🧠 El modelo está razonando
    OpenCode is thinking…|OpenCode denkt nach…|OpenCode está razonando…
    Model reasoning|Modellüberlegungen|Razonamiento del modelo
    Reasoning in progress…|Überlegungen laufen…|Razonamiento en curso…
    Reasoning complete|Überlegungen abgeschlossen|Razonamiento terminado
    Show reasoning details|Überlegungen anzeigen|Mostrar detalles del razonamiento
    Expand reasoning during generation.|Überlegungen während der Generierung aufklappen.|Expandir el razonamiento durante la generación.
    char.|Zeichen|car.
    Response complete · history saved.|Antwort abgeschlossen · Verlauf gespeichert.|Respuesta terminada · historial guardado.
    OpenCode response complete · history saved.|OpenCode-Antwort abgeschlossen · Verlauf gespeichert.|Respuesta de OpenCode terminada · historial guardado.
    Generation stopped. Partial response kept.|Generierung gestoppt. Teilantwort gespeichert.|Generación detenida. Respuesta parcial guardada.
    Find in this conversation|In dieser Unterhaltung suchen|Buscar en esta conversación
    Previous result|Vorheriges Ergebnis|Resultado anterior
    Next result|Nächstes Ergebnis|Resultado siguiente
    Close search|Suche schließen|Cerrar búsqueda
    Conversation goal|Ziel der Unterhaltung|Objetivo de la conversación
    Goal is limited to 4,000 characters.|Das Ziel darf höchstens 4.000 Zeichen enthalten.|El objetivo puede tener como máximo 4.000 caracteres.
    Conversation subagents and roles|Unteragenten und Rollen der Unterhaltung|Subagentes y roles de la conversación
    Plan or execution mode|Planungs- oder Ausführungsmodus|Modo plan o ejecución
    Set the conversation goal|Ziel der Unterhaltung festlegen|Definir el objetivo de la conversación
    Choose the model|Modell auswählen|Elegir modelo
    Reasoning effort|Denkniveau|Nivel de razonamiento
    Toggle automatic retry|Automatische Wiederholungen umschalten|Activar reintentos automáticos
    Change theme|Design ändern|Cambiar tema
    Open settings|Einstellungen öffnen|Abrir ajustes
    Stop this generation|Diese Generierung stoppen|Detener esta generación
    Show commands|Befehle anzeigen|Mostrar comandos
    Unknown command. Type /help or use Tab to autocomplete.|Unbekannter Befehl. Geben Sie /help ein oder verwenden Sie Tab zur Vervollständigung.|Comando desconocido. Escribe /help o usa Tab para autocompletar.
    Stop this generation before changing its mode.|Stoppen Sie diese Generierung, bevor Sie ihren Modus ändern.|Detén esta generación antes de cambiar su modo.
    Model unavailable from this provider.|Modell bei diesem Anbieter nicht verfügbar.|Modelo no disponible con este proveedor.
    Work mode|Arbeitsmodus|Modo de trabajo
    WORK MODE|ARBEITSMODUS|MODO DE TRABAJO
    Execution|Ausführung|Ejecución
    Mode|Modus|Modo
    Subagents|Unteragenten|Subagentes
    Subagent orchestration|Unteragentensteuerung|Orquestación de subagentes
    Forced|Erzwungen|Forzados
    Agent count|Anzahl der Agenten|Número de agentes
    Automatic count|Automatische Anzahl|Número automático
    Automatic roles|Automatische Rollen|Roles automáticos
    Agent roles and presets|Agentenrollen und Presets|Roles y presets de agentes
    Subagents for this conversation|Unteragenten für diese Unterhaltung|Subagentes de esta conversación
    Saved preset|Gespeichertes Preset|Preset guardado
    Preset name|Preset-Name|Nombre del preset
    Add role|Rolle hinzufügen|Añadir rol
    Role name|Rollenname|Nombre del rol
    Instructions|Anweisungen|Instrucciones
    Select an answer|Antwort auswählen|Selecciona una respuesta
    Select one or more answers|Eine oder mehrere Antworten auswählen|Selecciona una o varias respuestas
    Submit|Absenden|Enviar
    Tasks|Aufgaben|Tareas
    Scheduled tasks|Geplante Aufgaben|Tareas programadas
    Show task list|Aufgabenliste anzeigen|Mostrar lista de tareas
    Tools|Werkzeuge|Herramientas
    TOOLS AND CONTENT|WERKZEUGE UND INHALTE|HERRAMIENTAS Y CONTENIDO
    Templates|Vorlagen|Plantillas
    New template|Neue Vorlage|Nueva plantilla
    Template name|Vorlagenname|Nombre de la plantilla
    Template content|Vorlageninhalt|Contenido de la plantilla
    Select a template|Vorlage auswählen|Selecciona una plantilla
    Edit templates…|Vorlagen bearbeiten…|Editar plantillas…
    Response style|Antwortstil|Estilo de respuesta
    Automatic retry|Automatisch erneut versuchen|Reintentar automáticamente
    Number of retries|Anzahl der Wiederholungen|Número de reintentos
    Delay between retries (seconds)|Wartezeit zwischen Wiederholungen (Sekunden)|Tiempo entre reintentos (segundos)
    Retry network, rate-limit and interrupted-stream errors. Invalid API keys and completed tools are not retried.|Wiederholt Anfragen bei Netzwerkfehlern, Überlastung und unterbrochenen Streams. Ungültige API-Schlüssel und bereits ausgeführte Werkzeuge werden nicht wiederholt.|Reintenta errores de red, saturación y flujos interrumpidos. No repite claves API inválidas ni herramientas ya ejecutadas.
    Automatically continue after 12 steps|Nach 12 Schritten automatisch fortfahren|Continuar automáticamente tras 12 pasos
    Auto-continue…|Automatisch fortfahren…|Continuación automática…
    Conversation completed|Unterhaltung abgeschlossen|Conversación terminada
    Action required|Aktion erforderlich|Acción requerida
    Agent questions and approval requests.|Fragen und Berechtigungsanfragen des Agenten.|Preguntas y solicitudes de permiso del agente.
    Notification bell|Benachrichtigungsglocke|Campana de notificaciones
    Show alerts in the app and beside their conversations.|Hinweise in der Anwendung und bei den betreffenden Unterhaltungen anzeigen.|Mostrar alertas en la aplicación y junto a sus conversaciones.
    Play a sound|Ton abspielen|Reproducir un sonido
    System notifications|Systembenachrichtigungen|Notificaciones del sistema
    Show a Windows, macOS or Linux notification, including when the app is in the background. Do Not Disturb may hide it.|Systembenachrichtigungen unter Windows, macOS oder Linux anzeigen, auch im Hintergrund. „Nicht stören“ kann sie ausblenden.|Mostrar notificaciones de Windows, macOS o Linux, incluso en segundo plano. No molestar puede ocultarlas.
    Sound|Ton|Sonido
    Volume|Lautstärke|Volumen
    Preview sound|Ton anhören|Escuchar sonido
    Mark all as read|Alle als gelesen markieren|Marcar todo como leído
    No notifications|Keine Benachrichtigungen|Sin notificaciones
    Optimize|Optimieren|Optimizar
    Optimize database|Datenbank optimieren|Optimizar la base de datos
    Reset data|Daten zurücksetzen|Restablecer datos
    Reset project data|Projektdaten zurücksetzen|Restablecer datos de proyectos
    Type RESET to confirm|Zur Bestätigung RESET eingeben|Escribe RESET para confirmar
    Reset data?|Daten zurücksetzen?|¿Restablecer datos?
    Operation in progress…|Vorgang läuft…|Operación en curso…
    Data reset.|Daten zurückgesetzt.|Datos restablecidos.
    Database optimized.|Datenbank optimiert.|Base de datos optimizada.
    Finish running conversations before this operation.|Beenden Sie laufende Unterhaltungen vor diesem Vorgang.|Termina las conversaciones en curso antes de esta operación.
    A conversation just started. Try again when it finishes.|Eine Unterhaltung wurde gerade gestartet. Versuchen Sie es nach ihrem Ende erneut.|Acaba de iniciarse una conversación. Reintenta cuando termine.
    This deletion is permanent. All project data and memories will be erased.|Diese Löschung ist endgültig. Alle Projektdaten und Erinnerungen werden gelöscht.|La eliminación es definitiva. Se borrarán todos los datos de proyectos y las memorias.
    Delete projects, chats, messages, saved images, memories, tasks, templates and approvals. Settings, providers and source files on disk are kept.|Löscht Projekte, Unterhaltungen, Nachrichten, gespeicherte Bilder, Erinnerungen, Aufgaben, Vorlagen und Berechtigungen. Einstellungen, Anbieter und Quelldateien bleiben erhalten.|Elimina proyectos, conversaciones, mensajes, imágenes guardadas, memorias, tareas, plantillas y permisos. Conserva ajustes, proveedores y archivos de origen.
    Compact SQLite and optimize its indexes. Close other application instances before starting.|Komprimiert SQLite und optimiert die Indizes. Schließen Sie vorher andere Instanzen der Anwendung.|Compacta SQLite y optimiza sus índices. Cierra otras instancias de la aplicación antes de empezar.
    Git reading|Git-Lesezugriff|Lectura de Git
    Git writing|Git-Schreibzugriff|Escritura de Git
    Status, diffs, history and branches.|Status, Unterschiede, Verlauf und Branches.|Estado, diferencias, historial y ramas.
    Staging, commits, branches and network operations, subject to approvals.|Staging, Commits, Branches und Netzwerkaktionen gemäß den Berechtigungen.|Índice, commits, ramas y operaciones de red según los permisos.
    Custom skills|Eigene Skills|Skills personalizados
    Configured providers|Konfigurierte Anbieter|Proveedores configurados
    Provider name|Anbietername|Nombre del proveedor
    Add provider|Anbieter hinzufügen|Añadir proveedor
    Delete provider|Anbieter löschen|Eliminar proveedor
    New provider|Neuer Anbieter|Nuevo proveedor
    At least one provider is required.|Mindestens ein Anbieter ist erforderlich.|Se necesita al menos un proveedor.
    Provider name is required.|Der Anbietername ist erforderlich.|El nombre del proveedor es obligatorio.
    Each provider must have a unique name.|Jeder Anbieter braucht einen eindeutigen Namen.|Cada proveedor necesita un nombre único.
    API base URL|API-Basis-URL|URL base de la API
    API key (leave blank to keep the saved key)|API-Schlüssel (leer lassen, um den gespeicherten Schlüssel zu behalten)|Clave API (vacío: conservar la clave guardada)
    Key already saved|Schlüssel bereits gespeichert|Clave ya guardada
    Your API key|Ihr API-Schlüssel|Tu clave API
    Model ID|Modell-ID|Identificador del modelo
    Model context window (tokens)|Kontextfenster des Modells (Tokens)|Ventana de contexto del modelo (tokens)
    This model accepts images|Dieses Modell akzeptiert Bilder|Este modelo acepta imágenes
    Remove saved key|Gespeicherten Schlüssel entfernen|Eliminar clave guardada
    Load models / test key|Modelle laden / Schlüssel prüfen|Cargar modelos / probar clave
    Load models|Modelle laden|Cargar modelos
    Select a model|Modell auswählen|Selecciona un modelo
    Vision provider|Bildanalyse-Anbieter|Proveedor de visión
    Vision model|Bildanalyse-Modell|Modelo de visión
    Custom instructions|Eigene Anweisungen|Instrucciones personalizadas
    Naming provider|Anbieter für Titel|Proveedor de títulos
    Naming model|Modell für Titel|Modelo de títulos
    Automatically name new conversations|Neue Unterhaltungen automatisch benennen|Nombrar automáticamente las nuevas conversaciones
    Naming…|Benennung…|Asignando nombre…
    Permission request behavior|Verhalten bei Berechtigungsanfragen|Comportamiento de solicitudes de permiso
    Deny all|Alles verweigern|Rechazar todo
    Ask (default)|Nachfragen (Standard)|Preguntar (predeterminado)
    Automatic approval|Automatisch erlauben|Aceptar automáticamente
    Allow once|Einmal erlauben|Permitir una vez
    Always allow|Immer erlauben|Permitir siempre
    Deny|Verweigern|Rechazar
    Additional permission|Zusätzliche Berechtigung|Permiso adicional
    Access denied by the user.|Zugriff vom Benutzer verweigert.|Acceso rechazado por el usuario.
    No permanent permissions.|Keine dauerhaften Berechtigungen.|No hay permisos permanentes.
    Settings saved.|Einstellungen gespeichert.|Ajustes guardados.
    GitHub updates|GitHub-Updates|Actualizaciones de GitHub
    Check automatically at startup|Beim Start automatisch prüfen|Comprobar automáticamente al iniciar
    Check|Prüfen|Comprobar
    Check for a newer GUI release.|Nach einer neueren GUI-Version suchen.|Buscar una versión GUI más reciente.
    Download and restart|Herunterladen und neu starten|Descargar y reiniciar
    Checking GitHub…|GitHub wird geprüft…|Comprobando GitHub…
    Enable logs|Protokolle aktivieren|Activar registros
    Log level|Protokollstufe|Nivel de registro
    Log retention (days)|Protokollaufbewahrung (Tage)|Retención de registros (días)
    Memory · Database viewer|Gedächtnis · Datenbankansicht|Memoria · Vista de la base de datos
    View memory|Gedächtnis anzeigen|Ver memoria
    Sort by|Sortieren nach|Ordenar por
    Descending|Absteigend|Descendente
    Clear filters|Filter löschen|Borrar filtros
    Filter…|Filtern…|Filtrar…
    Copy row|Zeile kopieren|Copiar fila
    Previous|Zurück|Anterior
    Next|Weiter|Siguiente
    Show more|Mehr anzeigen|Mostrar más
    Create memory|Erinnerung erstellen|Crear memoria
    Save memory|Erinnerung speichern|Guardar memoria
    Edit memories…|Erinnerungen bearbeiten…|Editar memorias…
    Search|Suchen|Buscar
    Translation|Übersetzung|Traducción
    Translator|Übersetzer|Traductor
    Translate|Übersetzen|Traducir
    Source language|Ausgangssprache|Idioma de origen
    Target language|Zielsprache|Idioma de destino
    Detect language|Sprache erkennen|Detectar idioma
    Swap languages|Sprachen tauschen|Intercambiar idiomas
    French|Französisch|Francés
    English|Englisch|Inglés
    German|Deutsch|Alemán
    Spanish|Spanisch|Español
    Japanese|Japanisch|Japonés
    Spell checker|Rechtschreibprüfung|Corrector ortográfico
    Review, correct and rewrite your texts.|Texte prüfen, korrigieren und umformulieren.|Revisa, corrige y reformula tus textos.
    Text language|Sprache des Textes|Idioma del texto
    Corrections|Korrekturen|Correcciones
    Rewriting|Umformulierung|Reformulación
    Check text|Text prüfen|Verificar texto
    Apply all|Alle anwenden|Aplicar todo
    Ignore|Ignorieren|Ignorar
    Benchmark|Benchmark|Benchmark
    Model benchmark|Modell-Benchmark|Benchmark de modelos
    Difficulty|Schwierigkeitsgrad|Dificultad
    Easy|Leicht|Fácil
    Hard|Schwer|Difícil
    Start benchmark|Benchmark starten|Iniciar benchmark
    Preview|Vorschau|Vista previa
    Average speed|Durchschnittsgeschwindigkeit|Velocidad media
    Score|Bewertung|Nota
    Running…|Läuft…|En curso…
    Finished|Abgeschlossen|Terminado
    Open a local file|Lokale Datei öffnen|Abrir archivo local
    Run|Ausführen|Ejecutar
    Refresh changes|Änderungen aktualisieren|Actualizar cambios
    Open in Web|Im Web öffnen|Abrir en Web
    Run a terminal command|Terminalbefehl ausführen|Ejecutar comando de terminal
    Running…|Wird ausgeführt…|En curso…
    Terminal commands in:|Terminalbefehle in:|Comandos de terminal en:
    Read and send:|Lesen und senden:|Leer y enviar:
    Write to:|Schreiben in:|Escribir en:
    Local previews in:|Lokale Vorschauen in:|Vistas previas locales en:
    Website:|Website:|Sitio web:
    Browser page:|Browserseite:|Página del navegador:
    Stop the running conversations before deleting their project.|Stoppen Sie laufende Unterhaltungen, bevor Sie deren Projekt löschen.|Detén las conversaciones en curso antes de eliminar su proyecto.
    Stop this conversation before deleting it.|Stoppen Sie diese Unterhaltung, bevor Sie sie löschen.|Detén esta conversación antes de eliminarla.
    This provider is used by a running conversation.|Dieser Anbieter wird von einer laufenden Unterhaltung verwendet.|Este proveedor se usa en una conversación en curso.
    Delete the project and all its conversations? Source files will remain on disk.|Projekt und alle Unterhaltungen löschen? Quelldateien bleiben auf dem Datenträger.|¿Eliminar el proyecto y todas sus conversaciones? Los archivos de origen se conservarán.
    Delete this conversation and its images?|Diese Unterhaltung und ihre Bilder löschen?|¿Eliminar esta conversación y sus imágenes?
    Invalid HTTP(S) URL.|Ungültige HTTP(S)-URL.|URL HTTP(S) inválida.
    Tool not allowed.|Werkzeug nicht erlaubt.|Herramienta no permitida.
    Connection test in progress…|Verbindung wird geprüft…|Comprobando conexión…
    Testing connection…|Verbindung wird geprüft…|Comprobando conexión…
    Importing models…|Modelle werden importiert…|Importando modelos…
    Import models|Modelle importieren|Importar modelos
    No templates. Click New template to create one.|Keine Vorlagen. Klicken Sie auf Neue Vorlage, um eine zu erstellen.|No hay plantillas. Haz clic en Nueva plantilla para crear una.
    Template name and content are required.|Name und Inhalt der Vorlage sind erforderlich.|Se necesita el nombre y el contenido de la plantilla.
    Enter: send · Shift+Enter: new line|Enter: senden · Umschalt+Enter: neue Zeile|Intro: enviar · Mayús+Intro: nueva línea
    Conversation copied as Markdown.|Unterhaltung als Markdown kopiert.|Conversación copiada en Markdown.
    Export cancelled.|Export abgebrochen.|Exportación cancelada.
    Clear project rules|Projektregeln entfernen|Quitar reglas del proyecto
    Read permission.json|permission.json lesen|Leer permission.json
    No rules found.|Keine Regeln gefunden.|No se encontraron reglas.
    Replace|Ersetzen|Reemplazar
    Replace draft?|Entwurf ersetzen?|¿Reemplazar borrador?
    The template will replace the current text. Attachments are kept.|Die Vorlage ersetzt den aktuellen Text. Anhänge bleiben erhalten.|La plantilla reemplazará el texto actual. Se conservarán los adjuntos.
    Expand / restore panel|Bereich vergrößern / wiederherstellen|Ampliar / restaurar panel
    Text preview unavailable. Use Open in Web.|Textvorschau nicht verfügbar. Verwenden Sie Im Web öffnen.|Vista previa de texto no disponible. Usa Abrir en Web.
    A command is required.|Ein Befehl ist erforderlich.|Se necesita un comando.
    A command is already running.|Ein Befehl wird bereits ausgeführt.|Ya hay un comando en ejecución.
    Link a source folder using the + button.|Verknüpfen Sie einen Quellordner über die Schaltfläche +.|Asocia una carpeta de origen con el botón +.
    No Git repository: .git is missing from the project folder.|Kein Git-Repository: .git fehlt im Projektordner.|No hay repositorio Git: falta .git en la carpeta del proyecto.
    Command stopped or 60 second timeout reached.|Befehl gestoppt oder Zeitlimit von 60 Sekunden erreicht.|Comando detenido o límite de 60 segundos alcanzado.
    Each command starts an independent PowerShell session (60 s max).|Jeder Befehl startet eine eigene PowerShell-Sitzung (max. 60 s).|Cada comando inicia una sesión PowerShell independiente (máximo 60 s).
    This command can modify files and access the network with your Windows account privileges.|Dieser Befehl kann mit den Rechten Ihres Windows-Kontos Dateien ändern und auf das Netzwerk zugreifen.|Este comando puede modificar archivos y acceder a la red con los permisos de tu cuenta de Windows.
    Write outside the project scope|Außerhalb des Projektbereichs schreiben|Escribir fuera del ámbito del proyecto
    Create or replace the entire content of this file:|Gesamten Dateiinhalt erstellen oder ersetzen:|Crear o reemplazar todo el contenido del archivo:
    Replace the following text:|Folgenden Text ersetzen:|Reemplazar el siguiente texto:
    Local page in:|Lokale Seite in:|Página local en:
    DOM action and target are required.|DOM-Aktion und Ziel sind erforderlich.|Se necesitan la acción DOM y el destino.
    Requested DOM action:|Angeforderte DOM-Aktion:|Acción DOM solicitada:
    Target:|Ziel:|Destino:
    Text:|Text:|Texto:
    Interact with the web page|Mit der Webseite interagieren|Interactuar con la página web
    DOM interactions ·|DOM-Interaktionen ·|Interacciones DOM ·
    Invalid mouse action: move, click, or scroll.|Ungültige Mausaktion: move, click oder scroll.|Acción de ratón inválida: move, click o scroll.
    Coordinates are outside the browser viewport.|Koordinaten liegen außerhalb des Browserbereichs.|Las coordenadas están fuera del navegador.
    Coordinates are outside the Windows desktop.|Koordinaten liegen außerhalb des Windows-Desktops.|Las coordenadas están fuera del escritorio de Windows.
    Requested mouse action:|Angeforderte Mausaktion:|Acción de ratón solicitada:
    Button:|Taste:|Botón:
    Control the mouse in the browser|Maus im Browser steuern|Controlar ratón en el navegador
    Control the Windows mouse|Windows-Maus steuern|Controlar ratón de Windows
    Browser mouse ·|Browser-Maus ·|Ratón del navegador ·
    Mouse on the Windows desktop|Maus auf dem Windows-Desktop|Ratón en el escritorio de Windows
    Unable to move the pointer.|Zeiger konnte nicht bewegt werden.|No se pudo mover el puntero.
    Invalid keyboard action: type or press.|Ungültige Tastaturaktion: type oder press.|Acción de teclado inválida: type o press.
    Text to type is required.|Einzugebender Text ist erforderlich.|Se necesita el texto que escribir.
    A keyboard shortcut is required.|Eine Tastenkombination ist erforderlich.|Se necesita un atajo de teclado.
    Invalid shortcut format. Use Ctrl+S, Alt+Tab, or Enter, for example.|Ungültige Tastenkombination. Verwenden Sie z. B. Ctrl+S, Alt+Tab oder Enter.|Formato de atajo inválido. Usa por ejemplo Ctrl+S, Alt+Tab o Enter.
    Text is too long (10,000 characters maximum).|Text zu lang (höchstens 10.000 Zeichen).|Texto demasiado largo (máximo 10.000 caracteres).
    Requested keyboard input:|Angeforderte Tastatureingabe:|Entrada de teclado solicitada:
    Requested keyboard shortcut:|Angeforderte Tastenkombination:|Atajo de teclado solicitado:
    Control the keyboard in the browser|Tastatur im Browser steuern|Controlar teclado en el navegador
    Control the Windows keyboard|Windows-Tastatur steuern|Controlar teclado de Windows
    Browser keyboard ·|Browser-Tastatur ·|Teclado del navegador ·
    Keyboard on the Windows desktop|Tastatur auf dem Windows-Desktop|Teclado en el escritorio de Windows
    The active model does not accept images.|Das aktive Modell akzeptiert keine Bilder.|El modelo activo no acepta imágenes.
    Capture and send the page|Seite aufnehmen und senden|Capturar y enviar página
    Capture and send the Windows desktop|Windows-Desktop aufnehmen und senden|Capturar y enviar escritorio de Windows
    An image of the visible browser viewport will be sent to the AI provider.|Ein Bild des sichtbaren Browserbereichs wird an den KI-Anbieter gesendet.|Se enviará al proveedor de IA una imagen del área visible del navegador.
    An image of all visible displays will be sent to the AI provider.|Ein Bild aller sichtbaren Bildschirme wird an den KI-Anbieter gesendet.|Se enviará al proveedor de IA una imagen de todas las pantallas visibles.
    Browser screenshots ·|Browser-Aufnahmen ·|Capturas del navegador ·
    Capture of all Windows displays|Aufnahme aller Windows-Bildschirme|Captura de todas las pantallas de Windows
    Screenshot too large (8 MB maximum).|Bildschirmaufnahme zu groß (maximal 8 MB).|Captura demasiado grande (máximo 8 MB).
    Screenshot too large (16 MB maximum).|Bildschirmaufnahme zu groß (maximal 16 MB).|Captura demasiado grande (máximo 16 MB).
    Screenshot captured and attached to the next model request.|Aufnahme erstellt und an die nächste Modellanfrage angehängt.|Captura realizada y adjuntada a la siguiente solicitud al modelo.
    Browser DOM access is not allowed.|Zugriff auf das Browser-DOM ist nicht erlaubt.|No se permite acceder al DOM del navegador.
    The mouse control skill is not allowed.|Der Skill zur Maussteuerung ist nicht erlaubt.|No se permite el skill de control del ratón.
    The keyboard control skill is not allowed.|Der Skill zur Tastatursteuerung ist nicht erlaubt.|No se permite el skill de control del teclado.
    The screenshot skill is not allowed.|Der Skill für Bildschirmaufnahmen ist nicht erlaubt.|No se permite el skill de capturas de pantalla.
    A path is required.|Ein Pfad ist erforderlich.|Se necesita una ruta.
    Read a file outside the project scope|Datei außerhalb des Projektbereichs lesen|Leer un archivo fuera del ámbito del proyecto
    The content will be sent to the AI provider for this request.|Der Inhalt wird für diese Anfrage an den KI-Anbieter gesendet.|Se enviará el contenido al proveedor de IA para esta solicitud.
    Open a local file in the browser|Lokale Datei im Browser öffnen|Abrir archivo local en el navegador
    Authorized resource folder:|Autorisierter Ressourcenordner:|Carpeta de recursos autorizada:
    Configure MCP…|MCP konfigurieren…|Configurar MCP…
    MCP server|MCP-Server|Servidor MCP
    Command / executable|Befehl / Programm|Comando / ejecutable
    Arguments (JSON array)|Argumente (JSON-Array)|Argumentos (matriz JSON)
    Working directory (optional)|Arbeitsverzeichnis (optional)|Directorio de trabajo (opcional)
    Secrets JSON (blank: keep existing)|Secrets-JSON (leer: behalten)|Secretos JSON (vacío: conservar)
    Clear saved secrets|Gespeicherte Geheimnisse löschen|Borrar secretos guardados
    Add MCP server|MCP-Server hinzufügen|Añadir servidor MCP
    Test MCP connection|MCP-Verbindung prüfen|Probar conexión MCP
    MCP connection successful:|MCP-Verbindung erfolgreich:|Conexión MCP correcta:
    MCP connection failed. Check the configuration.|MCP-Verbindung fehlgeschlagen. Prüfen Sie die Konfiguration.|Falló la conexión MCP. Comprueba la configuración.
    Invalid MCP JSON.|Ungültiges MCP-JSON.|JSON MCP inválido.
    Template to edit|Zu bearbeitende Vorlage|Plantilla para editar
    Describe your request here.|Beschreiben Sie Ihre Anfrage hier.|Describe tu solicitud aquí.
    Reset Web app|Web-App zurücksetzen|Restablecer aplicación web
    Templates prefill the message without sending it. Changes are applied with Save.|Vorlagen füllen die Nachricht aus, ohne sie zu senden. Änderungen werden mit Speichern übernommen.|Las plantillas rellenan el mensaje sin enviarlo. Guarda para aplicar los cambios.
    Speed: —   •   Context: —|Geschwindigkeit: —   •   Kontext: —|Velocidad: —   •   Contexto: —
    AI browser access|KI-Browserzugriff|Acceso de IA al navegador
    AI DOM access and interaction|KI-DOM-Zugriff und Interaktion|Acceso e interacción DOM de IA
    Link source folder|Quellordner verknüpfen|Asociar carpeta de origen
    No source folder · Link a folder to explore it with AI|Kein Quellordner · Verknüpfen Sie einen Ordner, um ihn mit KI zu erkunden|Sin carpeta de origen · Asocia una carpeta para explorarla con IA
    Folder linked to the project. AI can list and read its text files on request.|Ordner mit dem Projekt verknüpft. Die KI kann seine Textdateien auf Anfrage auflisten und lesen.|Carpeta asociada al proyecto. La IA puede listar y leer sus archivos de texto cuando lo solicites.
    Chat with your model, attach an image or explore a source folder. Each project keeps its conversations and context.|Chatten Sie mit Ihrem Modell, hängen Sie ein Bild an oder erkunden Sie einen Quellordner. Jedes Projekt behält seine Unterhaltungen und seinen Kontext.|Conversa con tu modelo, adjunta una imagen o explora una carpeta. Cada proyecto conserva sus conversaciones y contexto.
    Up to four images per message.|Bis zu vier Bilder pro Nachricht.|Hasta cuatro imágenes por mensaje.
    Image too large (8 MB maximum).|Bild zu groß (maximal 8 MB).|Imagen demasiado grande (máximo 8 MB).
    Each provider keeps its own key, URL, model, and context limit.|Jeder Anbieter behält seinen eigenen Schlüssel, seine URL, sein Modell und sein Kontextlimit.|Cada proveedor conserva su clave, URL, modelo y límite de contexto.
    Create as many connections as needed. Each instance keeps its own key, URL, model, and context limit.|Erstellen Sie beliebig viele Verbindungen. Jede Instanz behält ihren Schlüssel, ihre URL, ihr Modell und ihr Kontextlimit.|Crea las conexiones que necesites. Cada instancia conserva su clave, URL, modelo y límite de contexto.
    + OpenAI compatible|+ OpenAI-kompatibel|+ Compatible con OpenAI
    OpenAI compatible|OpenAI-kompatibel|Compatible con OpenAI
    OpenCode username|OpenCode-Benutzername|Usuario de OpenCode
    Path to opencode.exe (optional)|Pfad zu opencode.exe (optional)|Ruta de opencode.exe (opcional)
    Start the OpenCode server automatically|OpenCode-Server automatisch starten|Iniciar automáticamente servidor OpenCode
    Enable OpenCode agent tools with permission prompts|OpenCode-Agentenwerkzeuge mit Berechtigungsabfragen aktivieren|Activar herramientas de agentes OpenCode con solicitudes de permiso
    BETA Bypass free limitation|BETA Kostenloses Limit umgehen|BETA Evitar límite gratuito
    Allows using free models on another harness|Ermöglicht kostenlose Modelle in einer anderen Anwendung|Permite usar modelos gratuitos en otra aplicación
    Server password (empty: none)|Serverpasswort (leer: keines)|Contraseña del servidor (vacío: ninguna)
    Load models / test OpenCode|Modelle laden / OpenCode prüfen|Cargar modelos / probar OpenCode
    Automatic OpenCode startup is limited to a local address.|Automatischer OpenCode-Start ist auf lokale Adressen beschränkt.|El inicio automático de OpenCode se limita a una dirección local.
    OpenCode executable not found.|OpenCode-Programm nicht gefunden.|No se encontró el ejecutable de OpenCode.
    Unable to start OpenCode.|OpenCode konnte nicht gestartet werden.|No se pudo iniciar OpenCode.
    Unable to start OpenCode. Check the executable path.|OpenCode konnte nicht gestartet werden. Prüfen Sie den Programmpfad.|No se pudo iniciar OpenCode. Comprueba la ruta del ejecutable.
    OpenCode stopped during startup.|OpenCode wurde während des Starts beendet.|OpenCode se detuvo al iniciar.
    The OpenCode server did not respond within the expected time.|Der OpenCode-Server antwortete nicht innerhalb des Zeitlimits.|El servidor OpenCode no respondió a tiempo.
    OpenCode tool permission|OpenCode-Werkzeugberechtigung|Permiso de herramienta OpenCode
    The model and limit must match your provider. The list is loaded from your API.|Modell und Limit müssen Ihrem Anbieter entsprechen. Die Liste wird von Ihrer API geladen.|El modelo y su límite deben corresponder al proveedor. La lista se carga desde su API.
    Skills add specialized instructions. Source and web access can be disabled independently.|Skills ergänzen spezielle Anweisungen. Quellen- und Webzugriff können getrennt deaktiviert werden.|Los skills añaden instrucciones especializadas. El acceso a fuentes y web se puede desactivar por separado.
    Permanent permissions are applied automatically within their scope. Uncheck an item and save to revoke it.|Dauerhafte Berechtigungen werden für ihren Bereich automatisch angewendet. Zum Widerruf abwählen und speichern.|Los permisos permanentes se aplican automáticamente a su ámbito. Desmarca una opción y guarda para revocarla.
    Model and context limit are required.|Modell und Kontextlimit sind erforderlich.|Se necesita el modelo y su límite de contexto.
    PROVIDER & MODEL|ANBIETER UND MODELL|PROVEEDOR Y MODELO
    Provider|Anbieter|Proveedor
    Output:|Ausgabe:|Salida:
    Reload models from API|Modelle von der API neu laden|Recargar modelos desde la API
    waiting|wartend|esperando
    estimating|geschätzt|estimando
    Enter your API key in Settings to load the model list.|Geben Sie Ihren API-Schlüssel in den Einstellungen ein, um Modelle zu laden.|Introduce tu clave API en Ajustes para cargar los modelos.
    Loading models from API…|Modelle werden von der API geladen…|Cargando modelos desde la API…
    Model thinking / reasoning effort level|Denkniveau des Modells|Nivel de razonamiento del modelo
    chars|Zeichen|car.
    Tool|Werkzeug|Herramienta
    Success|Erfolg|Éxito
    Error|Fehler|Error
    Details|Details|Detalles
    Collapse|Einklappen|Contraer
    Parameters sent:|Gesendete Parameter:|Parámetros enviados:
    Result received:|Erhaltenes Ergebnis:|Resultado obtenido:
    Detach source|Quelle trennen|Desasociar fuente
    Remove image|Bild entfernen|Quitar imagen
    Current speed (tok/s):|Aktuelle Geschwindigkeit (tok/s):|Velocidad actual (tok/s):
    Last response (tok/s):|Letzte Antwort (tok/s):|Última respuesta (tok/s):
    Minimum|Minimum|Mínimo
    Maximum|Maximum|Máximo
    Average|Durchschnitt|Promedio
    Discussion|Unterhaltung|Conversación
    responses|Antworten|respuestas
    response|Antwort|respuesta
    Test connection|Verbindung prüfen|Probar conexión
    Import OpenCode models|OpenCode-Modelle importieren|Importar modelos OpenCode
    Executable files (*.exe)|Programme (*.exe)|Archivos ejecutables (*.exe)
    Hover to preview · Click to enlarge|Vorschau beim Darüberfahren · Klicken zum Vergrößern|Pasa el ratón para ver · Haz clic para ampliar
    Click thumbnail to open full size|Miniatur anklicken, um das Bild zu vergrößern|Haz clic en la miniatura para ampliar
    Desktop screenshot|Desktop-Aufnahme|Captura del escritorio
    Browser screenshot|Browser-Aufnahme|Captura del navegador
    Proofreader|Rechtschreibprüfung|Corrector ortográfico
    Review, correct and rephrase your writing.|Texte prüfen, korrigieren und umformulieren.|Revisa, corrige y reformula tus textos.
    Translate text with the model of your choice.|Übersetzen Sie Texte mit dem Modell Ihrer Wahl.|Traduce textos con el modelo que elijas.
    Reasoning, code and interactive applications with a live preview.|Denken, Code und interaktive Anwendungen mit Live-Vorschau.|Razonamiento, código y aplicaciones interactivas con vista previa.
    Asset generator|Asset-Generator|Generador de recursos
    Automatic skill creation|Skills automatisch erstellen|Creación automática de skills
    Memory · Conversation|Gedächtnis · Unterhaltung|Memoria · Conversación
    Memory · Shared|Gedächtnis · Gemeinsam|Memoria · Compartida
    Python scripts|Python-Skripte|Scripts Python
    Application management|Anwendungsverwaltung|Gestión de aplicaciones
    Bypass image AI|Zusätzliche Bildanalyse|Visión de IA complementaria
    Semantic search RAG|Semantische Suche RAG|Búsqueda semántica RAG
    Code search glob/grep|Codesuche glob/grep|Búsqueda de código glob/grep
    Multi-file patch with diff|Mehrdateien-Patch mit Diff|Parche de varios archivos con diferencias
    Source exploration|Quellen erkunden|Exploración de fuentes
    Source editing|Quellen bearbeiten|Edición de fuentes
    Web research|Webrecherche|Búsqueda web
    Mouse control|Maussteuerung|Control del ratón
    Keyboard control|Tastatursteuerung|Control del teclado
    Screenshots|Bildschirmaufnahmen|Capturas de pantalla
    Code review|Codeprüfung|Revisión de código
    Planning|Planung|Planificación
    Summarization|Zusammenfassung|Síntesis
    · ≈ estimated|· ≈ geschätzt|· ≈ estimado
    · ≈ estimated counts|· ≈ geschätzte Werte|· ≈ valores estimados
    · checking…|· wird geprüft…|· comprobando…
    · generating…|· wird erzeugt…|· generando…
    · inspect the result|· Ergebnis prüfen|· revisa el resultado
    · provisional|· vorläufig|· provisional
    · Settings > About|· Einstellungen > Über die Anwendung|· Ajustes > Acerca de
    · View changelog|· Änderungsverlauf anzeigen|· Ver historial de cambios
    — Cancelled|— Abgebrochen|— Cancelada
    ← Providers|← Anbieter|← Proveedores
    ↻ Refresh models|↻ Modelle aktualisieren|↻ Actualizar modelos
    ＋ Add provider|＋ Anbieter hinzufügen|＋ Añadir proveedor
    ＋ New task|＋ Neue Aufgabe|＋ Nueva tarea
    ◉ In progress|◉ In Bearbeitung|◉ En curso
    ○ To do|○ Ausstehend|○ Pendiente
    ✓ Completed|✓ Abgeschlossen|✓ Terminada
    A preset with that name already exists.|Ein Preset mit diesem Namen existiert bereits.|Ya existe un preset con ese nombre.
    Add a provider to get started.|Fügen Sie zuerst einen Anbieter hinzu.|Añade un proveedor para empezar.
    ADD TO MESSAGE|ZUR NACHRICHT HINZUFÜGEN|AÑADIR AL MENSAJE
    Agent count (1 to 6)|Anzahl der Agenten (1 bis 6)|Número de agentes (de 1 a 6)
    Agents are required by the composite model.|Das zusammengesetzte Modell erfordert Agenten.|El modelo compuesto requiere agentes.
    All accessible scopes|Alle zugänglichen Bereiche|Todos los ámbitos accesibles
    All categories|Alle Kategorien|Todas las categorías
    All four applications|Alle vier Anwendungen|Las cuatro aplicaciones
    Answer and resume|Antworten und fortfahren|Responder y continuar
    API model|API-Modell|Modelo de API
    Application preview|Anwendungsvorschau|Vista previa de la aplicación
    Application to build|Zu erstellende Anwendung|Aplicación que construir
    Apply|Anwenden|Aplicar
    Attach and configure|Anhängen und konfigurieren|Adjuntar y configurar
    Automatic count · change in the settings on the right|Automatische Anzahl · rechts einstellen|Número automático · cambiar en los ajustes de la derecha
    Available models|Verfügbare Modelle|Modelos disponibles
    Average · tok/s|Durchschnitt · tok/s|Promedio · tok/s
    Background|Hintergrund|Fondo
    Base|Basis|Base
    Before / after|Vorher / nachher|Antes / después
    Benchmark complete|Benchmark abgeschlossen|Benchmark terminado
    Benchmark interrupted|Benchmark unterbrochen|Benchmark interrumpido
    Built-in theme: create a copy to customize it.|Integriertes Design: Kopie erstellen, um es anzupassen.|Tema integrado: crea una copia para personalizarlo.
    Cancelled|Abgebrochen|Cancelado
    Cancelled: partial response, metrics unavailable.|Abgebrochen: Teilantwort, keine Messwerte verfügbar.|Cancelado: respuesta parcial, métricas no disponibles.
    Cancelled. The original text is preserved.|Abgebrochen. Der Originaltext bleibt erhalten.|Cancelado. Se conserva el texto original.
    Category|Kategorie|Categoría
    Challenges|Aufgaben|Pruebas
    Changelog|Änderungsverlauf|Historial de cambios
    characters|Zeichen|caracteres
    Checking spelling, grammar and punctuation…|Rechtschreibung, Grammatik und Zeichensetzung werden geprüft…|Comprobando ortografía, gramática y puntuación…
    Choose an asset|Asset auswählen|Elegir recurso
    Choose the naming provider and model.|Wählen Sie Anbieter und Modell für Titel.|Elige el proveedor y modelo de títulos.
    Chrome path (optional)|Chrome-Pfad (optional)|Ruta de Chrome (opcional)
    Classic|Klassisch|Clásico
    Classic · smooth SVG shapes|Klassisch · glatte SVG-Formen|Clásico · formas SVG suaves
    Clear|Leeren|Borrar
    Clear all|Alles leeren|Borrar todo
    Clear background|Hintergrund entfernen|Quitar fondo
    Clear project index|Projektindex löschen|Borrar índice del proyecto
    Code / visual|Code / visuell|Código / visual
    Collapse or expand questions|Fragen ein- oder ausklappen|Contraer o expandir preguntas
    Combined suite|Kombinierte Aufgaben|Conjunto combinado
    Compact now|Jetzt komprimieren|Compactar ahora
    Compacting context…|Kontext wird komprimiert…|Compactando contexto…
    Compaction available after the response.|Komprimierung nach der Antwort verfügbar.|Compactación disponible después de la respuesta.
    Compaction stopped.|Komprimierung gestoppt.|Compactación detenida.
    Composite model|Zusammengesetztes Modell|Modelo compuesto
    Connecting to the model…|Verbindung zum Modell wird hergestellt…|Conectando con el modelo…
    Context compacted.|Kontext komprimiert.|Contexto compactado.
    Context usage|Kontextverbrauch|Uso del contexto
    Copied to clipboard.|In die Zwischenablage kopiert.|Copiado al portapapeles.
    Copy HTML|HTML kopieren|Copiar HTML
    Copy JSON report|JSON-Bericht kopieren|Copiar informe JSON
    Could not load the project. Select it to retry.|Projekt konnte nicht geladen werden. Erneut auswählen.|No se pudo cargar el proyecto. Selecciónalo para reintentar.
    Create a copy|Kopie erstellen|Crear copia
    Custom|Benutzerdefiniert|Personalizado
    Custom instruction|Eigene Anweisung|Instrucción personalizada
    Custom themes|Eigene Designs|Temas personalizados
    Dark|Dunkel|Oscuro
    Dark mode|Dunkler Modus|Modo oscuro
    Dedicated tools|Spezielle Werkzeuge|Herramientas dedicadas
    Delete this memory?|Diese Erinnerung löschen?|¿Eliminar esta memoria?
    Describe components and shapes with coordinates|Komponenten und Formen mit Koordinaten beschreiben|Describir componentes y formas con coordenadas
    Drawing mode|Zeichenmodus|Modo de dibujo
    E.g. architecture, preferences|Z. B. Architektur, Einstellungen|Por ejemplo: arquitectura, preferencias
    Edit colors below, or delete this custom theme.|Farben unten bearbeiten oder dieses Design löschen.|Edita los colores o elimina este tema personalizado.
    Edit MCP.json|MCP.json bearbeiten|Editar MCP.json
    Edit queued message|Nachricht in der Warteschlange bearbeiten|Editar mensaje en cola
    Embedded WebView2|Integriertes WebView2|WebView2 integrado
    Embeddings provider|Embedding-Anbieter|Proveedor de embeddings
    Enter a theme name.|Geben Sie einen Designnamen ein.|Introduce un nombre de tema.
    Enter your custom answer.|Geben Sie Ihre eigene Antwort ein.|Introduce tu respuesta personalizada.
    Expected answer|Erwartete Antwort|Respuesta esperada
    Export…|Exportieren…|Exportar…
    Failed|Fehlgeschlagen|Fallido
    Filter|Filtern|Filtrar
    Find edge cases and review the results.|Grenzfälle suchen und Ergebnisse prüfen.|Busca casos límite y revisa los resultados.
    Fit|Einpassen|Ajustar
    Fork from here|Ab hier verzweigen|Crear rama desde aquí
    Four dark themes and four light themes.|Vier dunkle und vier helle Designs.|Cuatro temas oscuros y cuatro claros.
    Frame duration|Bilddauer|Duración del fotograma
    Frame duration (ms)|Bilddauer (ms)|Duración del fotograma (ms)
    Generating…|Wird erzeugt…|Generando…
    Grid visible from 5 screen pixels per cell|Raster ab 5 Bildschirmpixeln pro Zelle sichtbar|Cuadrícula visible desde 5 píxeles por celda
    Height (px)|Höhe (px)|Alto (px)
    Hide list · reopen from +|Liste ausblenden · über + öffnen|Ocultar lista · abrir desde +
    History unavailable. Scroll up again to retry.|Verlauf nicht verfügbar. Zum Wiederholen erneut nach oben scrollen.|Historial no disponible. Vuelve a subir para reintentar.
    Icon color|Symbolfarbe|Color del icono
    Identify relevant files and project constraints.|Relevante Dateien und Projektvorgaben ermitteln.|Identifica archivos relevantes y restricciones del proyecto.
    including latency|einschließlich Latenz|incluida latencia
    Input|Eingabe|Entrada
    Instruction|Anweisung|Instrucción
    Interactive applications|Interaktive Anwendungen|Aplicaciones interactivas
    Layers · front first|Ebenen · vordere zuerst|Capas · primero las delanteras
    Light|Hell|Claro
    Loading conversation…|Unterhaltung wird geladen…|Cargando conversación…
    Loading failed · Retry|Laden fehlgeschlagen · Erneut versuchen|Error al cargar · Reintentar
    Loading failed. Refresh to retry.|Laden fehlgeschlagen. Zum Wiederholen aktualisieren.|Error al cargar. Actualiza para reintentar.
    Loading models…|Modelle werden geladen…|Cargando modelos…
    Loading project…|Projekt wird geladen…|Cargando proyecto…
    Loading settings…|Einstellungen werden geladen…|Cargando ajustes…
    Locate in text|Im Text finden|Localizar en el texto
    Logic|Logik|Lógica
    Low contrast: some text may be difficult to read.|Geringer Kontrast: Text kann schwer lesbar sein.|Contraste bajo: algunos textos pueden ser difíciles de leer.
    Maximum files|Maximale Dateianzahl|Máximo de archivos
    Maximum four images per message.|Höchstens vier Bilder pro Nachricht.|Máximo de cuatro imágenes por mensaje.
    MCP · no servers|MCP · keine Server|MCP · sin servidores
    Measured|Gemessen|Medido
    Minimum severity|Minimale Fehlerstufe|Gravedad mínima
    Model for this tool|Modell für dieses Werkzeug|Modelo para esta herramienta
    Model reply|Modellantwort|Respuesta del modelo
    Model response|Modellantwort|Respuesta del modelo
    More actions|Weitere Aktionen|Más acciones
    Name|Name|Nombre
    Name with AI|Mit KI benennen|Nombrar con IA
    Needs attention|Aufmerksamkeit erforderlich|Necesita atención
    New|Neu|Nuevo
    New asset|Neues Asset|Nuevo recurso
    New canvas|Neue Zeichenfläche|Nuevo lienzo
    New memory|Neue Erinnerung|Nueva memoria
    New project|Neues Projekt|Nuevo proyecto
    New tab|Neuer Tab|Nueva pestaña
    No active run.|Keine laufende Generierung.|No hay generación activa.
    No conversations|Keine Unterhaltungen|Sin conversaciones
    No memories found.|Keine Erinnerungen gefunden.|No se encontraron memorias.
    No models found.|Keine Modelle gefunden.|No se encontraron modelos.
    No models selected|Keine Modelle ausgewählt|Sin modelos seleccionados
    No newer compatible GUI release with a SHA-256 digest.|Keine neuere kompatible GUI-Version mit SHA-256-Prüfsumme.|No hay una versión GUI compatible más reciente con huella SHA-256.
    No remaining suggestions.|Keine weiteren Vorschläge.|No quedan sugerencias.
    No server configured|Kein Server konfiguriert|Sin servidor configurado
    No template|Keine Vorlage|Sin plantilla
    No useful reduction: history preserved.|Keine sinnvolle Reduzierung: Verlauf beibehalten.|Sin reducción útil: historial conservado.
    now|jetzt|ahora
    Only in the visible conversation.|Nur in der angezeigten Unterhaltung.|Solo en la conversación visible.
    Open and focus the latest AI tool|Zuletzt verwendetes KI-Werkzeug öffnen|Abrir y seleccionar la última herramienta de IA
    Output|Ausgabe|Salida
    Passed|Bestanden|Superado
    Paste your message here: formatting is preserved.|Nachricht hier einfügen: Formatierung bleibt erhalten.|Pega aquí tu mensaje: se conserva el formato.
    Pending|Ausstehend|Pendiente
    Pinned|Angeheftet|Fijado
    Pixel art · grid|Pixel-Art · Raster|Pixel art · cuadrícula
    Pixel art · pixel-by-pixel drawing|Pixel-Art · Pixel für Pixel zeichnen|Pixel art · dibujo píxel a píxel
    Preview · Here is some sample text.|Vorschau · Dies ist ein Beispieltext.|Vista previa · Este es un texto de ejemplo.
    Primary action|Hauptaktion|Acción principal
    Project|Projekt|Proyecto
    Prompt|Anweisung|Consigna
    Punctuation|Zeichensetzung|Puntuación
    Question|Frage|Pregunta
    Readable text contrast.|Gut lesbarer Textkontrast.|Contraste de texto legible.
    Ready to run all 7 challenges.|Bereit für alle 7 Aufgaben.|Listo para las 7 pruebas.
    Ready to run the challenges.|Bereit für die Aufgaben.|Listo para las pruebas.
    Reasoning and code|Denken und Code|Razonamiento y código
    Recommended resolution|Empfohlene Auflösung|Resolución recomendada
    Reload|Neu laden|Recargar
    Reload file|Datei neu laden|Recargar archivo
    Remove this text|Diesen Text entfernen|Quitar este texto
    Rephrase|Umformulieren|Reformular
    Rephrasing ready. You can replace the original text.|Umformulierung bereit. Sie können den Originaltext ersetzen.|Reformulación lista. Puedes reemplazar el texto original.
    Reproducible seed|Reproduzierbarer Seed|Semilla reproducible
    Required by the selected composite model|Vom zusammengesetzten Modell vorgegeben|Impuesto por el modelo compuesto seleccionado
    Results|Ergebnisse|Resultados
    Resume|Fortsetzen|Continuar
    Resume here|Ab hier fortsetzen|Continuar desde aquí
    Resume here?|Ab hier fortsetzen?|¿Continuar desde aquí?
    Resuming…|Wird fortgesetzt…|Reanudando…
    Retention in days|Aufbewahrung in Tagen|Retención en días
    Role|Rolle|Rol
    Run a check to see suggestions here.|Prüfung starten, um hier Vorschläge anzuzeigen.|Inicia la revisión para ver sugerencias aquí.
    Run benchmark|Benchmark starten|Ejecutar benchmark
    Running|Läuft|En curso
    Save as…|Speichern unter…|Guardar como…
    Saved history — estimates|Gespeicherter Verlauf — Schätzungen|Historial guardado — estimaciones
    Saved information|Gespeicherte Informationen|Información guardada
    Scale|Maßstab|Escala
    Scope|Bereich|Ámbito
    Search models…|Modelle suchen…|Buscar modelos…
    Search titles, tags and contents…|Titel, Tags und Inhalte suchen…|Buscar títulos, etiquetas y contenidos…
    Secondary text|Sekundärer Text|Texto secundario
    Select a model in provider settings.|Wählen Sie ein Modell in den Anbietereinstellungen.|Selecciona un modelo en los ajustes del proveedor.
    Select a row to inspect every field.|Zeile auswählen, um alle Felder anzuzeigen.|Selecciona una fila para ver todos los campos.
    Select a suggestion to locate it in the text.|Vorschlag auswählen, um ihn im Text zu finden.|Selecciona una sugerencia para localizarla en el texto.
    Select all|Alle auswählen|Seleccionar todo
    Select an answer or enter your text.|Antwort auswählen oder eigenen Text eingeben.|Selecciona una respuesta o escribe tu texto.
    Send to the agent at its next step|Beim nächsten Schritt an den Agenten senden|Enviar al agente en su siguiente paso
    Server names must be unique.|Servernamen müssen eindeutig sein.|Los nombres de servidor deben ser únicos.
    Set background|Hintergrund festlegen|Establecer fondo
    shapes|Formen|formas
    Shared|Gemeinsam|Compartida
    Show this preview|Diese Vorschau anzeigen|Mostrar esta vista previa
    Stable key|Stabiler Schlüssel|Clave estable
    Steer|Anweisen|Dirigir
    Step|Schritt|Paso
    Stop generation before resuming here.|Generierung stoppen, bevor Sie hier fortsetzen.|Detén la generación antes de continuar desde aquí.
    Subagent mode|Unteragentenmodus|Modo de subagentes
    Subagents saved for this conversation.|Unteragenten für diese Unterhaltung gespeichert.|Subagentes guardados para esta conversación.
    Success grade|Erfolgsbewertung|Nota de éxito
    Swap|Tauschen|Intercambiar
    Text too long (20,000 characters maximum).|Text zu lang (höchstens 20.000 Zeichen).|Texto demasiado largo (máximo 20.000 caracteres).
    The generated application will appear here.|Die erzeugte Anwendung erscheint hier.|La aplicación generada aparecerá aquí.
    The model found no errors.|Das Modell hat keine Fehler gefunden.|El modelo no encontró errores.
    The model is building the application…|Das Modell erstellt die Anwendung…|El modelo está creando la aplicación…
    Theme name|Designname|Nombre del tema
    Theme to customize|Anzupassendes Design|Tema que personalizar
    This name is already used.|Dieser Name wird bereits verwendet.|Este nombre ya está en uso.
    Time per challenge|Zeit pro Aufgabe|Tiempo por prueba
    Title|Titel|Título
    Tokens used|Verwendete Tokens|Tokens utilizados
    Tool result|Werkzeugergebnis|Resultado de la herramienta
    Translation complete.|Übersetzung abgeschlossen.|Traducción terminada.
    Transparent background|Transparenter Hintergrund|Fondo transparente
    Type your answer…|Antwort eingeben…|Escribe tu respuesta…
    Unable to check. Try again later.|Prüfung nicht möglich. Später erneut versuchen.|No se pudo comprobar. Reintenta más tarde.
    Unified diff|Einheitlicher Diff|Diferencias unificadas
    Unread completed replies|Ungelesene fertige Antworten|Respuestas terminadas sin leer
    Unread completed reply|Ungelesene fertige Antwort|Respuesta terminada sin leer
    URL changed during the test: try again.|URL während der Prüfung geändert: erneut versuchen.|La URL cambió durante la prueba: reintenta.
    URL changed: try again.|URL geändert: erneut versuchen.|La URL cambió: reintenta.
    Use this version|Diese Version verwenden|Usar esta versión
    User|Benutzer|Usuario
    View changelog|Änderungsverlauf anzeigen|Ver historial de cambios
    Waiting for the model's first output…|Warten auf die erste Modellausgabe…|Esperando la primera salida del modelo…
    Waiting for the model's response…|Warten auf die Modellantwort…|Esperando la respuesta del modelo…
    Waiting for your answer in the conversation|Warten auf Ihre Antwort in der Unterhaltung|Esperando tu respuesta en la conversación
    Width (px)|Breite (px)|Ancho (px)
    words|Wörter|palabras
    Work steps|Arbeitsschritte|Pasos de trabajo
    Write your own answer|Eigene Antwort schreiben|Escribe tu respuesta
    Your message|Ihre Nachricht|Tu mensaje
    Your rewritten text will appear here.|Ihr umformulierter Text erscheint hier.|Tu texto reformulado aparecerá aquí.
    Your translation will appear here.|Ihre Übersetzung erscheint hier.|Tu traducción aparecerá aquí.
    Zoom · screen pixels per cell|Zoom · Bildschirmpixel pro Zelle|Zoom · píxeles de pantalla por celda
    DEFAULT|STANDARD|PREDETERMINADO
    SHORT|KURZ|BREVE
    PRAGMATIC|PRAGMATISCH|PRAGMÁTICO
    DETAILED|AUSFÜHRLICH|DETALLADO
    FUN|HUMORVOLL|DIVERTIDO
    Before · HEAD|Vorher · HEAD|Antes · HEAD
    After · Working tree|Nachher · Arbeitsverzeichnis|Después · Directorio de trabajo
    Thinking|Nachdenken|Razonando
    Starting|Starten|Iniciando
    Response received|Antwort erhalten|Respuesta recibida
    No modified files.|Keine geänderten Dateien.|No hay archivos modificados.
    """;
}
