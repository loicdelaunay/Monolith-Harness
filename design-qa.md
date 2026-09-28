# Refonte Fluent — branche design

## Périmètre et référence

Refonte de l’application existante : chat, composition, navigation des réglages et cartes de skills. Les contrôles WinUI natifs, les actions et la persistance sont conservés.

Référence utilisateur : `C:/Users/le_ma/AppData/Local/Temp/codex-clipboard-e2ef99a7-e36e-4383-b0ce-1a769f031d0c.png`, 3432 × 1366 pixels, vue générale avec les réglages Skills ouverts. Direction : [Fluent pour Windows](https://learn.microsoft.com/en-us/windows/apps/design/) et [surfaces Mica](https://learn.microsoft.com/en-us/windows/apps/design/style/mica).

Il s’agit d’une amélioration de cette interface, pas d’une reproduction pixel par pixel de la capture. Les fenêtres de test et le contenu diffèrent de la référence ; la comparaison porte sur la hiérarchie, les contrôles, les marges et la lisibilité. Les couleurs Mica varient avec le fond Windows.

## Preuves visuelles

Captures locales dans `artifacts/design/` (non versionnées) :

| Capture | État inspecté | Dimensions capturées |
| --- | --- | --- |
| `winui-chat.jpg` | Chat, barre de titre, métriques sur deux lignes, composition | 1139 × 746 |
| `winui-general.jpg` | Navigation Fluent, trois cartes, actions Enregistrer/Annuler | 787 × 666 |
| `winui-skills.jpg` | Skills personnalisés repliés, toggles navigateur/DOM/RAG | 787 × 666 |
| `electron-chat.png` | Chat avec raisonnement et sous-agent terminé | 1860 × 1122 |
| `electron-skills.png` | Skills avec réglages RAG dépliés | 1859 × 1122 |
| `electron-skills-compact.png` | Réglages dans une fenêtre de 900 × 700 DIP | 1109 × 797 |

WinUI : fenêtre principale demandée à 1440 × 940 pixels, réglages à 1000 × 840 pixels ; captures normalisées par l’outil Windows, pas des pixels CSS. Electron : fenêtre normale de 1500 × 960 DIP et compacte de 900 × 700 DIP ; captures du contenu à l’échelle de l’écran Windows (~1,25), sans le cadre système. La normalisation et les cadres différents interdisent une mesure pixel à pixel avec la référence. Aucune retouche des captures.

Les vues complètes ont été inspectées, puis les régions navigation/cartes, titre et métriques comparées avant/après. Des recadrages supplémentaires n’étaient pas nécessaires : ces régions sont visibles à une taille lisible dans les captures natives.

## Itérations et constats

- **P2 corrigé — barre de titre** : le premier rendu utilisait une couleur transparente pour les boutons système, produisant un rectangle blanc. Une surface sombre opaque a remplacé cette couleur ; le rendu final affiche correctement réduire, agrandir et fermer.
- **P2 corrigé — sélecteur de modèle** : la barre de métriques sur une ligne comprimait le nom du modèle. Sous 920 unités de largeur disponible, le modèle occupe la première ligne et débit/contexte passent sur la suivante. Le nom complet est visible dans `winui-chat.jpg`.
- **P2 corrigé — densité des skills** : les instructions du dossier et les espacements des cartes prenaient trop de place. Instructions regroupées dans un Expander, espaces vides des lignes supprimés. Résultat dans `winui-skills.jpg`.
- **Vérification Electron** : absence de débordement horizontal dans le contenu des réglages à 900 × 700 DIP, y compris avec RAG déplié. Les captures attendent deux frames de rendu pour éviter une image de l’écran précédent.

## Surfaces évaluées

- Typographie : hiérarchie 28/24/14/12 dans les réglages natifs, composition 15, icônes Segoe Fluent ; police système WinUI conservée. Electron utilise Segoe UI Variable/Segoe UI et les polices système de repli. Les textes secondaires restent distincts des titres ; noms longs et descriptions peuvent revenir à la ligne.
- Espacement : navigation latérale intégrée, cartes de rayon 8, marges de 16/24, boutons principaux visibles ; colonne de lecture bornée à 1120 unités. Les contrôles gardent leurs comportements natifs.
- Couleurs : ressources de surfaces/texte/bordures WinUI ; fond Mica visible, états sémantiques des outils conservés. Electron utilise des gris neutres et un accent bleu clair. Les rendus finaux ont été inspectés visuellement ; pas d’audit WCAG exhaustif.
- Images/icônes : logo natif existant conservé ; commandes principales et navigation utilisant la police d’icônes Microsoft. Pas d’illustration générée ni de logo redessiné. Les icônes historiques des outils/contenus Electron restent en place.
- Texte : champs RAG et navigateur natifs localisés selon la langue active ; champs d’API masqués en mode RAG local. Certains anciens formulaires ont encore leurs libellés bilingues, hors de cette passe visuelle.

## Validation et limites

- Compilation WinUI Release : réussie, zéro avertissement et zéro erreur.
- Publication standalone : `artifacts/release/win-x64/OhMyHarness.App.exe`. `artifacts/official` n’a pas été modifié.
- Smoke Electron : réussi, comprenant les conversations concurrentes avec réglages ouverts, le Markdown, le scroll, les sous-agents, les modèles composés, MCP, le navigateur, Git, les terminaux et l’export. L’option `OHMYHARNESS_DESIGN_QA=1` ajoute les captures et le contrôle de débordement.
- WinUI : démarrage réel et inspection du chat, de Général et de Skills. Navigation et fermeture sans enregistrer contrôlées. Les réglages utilisent toujours une fenêtre indépendante des agents.
- macOS, contraste élevé, grossissement du texte et très petites fenêtres natives non vérifiés en session réelle. Le responsive Electron a été vérifié ; le mode compact natif est implémenté mais nécessite encore un essai à cette largeur.

Résultat : refonte visuelle validée sur les états Windows inspectés et les parcours Electron testés. Les limites ci-dessus restent explicites ; aucune certification de conformité Fluent ou d’accessibilité complète n’est revendiquée.

## Outils dédiés — 1.19.0 — 2026-09-27

final result: passed

### Références et périmètre

- Traducteur : `C:/Users/le_ma/AppData/Local/Temp/codex-clipboard-4d3b7610-3a7f-4d10-b5db-e011da8d5986.png`.
- Correcteur : `C:/Users/le_ma/AppData/Local/Temp/codex-clipboard-a20d4894-1506-4c27-9384-a03854cedbf2.png`.
- Implémentation : fenêtres natives Uno Desktop, adaptation demandée au style de l’application. Les références et captures finales ont été ouvertes ensemble pour la comparaison. Les cadres et hauteurs diffèrent des captures Web : comparaison des régions et parcours, sans prétention de reproduction au pixel près.
- Captures conservées dans `C:/Users/le_ma/.codex/visualizations/2026/09/23/01a0cecb-c3d1-78a3-9df4-2f9486311f2e/model-tools-1.19.0/` : `translator-light.png`, `translator-dark.png`, `proofreader-light.png`, `proofreader-dark.png`, `benchmark-light.png`, `benchmark-dark.png`, variantes `*-narrow.png` et `tools-menu-location.png`.

### Itérations

- P2 corrigé : le premier traducteur présentait des sélecteurs de langue coupés par le défilement. La hauteur initiale et les contraintes du panneau ont été corrigées ; les deux sélecteurs sont entièrement visibles dans `translator-light.png`. En largeur réduite, les panneaux passent en colonne avec défilement vertical, et les actions principales restent fixes.
- P2 corrigé : les cartes de correction initialement toutes développées cachaient une partie des suggestions. Les détails sont désormais repliables ; les trois propositions sont visibles dans `proofreader-light.png`, conformément à la structure de la maquette.
- Comportement corrigé : les événements de saisie différés d’Uno ne suppriment plus une traduction après inversion ni les suggestions restantes après application d’une correction.

### Surfaces et contrôles

- Typographie : police et contrôles natifs existants, titres 26, texte éditable 18 et textes secondaires 12–14. Libellés et métriques lisibles dans les thèmes clair et sombre inspectés.
- Disposition : marges de 24, deux panneaux égaux pour la traduction, éditeur dominant et suggestions à droite pour le correcteur. Le bouton déroulant Outils est adjacent à Tâches planifiées. Les contrôles de modèle et les actions de lancement restent accessibles dans les captures à 720 pixels de largeur de fenêtre.
- Couleurs et assets : ressources Fluent de l’application, accent, fond et bordures existants ; icônes Segoe Fluent du menu. Aucune illustration à recréer dans les références. Pas de logos ou de services tiers ajoutés à l’application.
- Contenu : choix de modèle indépendant, langues, traduction, correction/reformulation, compteur de caractères, copie, lancement/annulation, scores et tokens explicitement estimés si nécessaire. Le benchmark possède une suite locale documentée, avec corrigé consultable.

### Validation et limites

- 33 contrôles ciblés : validation des corrections et décalages Unicode, modifications successives, parsing, calculs de débit/tokens, notation des épreuves, requêtes SSE, annulation et isolation des sessions OpenCode avec outils natifs/MCP désactivés.
- Scénario natif réussi avec modèle simulé : fenêtres indépendantes, conservation des brouillons, traduction/inversion, correction/application/ignore, invalidation après saisie, reformulation, sept épreuves, erreur fournisseur, annulation et fermeture.
- Compilation Uno Desktop sans avertissement. Les captures correspondent à des réponses simulées ; aucun appel payant ni résultat de qualité linguistique d’un modèle réel n’a été validé. macOS, WinUI natif, lecteur d’écran et toutes les tailles de fenêtre ne sont pas couverts par cette vérification.
- Aucun P0/P1/P2 restant dans le périmètre inspecté. Amélioration P3 possible : annotation simultanée de tous les passages dans l’éditeur, en complément de la sélection du passage d’une suggestion.

## Conversations, agents et notifications — 1.22.0 — 2026-09-28

final result: passed

### Référence et comparaison

- Référence : `C:/Users/le_ma/AppData/Local/Temp/codex-clipboard-2ffdb553-4006-47ad-9efc-5d63f25a1ffd.png`. Adaptation native Uno Desktop : dossiers de projets, conversations indentées, dates discrètes à droite, section Conversations distincte. Les commandes Tâches planifiées, Outils et Réglages restent accessibles en bas.
- Captures finales conservées dans `C:/Users/le_ma/.codex/visualizations/2026/09/23/01a0cecb-c3d1-78a3-9df4-2f9486311f2e/conversation-workspace-1.22.0/` : `sidebar.png`, `naming.png`, `notifications.png`, `agent-settings.png`, `notification-settings.png`, et résultat `smoke-ok.txt`.
- Comparaison de la hiérarchie et de la densité avec le mockup, avec données fictives et dimensions différentes ; aucune revendication de reproduction au pixel près. Le panneau de notifications a été capturé dans une surface de contrôle utilisant son véritable composant, sans reproduire le cadre complet des Réglages.

### Itérations

- Lignes de conversation resserrées, surfaces et bordures allégées, recherche déplacée derrière le filtre et ajout de conversation discret au survol des projets.
- Icône de notification corrigée en cloche (`EA8F`, Ringer), après contrôle de la [table Microsoft des icônes](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font).
- Indicateur de nommage remplacé par un arc natif animé : le premier contrôle était invisible dans le rendu Uno Desktop. L’arc et le libellé sont visibles dans la capture finale, dans le titre et la ligne du chat.
- Fenêtre des sous-agents : contenu défilant, actions Annuler/Enregistrer fixes en bas à droite, action principale accentuée. Les rôles manuels, compte automatique et presets sont rattachés à la conversation.

### Contrôles et limites

- 14 contrôles ciblés du moteur réussis : relances 503, budget et délai, absence de relance 401, annulation pendant l’attente, flux interrompu, persistance des préférences/presets, capture des réglages d’un envoi, rôles manuels, limites de délégation, forks et planification automatique des rôles.
- Parcours natif réussi sur l’exécutable final : repli/réouverture des projets et archives, nommage, notifications de fin/action requise, Shift+Enter, zoom local/global et enregistrement/suppression d’un preset avec rechargement de conversation.
- Publications GUI et CLI réussies ; versions des deux exécutables contrôlées à `1.22.0.0`. Vérification `git diff --check` sans erreur.
- Données fictives isolées sous `artifacts/TEMP` ; aucune requête vers un modèle payant. Son coupé pendant le parcours automatisé : l’écoute réelle reste à vérifier avec le bouton de préécoute. Les contrôles du zoom appellent le gestionnaire de taille ; le geste matériel de la molette n’a pas été simulé.
- Captures inspectées dans le thème sombre sous Windows. macOS, thème clair, lecteur d’écran et toutes les dimensions de fenêtre ne sont pas couverts par cette passe. Aucun P0/P1/P2 visuel restant dans les surfaces inspectées.
