# Monolith Harness CLI 1.37.0

## Mise à jour du moteur partagé

- Le CLI est aligné sur la GUI 1.37.0 et embarque les nouveaux réglages et contrôles du service de détection IA du moteur partagé. Les outils de correction, traduction et détection restent accessibles dans la **GUI**.
- Les profils et données portables restent compatibles entre les deux interfaces. Les fonctions du CLI sont conservées : icône terminal dédiée, animation de réflexion, historique ↑/↓, activité des sous-agents et compactage configurable.
- Cette version accompagne les corrections de fluidité du collage formaté et le choix d’un fournisseur/modèle pour le détecteur IA dans la GUI.

## Téléchargements et installation

- **Windows x64** : `MonolithHarness-CLI-v1.37.0-win-x64.zip`.
- **Linux x64** : `MonolithHarness-CLI-v1.37.0-linux-x64.tar.gz`, construit dans Fedora 44.
- La publication **macOS est temporairement suspendue**. Les anciens téléchargements restent disponibles sur leurs releases.

Chaque archive contient uniquement le lanceur autonome, la licence et les instructions. Vérifier son empreinte avec **SHA256SUMS.txt**. L’archive Windows de compatibilité `OhMyHarness-CLI-v1.37.0-win-x64.zip` a exactement le même contenu que celle au nouveau nom.

Fermer le CLI, sauvegarder son dossier puis remplacer uniquement l’exécutable. Conserver `database.sqlite`, les ressources et les paramètres. Exécuter `MonolithHarness.exe` sur Windows ou `./MonolithHarness` sur Linux, puis `/connect` pour configurer un fournisseur. Installer GUI et CLI dans des dossiers séparés.

Sur Linux, extraire avec `tar -xzf` pour conserver les permissions et garder le dossier portable privé, avec la clé locale et la base. Les installations antérieures à 1.29.2 doivent remplacer manuellement leur ancien lanceur.

[Guide CLI](https://github.com/loicdelaunay/Monolith-Harness/blob/main/docs/cli.md) · [Changelog complet](https://github.com/loicdelaunay/Monolith-Harness/blob/main/CHANGELOG.md)
