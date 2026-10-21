# Consommation au repos — 2.3.0

## Comportement

- Le débit (200 ms) ne tourne que pendant une génération dont un indicateur est visible. Les notifications de début et de fin incluent les sous-agents du même processus et du même profil.
- L’activité (1 s) ne tourne que pour une conversation ou une validation visible en cours. Les actualisations périodiques de présentation s’arrêtent quand la fenêtre principale devient inactive ou minimisée et sont remises à jour au retour.
- Les sorties des terminaux sont conservées dans le moteur. Leur affichage réagit aux changements, regroupés sur 100 ms ; aucun minuteur ne sonde un terminal au repos. Les onglets masqués attendent leur prochain affichage.
- Le planificateur programme un réveil unique pour sa prochaine échéance. Un marqueur `database.sqlite.schedule` sans donnée de conversation notifie les autres instances GUI des modifications de tâches. Sans tâche active, aucun réveil périodique n’est nécessaire. Si les notifications de fichiers échouent, un repli d’une minute préserve la planification. La concurrence conserve le verrou par profil et l’absence de chevauchement d’une même tâche.
- La vérification des mises à jour, les éventuelles tentatives d’installation et l’entretien des conversations partagent une file de minuteurs. L’entretien est arrêté si ses automatismes sont tous désactivés.
- Les animations de fond, spinners et effets d’activité de la fenêtre principale sont suspendus hors activation. Une animation volontairement activée continue de consommer lorsqu’elle est affichée dans la fenêtre active.
- Les pages Web masquées sont suspendues via WebView2 quand le moteur Windows le permet. Sous Uno Desktop, le pont vers le contrôleur natif est optionnel et protégé contre les changements de version ; sous d’autres moteurs sans cette API, la vue est masquée mais ses scripts peuvent continuer. Une navigation en cours ou une conversation avec un agent actif protège ses onglets de la suspension. Les générations et commandes ne sont pas interrompues.

## Mesure

Dans **Outils → Débug**, les compteurs indiquent les rafraîchissements des indicateurs, terminaux, tâches et travaux de maintenance ; les réveils partagés de maintenance sont comptés séparément. Les layouts ne sont pas des images GPU. L’événement interne Uno `FrameRendered` permet d’observer les images dessinées de la fenêtre principale sans abonnement à `CompositionTarget.Rendering`, qui pourrait changer la cadence à mesurer. Si ce point d’observation est indisponible, le diagnostic le dit explicitement. Il ne mesure pas les présentations GPU matérielles.

**Mesurer le repos (30 s)** suspend les relevés automatiques du débug, attend leur fin, active la fenêtre principale et exclut son activation du relevé. Laisser l’application sans interaction. Le résultat donne le CPU moyen du processus (normalisé par le nombre de processeurs logiques) et les deltas de compteurs. Une interaction ou un changement de génération invalide l’interprétation comme repos complet. Une tâche planifiée ou une maintenance arrivant pendant cette période reste exécutée et apparaît dans les compteurs.

Le GPU et les processus externes du navigateur, des terminaux, des modèles locaux ou du serveur ne sont pas inclus dans ce CPU. Les contrôler séparément dans le Gestionnaire des tâches. Un processus en attente peut garder de la RAM sans consommer de CPU.

## Validation de cette livraison

Compilation et publications locales GUI, CLI et Server Windows réussies sans erreur ni avertissement. Les distributions GitHub utilisent le pipeline existant de construction et de validation des archives, à la même version pour les quatre éditions. Les nouveaux compteurs et le bouton de mesure permettent de relever la consommation réelle dans le profil utilisé, sans promettre un pourcentage matériel non mesuré.
