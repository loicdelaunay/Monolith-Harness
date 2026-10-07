# Affichage des conversations

## Virtualisation

**Paramètres / Général / Virtualiser l’affichage du chat** est activé par défaut. Les tours proches de l’écran conservent leurs contrôles enrichis, images et diagrammes ; les autres gardent un emplacement dans l’historique.

Désactiver cette option conserve toutes les bulles enrichies de la conversation affichée. Cela utilise davantage de mémoire. Le changement s’applique immédiatement, avec réalisation progressive des bulles pour conserver la réactivité ; les sélections et la recherche restent accessibles.

## Débit dans la zone de saisie

Le débit compact est calculé à partir des tokens reçus pendant les **deux dernières secondes**, pour le modèle en cours dans cette conversation. Au début d’un appel, la durée réellement disponible est utilisée. Une pause sans tokens réduit ce débit jusqu’à zéro. Sans compteur du fournisseur, la mesure est estimée à partir du texte et du raisonnement et marquée `≈`.

Chaque nouvel appel au modèle ou nouvelle tentative repart avec sa propre mesure. La dernière mesure récente peut rester affichée après la fin du tour pendant cette session. Une conversation rechargée sans mesure récente affiche `—` ; son tooltip conserve les statistiques historiques habituelles. Le calcul du tooltip est inchangé.

## Validation des commandes

Pendant une analyse automatique, le bouclier **Vérification…** apparaît à droite de la chip d’activité, avec un anneau animé. Son survol indique l’étape : attente, préparation, analyse ou contrôle de la décision, ainsi que le temps écoulé. Le validateur ne fournit pas de pourcentage de progression. L’indicateur disparaît à la fin de la vérification, y compris après erreur ou annulation.

## Pièces jointes et recherche

Le bouton **Pièces jointes et ressources (N)** replie ou déplie les aperçus sans détacher les fichiers ni les images. Le choix reste associé à la conversation pendant la session.

**Ctrl+F** recherche dans les messages utilisateur et assistant. Les correspondances sont surlignées dans le texte ; Entrée et les flèches passent d’un message trouvé à l’autre. Les bulles virtuelles sont réalisées au besoin. Effacer le texte ou fermer la recherche retire les surbrillances.
