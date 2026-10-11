# Android : passage à Flutter

Depuis la version 1.89.0, le projet Android actif est `src/MonolithHarness.App.Flutter`. L’ancien hôte Uno `MonolithHarnessGui.Portable`, son moteur .NET `Core.Mobile`, leurs tests spécifiques et le workflow `android-portable.yml` sont retirés du code courant. Les composants desktop Uno continuent d’utiliser Core et Core.Desktop.

Consulter le [guide Flutter Android](flutter-android.md) pour les fonctionnalités, les limites, la construction et les tests. Le script `publish-android.ps1` prépare désormais un APK Flutter de développement.

## Anciennes installations

Le retrait des sources ne désinstalle pas l’application présente sur les téléphones. Les APK et le code Uno historiques restent accessibles dans les anciens tags et releases GitHub.

Flutter utilise `com.monolithharness.flutter` ; Uno utilisait `com.monolithharness.portable`. Les deux applications ont des données privées distinctes. Aucune migration automatique des conversations, des clés ou des fichiers n’est disponible.

## Signature et publication

La publication Flutter sur GitHub sera configurée lors d’une demande de release. Les APK Flutter actuels ont une signature de développement.

La clé durable des releases Android antérieures demeure dans le dossier ignoré `.signing/android/`, avec un mot de passe protégé par Windows DPAPI. Les quatre secrets GitHub existants sont `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD` et `ANDROID_STORE_PASSWORD`. Ces fichiers privés ne doivent jamais être ajoutés aux sources. La signature durable devra être configurée pour Flutter avant sa première distribution publique et conservée pour ses futures mises à jour.
