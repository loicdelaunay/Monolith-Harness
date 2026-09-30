# Outils dédiés

Le menu **Outils**, à côté de **Tâches planifiées**, ouvre trois fenêtres indépendantes. Le modèle choisi ne change pas celui du chat ; chaque fenêtre conserve son brouillon tant qu’elle reste ouverte. Les actions principales sont accentuées, en bas à droite.

Choisir un modèle direct parmi les modèles cochés des fournisseurs configurés. **Actualiser** recharge la liste après une modification dans Réglages. Les connexions compatibles OpenAI, DeepSeek et OpenCode utilisent les mêmes moteurs que le chat. Les requêtes sont isolées, sans historique du chat ni outils agent.

## Copier-coller un message formaté

Dans le Traducteur ou le Correcteur, coller normalement le message avec **Ctrl+V**. L’éditeur conserve les styles usuels du presse-papiers HTML : gras, italique, soulignement, couleurs, tailles et polices, listes, liens, paragraphes et tableaux. Le texte reste modifiable et sélectionnable.

Après l’action, **Copier** place à la fois le HTML et le texte brut dans le presse-papiers. Utiliser un collage normal dans Gmail, Outlook ou un autre éditeur riche. Un collage « sans mise en forme », ou un email en mode texte brut, retire les styles.

La traduction et la reformulation envoient uniquement les passages textuels au modèle. Attributs, styles et liens restent locaux ; le résultat remplace les passages correspondants dans la structure d’origine. Si le modèle omet un passage ou invente un identifiant, le résultat est refusé. Le résultat formaté apparaît à la fin de la réponse, après validation.

Les corrections sont appliquées aux passages vérifiés en conservant les styles environnants. Une correction qui supprimerait un séparateur de paragraphe ou de tableau est refusée. Les caractères Unicode sont conservés lors de l’alignement des corrections.

L’import conserve le HTML habituel des emails, pas une page web complète : scripts, formulaires, contenus actifs et feuilles de style externes sont retirés. Les images distantes restent des références sans être chargées dans l’éditeur ; les pièces jointes propres au client mail ne sont pas transférées. Limite de saisie : 20 000 caractères.

## Traducteur

Choisir les langues source et cible, puis **Traduire**. **Détecter la langue** demande au modèle de reconnaître la langue source. **Inverser** échange les langues et les contenus formatés si une traduction existe ; une langue source précise doit être sélectionnée. **Copier** récupère le résultat formaté.

## Correcteur d’orthographe

Choisir la langue et l’onglet **Corrections**, puis **Vérifier le texte**. Développer une suggestion affiche son explication et permet de retrouver son passage. **Appliquer**, **Ignorer** et **Tout appliquer** permettent de retenir les corrections voulues. Une modification manuelle invalide les anciennes suggestions.

Fragments sources, positions, limites Unicode et chevauchements sont vérifiés avant application. Les décalages fournis par le modèle ne sont réancrés que si le fragment exact est unique.

L’onglet natif **Reformulation** propose une version plus fluide avec la même structure et les mêmes styles. **Utiliser cette version** la replace dans le panneau d’origine ; **Copier** la copie directement.

Ces deux fenêtres affichent la progression et les erreurs utiles, sans message « Prêt » ni compteurs de tokens ou de vitesse.

## Benchmark

La suite `omh-model-tools-v3-frontier` propose **Raisonnement et code**, **Applications interactives**, ou **Suite complète**. Pour les applications, choisir un exercice précis ou les quatre. **Délai par épreuve** laisse 3, 10 ou 20 minutes au modèle ; les contrôles locaux commencent après sa réponse.

La **Série reproductible** contrôle les instances et les données des contrôles fonctionnels. **Nouvelle** en choisit une autre. Pour comparer des modèles, conserver la même suite, le même niveau, la même série, la même sélection d’applications et le même délai.

### Raisonnement et code

| Niveau | Requêtes | Contenu |
| --- | ---: | --- |
| Facile | 7 | Mesure de débit, trois petits problèmes de logique et trois bugs simples |
| Moyen | 9 | Mesure de débit, cinq questions BIG-Bench Hard et trois adaptations HumanEval |
| Difficile | 13 | Mesure de débit, six tâches ARC-AGI-2, trois problèmes de contraintes/optimisation et trois régressions d’algorithmes complètes |

Le niveau difficile remplace les petits exercices de la version précédente :

- **ARC-AGI-2** : déduire une transformation à partir d’exemples et fournir chaque grille de sortie, pixel par pixel. Six tâches sélectionnées dans un pool public de 24, avec couleurs et orientation transformées de façon cohérente.
- **Tournée asymétrique** : trouver un cycle optimal sur 14 sommets. L’application calcule l’optimum indépendamment et accepte toute tournée ayant ce coût.
- **3-SAT** : satisfaire simultanément 190 clauses sur 42 variables. Toutes les clauses sont vérifiées, sans imposer une solution unique.
- **Killer Sudoku** : satisfaire lignes, colonnes, blocs et sommes de cages. Toute grille valide est acceptée.
- **Régressions complètes** : réparer le raisonnement sur Dijkstra, les transformations affines d’un segment tree et Tarjan, puis donner l’ensemble des résultats demandés, pas une réponse à choix multiple.

Les instances sont solvables par construction ou ont une réponse de référence. Cela renforce la difficulté, sans garantir un taux d’échec donné à un modèle particulier. Ce protocole local n’est pas étalonné sur les modèles frontier et ne correspond pas aux scores officiels ARC, BBH ou HumanEval. Les données publiques peuvent avoir été vues pendant un entraînement. Voir [les sources et licences](benchmark-sources.md).

### Applications interactives et aperçu à droite

Le modèle doit produire un document HTML complet avec CSS et JavaScript intégrés. L’application affiche **son code réellement exécuté** dans le panneau de droite. Aucune démonstration préfabriquée ne remplace le résultat du modèle. Les dépendances externes et CDNs ne sont pas disponibles.

| Application | Fonctionnement demandé | Contrôles automatiques |
| --- | --- | --- |
| Rubik’s Cube 3D | Rotations de faces, caméra orbitale, zoom, mélange, solveur et lecture animée | Permutations exactes des 54 facelets ; résolution d’états importés sans historique, jusqu’à 20 coups de mélange en difficile ; vérification indépendante des mouvements retournés et de leur lecture |
| Simulation orbitale 3D | Corps gravitationnels, trajectoires, caméra, pause, pas à pas et réglages | Intégration velocity Verlet de systèmes à deux et cinq corps ; positions, vitesses et masses ; caméra indépendante de l’état physique |
| Éditeur de circuits logiques | Portes déplaçables, connexions, entrées et propagation visuelle | Huit combinaisons d’un additionneur, circuit inédit aux nœuds mélangés, déplacement et détection d’un cycle déconnecté |
| Trajets multi-agents | Grille éditable, murs, départs/arrivées, animation et curseur temporel | Absence de collisions et d’échanges de cases, durée optimale du plan commun, cas impossible et positions au temps demandé |

Les fonctions exposées par la page partagent l’état réel de son interface. Le correcteur .NET fournit les entrées et calcule ses propres références ; il ne prend pas une note déclarée par le modèle. Les mélanges du cube ne sont pas communiqués : seul l’état des faces est importé, ce qui empêche un simple renversement d’historique connu.

L’aperçu est temporairement non interactif pendant les vérifications afin d’éviter de changer les données au milieu d’un contrôle. Ensuite, on peut manipuler le résultat. **Voir cet aperçu** rouvre une application d’une épreuve précédente, **Recharger** recommence son état initial et **Copier le HTML** récupère son code. Le rapport conserve aussi ce HTML.

La page générée s’exécute dans une iframe à origine opaque, avec scripts autorisés, sans accès au parent ni pont natif. Les politiques de contenu bloquent les connexions externes. Les vérifications ont des délais ; une application bloquée échoue et les contrôles restants sont indiqués comme interrompus.

**La note visuelle porte sur le fonctionnement vérifié.** La présence d’une surface graphique, de contrôles et d’un changement lors d’une commande caméra est vérifiée, mais cela ne suffit pas à certifier la qualité graphique, la justesse de tous les gestes ou l’ergonomie : examiner le rendu dans l’aperçu.

### Note et rapport

Pendant la génération, la zone de réponse affiche la réflexion transmise par le fournisseur, puis la réponse du modèle. Si le fournisseur ne transmet pas de réflexion, seule la réponse apparaît. Les longues réflexions affichent leur partie récente ; leur texte complet est conservé dans le rapport des réponses terminées.

Le titre de chaque épreuve terminée, échouée ou annulée indique sa durée totale, génération et vérifications comprises (par exemple `2 min 14 s`). Cette durée figure aussi dans `ElapsedSeconds` du rapport et reste distincte du temps utilisé pour calculer le débit du modèle.

La carte **Note de réussite** précède le débit moyen. Chaque épreuve a le même poids ; une application réussit si tous ses contrôles réussissent. La génération de débit n’est pas notée.

| Note | Réussite |
| --- | --- |
| S | 100 % |
| A | de 80 % à moins de 100 % |
| B | de 60 % à moins de 80 % |
| C | de 40 % à moins de 60 % |
| D | de 20 % à moins de 40 % |
| E | plus de 0 % à moins de 20 % |
| F | 0 % |

La note reste **provisoire** pendant une exécution ou après interruption. Les erreurs de requête comptent comme des échecs et sont détaillées. Avant toute épreuve notée, la carte affiche « — ».

Le débit agrégé est la somme des tokens de sortie divisée par la somme des durées des requêtes complètes, latence incluse. La vérification locale des pages n’entre pas dans ce calcul. Les compteurs viennent du fournisseur ; à défaut, ils sont estimés et marqués **≈**. Les requêtes interrompues ou en erreur peuvent consommer des tokens non remontés.

**Copier le rapport JSON** inclut versions, niveau, série, sélection, délai, modèle, sources, réponses, corrigés, note, contrôles individuels, métriques et HTML des applications, sans clé API. Les requêtes utilisent la connexion configurée et sa tarification habituelle. **Arrêter** ou fermer la fenêtre annule le travail en cours.


## AI Generated detector : service ou fournisseur IA

Dans **Service de détection · configuration**, choisissez **SlopTotal** ou un fournisseur configuré, puis son modèle. **Enregistrer la sélection** mémorise ce choix sans envoyer de requête de test ; une analyse le mémorise aussi. **Actualiser les fournisseurs** reprend les modèles cochés dans Réglages. Ce choix est indépendant du modèle de la conversation.

Le mode fournisseur analyse le texte collé et les documents TXT, MD, PDF et DOCX, extraits localement. Les pages web et empreintes de constructeurs de sites restent disponibles dans le mode SlopTotal. Un document scanné nécessite un OCR préalable. Les entrées sont limitées à 100 000 caractères et 10 Mo par document ; le contexte du modèle peut imposer une limite inférieure. L’analyse par paragraphe accepte jusqu’à 128 paragraphes ; désactivez-la pour un rapport global sur un texte plus fragmenté.

Les scores produits par un modèle sont des **appréciations non étalonnées**, pas des probabilités mesurées ni des preuves d’auteur. Le rapport indique le fournisseur, le modèle et ses explications ; les citations doivent correspondre au texte original. Une réponse invalide affiche une erreur, et une réponse sans indices suffisants reste indéterminée. Les réglages de retry, l’annulation et les compteurs de consommation s’appliquent. Aucun outil d’agent n’est autorisé dans ces requêtes.

### Collage dans le traducteur et le correcteur

Les champs formatés se synchronisent après modification, sans lecture périodique au repos. Le collage conserve les styles, paragraphes, tableaux et liens ; il traite les gros contenus par lots. Les contenus dépassant 20 000 caractères ou une complexité raisonnable sont refusés avec une explication, en conservant le texte déjà présent. Copiez le résultat avec **Copier** pour obtenir le texte et son format HTML.
