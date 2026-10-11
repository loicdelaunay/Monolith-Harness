# Monolith · Flutter Android

Réécriture de l’application Android portable avec Flutter et les composants Material du SDK. Les détails, les limites et la validation sont décrits dans le [guide Flutter](../../docs/flutter-android.md).

## Prérequis

- Flutter stable 3.47.1 / Dart 3.13.1 ou une version compatible avec le fichier `pubspec.lock`.
- SDK Android ; Android 8.0/API 26 minimum sur l’appareil.
- Un téléphone autorisé pour le débogage USB ou un émulateur.

## Lancer

```powershell
flutter pub get
flutter devices
flutter run -d <identifiant-du-téléphone>
```

## Vérifier

```powershell
flutter analyze
flutter test
flutter test integration_test/mobile_test.dart -d <identifiant-du-téléphone>
```

Le scénario d’intégration utilise une base en mémoire, des documents temporaires et un fournisseur simulé. Il ne contacte pas de modèle payant et ne lit pas les conversations existantes.

## APK de développement

```powershell
flutter build apk --debug --target-platform android-arm64
```

Résultat : `build/app/outputs/flutter-apk/app-debug.apk`. L’application s’appelle **Monolith**, avec le logo Android vert, et utilise l’identifiant `com.monolithharness.flutter`. Elle coexiste avec `com.monolithharness.portable` ; les conversations et les clés ne sont pas transférées automatiquement.

La compilation de développement utilise sa clé de développement. La distribution GitHub utilise la signature durable configurée dans les secrets du dépôt. Ne pas ajouter de clé API, certificat privé ou mot de passe aux sources.

## APK optimisé testé sur le Pixel

```powershell
flutter build apk --release --target-platform android-arm64
```

Résultat : `build/app/outputs/flutter-apk/app-release.apk`. La compilation optimise le code ; sans les variables privées de signature, elle utilise la signature de développement. Exécuter cette commande avec la résolution des dépendances habituelle : Flutter doit régénérer la liste des plugins après les tests d’intégration.

La validation du 11 octobre 2026 est décrite dans le [rapport de tests](../../docs/rapport-flutter-android-2026-10-11.md).

## Organisation

| Dossier | Fonction |
|---|---|
| `lib/domain` | Paramètres, projets, discussions et skills |
| `lib/data` | SQLite et documents privés |
| `lib/services` | API en streaming, outils, web et séparation du HTML |
| `lib/ui` | Écrans Material, Markdown et aperçu web |
| `assets` | Logo et bibliothèques web locales avec licences |
| `test` | Tests unitaires et responsive |
| `integration_test` | Scénario Android sur appareil |

Les fichiers du projet apparaissent dans les solutions .NET pour la navigation. Flutter conserve sa propre compilation et n’est pas requis pour compiler Desktop ou CLI.

Pour la publication durable, voir `publish-android.ps1 -RequireReleaseSigning` et le [guide Flutter](../../docs/flutter-android.md#publication-signée).
