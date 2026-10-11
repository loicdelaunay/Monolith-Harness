# Validation de Monolith Flutter Android

Date : 11 octobre 2026. Version : **1.89.0+125**.

## Environnement

- Flutter 3.47.1 stable, Dart 3.13.1.
- SDK Android : compilation et cible API 36 ; Android 8/API 26 minimum.
- Appareil : Pixel 7 Pro, Android 17/API 37, connecté en USB.
- Identifiant : `com.monolithharness.flutter`. Installation distincte de l’application Uno.
- APK ARM64 optimisé : 23 640 773 octets, signé avec le certificat de développement du projet Flutter.

## Résultats

| Vérification | Résultat |
|---|---|
| Analyse Dart/Flutter | Aucune erreur ni avertissement |
| Tests unitaires et widgets | **85 tests réussis** |
| Scénario d’intégration Android | **1 scénario initial et 1 scénario dédié aux messages réussis** sur le Pixel |
| APK optimisé | Compilation, installation et lancement réussis |
| Fournisseur réel | DeepSeek : détection des modèles, streaming et réponse vérifiés |
| Persistance | Fournisseur et clé restaurés conservés après redémarrage et après le test isolé ; persistance des conversations couverte par les tests de stockage |
| Web réel sur Android | Deux lectures `web_read` et une recherche `web_search` terminées, avec le contenu de la page officielle Flutter et une réponse finale |
| Web hors téléphone | Lecture HTTPS de `docs.flutter.dev` et huit résultats de recherche publics |
| HTML interactif | Plein écran visible, Tailwind appliqué, graphique Chart.js et compteur cliquable |
| Assets locaux | Quatre empreintes SHA-256 conformes au manifeste |
| Publications desktop locales | GUI et CLI reconstruits et alignés en 1.89.0 |

La clé DeepSeek a été configurée dans le coffre du téléphone. Elle n’est incluse ni dans le code, ni dans les tests, ni dans l’APK.

## Couverture et corrections

Les tests de widgets couvrent les tailles 320×480, 360×640, 412×892, 892×412 et 768×1024, ainsi qu’un clavier ouvert et un texte agrandi. Ils vérifient le sélecteur de modèle, les pièces jointes, les paramètres et l’insertion d’un slash skill sans envoi du message.

La réorganisation est testée par deux gestes successifs. Le tooltip du bouton de déplacement capturait auparavant l’appui long : il s’ouvre maintenant au toucher pour laisser le geste de réorganisation fonctionner.

Le scénario Android utilise une base en mémoire, un fournisseur simulé et des documents temporaires. Il couvre les refus, demandes et autorisations, l’interruption, les projets et discussions, les réglages et l’ouverture puis la fermeture d’un aperçu HTML. Les essais réels avec DeepSeek sont séparés de ce scénario.

La WebView utilise la composition native adaptée au Pixel pour éviter un aperçu vide. Tailwind 4.3.3 et Chart.js 4.5.1 fonctionnent depuis les assets locaux ; le compteur interactif passe de 0 à 1 et la page reste isolée du parent.

Le transport web fixe la connexion sur une adresse publique validée puis effectue la négociation TLS avec le nom de domaine original. La vérification du certificat reste active, conformément à [SecureSocket.secure](https://api.dart.dev/dart-io/SecureSocket/secure.html). Les tests vérifient cette négociation, l’annulation et le refus d’une résolution DNS qui contient une adresse privée.

Les tests de stockage couvrent l’absence de réutilisation des identifiants après suppression, le nettoyage des documents et souvenirs de la discussion supprimée, la conservation des données voisines et la migration du schéma SQLite 1 vers 2. L’import initial et le premier envoi conservent le brouillon et les images. Les échanges d’outils incomplets après interruption sont exclus de l’historique transmis au fournisseur.

## Vérification des options de messages

Le scénario `integration_test/message_interaction_test.dart` vérifie sur le Pixel la disparition de l’icône de copie, le menu d’appui long, la copie du texte Markdown complet dans le presse-papiers Android et le menu de sélection du texte. Deux étapes de réflexion sont transmises par un flux API simulé : « Lecture des fichiers du projet », puis « Vérification des modifications ». Le titre de la bulle et le statut au-dessus de la saisie suivent ces étapes.

La capture a permis d’ajouter une marge système sous le menu pour que son action reste au-dessus des boutons de navigation Android. Un test de widget reproduit cette zone réservée. Ce contrôle n’utilise aucune conversation ni clé réelle.

## Isolation des tests et incident de données

Les premières exécutions du scénario supplémentaire ont utilisé l’identifiant de production. Le lanceur de tests Flutter 3.47.1 désinstalle l’application par défaut à leur fin ; cela a effacé les données de la nouvelle installation Flutter. Le fournisseur DeepSeek a été reconfiguré. L’historique Flutter effacé n’a pas été récupéré. L’application Android Uno précédente n’a pas été désinstallée ni modifiée.

Le projet Gradle attribue automatiquement le suffixe `.qa` aux cibles contenues dans `integration_test/` et reconnaît le marqueur injecté par le lanceur Flutter pour ses cibles générées. L’APK de test s’appelle **Monolith QA**, avec l’identifiant `com.monolithharness.flutter.qa`, un stockage et des clés séparés. Les scénarios vérifient cet identifiant avant leurs opérations. L’identifiant réel de l’APK a aussi été contrôlé avant son installation. Le scénario des messages a ensuite réussi avec cet identifiant séparé et `--no-uninstall`. Les deux applications étaient présentes simultanément ; la date de première installation de Monolith est restée au 11 octobre 2026 à 14:13:22 et sa configuration DeepSeek restaurée était encore visible après le test. Cela vérifie la séparation corrigée ; cela ne récupère pas l’historique perdu lors des premiers essais.

## Reproduire

Depuis `src/MonolithHarness.App.Flutter` :

```powershell
flutter analyze
flutter test
flutter test integration_test/mobile_test.dart -d <identifiant-du-téléphone> --no-uninstall
flutter test integration_test/message_interaction_test.dart -d <identifiant-du-téléphone> --no-uninstall
flutter build apk --release --target lib/main.dart --target-platform android-arm64
```

Empreinte SHA-256 de l’APK validé :

```text
cb69f7691f0d46ae2f0f42c85526bc92658b9cdaaabe2ff20c4eff5973aa013c
```

## Portée de la validation

Les essais sur matériel concernent ce Pixel uniquement. L’application Flutter possède ses propres données ; le portage ne transfère pas automatiquement les conversations de l’ancienne application. Le périmètre repris et les fonctions absentes sont détaillés dans le [guide Flutter Android](flutter-android.md).

La signature durable et la distribution GitHub ne sont pas configurées pour ce nouveau projet. Le code, les guides et l’APK sont préparés localement.
