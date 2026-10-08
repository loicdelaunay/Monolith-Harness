# Périmètre des pièces jointes (1.73.0)

Le menu du bouclier dans la zone de saisie contient **Travailler hors des pièces jointes**. Le réglage est enregistré par conversation, puis transmis à ses sous-agents. Il est activé par défaut, y compris pour les anciennes conversations, pour conserver les usages existants.

## Activé

Les accès extérieurs suivent le mode d’approbation choisi et les règles du projet. Cette option ne désactive pas les permissions, les exclusions de fichiers ou la sandbox.

## Désactivé

Les outils de fichiers sont limités aux fichiers et dossiers associés à cette conversation. Un fichier joint individuellement ne donne pas accès à son dossier parent. L’espace de travail automatique de la conversation reste accessible lorsqu’il est présent parmi les ressources. Les chemins absolus extérieurs et les chemins `..` sortant de ce périmètre sont refusés, sans ouvrir de dialogue d’autorisation. Les autorisations mémorisées et **Tout autoriser** ne lèvent pas cette limite.

Une commande shell ou un script arbitraire peut accéder à d’autres fichiers même si son dossier de départ est autorisé. Pour ne pas présenter ce réglage comme une isolation qui n’existe pas, le terminal local, Python, les outils GIT locaux, le navigateur, le bureau, les outils MCP et les agents externes OpenCode/ACP deviennent indisponibles pour l’IA. Les outils inconnus sont refusés par défaut. Les commandes sont possibles dans la sandbox Docker/Podman existante, qui utilise des copies privées des sources et nécessite un moteur de conteneurs actif.

Les sous-agents directs héritent du même filtre. Ils travaillent dans les ressources jointes et ne créent pas de worktree externe lorsque cette limite est active. Les outils internes pour les skills, la mémoire, les images de la conversation et les résultats HTTP restent accessibles selon leurs propres contrôles. La recherche RAG filtre ses résultats selon les sources de cette conversation.

L’option peut être changée une fois la réponse arrêtée. Chaque génération utilise un instantané du réglage. Les outils manipulés directement par l’utilisateur restent disponibles.

Le moteur partagé enregistre aussi `allowOutsideResources` via `chat.modes` et renvoie `AllowOutsideResources` dans sa liste de conversations.
