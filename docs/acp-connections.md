# Connexions ACP

Disponibles depuis Monolith Harness 1.57.0 dans **Fournisseurs → Ajouter un fournisseur**, le premier démarrage et `/connect` dans le CLI. La GUI et le CLI utilisent le même client ACP v1.

## ChatGPT Plus / Pro · Codex

1. Installez Node.js avec npm si nécessaire.
2. Ajoutez **ChatGPT Plus / Pro · Codex**, puis cliquez sur **Connecter mon compte ChatGPT**. Terminez la connexion OpenAI dans le navigateur. Dans le CLI, `/connect` propose le même parcours.
3. Choisissez un modèle dans le catalogue et enregistrez les réglages.

Cette connexion utilise l’authentification officielle de Codex et les limites Codex de l’abonnement ChatGPT. Elle ne donne pas accès à l’historique ChatGPT ni à tous les modèles de l’interface ChatGPT. Le catalogue de l’agent n’est pas une preuve d’accès : seule une réponse réussie vérifie l’accès au modèle. Aucune clé API n’est enregistrée pour cette connexion et les variables de clés API ne sont pas transmises à l’adaptateur.

L’adaptateur `@agentclientprotocol/codex-acp@2.1.1` inclut sa dépendance Codex et se télécharge au premier lancement. Les identifiants de connexion restent gérés par Codex. Le mode Agent / Exécution utilise le mode natif avec accès au workspace et demandes d’autorisation pour les actions qui l’exigent ; Chat et Plan imposent le mode en lecture seule. Les outils web et Python Monolith ne sont pas transmis en mode Chat.

Sources : [Authentification Codex](https://learn.chatgpt.com/docs/auth), [abonnements et limites](https://learn.chatgpt.com/docs/pricing), [adaptateur ACP Codex](https://github.com/agentclientprotocol/codex-acp).

## Antigravity · ACP

1. Installez Node.js avec npm et [Antigravity CLI (agy)](https://www.antigravity.google/docs/cli/install/).
2. Lancez `agy` pour connecter votre compte Google.
3. Ajoutez **Antigravity · ACP**, testez la connexion, puis choisissez **Agent → Exécution** pour envoyer des demandes.

L’adaptateur `google-antigravity-acp@2.0.1` est téléchargé au premier lancement. Le binaire `agy` est recherché dans le PATH et les dossiers d’installation usuels. Un emplacement différent peut être indiqué dans les arguments avancés, par exemple `["--binary-path", "C:/outils/agy.exe"]`.

La connexion conserve les permissions d’Antigravity et impose `--no-skip-permissions`. L’adaptateur actuel ne relaie pas les demandes d’autorisation dans Monolith Harness et ne permet pas d’imposer le mode Chat ou Plan : ces modes sont donc refusés avant de lancer une génération. Antigravity décide lui-même des actions permises dans le workspace et refuse les actions qui demanderaient une approbation impossible à obtenir en mode headless. L’adaptateur n’expose pas de catalogue : `default` conserve le modèle configuré dans Antigravity ; un identifiant exact peut être saisi pour le sélectionner au lancement.

Sources : [adaptateur Antigravity ACP](https://github.com/sibbl/google-antigravity-acp), [permissions en mode headless](https://www.antigravity.google/docs/cli/headless/).

## Fonctionnement commun

Les réponses, réflexions et activités d’outils sont reçues en direct. L’arrêt envoie `session/cancel` puis ferme les processus créés pour ce tour. Chaque tour ouvre une nouvelle session et transmet l’historique local ; les sessions persistantes et les reprises natives ne sont pas encore utilisées. Les outils et sous-agents internes de Monolith ne sont pas transmis : l’agent utilise ses propres capacités. Les images sont envoyées seulement si l’agent annonce cette capacité ; le relais vision configuré reste disponible pour les modèles sans vision. Le sandbox Monolith ne prend pas en charge les agents externes.

Les options avancées acceptent un exécutable ACP autonome ou un script `.js` avec un tableau JSON d’arguments, sans interpréter une commande shell. Les versions des adaptateurs standard sont fixées pour rendre le lancement reproductible. Les statistiques ACP non communiquées (tokens par tour et tokens mis en cache) restent inconnues et ne sont pas inventées.
