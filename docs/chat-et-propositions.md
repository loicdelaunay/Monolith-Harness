# Conversations, diagnostics et propositions — 1.69.0

## Organiser la barre latérale

Glissez un projet au-dessus ou au-dessous d’un autre projet. Glissez une conversation au-dessus ou au-dessous d’une autre conversation pour changer son ordre ; déposez-la sur un projet pour la déplacer dans ce projet. L’ordre est enregistré et conservé au redémarrage. Une conversation en cours doit être arrêtée avant un changement de projet. Les sections épinglées et archivées conservent leur rôle.

Sans conversation sélectionnée, **Lancer une nouvelle discussion** crée une conversation dans le projet sélectionné, ou dans le projet Conversations.

## Réglages / Test

- **Test simple** : teste le modèle de nommage configuré, les embeddings RAG et le validateur d’autorisation automatique choisi.
- **Test complet** : effectue les mêmes vérifications et interroge les modèles configurés de chaque fournisseur.
- **Arrêter** : annule la série en cours. Chaque contrôle affiche son état, sa durée et une explication en cas d’échec. Les requêtes utilisent la configuration enregistrée ; un modèle ou un validateur manquant produit une erreur explicite.

Les tests envoient de petits exemples synthétiques aux fournisseurs configurés. Les requêtes API peuvent être facturées. La commande d’exemple du validateur est uniquement analysée comme texte et n’est jamais exécutée.

## Appeler un skill

Tapez `/` puis le nom d’un skill dans le champ de discussion. La liste propose les commandes et les skills intégrés ou propres au projet ; Tab complète le choix. Par exemple : `/git explique les changements en attente`.

Le message transmis au modèle contient une demande explicite de suivre ce skill. Les autorisations et le mode de travail restent appliqués ; un skill désactivé doit être activé dans Réglages / Skills. En CLI, Tab/Entrée complète également les noms de skills ; un nom correspondant à un skill est envoyé au modèle avec la même instruction. Dans le mode Chat, seuls les skills disponibles pour ce mode sont proposés.

## Plan / Proposition / Exécution

| Mode | Comportement |
| --- | --- |
| Plan | Inspecter les sources et expliquer un plan, sans écrire ni exécuter des commandes. |
| Proposition | Inspecter les sources, lister les fichiers à modifier et préparer leurs nouveaux contenus pour revue. |
| Exécution | Utiliser les outils autorisés pour réaliser la demande. |

En **Proposition**, l’outil `propose_file_changes` enregistre des brouillons séparés des fichiers du projet. Ouvrez **Réviser les fichiers proposés** dans les réglages de la conversation ou avec `/proposals`, examinez les différences, cochez les fichiers puis choisissez **Appliquer la sélection**. La CLI propose la même revue fichier par fichier avec une confirmation finale.

Les propositions sont conservées par conversation. Une revue devenue ancienne, un fichier modifié depuis la préparation ou un dossier source différent bloque l’application. L’application conserve l’encodage des fichiers existants. Les propositions sont limitées à vingt fichiers texte des sources jointes, 128 000 caractères par contenu et deux millions d’unités cumulées (contenus et originaux). Les créations sont possibles ; les suppressions, renommages et fichiers binaires ne sont pas proposés par cet outil.

OpenCode et les moteurs ACP restent dans leur mode lecture seule et peuvent fournir des différences textuelles. Le brouillon structuré est disponible avec les modèles utilisant les outils Monolith, hors Sandbox. Le Sandbox conserve sa revue propre.

## Autorisations dans la CLI

Le dialogue présente la commande, la catégorie de risque, le validateur et son explication, le dossier de travail, le shell et le délai. **Plus d’infos sur l’impact** demande une explication au modèle courant après confirmation de la transmission au fournisseur. L’explication ne lance pas la commande ; l’autorisation reste une décision distincte.

## Affichage

Le pied du champ de saisie reste sur une ligne et compacte les libellés quand la fenêtre est étroite. Le défilement du terminal conserve la position de lecture ; si vous êtes déjà en bas, il suit les nouvelles sorties. Les diagrammes Mermaid utilisent la palette de l’application et un cache distinct pour chaque palette.
