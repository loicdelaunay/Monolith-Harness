# Monolith Harness CLI 1.36.0

## Nouveautés depuis 1.33.1

- **Icône Windows dédiée au CLI** : le M apparaît dans une fenêtre de terminal pour distinguer les deux exécutables.
- **Réflexion animée** dans la zone de saisie vide ; taper restaure immédiatement l’éditeur et permet de préparer un message pendant une réponse.
- **↑ / ↓** rappellent les anciens messages et commandes de la conversation, puis restaurent le brouillon. L’historique enregistré reste disponible après redémarrage.
- **Sous-agents visibles** : panneau d’activité avec une ligne animée par agent et son action en cours ; les agents terminés, arrêtés ou en erreur sont distingués.
- **`/settings` → Compactage** : seuil automatique, cible de contexte, force, résumé personnalisé et stratégie. La politique est partagée avec la GUI et les sous-agents compatibles ; un échec conserve les messages originaux.
- Renommage interne complet en **MonolithHarness**, avec conservation des données portables et compatibilité des anciens index et mises à jour.

## Téléchargements et installation

- **Windows x64** : `MonolithHarness-CLI-v1.36.0-win-x64.zip`.
- **Linux x64** : `MonolithHarness-CLI-v1.36.0-linux-x64.tar.gz`, construit dans Fedora 44.
- La publication **macOS est temporairement suspendue**. Les anciens téléchargements restent disponibles sur leurs releases.

Chaque archive contient uniquement le lanceur autonome, la licence et les instructions. Vérifier son empreinte avec **SHA256SUMS.txt**. Les archives Windows de compatibilité ont le même contenu que l’archive au nouveau nom.

Fermer le CLI, sauvegarder son dossier puis remplacer uniquement l’exécutable. Conserver `database.sqlite`, les ressources et les paramètres. Exécuter `MonolithHarness.exe` sur Windows ou `./MonolithHarness` sur Linux, puis `/connect` pour configurer un fournisseur. Installer GUI et CLI dans des dossiers séparés.

Sur Linux, extraire avec `tar -xzf` pour conserver les permissions et garder le dossier portable privé, avec la clé locale et la base. Les installations antérieures à 1.29.2 doivent remplacer manuellement leur ancien lanceur.

[Guide CLI](https://github.com/loicdelaunay/Monolith-Harness/blob/main/docs/cli.md) · [Changelog complet](https://github.com/loicdelaunay/Monolith-Harness/blob/main/CHANGELOG.md)
