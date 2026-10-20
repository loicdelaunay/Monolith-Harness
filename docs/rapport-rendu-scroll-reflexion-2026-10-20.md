# Rendu desktop, fluidité du chat et regroupement dans la réflexion

Analyse du 20 octobre 2026 — constats initiaux sur 2.2.0, mise à jour 2.2.1.

## Mise à jour 2.2.1

- Avancé est placé juste avant Réinitialisation.
- Carte graphique du rendu affiche les GPU dont une activité 3D a été observée pour le processus de l’application sous Windows. Les identifiants des compteurs sont reliés aux noms DXGI. Cette observation inclut l’activité depuis le lancement et peut mentionner plusieurs cartes ; elle ne prétend pas identifier à elle seule la carte d’une surface précise. Aucun nom de carte installée n’est présenté comme actif sans observation correspondante. Si le moteur fonctionne en logiciel, la section indique CPU ; si le nom n’est pas disponible, elle l’indique explicitement.
- Rafraîchissement affiche les Hz signalés par Windows pour l’écran de la fenêtre principale, relus chaque seconde lorsque la section est affichée. La cadence de rendu réellement configurée au démarrage est présentée séparément de cette fréquence et de la sélection en cours de modification. Ce n’est pas une mesure des images effectivement dessinées.
- Les messages assistant contenant suggest_actions restent dans le chat, même s’ils contiennent également un autre appel d’outil. Cette exception est appliquée à la réception, à la réouverture et lors du remontage d’un tour virtualisé. Une carte de propositions suivant directement une réponse sans métadonnées d’outil préserve aussi cette réponse lors de la lecture de l’historique.

Sources des observations Windows : [compteurs PDH](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetrawcounterarrayw), [identification DXGI](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiadapter1-getdesc1), [mode d’affichage courant](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsw).

## Portée

Inspection du code actuel et des sources officielles Uno correspondant à la version installée : Uno.WinUI 6.7.135 / SDK 6.7.30. Les constats ci-dessous décrivent des mécanismes vérifiés dans le code ; leur poids dans les saccades ressenties sur une conversation précise reste à mesurer. Aucun profilage de la session utilisateur ni test interactif de défilement n’a été effectué pour ce rapport.

Le rapport du 6 octobre décrivait une version antérieure. Depuis, la virtualisation, le rendu natif des mathématiques et de certains diagrammes, ainsi que des caches de rendu ont été ajoutés. Il serait donc inexact d’attribuer aujourd’hui toutes les saccades à une absence de virtualisation ou à un navigateur obligatoire pour chaque formule.

## 1. Réglages ajoutés : Avancé

| Réglage | Comportement |
| --- | --- |
| Technologie de rendu | Automatique, OpenGL ou Vulkan sur l’édition Uno Desktop Windows/Linux. Automatique conserve le défaut du moteur ; cette version utilise OpenGL par défaut. |
| Accélération matérielle | Activée par défaut. Désactivée : le moteur de l’interface utilise le CPU. Les WebView et le calcul des modèles IA ont leur propre configuration. |
| Consommation / carte graphique | Préférence existante déplacée de Général vers Avancé : Auto, Haute performance ou Basse consommation. Le choix précédemment enregistré est conservé. Windows et les pilotes décident de la carte effectivement utilisée. |
| Rafraîchissement Auto | Conserve le comportement antérieur : sous Windows, cadence temporisée déterminée par l’écran principal au démarrage, entre 60 et 240 images/s ; sur les autres plateformes, synchronisation par défaut du moteur. |
| En fonction de l’écran | Active le suivi de la fréquence d’affichage par le moteur. Sous Windows, OpenGL utilise la synchronisation verticale ; Vulkan et le rendu logiciel sont cadencés par le compositeur Windows. Le suivi des changements d’écran dépend du support de la plateforme. |
| Manuel | Cadence cible entière de 15 à 500 images/s. Ce réglage ne change pas les Hz configurés dans Windows. Il ne garantit pas que la machine pourra produire toutes ces images. |

Ces choix sont persistés dans les préférences du profil, puis lus avant la création de l’hôte graphique au prochain démarrage. Le champ manuel est affiché uniquement en mode Manuel. En cas de préférence invalide, le chargement revient aux valeurs par défaut. Si le profil n’est pas encore créé ou ne peut pas être lu au démarrage, le moteur utilise aussi les valeurs par défaut et journalise une erreur de lecture éventuelle.

Vulkan suit le repli prévu par Uno : Vulkan → OpenGL → logiciel si nécessaire. Ce repli permet de demander Vulkan sans présenter son activation effective comme garantie. Les diagnostics distinguent le rendu GPU/CPU observé et la configuration demandée au démarrage.

Sous macOS, le rendu accéléré utilise Metal : le sélecteur OpenGL/Vulkan est désactivé, et le basculement GPU/CPU reste disponible. Dans l’édition WinUI native, ces contrôles propres au moteur Skia sont désactivés avec une explication ; la préférence GPU Windows reste disponible.

Références : [onglet Avancé](E:/Projects/MonolithHarness/src/MonolithHarness.App/AdvancedSettingsUi.cs:10), [démarrage graphique](E:/Projects/MonolithHarness/src/MonolithHarness.App/DesktopRendering.cs:15), [préférences conservées](E:/Projects/MonolithHarness/src/MonolithHarness.Core.Desktop/FeatureSettings.cs:61), [documentation Uno](https://platform.uno/docs/articles/features/using-vulkan-rendering.html), [source Win32RenderPacer 6.7.135](https://github.com/unoplatform/uno/blob/6.7.135/src/Uno.UI.Runtime.Skia.Win32/Rendering/Win32RenderPacer.cs).

## 2. Pourquoi le scroll peut sembler peu fluide

### Mécanismes confirmés

| Mécanisme actuel | Effet plausible sur le défilement |
| --- | --- |
| La virtualisation travaille par tour de conversation, depuis un message utilisateur jusqu’au suivant, et non par petite portion de texte. Un tour contenant de nombreux outils et réponses peut donc être très volumineux. | Une seule reconstruction peut encore produire beaucoup de contrôles, même lorsque peu de tours sont affichés. |
| Les tours proches de l’écran sont montés à la demande ; les autres sont détachés, avec une hauteur conservée ou estimée. Une marge de deux hauteurs d’écran est prévue. | En lecture rapide d’une longue conversation, de nouvelles bulles doivent être reconstruites pendant le geste. |
| Le travail de virtualisation est regroupé par une minuterie de 40 ms, avec au plus deux montages par passage. Le montage appelle néanmoins RenderHistory pour un tour entier. | Le regroupement limite les appels ; il ne plafonne pas la durée de reconstruction d’un gros tour sur le thread de l’interface. Un tour coûteux peut retarder la prise en compte des gestes. |
| La recherche des tours à afficher parcourt la liste des tours et conserve un élément de hauteur pour chacun. | Le coût augmente avec la longueur de l’historique. Il est inférieur à celui de toutes les bulles riches montées, mais n’est pas constant. |
| La hauteur estimée est remplacée par la hauteur réelle après montage ; les images et vues riches peuvent modifier cette hauteur plus tard. | L’application corrige le décalage de lecture lorsqu’un élément situé au-dessus change de taille. Cette correction protège l’ancrage, mais peut être perceptible comme un à-coup. |
| Pendant une génération, les mises à jour visibles du texte arrivent au plus une fois toutes les 70 ms. Les blocs stables sont réutilisés ; la fin de réponse déclenche une analyse complète. | La croissance du texte progresse par paliers d’environ 14 mises à jour/s. Ce chiffre ne représente pas la fréquence de rendu de la fenêtre. Le rendu Markdown, les styles et la mise en page restent du travail supplémentaire. |
| Le suivi automatique du bas réagit aux changements de mise en page, puis applique un ChangeView sans animation. | Quand le contenu grandit, la position suit par corrections. Ce mécanisme est suspendu quand l’utilisateur remonte ; il ne suffit donc pas à expliquer toutes les saccades pendant une lecture manuelle plus haut. |
| Le mode Auto Windows utilise une cadence temporisée, sans synchronisation verticale, calculée une fois depuis l’écran principal. | Sur un autre écran ou après un changement de fréquence, la cible peut ne plus correspondre à l’écran utilisé. Même avec une fréquence nominale égale, un timer n’aligne pas chaque image sur le rafraîchissement de l’écran. |
| Les réponses peuvent contenir des images, du code coloré, des mathématiques natives et, si leur option est activée, des WebView. | Le coût de reconstruction et de mesure dépend du contenu. Un changement OpenGL/Vulkan n’élimine pas les coûts de mise en page sur le CPU. |

Références : [minuterie et virtualisation](E:/Projects/MonolithHarness/src/MonolithHarness.App/ConversationVirtualization.cs:14), [parcours et montage](E:/Projects/MonolithHarness/src/MonolithHarness.App/ConversationVirtualization.cs:184), [estimation et correction de hauteur](E:/Projects/MonolithHarness/src/MonolithHarness.App/ConversationVirtualization.cs:228), [suivi du bas](E:/Projects/MonolithHarness/src/MonolithHarness.App/ChatAutoScroll.cs:99), [réception du texte](E:/Projects/MonolithHarness/src/MonolithHarness.App/MainWindow.cs:2480), [réutilisation du Markdown](E:/Projects/MonolithHarness/src/MonolithHarness.App/MarkdownRenderer.cs:170), [limite finale du rendu](E:/Projects/MonolithHarness/src/MonolithHarness.App/MainWindow.cs:790).

### Protections déjà présentes

- Virtualisation activée par défaut, marge autour de l’écran, conservation des éléments sélectionnés et limitation des montages par passage.
- Réutilisation des blocs de texte stables pendant la génération et report de certaines mises à jour pendant la lecture d’une autre zone.
- Mathématiques natives et prise en charge native de certains diagrammes ; WebView du chat désactivée par défaut sur les nouveaux profils.
- Cache Mermaid et protection contre le renvoi d’un contenu Web inchangé. Le clignotement décrit dans le rapport ancien ne doit donc pas être supposé identique sans reproduction.
- Suspension de l’animation de fond pendant le scroll et lorsque la fenêtre est inactive.

Références complémentaires : [rendu natif](E:/Projects/MonolithHarness/src/MonolithHarness.App/MarkdownRenderer.cs:613), [cache de la vue Web](E:/Projects/MonolithHarness/src/MonolithHarness.App/AdvancedMarkdownView.cs:152), [animation de fond](E:/Projects/MonolithHarness/src/MonolithHarness.App/ChatBackground.cs:25).

### Diagnostic à privilégier

Pour un scroll saccadé sur une réponse terminée, les deux premières pistes sont la reconstruction des tours riches et le cadencement des images. Pendant une réponse en cours, les mises à jour et changements de hauteur s’ajoutent à ces coûts. Un repli effectif vers le rendu logiciel peut aussi amplifier le problème ; sa présence sur cette machine n’a pas été mesurée ici.

Une fenêtre à 60 images/s dispose d’environ 16,7 ms par image ; à 144 images/s, environ 6,9 ms. Demander davantage d’images ne suffit pas si le travail de l’interface dépasse ce budget.

### Suite recommandée

1. Mesurer les durées de montage, mise en page et présentation sur une même conversation : texte seul puis code/images/diagrammes, lecture arrêtée puis génération active. Relever le moteur réellement utilisé, l’écran et son taux de rafraîchissement.
2. Comparer Auto et En fonction de l’écran sur cette machine. Vulkan constitue une option supplémentaire à comparer, sans garantie de gain pour la partie CPU.
3. Réduire la durée maximale d’un montage : découper les gros tours, réutiliser davantage leurs contrôles et répartir le travail selon un budget de temps plutôt qu’un nombre fixe de tours.
4. Stabiliser les hauteurs et les corrections d’ancrage ; éviter les reconstructions qui ne correspondent pas à un changement visible.

Ce chantier de fluidité nécessite une mesure avant/après. La présente mise à jour ajoute les commandes de rendu et documente les causes ; elle ne constitue pas une refonte de la virtualisation.

## 3. Pourquoi une réponse passait dans la bulle de réflexion en 2.2.0

La cause identifiée en 2.2.0 était une règle de regroupement visuel trop large. Les mécanismes ci-dessous décrivent ce diagnostic ; l’exception appliquée aux propositions d’actions en 2.2.1 est précisée en tête du rapport.

### Pendant la réponse

À la fin d’un message assistant, dès que le fournisseur retourne au moins un tool_call, MainWindow appelle MoveToActivity. Cette méthode retire la carte complète du message de la liste principale et la place dans les détails du groupe de réflexion. Le texte enregistré reste un message assistant.

La règle traite tous les outils de la même façon. Ainsi, une réponse lisible telle que « J’ai trouvé le problème ; voici les actions possibles » est déplacée dès qu’elle accompagne suggest_actions. La carte des boutons est ensuite ajoutée directement au chat, ce qui accentue la séparation entre la réponse et ses actions.

Références : [appel systématique avant les outils](E:/Projects/MonolithHarness/src/MonolithHarness.App/MainWindow.cs:2523), [déplacement de carte](E:/Projects/MonolithHarness/src/MonolithHarness.App/ReasoningGroupUi.cs:84), [carte de propositions dans le chat](E:/Projects/MonolithHarness/src/MonolithHarness.App/ActionSuggestionsUi.cs:11).

### À la réouverture ou au remontage d’un tour

IntermediateMessage classe également comme intermédiaire :

- tout message contenant des tool_calls ;
- tout message assistant suivi d’un autre message assistant avant le prochain message utilisateur.

Cette seconde règle peut déplacer du texte même sans appel d’outil attaché à ce message. Elle est utilisée lors du chargement de l’historique et de la reconstruction d’un tour virtualisé.

Références : [heuristique historique](E:/Projects/MonolithHarness/src/MonolithHarness.App/ReasoningGroupUi.cs:90), [utilisation dans RenderHistory](E:/Projects/MonolithHarness/src/MonolithHarness.App/MainWindow.cs:629).

### Correction recommandée

Conserver la réponse destinée à l’utilisateur à côté des propositions d’actions et des questions. Traiter explicitement les outils d’interaction comme suggest_actions et question ; conserver les détails techniques et le champ de raisonnement dans le groupe d’activité. La même classification devra être utilisée en direct et lors de la lecture de l’historique, avec une règle définie pour les messages contenant à la fois outils techniques et propositions.

À la demande suivante de l’utilisateur, l’exception pour suggest_actions a été implémentée dans la version 2.2.1, y compris pour les messages mêlant propositions et outils techniques. Les autres critères de regroupement restent applicables aux messages techniques.

## Validation de cette mise à jour

- Compilation Uno Desktop Release réussie, sans avertissement ni erreur.
- Aucun scénario automatisé ni essai interactif ajouté ou exécuté pour cette demande.
- Publications locales GUI, CLI et Server réussies dans artifacts/GUI, artifacts/CLI et artifacts/SERVER. Les trois exécutables ont été vérifiés : version 2.2.1, publications réussies.
- Contrôle des différences Git sans erreur d’espacement. Le code et le rapport ont ensuite été poussés sur GitHub à la demande de l’utilisateur. Les quatre releases 2.2.1 sont publiques ; leurs 13 fichiers et les empreintes SHA-256 ont été vérifiés. Le build de release a réussi après reprise d’un téléversement interrompu par une erreur réseau 408.
