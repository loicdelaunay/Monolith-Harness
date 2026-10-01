# Affichage et entretien des conversations

## Apparence → Densité des messages

- **Compact** : texte de 13,5 px, interligne de 18 px, marges réduites et 8 px entre les cartes.
- **Normal** (par défaut) : texte de 14,5 px, interligne de 21 px et 12 px entre les cartes.
- **Espacé** : texte de 15,5 px, interligne de 25 px et 20 px entre les cartes.

Ces dimensions sont les valeurs à 100 % de zoom. Les choix de police et les raccourcis de zoom restent indépendants. Le réglage s’applique aux messages visibles et futurs ; les listes utilisent un seul saut de ligne entre leurs éléments. Shift+Entrée insère une nouvelle ligne, Entrée envoie le message.

## Général

**Nommage automatique** : après activation et choix du fournisseur/modèle, sélectionner « Dès le premier message de l’utilisateur » ou « Après la fin de la première réponse ». Le premier choix utilise seulement le message initial et n’attend pas la génération principale. Le nommage manuel reste disponible par clic droit.

**Section Attention requise** : activée par défaut, elle apparaît au-dessus des conversations épinglées lorsqu’une question, une autorisation ou une erreur non lue requiert votre attention. La désactiver masque ce regroupement sans modifier les notifications ou les badges des projets.

**Entretien** : deux interrupteurs distincts, désactivés par défaut. L’archivage intervient après 30 jours d’inactivité et la suppression définitive des archives après 45 jours d’inactivité au total. Les délais sont configurables de 1 à 3650 jours ; si les deux options sont actives, le délai de suppression doit être supérieur à celui d’archivage. Le contrôle s’exécute au démarrage, après l’enregistrement des réglages et toutes les heures tant que la GUI est ouverte. Les discussions affichées, épinglées, favorites, en cours ou en attente d’action/envoi sont conservées. La dernière réponse achevée compte dans l’activité, même si la génération a duré longtemps. Un historique ancien sans date d’activité fiable est conservé. La suppression retire les messages, images et mémoires appartenant à la conversation ; les sources sur disque et les statistiques de consommation restent disponibles.

**Carte graphique du rendu** : sous Windows, Auto laisse le système gérer le choix, Haute performance privilégie le GPU dédié et Basse consommation le GPU intégré. La préférence est enregistrée pour l’exécutable courant et nécessite son redémarrage. Le résultat dépend des cartes disponibles et des pilotes ; cela concerne l’interface et non les calculs du fournisseur IA. Sur les autres plateformes, le choix reste géré par le système.

## Activité des outils

Les résultats des appels d’outils affichent « Succès en … » ou « Échec en … », avec une durée réellement mesurée et sauvegardée dans l’historique. Les anciens appels sans durée connue gardent seulement leur statut. Pendant l’exécution, la puce indique le nom de l’outil, un détail issu de ses paramètres (chemin, URL, commande…) et le temps écoulé. Ce détail décrit l’action demandée ; il ne prétend pas mesurer l’avancement interne d’un programme externe. Les outils natifs gérés entièrement par un fournisseur externe peuvent ne pas fournir ces informations.

## Préchargement du contexte

Pour les connexions directes aux API de chat, la GUI et le CLI affichent **Préchargement du contexte · ≈ N tokens · durée** avant les premiers tokens de sortie. Cela concerne chaque requête, y compris après réouverture d’une conversation, après un appel d’outil et lors d’une nouvelle tentative automatique.

Dans la GUI, survolez la puce pour voir la phase observée (préparation de la requête, envoi/connexion au fournisseur, attente des premiers tokens), le contexte estimé par rapport à la limite du modèle, le nombre de messages transmis et la taille de la requête encodée lorsqu’elle est connue. Les instructions, résultats d’outils et définitions d’outils participent à l’estimation. Le compteur repart à zéro pour une nouvelle tentative et reste associé à sa conversation lorsque vous naviguez ailleurs.

Un événement vide ne suffit pas à terminer le préchargement : le premier texte, raisonnement ou fragment d’appel d’outil le termine. Le statut passe alors à la réflexion ou à la réponse en cours. L’affichage est temporaire et ne s’enregistre pas comme texte du modèle dans l’historique. Les outils gérés entièrement par un fournisseur externe comme OpenCode conservent leurs propres indications.

L’attente peut comprendre le transfert, le traitement du contexte ou la file d’attente du fournisseur. L’API ne communique pas de pourcentage de traitement ni l’état actuel du cache ; l’application n’affiche donc aucune progression interne supposée. Ce compteur d’attente est distinct du débit de génération.
