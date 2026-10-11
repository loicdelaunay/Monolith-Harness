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
| `reset-password --email <adresse>` | Émet un code de récupération d’une heure (accès console = preuve de propriété) |
| `restore --from <sauvegarde.sqlite>` | Restaure une sauvegarde, serveur arrêté ; l’ancienne base est conservée |

Ordre de priorité : valeurs par défaut < `server.json` (créé au premier démarrage) < variables `MONOLITH_SERVER_*` < ligne de commande.

Au premier démarrage, un compte administrateur est créé une seule fois ; les redémarrages ne réinitialisent pas ses identifiants. Définissez votre propre mot de passe avant tout accès distant : la commande console `MonolithHarness reset-password --email admin@admin.com` émet un code temporaire à utiliser avec « Recovery code » sur la page de connexion. Voir aussi le [guide anglais](../../docs/server.md).

## Fonctionnalités activables (administration > Fonctionnalités et réglages)

| Ensemble | Par défaut | Résumé |
|---|---|---|
| Exécution cloud | désactivée | Projets cloud, conversations et requêtes exécutées sur le serveur dans un workspace isolé |
| Partage en temps réel | activé | Une conversation locale suivie en direct via un bridge ; spectateurs : lire et commenter |
| Tâches planifiées cloud | désactivée | CRON + fuseau, sans client connecté |
| File d’attente | activée | Utilisateurs / exécutions simultanés, capacité, équité entre utilisateurs |
| Providers partagés | activé | Catalogue côté serveur, clés chiffrées, relais OpenAI-compatible |

Une opération exige : fonctionnalité activée **et** permission du rôle **et** droit sur la ressource (propriétaire/membre du projet, participant du partage, accès au provider). Les rôles sont personnalisables ; `Administrator` détient toutes les permissions.

## API

* REST : `/api/...`, documentation `/openapi/v1.json`, interface `/swagger` (jeton `Authorization: Bearer`, obtenu avec `POST /api/auth/login`).
* WebSocket : `/ws` (en-tête Bearer ou premier message `{"type":"auth","token":"…"}`). Messages : `subscribe`/`unsubscribe` (`run:{id}`, `project:{id}`, `share:{id}`, `queue`, `server` ; `user:{id}` est implicite), `ping`, et pour le partage `bridge.attach`, `bridge.snapshot`, `bridge.event`, `bridge.detach`, `share.comment`. Les événements arrivent sous `{"type":"event","topic","name","data"}`.
* Relais de providers : `/api/providers/{id}/relay/{chat/completions|embeddings|models}` ; la clé amont est ajoutée par le serveur.

## Sécurité

* Mots de passe : PBKDF2-HMAC-SHA512 salé (210 000 itérations). Jetons de session opaques, seul leur SHA-256 est stocké ; révocation immédiate (les WebSocket de la session sont fermés).
* Limitation des tentatives par compte et par adresse, réponse identique pour un compte inconnu.
* Récupération : code à usage unique d’une heure émis par un administrateur (ou `reset-password` en console) ; il révoque toutes les connexions.
* Cookie web `HttpOnly` + `SameSite=Strict`, refus des écritures inter-sites, poignée de main WebSocket protégée.
* Clés de providers chiffrées (Data Protection) et jamais renvoyées par l’API.
* Journal d’audit des opérations sensibles ; sauvegardes cohérentes de la base (`VACUUM INTO`).

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
* Partage en direct : l’appareil garde l’exécution et les outils ; le client relaie les événements au serveur, les spectateurs lisent et commentent.
* Sécurité du lien : HTTPS par défaut ; HTTP en clair refusé sauf boucle locale ou option explicite « accepter HTTP ». Le mot de passe n’est jamais conservé ; seul le jeton de session est stocké, protégé par le système (DPAPI / trousseau), dans `server-link.json` à côté de la base. Une session révoquée côté serveur est détectée et le client demande de se reconnecter.

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
* Pas d’inscription libre ni d’envoi d’e-mails ; pas de quotas de consommation par utilisateur sur le relais.
* La sauvegarde couvre la base ; le dossier `workspace/projects` se copie serveur arrêté.

## Tests

```powershell
dotnet run --project tests/MonolithHarness.Server.Tests              # tout (≈ 1 min)
dotnet run --project tests/MonolithHarness.Server.Tests -- --only accounts,providers
```

Les tests démarrent le serveur en mémoire sur un port libre, avec un faux provider OpenAI ; la zone `worker` lance de vrais workers et la zone `clients` exerce la bibliothèque cliente (`MonolithHarness.Core.Remote`) à travers le moteur partagé.
