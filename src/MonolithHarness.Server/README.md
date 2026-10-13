# Monolith Harness Server

Application console .NET autonome qui héberge, dans un seul exécutable : l’interface web (Blazor + MudBlazor), l’API REST (OpenAPI + Swagger), un canal WebSocket temps réel et les workers d’exécution cloud qui embarquent le moteur partagé. Elle suit le cadrage « Monolith Harness Server ».

Comme le Desktop, les données sont **portables** : `database.sqlite`, `server.json` et le dossier `workspace/` se trouvent à côté de l’exécutable (ou dans `--data <dossier>`).

## Démarrer

```powershell
./publish-server.ps1 -OutputDirectory artifacts/TEMP/mon-test   # ou: dotnet run --project src/MonolithHarness.Server
MonolithHarness.exe                                              # http://127.0.0.1:5800
MonolithHarness.exe --port 8080 --host 0.0.0.0                   # accessible à distance
```

| Option | Rôle |
|---|---|
| `--port <n>` / `--host <adresse>` | Port (défaut 5800) et adresse d’écoute (défaut `127.0.0.1`, boucle locale seulement) |
| `--https-port <n> --cert <pfx> --cert-password <mdp>` | HTTPS direct (certificat PKCS#12) |
| `--behind-proxy` / `--redirect-https` | Faire confiance à `X-Forwarded-*` d’un proxy TLS / rediriger HTTP vers HTTPS |
| `--data <dossier>` | Dossier des données (défaut : à côté de l’exécutable) |
| `--update <ask\|auto\|notify\|off>` / `--no-update-check` | Nouvelle version au démarrage : question Y/N (défaut), installation automatique, simple notification ou aucune recherche (`updateMode` dans `server.json`) |
| `--update-feed <url>` | Flux des versions à lire (par défaut les releases GitHub du projet ; HTTPS, ou HTTP sur cette machine seulement) |
| `service install\|uninstall\|status` | Installe le serveur comme service qui démarre avec la machine et redémarre après une panne (voir « Maintenance ») |
| `reset-password --email <adresse>` | Émet un code de récupération d’une heure (accès console = preuve de propriété) |
| `restore --from <sauvegarde.sqlite>` | Restaure une sauvegarde, serveur arrêté ; l’ancienne base est conservée |

Ordre de priorité : valeurs par défaut < `server.json` (créé au premier démarrage) < variables `MONOLITH_SERVER_*` < ligne de commande.

Réglages de `server.json` : `updateMode` (`ask`, `auto`, `notify`, `off`), `updateTimeoutSeconds` (5 s : délai de la recherche, un réseau absent ne retarde jamais le démarrage), `updatePromptSeconds` (20 s : délai de la réponse Y/N, sans réponse = non) et `updateFeed`. Variables : `MONOLITH_SERVER_UPDATE`, `MONOLITH_SERVER_UPDATE_FEED`.

Au premier démarrage, un compte administrateur est créé une seule fois ; les redémarrages ne réinitialisent pas ses identifiants. Définissez votre propre mot de passe avant tout accès distant : la commande console `MonolithHarness reset-password --email admin@admin.com` émet un code temporaire à utiliser avec « Recovery code » sur la page de connexion. Voir aussi le [guide anglais](../../docs/server.md).

## Fonctionnalités activables (administration > Fonctionnalités et réglages)

| Ensemble | Par défaut | Résumé |
|---|---|---|
| Exécution cloud | désactivée | Projets cloud, conversations et requêtes exécutées sur le serveur dans un workspace isolé |
| Partage en temps réel | activé | Une conversation locale suivie en direct via un bridge ; un code unique à transmettre (ou une liste publique) ; spectateurs : lire et commenter |
| Tâches planifiées cloud | désactivée | CRON + fuseau, sans client connecté |
| File d’attente | activée | Utilisateurs / exécutions simultanés, capacité, équité entre utilisateurs ; s’applique aussi aux appels des conversations locales qui utilisent un provider partagé (relais) |
| Providers partagés | activé | Catalogue côté serveur, clés chiffrées, relais OpenAI-compatible |
| Statistiques d’utilisation | activé | Reçoit la vitesse et la consommation de jetons que les utilisateurs choisissent de partager (Statistiques, vue commune ou par utilisateur) |

Une opération exige : fonctionnalité activée **et** permission du rôle **et** droit sur la ressource (propriétaire/membre du projet, participant du partage, accès au provider). Les rôles sont personnalisables ; `Administrator` détient toutes les permissions.

## API

* REST : `/api/...`, documentation `/openapi/v1.json`, interface `/swagger` (jeton `Authorization: Bearer`, obtenu avec `POST /api/auth/login`).
* WebSocket : `/ws` (en-tête Bearer ou premier message `{"type":"auth","token":"…"}`). Messages : `subscribe`/`unsubscribe` (`run:{id}`, `project:{id}`, `share:{id}`, `queue`, `server` ; `user:{id}` est implicite), `ping`, et pour le partage `bridge.attach`, `bridge.snapshot`, `bridge.event`, `bridge.detach`, `share.comment`. Événements d'une conversation partagée : `bridge`, `snapshot`, `message` (avec `reasoning`, `toolCalls`, `author`), `stream` (texte et réflexion en cours), `tool` (outil qui démarre), `title` (renommage chez l'hôte, qui devient le titre du partage), `comment`, `closed`, et `viewers` (nombre de personnes qui suivent, calculé par le serveur et non imposable par un client). Les événements arrivent sous `{"type":"event","topic","name","data"}`.
* Relais de providers : `/api/providers/{id}/relay/{chat/completions|embeddings|models}` ; la clé amont est ajoutée par le serveur. Chaque appel `chat/completions` d’une conversation locale prend sa place dans la file (voir ci-dessous).
* Test d’un provider : `POST /api/admin/providers/probe` (`baseUrl`, `secret` ou `providerId`) appelle `GET {base}/models`, renvoie l’état, le délai et les modèles publiés (contexte et images quand le provider les donne). La clé n’est jamais renvoyée.
* Partages : `POST /api/shares` (`isPublic` facultatif, renvoie le `code`), `POST /api/shares/join` (`code`), `GET /api/shares/public`, `PATCH /api/shares/{id}` (`isPublic`), `POST|DELETE /api/shares/{id}/code`, `POST /api/shares/{id}/comments`, `POST /api/shares/{id}/leave`.
* Conversations cloud : `PATCH /api/projects/{id}/chats/{chatId}` (`title`) renomme pour tous les membres. Les messages renvoyés par `GET .../messages` portent `authorId`, `authorEmail` et `authorName` pour les messages utilisateur (qui l'a envoyé), et `wire` (appels d'outils et identifiant d'appel, sans le détail interne du moteur) pour dessiner les cartes d'outils comme chez l'hôte.
* Message du moment : `GET /api/server` (anonyme) renvoie `announcement` (`text`, `level` = `info`, `warning` ou `error`, `updatedUtc`, `expiresUtc`, `by`) ou `null` ; `PUT /api/admin/announcement` (`text` ≤ 500 caractères, `level`, `expiresMinutes` facultatif) le publie, `DELETE /api/admin/announcement` le retire (permission `settings.manage`, audité). L’événement `announcement` du sujet `server` (données nulles pour un retrait) le pousse tout de suite aux clients connectés.
* Rang dans la file : l’événement `run.state` (sujets `user:{id}`, `run:{id}`, `project:{id}`, `queue`) porte `queuePosition`, `providerId` et `model` ; il est renvoyé à chaque fois que la file avance, y compris pour les appels d’une conversation locale (`source` = `relay`).
* Statistiques : `POST /api/usage` (`deviceId`, `samples[]` de 1000 au plus : `id`, `completedUtc`, `provider`, `model`, `activity`, `inputTokens`, `outputTokens`, `cachedInputTokens`, `seconds`, `status`, `estimated`, `viaServer`) est idempotent ; `GET /api/usage/summary?scope=me|all|user&period=24h|7d|30d|1y&userId=` (totaux pondérés par la durée, courbe, détail par modèle et par utilisateur) ; `GET /api/usage/status` ; `DELETE /api/usage` (ses propres mesures ; `?userId=` pour un administrateur). Chacun lit ses chiffres ; la vue commune et la vue par utilisateur exigent la permission `usage.viewAll`. Durée de conservation réglable (365 jours par défaut).
* Maintenance (permission `settings.manage`) : `GET /api/admin/update`, `POST /api/admin/update/check`, `POST /api/admin/update/apply` ; `GET /api/admin/service`, `POST /api/admin/service/install`, `DELETE /api/admin/service`.
* Annuaire : `GET /api/users/search?query=` (comptes actifs, pour choisir des membres ou des invités).
* Synchronisation des clients : le serveur envoie des événements `sync` sans données (`{area: providers|projects|chats|shares, projectId?, chatId?}`) sur le sujet `user:{id}` (ce qui concerne le compte) ou `server` (catalogue, partages publics) ; le client relit ce qui a changé avec ses propres droits.

## Sécurité

* Mots de passe : PBKDF2-HMAC-SHA512 salé (210 000 itérations). Jetons de session opaques, seul leur SHA-256 est stocké ; révocation immédiate (les WebSocket de la session sont fermés).
* Limitation des tentatives par compte et par adresse, réponse identique pour un compte inconnu.
* Récupération : code à usage unique d’une heure émis par un administrateur (ou `reset-password` en console) ; il révoque toutes les connexions.
* Cookie web `HttpOnly` + `SameSite=Strict`, refus des écritures inter-sites, poignée de main WebSocket protégée.
* Clés de providers chiffrées (Data Protection) et jamais renvoyées par l’API.
* Journal d’audit des opérations sensibles ; sauvegardes cohérentes de la base (`VACUUM INTO`).

## Maintenance : message du moment, mises à jour, service

Administration › Maintenance (permission « Activer les fonctionnalités et régler le serveur ») réunit trois outils.

* **Message du moment** : un texte (500 caractères au plus), un niveau d’alerte (information bleu, avertissement orange, incident rouge) et une durée facultative. Il s’affiche tout de suite en bandeau sur toutes les pages (la page de connexion comprise), en bande colorée en haut du Desktop (fermable jusqu’au message suivant) et sur la première ligne du CLI.
* **Mises à jour** : au démarrage, le serveur cherche une version publiée (étiquette `server-vX.Y.Z`, archive `MonolithHarness-Server-vX.Y.Z-<plateforme>`) avec un délai limité, puis, selon `updateMode`, pose la question **Y/N** sur la console (sans réponse dans le délai : non, le serveur démarre), installe sans demander (`auto`) ou le signale seulement (`notify`). Sans console (service) il ne fait que le signaler, ici et en bandeau pour les administrateurs ; un service le recherche aussi toutes les 6 heures. L’installation vérifie la somme SHA-256 publiée avec l’archive, n’extrait que l’exécutable, vérifie qu’il démarre et annonce la bonne version, renomme l’ancien exécutable en `.previous` (retour arrière possible) puis redémarre ; la base, `server.json` et `workspace/` ne sont jamais touchés. Les requêtes en cours sont interrompues par le redémarrage. Seul l’exécutable publié peut se mettre à jour ; un lancement de développement (`dotnet …dll`) ne le peut pas.
* **Service** : « Installer comme service » (ou `MonolithHarness service install`) démarre le serveur avec la machine, sans session ouverte, et le redémarre après une panne. Windows : service `MonolithHarnessServer` sous le compte virtuel `NT SERVICE\MonolithHarnessServer` (sans mot de passe ni droits d’administrateur), actions de reprise au bout de 5 s, 15 s puis 60 s, y compris pour le redémarrage voulu après une mise à jour ; l’élévation (fenêtre UAC) est demandée **sur la machine du serveur**, le compte reçoit le droit d’écrire dans le dossier du serveur, les clés de chiffrement des providers sont protégées pour la machine (une copie de sécurité est faite dans `workspace/`). Linux : unité systemd `monolith-harness-server` (`Restart=always`), macOS : daemon launchd ; ces deux plateformes exigent `sudo` (la page affiche la commande). Le journal d’un service est dans `workspace/logs/`. L’isolation par conteneur des exécutions cloud exige un compte qui sait utiliser Docker ou Podman : sinon lancez le service sous un compte adapté (`sc config … obj=`).

## Exécutions cloud

Chaque requête lance un **worker** : le même exécutable relancé en mode `worker`, avec un environnement minimal, la base du moteur et le dossier de fichiers **du seul projet**, et un jeton de run qui n’ouvre que le relais de providers (révoqué à la fin). Le worker ne voit jamais la clé du provider.

* Isolation : un workspace et une base moteur par projet, chemins validés (pas de `..`, de lien, de nom réservé), quota disque, workers refusés si le serveur tourne avec des privilèges administrateur (réglable).
* Code exécuté : uniquement dans le conteneur (Docker/Podman, réseau coupé, sans privilèges). Si `execution.requireContainerSandbox` est actif (défaut) et qu’aucun runtime n’est disponible, la requête **échoue avant tout appel** ; sans conteneur, l’agent est limité aux outils de fichiers.
* Limites appliquées par le serveur : durée (hors attente d’approbation), mémoire, temps CPU, disque.
* Approbations : envoyées au seul utilisateur qui a lancé la requête (pas aux administrateurs) ; une tâche planifiée sans personne reste en attente d’approbation.

## Clients

La connexion est **facultative** : sans serveur configuré, le Desktop et le CLI restent entièrement locaux.

* Desktop : Réglages › Serveur (adresse, connexion, providers partagés, changement de mot de passe, oubli du serveur). Une conversation se partage en direct depuis son menu contextuel (« Partager en direct… »).
* CLI : commande `/server` dans l’interface interactive ; `MonolithHarness server status|connect|login|logout|sync|projects|open|new|forget` sans interface (`--json`, `--password-stdin`, `MONOLITH_SERVER_PASSWORD`) ; `run "…" --cloud <projet>` pour exécuter une requête sans interface sur un projet cloud.
* Providers partagés : copiés comme providers locaux « … · serveur » (utilisables en local) ou « … · cloud » (exécutions cloud seulement) ; ils pointent vers le relais du serveur avec le jeton de session comme clé, la clé du fournisseur amont ne quitte jamais le serveur.
* Projets cloud : reflétés localement (projet, conversations, messages) ; le serveur reste la source de vérité et exécute la requête dans son workspace isolé. Les approbations et questions reviennent à l’utilisateur qui a lancé la requête.
* Partage en direct : l’appareil garde l’exécution et les outils ; le client relaie les événements au serveur, les spectateurs lisent et commentent. L’hôte voit et copie le code depuis la boîte de partage.
* Conversations cloud : chaque message utilisateur affiche l'avatar (initiales sur une couleur dérivée de l'adresse) et le nom de son auteur ; une requête lancée par un autre membre (ou un autre appareil du même compte) se suit en direct, réflexion et texte au plus toutes les 500 ms, les messages complets étant relus au plus toutes les 800 ms ; un renommage fait par un membre est vu par tous, et un renommage fait sur l'appareil est envoyé au serveur.
* Message du moment : le Desktop l’affiche dans une bande colorée au-dessus de l’application (bleu, orange, rouge selon le niveau), le CLI sur sa première ligne ; il apparaît, change et disparaît tout seul, et l’utilisateur peut le fermer jusqu’au message suivant (`server dismiss` en CLI).
* Position dans la file : une requête cloud, ou l’appel d’une conversation locale qui utilise un provider « · serveur », affiche « En attente sur le serveur (position N) » dans le Desktop et le CLI, mise à jour en direct quand la file avance. Les appels relayés ne portent pas l’identifiant de leur conversation : si plusieurs conversations attendent le même provider, les positions leur sont attribuées dans l’ordre.
* Mode serveur en un clic : bouton d’alimentation à côté de l’état du serveur (Desktop), interrupteur dans Réglages › Serveur, `/server` ou `server on|off` (CLI). Désactivé : ni synchronisation, ni canal temps réel, ni vue Cloud, ni providers partagés (une requête qui les utiliserait est refusée avec un message clair), ni requêtes cloud, ni conversations partagées (elles se rattachent à la réactivation) ; le compte et les liens sont conservés.
* Statistiques partagées : Réglages › Serveur › « Partager ma vitesse et ma consommation » (désactivé par défaut ; `server share-usage on|off|erase-usage` en CLI). Le client envoie par lots, toutes les minutes, le provider, le modèle, les jetons (envoyés, générés, réutilisés), la durée, la vitesse et l’état de chaque appel — jamais un message, ni un nom de projet ou de conversation ; les 30 derniers jours partent à l’activation, l’envoi est idempotent et peut être effacé sur le serveur (ce qui est effacé n’est pas renvoyé).
* Synchronisation automatique : providers, projets cloud, conversations (création, renommage, suppression côté serveur) et conversations partagées se mettent à jour tout seuls, en direct. Réglages › Serveur propose une resynchronisation complète **Jamais / 1 minute / 5 minutes (défaut) / 1 heure / 1 jour** en filet de sécurité. Supprimer un projet ou une conversation cloud depuis un client la supprime sur le serveur (propriétaire pour un projet) ; une copie locale dont le serveur n’a plus trace est retirée.
* Interface Desktop : bascule **Local / Cloud** au-dessus des projets (la vue Cloud liste les projets cloud, les conversations partagées avec le compte, les conversations publiques et le bouton « Rejoindre avec un code »), puce d’état de la connexion avec un badge info / avertissement / erreur ; un clic ouvre les informations de connexion, les problèmes et les événements récents. « Tâches planifiées » est dans le menu Outils.
* Sécurité du lien : HTTPS par défaut ; HTTP en clair refusé sauf boucle locale ou option explicite « accepter HTTP ». Le mot de passe n’est jamais conservé ; seul le jeton de session est stocké, protégé par le système (DPAPI / trousseau), dans `server-link.json` à côté de la base. Une session révoquée côté serveur est détectée et le client demande de se reconnecter.

## File d’attente et conversations locales

Les requêtes des projets cloud **et** les appels des conversations locales qui utilisent un provider « · serveur » (Desktop, CLI, Android) partagent la même file, le même ordre équitable et les mêmes limites : capacité, utilisateurs simultanés, exécutions par utilisateur et par provider, longueur et attente maximales. Un appel qui doit attendre reste en suspens jusqu’à son tour (réglage « Attente maximale d’un appel d’une conversation locale », 300 s par défaut, puis `queue_timeout`). Les appels relayés sont visibles dans Administration › File d’attente (origine « Conversation locale ») et annulables. Les appels d’une exécution cloud ne sont pas comptés deux fois : ils portent le jeton de leur exécution. Avec la fonctionnalité « File d’attente » désactivée, aucune limite n’est appliquée mais la liste montre toujours ce qui tourne.

## Partage : code, public, invitation

L'hôte envoie ce qu'il affiche : chaque message avec sa réflexion, ses appels d'outils, ses durées et l'expéditeur des messages utilisateur, le texte et la réflexion au fil de l'écriture (deux fois par seconde au plus) et les outils qui démarrent. Le spectateur redessine la conversation comme l'hôte. Le serveur compte les personnes qui suivent (une par compte, hors hôte) et l'annonce en direct à l'hôte, qui l'affiche dans une pastille en haut de la conversation.

Chaque partage reçoit un **code unique** (10 symboles sans caractères ambigus, affiché `XXXXX-XXXXX`) : quiconque le saisit (page Partages du serveur, « Rejoindre avec un code » du Desktop, `server join <code>` du CLI) suit la conversation comme un invité. Les essais erronés sont limités (8 par 10 minutes et par compte). L’hôte peut générer un nouveau code (l’ancien cesse de fonctionner, les spectateurs déjà arrivés restent), le désactiver, inviter des comptes, ou rendre la conversation **publique** : elle apparaît alors dans la vue Cloud de tous les comptes autorisés. Révoquer ferme tout.

## Choix pour les points ouverts du cadrage

* Partage : le serveur ne conserve aucun historique quand l’appareil se déconnecte ; l’hôte révoque ou retire des spectateurs, un spectateur peut quitter ; commentaires limités à 2 000 caractères et 1 par seconde.
* File : limites configurables par utilisateur, par exécution simultanée, par provider, longueur maximale ; ordre équitable (l’utilisateur ayant le moins d’exécutions passe en premier) ; les sous-agents font partie de la requête parente.
* Planification : fuseau par tâche, occurrence unique garantie par une clé unique, aucun chevauchement, arriéré regroupé ; politique `skip` (défaut) ou `run-once` pour les occurrences manquées à l’arrêt ; permissions réévaluées à chaque occurrence.
* Providers : un provider peut être autorisé aux exécutions locales (`allowLocalUse`) ; accès par utilisateur ou par rôle.
* Exploitation : HTTPS direct ou proxy ; sauvegarde de la base ; désactiver une fonctionnalité ferme ce qui en dépend (partages en direct, file) sans interrompre les exécutions en cours.

## Limites actuelles

* L’application Android n’a pas encore de connexion au serveur (portage prévu) ; le Desktop et le CLI sont intégrés (voir ci-dessus).
* Le chemin conteneur (sandbox Docker/Podman, fusion des changements à la fin de l’exécution) n’a pas pu être exercé sur la machine de développement, sans runtime de conteneur ; le refus « fail closed » est testé.
* Les limites de ressources sont surveillées par le serveur (pas de cgroups ni de Job Object) et le worker tourne sous le compte du serveur : l’isolation forte repose sur le conteneur.
* Pas d’inscription libre ni d’envoi d’e-mails ; pas de quotas de consommation par utilisateur sur le relais (la file limite la simultanéité, pas le volume).
* La conversation partagée n’est visible qu’en direct : le serveur n’en garde aucun historique ; le CLI la lit une fois (`server join`), sans vue temps réel dans le terminal.
* La sauvegarde couvre la base ; le dossier `workspace/projects` se copie serveur arrêté.

## Tests

```powershell
dotnet run --project tests/MonolithHarness.Server.Tests              # tout (≈ 1 min)
dotnet run --project tests/MonolithHarness.Server.Tests -- --only accounts,providers
dotnet run --project tests/MonolithHarness.Server.Tests -- --only probe,relayqueue,sharecodes,sync,liveview   # test de provider, file et relais, codes de partage, synchronisation
dotnet run --project tests/MonolithHarness.Server.Tests -- --only announcement,positions,usage,update,service,clientfeatures   # message du moment, rang en direct, statistiques, mise à jour, service, effets côté client
```

Les tests démarrent le serveur en mémoire sur un port libre, avec un faux provider OpenAI ; la zone `worker` lance de vrais workers et la zone `clients` exerce la bibliothèque cliente (`MonolithHarness.Core.Remote`) à travers le moteur partagé.
