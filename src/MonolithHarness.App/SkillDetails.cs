using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MonolithHarness.Core;
using static MonolithHarness.App.UiText;

namespace MonolithHarness.App;

public sealed partial class MainWindow
{
    readonly HashSet<XamlRoot> skillDetailRoots = [];
    static bool IsBetaSkill(SkillDefinition skill) => skill.Id is GitTools.SkillId or FileIndexTools.SkillId or ImageGenerationTools.SkillId;

    FrameworkElement SkillLabel(SkillDefinition skill, bool bold = false)
    {
        var row = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = WorkflowText(skill.FrenchName, skill.EnglishName), TextWrapping = TextWrapping.Wrap,
            FontSize = 14, FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = FluentDesign.Primary, VerticalAlignment = VerticalAlignment.Center });
        if (IsBetaSkill(skill))
        {
            var badge = new Border { CornerRadius = new(9), Padding = new(7, 2, 7, 2), VerticalAlignment = VerticalAlignment.Center,
                Background = FluentDesign.Resource("ControlSelectedBrush"), BorderBrush = FluentDesign.Resource("AccentFillColorDefaultBrush"), BorderThickness = new(1),
                Child = new TextBlock { Text = WorkflowText("Bêta", "Beta"), FontSize = 10, Foreground = FluentDesign.Primary } };
            ToolTipService.SetToolTip(badge, WorkflowText("Skill en version bêta", "Beta skill"));
            Grid.SetColumn(badge, 1); row.Children.Add(badge);
        }
        return row;
    }

    Button SkillInfoButton(SkillDefinition skill, Action? beforeOpen = null)
    {
        var button = new Button { Width = 30, Height = 30, MinWidth = 0, MinHeight = 0, Padding = new(0), CornerRadius = new(15),
            BorderThickness = new(0), Background = FluentDesign.Resource("TransparentBrush"), VerticalAlignment = VerticalAlignment.Center };
        FluentDesign.IconButton(button, "\uE946", WorkflowText("Détails du skill · ", "Skill details · ") + WorkflowText(skill.FrenchName, skill.EnglishName), false);
        button.Click += async (_, _) => await Guard(async () =>
        {
            var owner = button.XamlRoot ?? root.XamlRoot;
            beforeOpen?.Invoke();
            await Task.Yield();
            if (owner != null) await ShowSkillDetailsAsync(skill, owner);
        });
        return button;
    }

    Grid SkillChoiceRow(SkillDefinition skill, CheckBox check, Action? beforeOpen = null)
    {
        check.Content = SkillLabel(skill);
        check.HorizontalAlignment = HorizontalAlignment.Stretch;
        check.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        check.VerticalAlignment = VerticalAlignment.Center;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(check, WorkflowText(skill.FrenchName, skill.EnglishName));
        var row = new Grid { ColumnSpacing = 8, MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Stretch };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(check);
        var info = SkillInfoButton(skill, beforeOpen); Grid.SetColumn(info, 1); row.Children.Add(info);
        return row;
    }

    async Task ShowSkillDetailsAsync(SkillDefinition skill, XamlRoot owner)
    {
        if (!skillDetailRoots.Add(owner)) return;
        try
        {
            var body = new StackPanel { Spacing = 16, MaxWidth = 620 };
            void Section(string frenchTitle, string englishTitle, string french, string english)
            {
                var section = new StackPanel { Spacing = 6 };
                section.Children.Add(new TextBlock { Text = WorkflowText(frenchTitle, englishTitle), FontSize = 15,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = FluentDesign.Primary, TextWrapping = TextWrapping.Wrap });
                section.Children.Add(new TextBlock { Text = WorkflowText(french, english), FontSize = 14, TextWrapping = TextWrapping.Wrap,
                    Foreground = FluentDesign.Primary, IsTextSelectionEnabled = true });
                body.Children.Add(section);
            }
            body.Children.Add(SkillLabel(skill, bold: true));
            Section("Fonctionnement", "How it works", skill.FrenchDescription, skill.EnglishDescription);
            if (skill.Id == ImageGenerationTools.SkillId)
            {
                Section("Fonctionnement", "How it works", "Le modèle du chat appelle generate_image. Un fournisseur et un modèle dédiés, choisis dans Réglages → Skills, réalisent la génération. L’image est jointe au chat et conservée dans images/chat-…/.", "The chat model calls generate_image. A dedicated provider and model selected in Settings → Skills generate the image. It is attached to chat and saved in images/chat-…/.");
                Section("Conception et limites", "Design and limits", "Une image par appel, autorisation du chat, délai maximum de 15 minutes et annulation. API distante images/generations ou moteur stable-diffusion.cpp local. Cette bêta charge des checkpoints complets SD/SDXL ; les composants séparés et la retouche ne sont pas pris en charge. Le skill ne s’active pas automatiquement.", "One image per call, chat permissions, cancellation and a 15-minute timeout. Remote images/generations API or local stable-diffusion.cpp engine. This beta loads full SD/SDXL checkpoints; separate components and image editing are not supported. The skill is not automatically enabled.");
            }
            else if (skill.Id == "web")
            {
                Section("Lecture ciblée", "Targeted reading",
                    "En mode Smart, web_http_request conserve le résultat dans des fichiers propres à la conversation. Le modèle reçoit un aperçu, les lignes du sommaire et un identifiant. Il est invité à utiliser web_http_search puis web_http_read pour ne charger que les passages utiles et réutiliser le fichier lors des questions suivantes. Le HTML est transformé en texte lisible avec les liens, sans exécuter JavaScript ; la vue raw permet de lire le contenu brut par extraits.",
                    "In Smart mode, web_http_request saves the result in conversation-scoped files. The model receives a preview, outline line numbers and an identifier. It is instructed to use web_http_search then web_http_read to load only relevant passages and reuse the file for follow-up questions. HTML becomes readable text with links, without executing JavaScript; raw view reads excerpts of the original body.");
                Section("Réglages et conservation", "Settings and retention",
                    "Choisissez Smart ou Legacy dans Réglages → Skills → Recherche web. Smart est le mode recommandé et choisi par défaut pour économiser le contexte. Legacy charge le contenu et les en-têtes directement ; il est réservé à ce choix explicite. Le téléchargement est limité à 1 Mio et signale toute troncature. Dans Général → Entretien des conversations, Nettoyer les artefacts supprime le cache inutilisé après 7 jours par défaut. Une lecture prolonge la durée de conservation.",
                    "Choose Smart or Legacy in Settings → Skills → Web research. Smart is recommended and selected by default to save context. Legacy loads the body and headers directly and is reserved for that explicit choice. Downloads are limited to 1 MiB and report truncation. In General → Conversation maintenance, Clean tool artifacts deletes unused cache after 7 days by default. Reading extends retention.");
                Section("Autorisations", "Permissions",
                    "Les requêtes et redirections demandent les autorisations habituelles. La recherche et la lecture sont limitées aux résultats de la conversation ; tous les contenus web restent des données non fiables. Les outils HTTP sont désactivés en mode Plan et sandbox. Les sessions OpenCode utilisent leurs propres outils natifs.",
                    "Requests and redirects require the existing permissions. Search and reading are scoped to this conversation's results; all web content remains untrusted data. HTTP tools are disabled in Plan and sandbox mode. OpenCode sessions use their native tools.");
            }
            else if (skill.Id == GitTools.SkillId)
            {
                Section("Conception", "Design",
                    "Le modèle appelle des outils Git dédiés. L’application prépare les commandes et les exécute avec Git installé sur le PC, dans le dépôt associé aux sources de la conversation.\n\nLa lecture couvre l’état, les différences, l’historique et les branches. L’écriture couvre l’initialisation, les fichiers à indexer, les commits, les branches et les opérations fetch, pull et push. Chaque fichier à indexer est sélectionné explicitement.",
                    "The model calls dedicated Git tools. The app builds the commands and runs the Git installation on this computer, in the repository attached to the chat's sources.\n\nReading covers status, diffs, history and branches. Writing covers initialization, staging files, commits, branches, fetch, pull and push. Files to stage are selected explicitly.");
                Section("Réglages et autorisations", "Settings and permissions",
                    "La lecture et l’écriture s’activent séparément dans Réglages > Skills > Git. Une capacité désactivée est retirée des outils exposés et refusée à l’exécution.\n\nLes écritures et opérations réseau suivent vos autorisations. Autoriser un commit n’autorise pas un push. Le pull utilise uniquement une avance rapide. Les outils dédiés n’exposent ni push forcé, ni reset destructif, ni clean.",
                    "Reading and writing can be enabled independently in Settings > Skills > Git. A disabled capability is removed from exposed tools and rejected at execution.\n\nWrites and network operations follow your permissions. Approving a commit does not approve a push. Pull uses fast-forward only. Dedicated tools expose no forced push, destructive reset or clean.");
                Section("Pour commencer", "Getting started",
                    "Associez le dossier racine du dépôt, activez Git, puis demandez par exemple : « Montre les changements et propose un message de commit. » Le modèle est invité à examiner le dépôt avant de proposer une modification.",
                    "Attach the repository root, enable Git, then ask, for example: “Show the changes and suggest a commit message.” The model is instructed to inspect the repository before proposing a change.");
            }
            else if (skill.Id == FileIndexTools.SkillId)
            {
                Section("Conception", "Design",
                    "Le dossier choisi contient un fichier JSON portable nommé index.ohm. Chaque entrée décrit un fichier ou un dossier : chemin relatif, type, taille, date de modification, empreinte et description.\n\nL’application produit une description structurelle. Le modèle peut ensuite l’enrichir après avoir consulté les fichiers pertinents. Une empreinte permet de détecter les changements et de signaler une description du modèle devenue périmée.",
                    "The selected folder contains a portable JSON file named index.ohm. Each entry describes a file or folder: relative path, type, size, modification time, fingerprint and description.\n\nThe app generates structural descriptions. The model can enrich them after inspecting relevant files. Fingerprints detect changes and mark outdated model descriptions.");
                Section("Mise à jour", "Updates",
                    "file_index_update crée ou actualise l’index ; file_index_read recherche les chemins et descriptions avec pagination ; file_index_describe enrichit une description à partir de son empreinte actuelle.\n\nAvec la mise à jour automatique activée, les index existants sont réconciliés après les écritures des outils de fichiers. Les changements externes sont détectés à la prochaine consultation de l’index.",
                    "file_index_update creates or refreshes the index; file_index_read searches paths and descriptions with pagination; file_index_describe enriches a description using its current fingerprint.\n\nWith automatic updates enabled, existing indexes are reconciled after file-tool writes. External changes are detected at the next index read.");
                Section("Périmètre et autorisations", "Scope and permissions",
                    "L’index couvre les fichiers autorisés du dossier associé, avec une limite de 10 000 entrées et de 16 Mio par index. Les fichiers protégés et les liens symboliques sont exclus.\n\nCréer, actualiser ou enrichir l’index suit vos autorisations. En mode Plan, la lecture montre une vue actualisée en mémoire sans écrire sur le disque. Pour une très grande arborescence, choisissez un sous-dossier.",
                    "The index covers allowed files in the attached folder, with limits of 10,000 entries and 16 MiB per index. Protected files and symbolic links are excluded.\n\nCreating, refreshing or enriching the index follows your permissions. In Plan mode, reading provides a refreshed in-memory view without disk writes. Choose a subfolder for very large trees.");
            }
            else if (skill.Instruction.Length > 0)
            {
                Section("Conception", "Design",
                    "Ce skill ajoute une consigne spécialisée au contexte du modèle lorsqu’il est activé. Les capacités disponibles dépendent des outils exposés par l’application, du mode de la conversation et de vos autorisations. Le modèle applique cette consigne selon votre demande.",
                    "When enabled, this skill adds specialized instructions to the model's context. Available capabilities depend on the app's exposed tools, the chat mode and your permissions. The model applies these instructions according to your request.");
            }
            else
            {
                Section("Conception", "Design",
                    "Ce skill personnalisé est décrit par un fichier SKILL.md dans son dossier global ou dans .omh-ai/skills du projet. Sa description apparaît dans le catalogue ; le modèle consulte le fichier et ses ressources lorsqu’il utilise le skill. Ces instructions restent soumises au mode de la conversation et à vos autorisations.",
                    "This custom skill is defined by a SKILL.md file in its global folder or in the project's .omh-ai/skills folder. Its description appears in the catalog; the model reads the file and resources when using the skill. Those instructions remain subject to the chat mode and your permissions.");
            }
            if (skill.Instruction.Length > 0)
                body.Children.Add(new Expander { Header = WorkflowText("Consigne du skill", "Skill instructions"),
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = new TextBlock { Text = skill.Instruction, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
                        FontFamily = new FontFamily("Cascadia Code, Consolas"), FontSize = 12, Foreground = FluentDesign.Secondary } });
            var viewer = new ScrollViewer { Content = body, MaxHeight = Math.Clamp(owner.Size.Height * .65, 160, 560), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var dialog = new ContentDialog { XamlRoot = owner, Title = WorkflowText("Détails du skill", "Skill details"), Content = viewer,
                PrimaryButtonText = T("Fermer"), DefaultButton = ContentDialogButton.Primary };
            TextZoom.Apply(body);
            if (ReferenceEquals(owner, root.XamlRoot)) await ShowDialogAsync(dialog);
            else await dialog.ShowAsync();
        }
        finally { skillDetailRoots.Remove(owner); }
    }
}
