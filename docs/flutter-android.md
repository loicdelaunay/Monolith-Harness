# Monolith sur Android avec Flutter

Le projet `src/MonolithHarness.App.Flutter` réécrit l’interface et le moteur mobile en Dart. Il utilise les composants Material du SDK : navigation, tiroir latéral, listes réorganisables, dialogues, menus, champs, boutons, sliders et switches. Les bibliothèques existantes prennent en charge SQLite, le coffre Android, les pièces jointes, Markdown, HTML et WebView.

L’interface desktop .NET Uno et les moteurs Core et Core.Desktop n’ont aucune dépendance Flutter. L’ancien projet Android Uno, son moteur Core.Mobile et ses tests dédiés sont retirés du code courant ; leurs versions historiques restent accessibles dans les anciens tags Git. La nouvelle application a sa propre base et ses propres clés : aucune migration ni synchronisation automatique entre les applications n’est prévue dans cette version.

## Fonctionnalités

| Fonction de la version Android existante | Flutter |
|---|---|
| Projets et discussions | Sidebar, création, renommage, suppression et réorganisation |
| Fournisseurs compatibles OpenAI | URL HTTPS, clé sécurisée, modèle, détection des modèles |
| Conversations | Historique SQLite, brouillons, streaming, interruption et raisonnement optionnel |
| Images jointes | PNG, JPEG, GIF ou WebP ; maximum 4 images de 8 Mo par message |
| Documents | Import UTF-8/UTF-16, liste, lecture, recherche, écriture, édition et export de copies privées |
| Skills | Synthèse, Planification, Revue de code, Informations, Recherche web, Fichiers, Mémoire, Create web app |
| Autocomplétion | `/skill` complète le texte avec un espace ; instructions renforcées uniquement à l’envoi |
| Autorisations | Refuser tout / Demander tout / Autoriser tout ; limites web, fichiers et mémoire partagée indépendantes |
| Appels d’outils | Une bulle compacte par tour, mise à jour pendant l’exécution et conservée dans l’historique |
| Mémoire | Portées discussion, projet et partagée optionnelle ; consultation et suppression |
| Markdown | Titres, listes, tableaux, blocs de code, liens et images |
| HTML/CSS | Carte native dans le chat, source dépliable et aperçu en plein écran |
| JavaScript / graphiques | Aperçu isolé, copies locales de Tailwind CSS 4.3.3 et Chart.js 4.5.1, toggles séparés |
| Thème | Couleurs Android, clair/sombre/système, taille de lecture configurable |

## Options des messages et activité du modèle

Un appui long sur une bulle ouvre ses options pour copier le message. Sur le texte, le menu de sélection propose la copie du message entier et celle de la sélection. L’icône de copie n’occupe plus l’en-tête des bulles.

Pendant la réflexion, l’étape la plus récente reçue du modèle remplace le libellé générique au-dessus de la saisie et dans la section de raisonnement active. Le texte est limité pour rester lisible ; si le fournisseur ne transmet pas encore d’étape, un statut d’attente reste affiché.

## Paramètres

- **Général** : langue des réponses, thème et taille du texte.
- **Chat** : outils, raisonnement, bibliothèques HTML et limite des tours d’outils.
- **Fournisseurs** : ajout, modification, détection des modèles et suppression.
- **Skills** : activation individuelle et catégories d’informations du téléphone.
- **Autorisations** : politique globale et restrictions indépendantes.
- **Mémoire** : consultation, recherche et gestion des souvenirs visibles.
- **À propos** : version, périmètre mobile et licences.

## Données et accès

Les clés API sont stockées séparément de SQLite par `flutter_secure_storage` dans le coffre Android. La sauvegarde Android de l’application est désactivée. Les paramètres et messages ne contiennent pas les clés du fournisseur.

Le sélecteur Android importe une copie des documents texte dans les données privées de la discussion. Les outils n’accèdent pas directement aux originaux ni à d’autres dossiers du téléphone. Les chemins absolus, les traversées `..` et les liens symboliques sont refusés. Un document est limité à 1 Mo.

Les requêtes du chat vont au fournisseur choisi, avec les messages et les résultats d’outils nécessaires. La recherche web autorise seulement les URL HTTPS publiques et refuse les adresses privées, même après résolution DNS ou redirection. Les liens Markdown sont ouverts à l’initiative de l’utilisateur.

Les cartes HTML du chat sont rendues avec un widget natif sans JavaScript. Le plein écran emploie une WebView avec un document isolé dans une iframe sans accès à l’application par pont JavaScript, aux fichiers, aux cookies du parent ni au réseau. Tailwind et Chart.js sont injectés depuis les assets locaux ; leurs options ne nécessitent aucun CDN. Fermer l’aperçu réinitialise l’état de la page et conserve sa carte dans le chat.

## Limites

Les modèles locaux, le terminal, le contrôle du téléphone/du desktop, MCP, les documents PDF/Office, la revue des propositions, Mermaid, le rendu LaTeX et la synchronisation desktop/mobile ne sont pas ajoutés par ce portage. Ces fonctions ne faisaient pas partie du périmètre opérationnel de l’application Android Uno. Les documents HTML interactifs requièrent Android System WebView ; les autres écrans et le Markdown utilisent Flutter.

Le fichier de signature livré par le modèle Flutter est réservé au développement. La configuration de publication et la migration de données pourront être ajoutées lors d’une demande de distribution. Aucun workflow GitHub ni release n’est créé par cette mise en œuvre locale.

## Construction et essais

Voir les commandes du [README du projet](../src/MonolithHarness.App.Flutter/README.md). Le fichier de verrouillage des dépendances est conservé. La compilation incrémentale Kotlin est désactivée dans ce projet pour éviter un défaut de cache Windows lorsque le projet et le cache Pub sont sur des disques différents.

Les cibles Android situées dans `integration_test/` et les lancements par `flutter test` utilisent automatiquement **Monolith QA** (`com.monolithharness.flutter.qa`), avec leur propre stockage et leurs propres clés. La configuration reconnaît aussi le marqueur du lanceur de tests Flutter, dont la cible générée peut se trouver hors du dossier `integration_test/`. Chaque scénario vérifie son identifiant avant de commencer. Utiliser également `--no-uninstall` pour désactiver la désinstallation automatique de fin de test.

Les tests couvrent les autorisations avant effet, la portée des fichiers et de la mémoire, les trames de streaming fragmentées, la séparation des documents HTML, l’arrêt et la reprise des outils, l’autocomplétion, les pièces jointes et le responsive. Les tests Android utilisent des données synthétiques isolées pour vérifier aussi les composants natifs. Les résultats exacts du contrôle sur Pixel figurent dans le [rapport de validation du 11 octobre 2026](rapport-flutter-android-2026-10-11.md).

Le contrôle sur Pixel 9 Pro Fold et Lenovo Tab P11 Pro, avec captures en portrait et paysage, est documenté dans le [rapport responsive du 11 octobre 2026](rapport-responsive-flutter-fold-tablet-2026-10-11.md). Le scénario `integration_test/responsive_device_test.dart` vérifie aussi le clavier, les menus et la conservation du brouillon entre les écrans du Fold. Les tests de widgets couvrent les mêmes tailles et les menus avec du texte agrandi.

## Publication signée

Le workflow GitHub `Release packages` compile Flutter 3.47.1 et signe l’APK ARM64 avec les quatre secrets Android déjà configurés. Les secrets ne sont jamais écrits dans le dépôt. Le téléchargement est nommé `MonolithHarness-Flutter-vVERSION-android-arm64.apk` et possède une empreinte SHA-256.

Pour une publication locale durable, renseigner `ANDROID_KEYSTORE_PATH`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` et `ANDROID_STORE_PASSWORD` dans l’environnement privé, puis appeler `./publish-android.ps1 -RequireReleaseSigning`. Une variable manquante interrompt la publication ; les builds de développement ordinaires restent possibles avec leur signature de développement.
