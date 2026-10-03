# Modes Chat et Agent

Le sélecteur à côté du bouton **+** choisit le mode de la conversation. **Agent** est le mode par défaut, y compris pour les conversations existantes. Le choix est enregistré et repris à la réouverture ou lors d'une branche. Les commandes `/chat` et `/agent` sont aussi disponibles dans la GUI et le CLI.

## Chat

Discussion, rédaction, explications et analyse des pièces jointes avec le modèle et le niveau de réflexion sélectionnés. Le menu **+ → Skills · Chat** propose deux skills indépendants des réglages Agent, enregistrés par conversation :

- **Recherche web**, activée par défaut : HTTP .NET, avec résultats Smart conservés dans un fichier et lectures ciblées. Les autorisations réseau habituelles s'appliquent.
- **Exécution de scripts Python**, désactivée par défaut : Python embarqué, écriture et exécution après autorisation. Sans dossier projet attaché, le répertoire des scripts de cette conversation sert de dossier de travail. L'exécution utilise les droits de l'utilisateur et ne constitue pas une sandbox.

Les autres outils restent indisponibles : fichiers du projet, terminal, contrôle du navigateur, Git, MCP, mémoire et sous-agents. OpenCode utilise uniquement ses outils web natifs autorisés par la connexion ; ses autres outils restent interdits. Le skill Python Monolith nécessite un fournisseur utilisant les outils Monolith et est indiqué indisponible avec OpenCode. Le CLI configure les deux choix via `/chat-skills`.

La GUI permet de joindre des images et des documents choisis explicitement. Le texte des documents est extrait en arrière-plan et ajouté au brouillon avant l'envoi : quatre fichiers maximum par ajout, 32 MiB par fichier et 64 000 caractères extraits par document. Les PDF scannés ne bénéficient pas d'OCR. Les images conservent le traitement natif ou le relais vision configuré.

L'historique enregistré reste intact. Les échanges des outils Chat encore activés sont conservés pour les tours suivants ; les autres résultats d'outils servent uniquement de contexte historique, sans réémettre leurs appels. La gestion du contexte, les nouvelles tentatives et le nommage des conversations restent actifs.

## Agent

Fonctionnement habituel avec les skills activés et les autorisations de l'utilisateur. **Plan / Exécution**, les sous-agents **Désactivés / Auto / Forcés** et la sandbox sont des réglages de ce mode. Ils sont conservés lors du passage en Chat, sans être utilisés pour les nouveaux tours Chat.

Le changement de mode concerne le prochain message et nécessite d'arrêter la réponse en cours. Il ne change ni le modèle choisi ni les messages déjà enregistrés.
