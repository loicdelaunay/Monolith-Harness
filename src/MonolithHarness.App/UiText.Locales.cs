using System.Globalization;

namespace MonolithHarness.App;

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
        foreach (var prefix in new[] { "Error: ", "Goal: ", "Setting saved · ", "Provider: ", "Tool: ", "Folder: ", "Duration: ", "Answered at ", "Path not found on disk: ", "Thinking: " })
            if (english.StartsWith(prefix, StringComparison.Ordinal) && Translations.TryGetValue(prefix.Trim(), out var translated))
                return (locale == "de" ? translated.German : translated.Spanish) + " " + english[prefix.Length..];
        if (english.StartsWith("No response for ") && english.EndsWith(" seconds"))
        {
            var seconds = english[16..^8];
            return locale == "de" ? $"Seit {seconds} Sekunden keine Antwort" : $"Sin respuesta desde hace {seconds} segundos";
        }
        if (english.EndsWith(" skills enabled", StringComparison.Ordinal) && int.TryParse(english[..^15], out var skills))
            return locale == "de" ? $"{skills} Skills aktiviert" : $"{skills} skills activados";
        const string connectedPrefix = "Connected · ", connectedSuffix = " models available. Choose a model to use.";
        if (english.StartsWith(connectedPrefix, StringComparison.Ordinal) && english.EndsWith(connectedSuffix, StringComparison.Ordinal))
        {
            var count = english[connectedPrefix.Length..^connectedSuffix.Length];
            return locale == "de" ? $"Verbunden · {count} Modelle verfügbar. Wählen Sie ein Modell." : $"Conectado · {count} modelos disponibles. Elige un modelo.";
        }
        return english;
    }
    const string LocaleData = """
    Automatic maximum capacity|Automatische maximale Kapazität|Capacidad máxima automática
    Automatic|Automatisch|Automático
    Custom|Benutzerdefiniert|Personalizado
    Limit for this model (tokens)|Limit für dieses Modell (Tokens)|Límite para este modelo (tokens)
    Select a model.|Wählen Sie ein Modell.|Selecciona un modelo.
    Model:|Modell:|Modelo:
    Reported maximum: {0:N0} tokens.|Gemeldetes Maximum: {0:N0} Tokens.|Máximo comunicado: {0:N0} tokens.
    Maximum not reported. Estimated fallback: {0:N0} tokens; change it in custom mode.|Kein Maximum gemeldet. Geschätzter Ersatzwert: {0:N0} Tokens, im benutzerdefinierten Modus änderbar.|Máximo no comunicado. Valor alternativo estimado: {0:N0} tokens; se puede cambiar en modo personalizado.
    The provider reports an input token limit.|Der Anbieter meldet ein Limit für Eingabetokens.|El proveedor indica un límite de tokens de entrada.
    The engine's supported limit is lower.|Das vom System unterstützte Limit ist niedriger.|El límite admitido por el motor es inferior.
    Model context|Modellkontext|Contexto del modelo
    Context|Kontext|Contexto
    Thinking|Denken|Razonamiento
    Context information|Kontextinformationen|Información del contexto
    Window declared in GGUF metadata. Available memory may require a lower limit.|In den GGUF-Metadaten angegebenes Kontextfenster. Der verfügbare Speicher kann ein niedrigeres Limit erfordern.|Ventana indicada en los metadatos GGUF. La memoria disponible puede requerir un límite menor.
    Enter the project name.|Geben Sie den Projektnamen ein.|Introduce el nombre del proyecto.
    Choose folders only:|Wählen Sie nur Ordner:|Elige solo carpetas:
    Compare multiple detectors for signs of AI-generated text.|Vergleichen Sie mehrere Detektoren auf Hinweise auf KI-generierte Texte.|Compara varios detectores para identificar señales de texto generado por IA.
    SlopTotal service address|Adresse des SlopTotal-Dienstes|Dirección del servicio SlopTotal
    Detection service · setup|Erkennungsdienst · Einrichtung|Servicio de detección · Configuración
    Connect and save|Verbinden und speichern|Conectar y guardar
    Install / start locally|Lokal installieren / starten|Instalar / iniciar en local
    Stop local service|Lokalen Dienst stoppen|Detener servicio local
    Install Docker|Docker installieren|Instalar Docker
    Source to analyze|Zu analysierende Quelle|Fuente que analizar
    Web page text|Text einer Webseite|Texto de una página web
    AI-built website|Mit KI erstellte Website|Sitio web creado con IA
    Paste text to examine…|Text zur Untersuchung einfügen…|Pega el texto que deseas examinar…
    Page URL|URL der Seite|URL de la página
    Choose document…|Dokument auswählen…|Elegir documento…
    Show paragraph scores|Absatzbewertungen anzeigen|Mostrar puntuaciones por párrafo
    Your report will appear here|Ihr Bericht erscheint hier|Tu informe aparecerá aquí
    AI generation index|Index für KI-Generierung|Índice de generación por IA
    Detector results|Ergebnisse der Detektoren|Resultados de los detectores
    All engines|Alle Detektoren|Todos los motores
    AI signals detected|KI-Hinweise erkannt|Señales de IA detectadas
    Few AI signals|Wenige KI-Hinweise|Pocas señales de IA
    Unavailable engines|Nicht verfügbare Detektoren|Motores no disponibles
    Paragraph analysis|Absatzanalyse|Análisis por párrafo
    Website builder fingerprints|Spuren des Website-Erstellers|Huellas del creador del sitio
    Copy report|Bericht kopieren|Copiar informe
    Analyzed by your local service. Nothing sent to the chat provider.|Analyse durch Ihren lokalen Dienst. Keine Übertragung an den Chat-Anbieter.|Análisis en tu servicio local. No se envía nada al proveedor del chat.
    Content will be sent to:|Inhalt wird gesendet an:|El contenido se enviará a:
    Enter a valid service address.|Geben Sie eine gültige Dienstadresse ein.|Introduce una dirección válida del servicio.
    Connecting to service…|Verbindung zum Dienst…|Conectando con el servicio…
    Service reachable|Dienst erreichbar|Servicio accesible
    listed engines|aufgelistete Detektoren|motores disponibles
    Degraded service|Eingeschränkter Dienst|Servicio degradado
    Installing / starting local service…|Lokaler Dienst wird installiert / gestartet…|Instalando / iniciando el servicio local…
    Downloading dependencies…|Abhängigkeiten werden heruntergeladen…|Descargando dependencias…
    Installing dependencies…|Abhängigkeiten werden installiert…|Instalando dependencias…
    Preparing local service… First startup may take several minutes.|Lokaler Dienst wird vorbereitet… Der erste Start kann einige Minuten dauern.|Preparando el servicio local… El primer inicio puede tardar varios minutos.
    Waiting for startup… Models may still be downloading.|Warten auf den Start… Modelle werden eventuell noch heruntergeladen.|Esperando el inicio… Puede que los modelos aún se estén descargando.
    Local service ready. The first scan may wait for models to load.|Lokaler Dienst bereit. Die erste Prüfung muss eventuell auf die Modelle warten.|Servicio local preparado. El primer análisis puede esperar a que se carguen los modelos.
    Local service stopped. Downloaded models are retained.|Lokaler Dienst gestoppt. Heruntergeladene Modelle bleiben erhalten.|Servicio local detenido. Los modelos descargados se conservan.
    Paste at least 50 characters to start analysis.|Fügen Sie mindestens 50 Zeichen ein, um die Analyse zu starten.|Pega al menos 50 caracteres para iniciar el análisis.
    Choose a document to analyze.|Wählen Sie ein Dokument zur Analyse aus.|Elige un documento que analizar.
    Enter the page URL.|Geben Sie die URL der Seite ein.|Introduce la URL de la página.
    Connecting to detectors…|Verbindung zu den Detektoren…|Conectando con los detectores…
    Extracting document…|Dokument wird ausgelesen…|Extrayendo el documento…
    Looking for builder fingerprints and analyzing text…|Spuren des Erstellers werden gesucht und Text wird analysiert…|Buscando huellas del creador y analizando el texto…
    Queued · position|Warteschlange · Position|En cola · posición
    Analyzing…|Analyse läuft…|Analizando…
    Provisional score|Vorläufige Bewertung|Puntuación provisional
    engines completed|Detektoren abgeschlossen|motores terminados
    Analyzing paragraphs…|Absätze werden analysiert…|Analizando los párrafos…
    Analysis completed in|Analyse abgeschlossen in|Análisis completado en
    Indeterminate result|Unbestimmtes Ergebnis|Resultado indeterminado
    Incomplete report · limited interpretation|Unvollständiger Bericht · begrenzte Aussagekraft|Informe incompleto · interpretación limitada
    flag AI signals|melden KI-Hinweise|detectan señales de IA
    Few signs of AI generation|Wenige Hinweise auf KI-Generierung|Pocas señales de generación por IA
    Weak signals|Schwache Hinweise|Señales débiles
    Mixed or suspicious signals|Gemischte oder verdächtige Hinweise|Señales mixtas o sospechosas
    Likely AI-generated according to detectors|Laut Detektoren wahrscheinlich KI-generiert|Probablemente generado por IA según los detectores
    Strong signs of AI generation|Starke Hinweise auf KI-Generierung|Señales claras de generación por IA
    Short text: interpret the result with care.|Kurzer Text: Ergebnis mit Vorsicht interpretieren.|Texto breve: interpreta el resultado con cautela.
    Engine documentation|Dokumentation des Detektors|Documentación del motor
    Unavailable|Nicht verfügbar|No disponible
    No engines match this filter.|Keine Detektoren entsprechen diesem Filter.|Ningún motor coincide con este filtro.
    Start an analysis to compare detectors.|Starten Sie eine Analyse zum Vergleich der Detektoren.|Inicia un análisis para comparar los detectores.
    No usable paragraphs.|Keine auswertbaren Absätze.|No hay párrafos que se puedan analizar.
    Confirmed fingerprints|Bestätigte Spuren|Huellas confirmadas
    Possible fingerprints|Mögliche Spuren|Huellas posibles
    Quick analysis of website text|Schnellanalyse des Website-Textes|Análisis rápido del texto del sitio
    Insufficient prose for an AI score|Zu wenig Text für eine KI-Bewertung|Texto insuficiente para una puntuación de IA
    Builder fingerprints and the text score are two independent measurements.|Spuren des Erstellers und Textbewertung sind zwei unabhängige Messungen.|Las huellas del creador y la puntuación del texto son dos medidas independientes.
    JSON report copied.|JSON-Bericht kopiert.|Informe JSON copiado.
    Local: Docker must be installed and running. First start downloads dependencies and several GB of models. The service keeps running after this window closes; use Stop to free its resources. Models and reports are retained in Docker volumes.|Lokal: Docker muss installiert sein und laufen. Beim ersten Start werden Abhängigkeiten und mehrere GB Modelle heruntergeladen. Der Dienst läuft nach dem Schließen dieses Fensters weiter; Stoppen gibt Ressourcen frei. Modelle und Berichte bleiben in Docker-Volumes erhalten.|Local: Docker debe estar instalado y en ejecución. El primer inicio descarga dependencias y varios GB de modelos. El servicio sigue en ejecución al cerrar esta ventana; usa Detener para liberar recursos. Los modelos e informes se conservan en volúmenes Docker.
    SlopTotal retains reports according to server settings (30 days by default).|SlopTotal speichert Berichte gemäß den Servereinstellungen (standardmäßig 30 Tage).|SlopTotal conserva los informes según la configuración del servidor (30 días por defecto).
    The score is an indicator, not proof of authorship. Texts under 80 words are unreliable; aim for at least 200 words. These engines evaluate prose, not images or source code. Reliability varies by language and rewriting.|Die Bewertung ist ein Hinweis, kein Nachweis der Urheberschaft. Texte unter 80 Wörtern sind unzuverlässig; mindestens 200 Wörter werden empfohlen. Die Detektoren bewerten Fließtext, keine Bilder oder Quellcodes. Die Zuverlässigkeit hängt von Sprache und Überarbeitung ab.|La puntuación es un indicio, no una prueba de autoría. Los textos de menos de 80 palabras son poco fiables; se recomiendan al menos 200. Los motores evalúan texto, no imágenes ni código fuente. La fiabilidad varía según el idioma y las modificaciones.
    Some detectors are missing or failed. The service score may be biased; do not rely on its overall verdict.|Einige Detektoren fehlen oder sind fehlgeschlagen. Die Dienstbewertung kann verzerrt sein; verlassen Sie sich nicht auf das Gesamturteil.|Faltan algunos detectores o han fallado. La puntuación del servicio puede estar sesgada; no te bases en su veredicto global.
    Calibrated score supplied by SlopTotal. Compare engines before drawing a conclusion.|Kalibrierte Bewertung von SlopTotal. Vergleichen Sie die Detektoren vor einer Schlussfolgerung.|Puntuación calibrada proporcionada por SlopTotal. Compara los motores antes de sacar conclusiones.
    These quick passage scores are separate from the full report. The service may merge short paragraphs and truncate displayed excerpts.|Diese schnellen Abschnittsbewertungen sind vom vollständigen Bericht getrennt. Der Dienst kann kurze Absätze zusammenfassen und angezeigte Auszüge kürzen.|Estas puntuaciones rápidas por fragmento son distintas del informe completo. El servicio puede agrupar párrafos breves y recortar los fragmentos mostrados.
    No AI builder fingerprints identified. This does not prove the site was written without AI.|Keine Spuren eines KI-Website-Erstellers gefunden. Das beweist nicht, dass die Website ohne KI erstellt wurde.|No se han identificado huellas de un creador de sitios con IA. Esto no demuestra que el sitio se haya creado sin IA.
    Open|Öffnen|Abrir
    Open in browser|Im Browser öffnen|Abrir en el navegador
    Open containing folder|Übergeordneten Ordner öffnen|Abrir la carpeta del archivo
    Path not found on disk:|Pfad auf dem Datenträger nicht gefunden:|Ruta no encontrada en el disco:
    Ambiguous path: specify the source folder or an absolute path.|Mehrdeutiger Pfad: Quellordner oder absoluten Pfad angeben.|Ruta ambigua: indica la carpeta de origen o una ruta absoluta.
    General|Allgemein|General
    Appearance|Darstellung|Apariencia
    Welcome to|Willkommen bei|Bienvenido a
    Welcome guide|Willkommensassistent|Guía de bienvenida
    Open welcome guide|Willkommensassistent öffnen|Abrir guía de bienvenida
    Revisit the steps to choose a theme, connect a provider and discover skills.|Wählen Sie erneut ein Design, verbinden Sie einen Anbieter und entdecken Sie Skills.|Repasa los pasos para elegir un tema, conectar un proveedor y descubrir skills.
    Later|Später|Más tarde
    Get started|Loslegen|Comenzar
    Your space, your colors.|Ihr Raum, Ihre Farben.|Tu espacio, tus colores.
    Your first model.|Ihr erstes Modell.|Tu primer modelo.
    Your toolkit.|Ihre Werkzeuge.|Tus herramientas.
    Light, dark or custom: start by making yourself at home.|Hell, dunkel oder individuell: Machen Sie es sich gemütlich.|Claro, oscuro o personalizado: empieza sintiéndote como en casa.
    A URL, a key, a model: your first chat is within reach.|Eine URL, ein Schlüssel, ein Modell: Ihr erster Chat ist zum Greifen nah.|Una URL, una clave, un modelo: tu primer chat está a tu alcance.
    Research, code, memory… Build the toolkit that suits you.|Recherche, Code, Gedächtnis… Stellen Sie Ihre passenden Werkzeuge zusammen.|Investigación, código, memoria… Crea el conjunto de herramientas que necesitas.
    Choose the look you prefer. Preview it immediately; you can change it again in Appearance.|Wählen Sie Ihr bevorzugtes Design mit sofortiger Vorschau. Sie können es später unter Darstellung ändern.|Elige el aspecto que prefieras con vista previa inmediata. Puedes cambiarlo después en Apariencia.
    Tip: Appearance also lets you choose fonts, customize your logo and create your own theme.|Tipp: Unter Darstellung können Sie auch Schriften wählen, Ihr Logo anpassen und ein eigenes Design erstellen.|Consejo: Apariencia también permite elegir fuentes, personalizar el logo y crear tu propio tema.
    Your provider gives access to the models used in chats. Choose a connection, enter a key if needed, then select a model.|Ihr Anbieter stellt die Modelle für Chats bereit. Wählen Sie eine Verbindung, geben Sie bei Bedarf einen Schlüssel ein und wählen Sie ein Modell.|Tu proveedor da acceso a los modelos del chat. Elige una conexión, introduce una clave si es necesaria y selecciona un modelo.
    I'll set up my provider later|Ich richte meinen Anbieter später ein|Configuraré mi proveedor más tarde
    Skills give the model specialized capabilities. Enable what you need; operations still follow your permissions. Adjust this selection later in Settings or the chat's ＋ menu.|Skills geben dem Modell spezielle Fähigkeiten. Aktivieren Sie, was Sie brauchen; Ihre Berechtigungen gelten weiterhin. Ändern Sie die Auswahl später in den Einstellungen oder im ＋-Menü des Chats.|Los skills dan capacidades especializadas al modelo. Activa los que necesites; las acciones siguen tus permisos. Puedes cambiar la selección en Ajustes o en el menú ＋ del chat.
    Essentials|Grundausstattung|Esenciales
    Skill presets|Skill-Presets|Presets de skills
    Development|Entwicklung|Desarrollo
    Disable all|Alle deaktivieren|Desactivar todos
    Git reading|Git-Lesezugriff|Lectura Git
    Git writing|Git-Schreibzugriff|Escritura Git
    Git permissions|Git-Berechtigungen|Permisos Git
    Setup saved. Enjoy exploring!|Einrichtung gespeichert. Viel Spaß beim Entdecken!|Configuración guardada. ¡Disfruta explorando!
    Choose a provider|Anbieter wählen|Elegir un proveedor
    Other compatible API|Andere kompatible API|Otra API compatible
    Load models|Modelle laden|Cargar modelos
    Model and server options|Modell- und Serveroptionen|Opciones del modelo y del servidor
    Enter your details, then load models or type a model ID directly. A local API may work without a key.|Geben Sie Ihre Daten ein. Laden Sie dann Modelle oder geben Sie eine Modell-ID direkt ein. Eine lokale API kann ohne Schlüssel funktionieren.|Introduce tus datos y carga los modelos o escribe su identificador. Una API local puede funcionar sin clave.
    Finding models…|Modelle werden gesucht…|Buscando modelos…
    Connected, no models found. You can enter a model ID manually.|Verbunden, aber keine Modelle gefunden. Sie können eine Modell-ID manuell eingeben.|Conectado, sin modelos encontrados. Puedes introducir el identificador manualmente.
    Search cancelled or timed out. You can retry.|Suche abgebrochen oder Zeitlimit erreicht. Sie können es erneut versuchen.|Búsqueda cancelada o tiempo agotado. Puedes volver a intentarlo.
    Providers|Anbieter|Proveedores
    Settings|Einstellungen|Ajustes
    ⚙  Settings|⚙  Einstellungen|⚙  Ajustes
    Skills|Skills|Skills
    Beta|Beta|Beta
    Beta skill|Skill in Betaversion|Skill en versión beta
    Skill details|Skill-Details|Detalles del skill
    Skill details ·|Skill-Details ·|Detalles del skill ·
    How it works|Funktionsweise|Funcionamiento
    Design|Konzeption|Diseño
    Settings and permissions|Einstellungen und Berechtigungen|Ajustes y permisos
    Getting started|Erste Schritte|Primeros pasos
    Updates|Aktualisierungen|Actualizaciones
    Scope and permissions|Umfang und Berechtigungen|Alcance y permisos
    Skill instructions|Skill-Anweisungen|Instrucciones del skill
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
    Two-line application name|Anwendungsname in zwei Zeilen|Nombre de la aplicación en dos líneas
    The first word is the title, with the rest as a smaller subtitle. Names without spaces stay on one line.|Das erste Wort ist der Titel, der Rest ein kleinerer Untertitel. Namen ohne Leerzeichen bleiben in einer Zeile.|La primera palabra es el título y el resto, un subtítulo más pequeño. Los nombres sin espacios permanecen en una línea.
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
    Model and thinking|Modell und Denkniveau|Modelo y razonamiento
    Thinking level|Denkniveau|Nivel de razonamiento
    Thinking:|Denkniveau:|Razonamiento:
    Choose a model|Modell wählen|Elegir un modelo
    Choose model and thinking|Modell und Denkniveau wählen|Elegir modelo y razonamiento
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
    Automatic updates|Automatische Updates|Actualizaciones automáticas
    Notify only|Nur informieren|Solo informar
    Install automatically|Automatisch installieren|Instalar automáticamente
    Update|Aktualisieren|Actualizar
    Updating…|Wird aktualisiert…|Actualizando…
    Disabled: no automatic checks. Otherwise, check at startup and every 2 hours. Automatic installation waits for agents, drafts and editing windows to finish.|Deaktiviert: keine automatischen Prüfungen. Sonst beim Start und alle 2 Stunden prüfen. Die automatische Installation wartet, bis Agenten, Entwürfe und Bearbeitungsfenster geschlossen sind.|Desactivadas: sin comprobaciones automáticas. Si se activan, se comprueba al iniciar y cada 2 horas. La instalación automática espera a que terminen los agentes y borradores y se cierren las ventanas de edición.
    Installation requires a standalone Windows release.|Die Installation erfordert eine eigenständige Windows-Version.|La instalación requiere una versión autónoma para Windows.
    Save or close settings before installing.|Vor der Installation Einstellungen speichern oder schließen.|Guarda o cierra los ajustes antes de instalar.
    Automatic installation failed:|Automatische Installation fehlgeschlagen:|La instalación automática ha fallado:
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
    Token consumption|Token-Verbrauch|Consumo de tokens
    Track tokens used by your chats, agents and tools.|Verfolgen Sie den Token-Verbrauch Ihrer Chats, Agenten und Werkzeuge.|Consulta los tokens utilizados por tus chats, agentes y herramientas.
    Period|Zeitraum|Periodo
    Today|Heute|Hoy
    3 days|3 Tage|3 días
    7 days|7 Tage|7 días
    30 days|30 Tage|30 días
    1 year|1 Jahr|1 año
    Activity|Verwendung|Uso
    All providers|Alle Anbieter|Todos los proveedores
    All models|Alle Modelle|Todos los modelos
    All activities|Alle Verwendungen|Todos los usos
    All projects|Alle Projekte|Todos los proyectos
    Total tokens|Tokens insgesamt|Tokens totales
    Input tokens|Eingabe-Tokens|Tokens de entrada
    Output tokens|Ausgabe-Tokens|Tokens de salida
    Recorded calls|Erfasste Aufrufe|Llamadas registradas
    Token timeline|Token-Verbrauch im Zeitverlauf|Evolución de tokens
    Tokens by model|Token-Verbrauch nach Modell|Tokens por modelo
    Input tokens include history sent again with each call. ≈ marks estimates. These figures cover this installation, not the provider's billing statement.|Eingabe-Tokens enthalten den bei jedem Aufruf erneut gesendeten Verlauf. ≈ kennzeichnet Schätzungen. Diese Zahlen gelten für diese Installation, nicht für die Abrechnung des Anbieters.|Los tokens de entrada incluyen el historial reenviado en cada llamada. ≈ indica una estimación. Estas cifras corresponden a esta instalación, no a la factura del proveedor.
    Completed at|Abgeschlossen am|Finalizada el
    Provider / model|Anbieter / Modell|Proveedor / modelo
    Project / chat|Projekt / Chat|Proyecto / chat
    Total|Gesamt|Total
    Measurement / status|Messung / Status|Medición / estado
    Call details|Details der Aufrufe|Detalle de las llamadas
    Copy details as CSV|Details als CSV kopieren|Copiar detalle como CSV
    Model not recorded|Modell nicht erfasst|Modelo no registrado
    local time|Ortszeit|hora local
    calls with estimates|Aufrufe mit Schätzungen|llamadas con estimaciones
    No usage recorded for these filters. New calls appear after they finish.|Für diese Filter ist kein Verbrauch erfasst. Neue Aufrufe erscheinen nach ihrem Abschluss.|No hay consumo registrado con estos filtros. Las nuevas llamadas aparecen al finalizar.
    Old measurements are imported with the available information; provider, model or counters may be missing.|Alte Messungen werden mit den verfügbaren Informationen übernommen; Anbieter, Modell oder Zähler können fehlen.|Las mediciones anteriores se importan con la información disponible; pueden faltar el proveedor, el modelo o los contadores.
    Old replies without dates excluded:|Ausgeschlossene alte Antworten ohne Datum:|Respuestas anteriores sin fecha excluidas:
    History · unknown provider|Verlauf · unbekannter Anbieter|Historial · proveedor desconocido
    Chats|Chats|Chats
    Naming|Titelgenerierung|Nombrado
    Compaction|Komprimierung|Compactación
    Translation|Übersetzung|Traducción
    Proofreading / rephrasing|Korrektur / Umformulierung|Corrección / reformulación
    Other models|Andere Modelle|Otros modelos
    No data for this period.|Keine Daten für diesen Zeitraum.|Sin datos para este periodo.
    History|Verlauf|Historial
    Reported|Gemeldet|Declarado
    Interrupted|Unterbrochen|Interrumpida
    Complete|Abgeschlossen|Finalizada
    No calls|Keine Aufrufe|Sin llamadas
    calls|Aufrufe|llamadas
    CSV details copied.|CSV-Details kopiert.|Detalle CSV copiado.
    Context is the information the model receives to prepare its response.|Der Kontext umfasst die Informationen, die das Modell zur Vorbereitung seiner Antwort erhält.|El contexto reúne la información que recibe el modelo para preparar su respuesta.
    Estimate|Schätzung|Estimación
    Reported by the provider|Vom Anbieter gemeldet|Declarado por el proveedor
    Context used|Genutzter Kontext|Contexto utilizado
    Available:|Verfügbar:|Disponible:
    Capacity:|Kapazität:|Capacidad:
    used|genutzt|utilizado
    Last call · reported tokens|Letzter Aufruf · gemeldete Tokens|Última llamada · tokens declarados
    Model input|Modelleingabe|Entrada del modelo
    Model output|Modellausgabe|Salida del modelo
    The provider has not reported this value yet.|Der Anbieter hat diesen Wert noch nicht gemeldet.|El proveedor aún no ha comunicado este valor.
    Active history · token estimates|Aktiver Verlauf · Token-Schätzungen|Historial activo · estimaciones de tokens
    Your messages|Ihre Nachrichten|Tus mensajes
    Tool results|Werkzeugergebnisse|Resultados de herramientas
    Compaction summary|Zusammenfassung der Komprimierung|Resumen de compactación
    Images (approx.)|Bilder (ungefähr)|Imágenes (aprox.)
    These estimates exclude system instructions and tool definitions. They may differ from the total.|Diese Schätzungen enthalten keine Systemanweisungen oder Werkzeugdefinitionen. Sie können vom Gesamtwert abweichen.|Estas estimaciones no incluyen las instrucciones del sistema ni las definiciones de herramientas. Pueden diferir del total.
    Automatic compaction at 95%: the model summarizes history to free up space. This may consume tokens.|Automatische Komprimierung bei 95 %: Das Modell fasst den Verlauf zusammen, um Platz zu schaffen. Dies kann Tokens verbrauchen.|Compactación automática al 95 %: el modelo resume el historial para liberar espacio. Esto puede consumir tokens.
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
    Compaction|Kontextkomprimierung|Compactación
    Free context, keep what matters|Kontext freigeben, Wichtiges behalten|Liberar contexto, conservar lo importante
    Automatic compaction|Automatische Kontextkomprimierung|Compactación automática
    Trigger threshold (% of context)|Auslöseschwelle (% des Kontexts)|Umbral de activación (% del contexto)
    Target after compaction (% of context)|Ziel nach der Komprimierung (% des Kontexts)|Objetivo tras la compactación (% del contexto)
    Compaction strength|Komprimierungsstärke|Intensidad de compactación
    Gentle · more detail|Sanft · mehr Details|Suave · más detalles
    Balanced|Ausgewogen|Equilibrado
    Strong · very concise|Stark · sehr knapp|Fuerte · muy conciso
    Custom|Benutzerdefiniert|Personalizado
    Desired summary size (% of compacted text)|Gewünschte Zusammenfassungsgröße (% des komprimierten Texts)|Tamaño deseado del resumen (% del texto compactado)
    Maximum summary budget (estimated tokens)|Maximales Budget der Zusammenfassung (geschätzte Tokens)|Presupuesto máximo del resumen (tokens estimados)
    Custom instructions|Eigene Anweisungen|Instrucciones personalizadas
    Compaction strategy|Komprimierungsstrategie|Estrategia de compactación
    Keep recent exchanges|Neueste Nachrichten behalten|Conservar los intercambios recientes
    Summarize the entire history|Gesamten Verlauf zusammenfassen|Resumir todo el historial
    Reduce tool exchanges first|Zuerst Werkzeugaufrufe zusammenfassen|Reducir primero los intercambios con herramientas
    Recent exchanges to prioritize|Bevorzugt zu behaltende aktuelle Nachrichten|Intercambios recientes prioritarios
    Restore compaction defaults|Standardeinstellungen der Komprimierung wiederherstellen|Restablecer los ajustes de compactación
    Choose summary size, token budget and your own instructions.|Wählen Sie Größe, Tokenbudget und eigene Anweisungen.|Elige el tamaño, el presupuesto de tokens y tus propias instrucciones.
    Enter whole numbers within the displayed limits.|Geben Sie ganze Zahlen innerhalb der angezeigten Grenzen ein.|Introduce números enteros dentro de los límites indicados.
    The target after compaction must be below the trigger threshold.|Das Ziel nach der Komprimierung muss unter der Auslöseschwelle liegen.|El objetivo tras la compactación debe ser inferior al umbral de activación.
    Automatic compaction disabled. Configure in Settings > Compaction.|Automatische Komprimierung deaktiviert. Einstellbar unter Einstellungen > Kontextkomprimierung.|Compactación automática desactivada. Configúrala en Ajustes > Compactación.
    Insufficient summary reduction: original history preserved.|Zusammenfassung nicht ausreichend reduziert: ursprünglicher Verlauf bleibt erhalten.|Reducción insuficiente del resumen: se conserva el historial original.
    Compaction target not met: original history preserved.|Komprimierungsziel nicht erreicht: ursprünglicher Verlauf bleibt erhalten.|No se alcanzó el objetivo de compactación: se conserva el historial original.
    Compaction target cannot be met: the current request or instructions are too large.|Komprimierungsziel nicht erreichbar: aktuelle Anfrage oder Anweisungen sind zu umfangreich.|No se puede alcanzar el objetivo: la petición actual o las instrucciones son demasiado grandes.
    Invalid native tool summary: original preserved.|Ungültige Zusammenfassung nativer Werkzeuge: Original bleibt erhalten.|Resumen de herramientas nativas no válido: se conserva el original.
    These settings apply to subsequent requests in the GUI, CLI and subagents. Compaction uses the selected model and consumes tokens. Original messages remain available in the conversation.|Diese Einstellungen gelten für nachfolgende Anfragen in GUI, CLI und Unteragenten. Die Komprimierung nutzt das gewählte Modell und verbraucht Tokens. Originalnachrichten bleiben im Chat verfügbar.|Estos ajustes se aplican a las próximas peticiones en la GUI, la CLI y los subagentes. La compactación usa el modelo seleccionado y consume tokens. Los mensajes originales siguen disponibles en la conversación.
    Create a summary when context usage reaches your threshold. Manual compaction remains available.|Erstellt eine Zusammenfassung, wenn die Kontextnutzung den Schwellenwert erreicht. Manuelle Komprimierung bleibt verfügbar.|Crea un resumen cuando el uso del contexto alcanza el umbral. La compactación manual sigue disponible.
    Example: preserve decisions and paths; group tool results by file; end with remaining tasks.|Beispiel: Entscheidungen und Pfade behalten; Werkzeugergebnisse nach Datei gruppieren; offene Aufgaben am Ende aufführen.|Ejemplo: conservar decisiones y rutas; agrupar resultados por archivo; terminar con las tareas pendientes.
    The budget is reduced if needed to meet the overall target. Requirements and unfinished work are always preserved.|Das Budget wird bei Bedarf reduziert, um das Gesamtziel zu erreichen. Anforderungen und offene Arbeiten werden stets berücksichtigt.|El presupuesto se reduce si es necesario para alcanzar el objetivo global. Se mantienen los requisitos y las tareas pendientes.
    Keep more explanations and examples. Desired summary: 40% of the source, up to 4,000 estimated tokens.|Behält mehr Erklärungen und Beispiele. Angestrebte Zusammenfassung: 40 % des Ausgangstexts, bis zu 4.000 geschätzte Tokens.|Conserva más explicaciones y ejemplos. Resumen deseado: 40 % del texto, hasta 4.000 tokens estimados.
    Favor short notes: constraints, decisions, results and next actions. Desired summary: 10%, up to 1,000 estimated tokens.|Bevorzugt kurze Notizen: Vorgaben, Entscheidungen, Ergebnisse und nächste Schritte. Angestrebte Zusammenfassung: 10 %, bis zu 1.000 geschätzte Tokens.|Prioriza notas breves: restricciones, decisiones, resultados y próximos pasos. Resumen deseado: 10 %, hasta 1.000 tokens estimados.
    Keep useful technical facts and remove repetition. Desired summary: 20%, up to 2,000 estimated tokens.|Behält nützliche technische Fakten und entfernt Wiederholungen. Angestrebte Zusammenfassung: 20 %, bis zu 2.000 geschätzte Tokens.|Conserva los datos técnicos útiles y elimina repeticiones. Resumen deseado: 20 %, hasta 2.000 tokens estimados.
    Summarize all available exchanges while keeping the current request intact during automatic compaction.|Fasst alle verfügbaren Nachrichten zusammen; die aktuelle Anfrage bleibt bei automatischer Komprimierung unverändert.|Resume todos los intercambios disponibles y conserva íntegra la petición actual durante la compactación automática.
    Summarize older tool calls and results first, then other exchanges if needed to reach the target.|Fasst zuerst ältere Werkzeugaufrufe und Ergebnisse zusammen, dann bei Bedarf andere Nachrichten, um das Ziel zu erreichen.|Resume primero las llamadas y los resultados antiguos de herramientas y después otros intercambios si es necesario para alcanzar el objetivo.
    Summarize oldest exchanges and prioritize recent ones. Some recent exchanges may also be summarized if needed to reach the target.|Fasst die ältesten Nachrichten zusammen und bevorzugt die neuesten. Falls nötig, werden auch einige neuere Nachrichten zusammengefasst.|Resume los intercambios más antiguos y prioriza los recientes. Algunos recientes también pueden resumirse si el objetivo lo exige.
    The current request stays intact in automatic mode. If it alone exceeds the target, history is preserved and the target cannot be met. Large histories are summarized in segments without silent truncation.|Die aktuelle Anfrage bleibt im automatischen Modus unverändert. Überschreitet sie allein das Ziel, bleibt der Verlauf erhalten. Große Verläufe werden abschnittsweise ohne unbemerkte Kürzung zusammengefasst.|La petición actual se conserva íntegra en modo automático. Si por sí sola supera el objetivo, se conserva el historial. Los historiales extensos se resumen por segmentos sin recortes silenciosos.
    Budgets and final context size are estimated. OpenCode manages its subagent sessions' internal compaction.|Budgets und endgültige Kontextgröße sind Schätzungen. OpenCode verwaltet die interne Komprimierung seiner Unteragentensitzungen.|Los presupuestos y el tamaño final del contexto son estimados. OpenCode gestiona la compactación interna de las sesiones de sus subagentes.
    Trigger at {0:0}% → target {1:0}%. The automatic target includes instructions, tools, summary and retained exchanges.|Auslösung bei {0:0} % → Ziel {1:0} %. Das automatische Ziel umfasst Anweisungen, Werkzeuge, Zusammenfassung und beibehaltene Nachrichten.|Activación al {0:0} % → objetivo {1:0} %. El objetivo automático incluye instrucciones, herramientas, resumen e intercambios conservados.
    Automatic compaction disabled. Manual history target: at most {0:0}%.|Automatische Komprimierung deaktiviert. Ziel für den manuell komprimierten Verlauf: höchstens {0:0} %.|Compactación automática desactivada. Objetivo del historial manual: como máximo {0:0} %.
    Automatic compaction at {0}% · target {1}%. Configure in Settings > Compaction. This operation consumes tokens.|Automatische Komprimierung bei {0} % · Ziel {1} %. Einstellbar unter Einstellungen > Kontextkomprimierung. Dieser Vorgang verbraucht Tokens.|Compactación automática al {0} % · objetivo {1} %. Configúrala en Ajustes > Compactación. Esta operación consume tokens.
    Reused tokens (cache):|Wiederverwendete Tokens (Cache):|Tokens reutilizados (caché):
    Reused from cache|Aus dem Cache wiederverwendet|Reutilizados de la caché
    Not reported|Nicht gemeldet|No comunicado
    Input tokens reused from the cache, reported by the provider. They remain part of the context.|Vom Anbieter gemeldete, aus dem Cache wiederverwendete Eingabe-Tokens. Sie bleiben Teil des Kontexts.|Tokens de entrada reutilizados de la caché, comunicados por el proveedor. Siguen formando parte del contexto.
    The provider has not reported reused tokens. This value is not estimated.|Der Anbieter hat keine wiederverwendeten Tokens gemeldet. Dieser Wert wird nicht geschätzt.|El proveedor no ha comunicado los tokens reutilizados. Este valor no se estima.
    Search models or providers…|Modelle oder Anbieter suchen…|Buscar modelos o proveedores…
    Search models|Modelle suchen|Buscar modelos
    Show enabled models|Aktivierte Modelle anzeigen|Mostrar modelos activados
    Enable models in Settings > Providers.|Aktiviere Modelle unter Einstellungen > Anbieter.|Activa modelos en Ajustes > Proveedores.
    {0} enabled models · choose a suggestion or press Enter.|{0} aktivierte Modelle · Vorschlag auswählen oder Eingabetaste drücken.|{0} modelos activados · elige una sugerencia o pulsa Intro.
    No matching model. Try another name or provider.|Kein passendes Modell. Versuche einen anderen Namen oder Anbieter.|No hay modelos coincidentes. Prueba otro nombre o proveedor.
    {0} matching models|{0} passende Modelle|{0} modelos coincidentes
    · Showing 50 suggestions; refine your search.|· Es werden 50 Vorschläge angezeigt; grenze die Suche ein.|· Se muestran 50 sugerencias; precisa la búsqueda.
    Refresh models|Modelle aktualisieren|Actualizar modelos
    Find your model and adjust thinking for your task.|Finde dein Modell und passe das Denkniveau an deine Aufgabe an.|Encuentra tu modelo y ajusta el razonamiento a tu tarea.
    Choices saved immediately.|Auswahl wird sofort gespeichert.|Las opciones se guardan de inmediato.
    No model selected|Kein Modell ausgewählt|Ningún modelo seleccionado
    Requests brief thinking to favor speed.|Fordert kurzes Nachdenken für höhere Geschwindigkeit an.|Solicita un razonamiento breve para priorizar la rapidez.
    Requests a balance between thinking and speed.|Fordert ein Gleichgewicht zwischen Nachdenken und Geschwindigkeit an.|Solicita un equilibrio entre razonamiento y rapidez.
    Requests deeper thinking for complex tasks.|Fordert tieferes Nachdenken für komplexe Aufgaben an.|Solicita un razonamiento profundo para tareas complejas.
    Requests a response without extended reasoning.|Fordert eine Antwort ohne erweitertes Nachdenken an.|Solicita una respuesta sin razonamiento extendido.
    Uses the model's default thinking level.|Verwendet das voreingestellte Denkniveau des Modells.|Utiliza el nivel de razonamiento predeterminado del modelo.
    Support for thinking levels depends on the provider and model.|Die Unterstützung der Denkniveaus hängt vom Anbieter und Modell ab.|La compatibilidad de los niveles de razonamiento depende del proveedor y del modelo.
    Refreshing models…|Modelle werden aktualisiert…|Actualizando modelos…
    {0} models available for {1}.|{0} Modelle für {1} verfügbar.|{0} modelos disponibles para {1}.
    ← Parent conversation|← Übergeordnete Unterhaltung|← Conversación principal
    Preparing task plan…|Aufgabenplan wird erstellt…|Preparando el plan de tareas…
    Task plan not provided|Kein Aufgabenplan mitgeteilt|Plan de tareas no proporcionado
    tasks completed|Aufgaben abgeschlossen|tareas completadas
    cancelled|abgebrochen|canceladas
    This agent's plan|Plan dieses Agenten|Plan de este agente
    Task plan updated|Aufgabenplan aktualisiert|Plan de tareas actualizado
    Completed|Abgeschlossen|Completada
    In progress|In Bearbeitung|En curso
    Limit reached|Grenze erreicht|Límite alcanzado
    Task|Aufgabe|Tarea
    Tool completed|Werkzeug abgeschlossen|Herramienta finalizada
    Compacting context|Kontext wird komprimiert|Compactando el contexto
    """;
}
