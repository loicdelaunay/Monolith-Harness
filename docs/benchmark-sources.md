# Sources des benchmarks de modèles

Les fichiers suivants sont intégrés au moteur desktop, avec leurs licences MIT ou Apache-2.0 complètes dans `src/MonolithHarness.Core.Desktop/Benchmarks`. Aucun téléchargement de dataset n’est nécessaire pendant un benchmark.

## BIG-Bench Hard

- Auteurs : Mirac Suzgun et collaborateurs, *Challenging BIG-Bench Tasks and Whether Chain-of-Thought Can Solve Them* (2022).
- [Dépôt original](https://github.com/suzgunmirac/BIG-Bench-Hard), révision `9ee07bd481feebf959a6b59d61ea57bdcf30964d`.
- [Publication](https://arxiv.org/abs/2210.09261) ; [licence MIT](https://github.com/suzgunmirac/BIG-Bench-Hard/blob/9ee07bd481feebf959a6b59d61ea57bdcf30964d/LICENSE).
- Notice : `Benchmarks/BIG-Bench-Hard.LICENSE.txt`, Copyright (c) 2022 suzgunmirac.
- Sélection exacte, indices à partir de zéro, questions, cibles, URLs figées et marqueur de dataset : `Benchmarks/bbh-subset.json`.
- Les consignes et cibles originales sont conservées. L’adaptation demande seulement de renvoyer la réponse dans un objet JSON. Cette sélection n’est pas une évaluation officielle complète de BBH.

## HumanEval

- Auteurs : OpenAI, *Evaluating Large Language Models Trained on Code* (2021).
- [Dépôt original](https://github.com/openai/human-eval), révision `6d43fb980f9fee3c892a914eda09951f772ad10d`.
- [Publication](https://arxiv.org/abs/2107.03374) ; [licence MIT](https://github.com/openai/human-eval/blob/6d43fb980f9fee3c892a914eda09951f772ad10d/LICENSE).
- Notice : `Benchmarks/HumanEval.LICENSE.txt`, Copyright (c) OpenAI.
- Spécifications sources des tâches `HumanEval/10`, `/12`, `/20`, `/32`, `/115` et `/129` dans `Benchmarks/humaneval-source.json`.
- Monolith Harness ajoute les bugs, les entrées de régression et les formats de réponse utilisés dans l’interface. Le code généré n’est pas exécuté ; une liste d’expressions acceptées et les sorties attendues servent à la correction. Le résultat n’est donc pas HumanEval pass@1.

## Reproductibilité

Le rapport exporté inclut la suite `omh-model-tools-v3-frontier`, le niveau, la version du logiciel, la série, les identifiants sélectionnés, le délai, l’URL et l’identifiant exact de chaque source, la nature de son adaptation, les requêtes, les réponses, les corrigés, la note et les métriques. Le débit est mesuré sur la durée complète des requêtes, hors vérification fonctionnelle locale. Échantillonnage et raisonnement restent ceux du fournisseur.

## ARC-AGI-2 · niveau difficile depuis 1.21.0

- [Dépôt original ARC Prize](https://github.com/arcprize/ARC-AGI-2), révision `f3283f727488ad98fe575ea6a5ac981e4a188e49`.
- [Rapport technique officiel](https://arcprize.org/blog/arc-agi-2-technical-report) ; [licence Apache-2.0](https://github.com/arcprize/ARC-AGI-2/blob/f3283f727488ad98fe575ea6a5ac981e4a188e49/LICENSE).
- Notice complète : `Benchmarks/ARC-AGI-2.LICENSE.txt`. Données : `Benchmarks/arc-agi-2-subset.json`.
- Pool : les 24 premiers noms de fichiers du répertoire public `data/evaluation`, triés. Sélection de six tâches selon la série ; rotation et permutation cohérentes des couleurs, avec 0 conservé. Ces transformations sont effectuées à l’exécution et décrites dans chaque rapport.
- Les entrées et sorties d’exemples sont transmises, ainsi que les seules **entrées** de test. Les sorties attendues restent dans le correcteur. Il exige tous les pixels et toutes les dimensions exacts, en **une tentative**. Le protocole diffère donc du score officiel ARC et de ses règles de plusieurs essais.
- Le jeu est public et peut avoir été vu par certains modèles. Ni le sous-ensemble ni les transformations n’ont été calibrés sur un panel de modèles frontier. Les métriques publiées pour d’autres protocoles ne s’appliquent pas à cette application.

## Épreuves originales Monolith Harness

Le niveau difficile complète ARC avec une tournée asymétrique sur 14 sommets (optimum calculé par programmation dynamique Held–Karp), 190 clauses 3-SAT sur 42 variables et un Killer Sudoku construit depuis une grille valide. Les solutions soumises sont contrôlées par contraintes : une autre solution valide est acceptée, et une tournée non optimale échoue. Trois régressions d’algorithmes exigent des tableaux de résultats complets : plus courts chemins, compositions affines et composantes fortement connexes.

Les quatre applications HTML sont des tâches originales, pas des échantillons d’un benchmark tiers. Le correcteur fonctionnel .NET ne fait pas confiance à une note renvoyée par la page : il compare les données de son API à ses propres états et calculs. Des contrôles de présence du rendu et de changement de vue complètent ces vérifications ; ils ne certifient pas à eux seuls le réalisme du rendu, les gestes ou l’ergonomie. Ces aspects restent visibles dans l’aperçu pour inspection.
