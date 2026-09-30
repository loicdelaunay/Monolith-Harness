# Monolith Harness CLI 1.29.2

- Nouveau monogramme M aux couleurs du thème et captures terminal actualisées.
- Le lanceur publié s’appelle `MonolithHarness.exe` sur Windows et `MonolithHarness` sur macOS et Linux.
- Commandes d’aide, documentation, profils de terminal et updater alignés avec cette publication.

## Plateformes

Windows x64, macOS Apple Silicon, macOS Intel et Linux x64 (Fedora). Chaque archive contient uniquement le lanceur CLI, la licence et les instructions. Vérifier les téléchargements avec `SHA256SUMS.txt`.

## Installation et transition

Fermer le CLI et sauvegarder son dossier. Remplacer manuellement l’ancien `omh.exe`/`omh` par le nouveau lanceur dans le même dossier, en conservant `database.sqlite`, les ressources et les paramètres. Exécuter `MonolithHarness.exe` sur Windows ou `./MonolithHarness` sur macOS/Linux, puis `/connect` pour configurer un fournisseur. Les téléchargements GUI et CLI restent séparés ; utiliser des dossiers séparés pour installer les deux.

La mise à jour automatique est disponible sur Windows à partir de cette version. Sur macOS/Linux, extraire avec `tar -xzf` pour conserver les permissions. Les builds macOS ne sont ni signés ni notariés.

[Guide CLI](https://github.com/loicdelaunay/Monolith-Harness/blob/main/docs/cli.md)
