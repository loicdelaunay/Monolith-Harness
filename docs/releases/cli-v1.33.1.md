# Monolith Harness CLI 1.33.1

## Nouveautés depuis 1.29.2

- Le moteur partagé enregistre désormais la consommation des conversations, sous-agents, nommage, vision et compactage dans une table dédiée. Les compteurs déclarés par les fournisseurs sont distingués des estimations, y compris pour les réponses partielles interrompues ou en erreur.
- Import des compteurs historiques datés, sans compter deux fois les réponses copiées dans les branches. Les données d’usage ne contiennent ni prompts, ni réponses, ni identifiants secrets ; elles survivent à la suppression individuelle d’une conversation et sont effacées par la réinitialisation des données.
- Même moteur et version que la GUI 1.33.1, qui ajoute le tableau de consommation, le détecteur SlopTotal, le guide de bienvenue et le formulaire complet de création de projet.

## Plateformes et installation

Windows x64, macOS Apple Silicon, macOS Intel et Linux x64 (construction Fedora 44). Chaque archive contient uniquement `MonolithHarness.exe` ou `MonolithHarness`, la licence et les instructions. Vérifier les téléchargements avec **SHA256SUMS.txt**.

Fermer le CLI, sauvegarder son dossier puis remplacer uniquement l’exécutable. Conserver `database.sqlite`, les ressources et les paramètres. Exécuter `MonolithHarness.exe` sur Windows ou `./MonolithHarness` sur macOS/Linux, puis `/connect` pour configurer un fournisseur. Installer GUI et CLI dans des dossiers séparés.

Sur macOS/Linux, extraire avec `tar -xzf` pour conserver les permissions. Les builds macOS ne sont ni signés ni notariés. Les installations antérieures à 1.29.2 doivent remplacer manuellement leur ancien `omh.exe` / `omh` avant les mises à jour suivantes.

[Guide CLI](https://github.com/loicdelaunay/Monolith-Harness/blob/main/docs/cli.md) · [Changelog complet](https://github.com/loicdelaunay/Monolith-Harness/blob/main/CHANGELOG.md)
