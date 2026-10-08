# Outils natifs sans WebView — 1.70.0

Le correcteur et le traducteur utilisent désormais des contrôles natifs, sans créer de WebView, exécuter de JavaScript ou charger de ressources distantes pour leur interface.

## Texte et mise en forme

- **Texte** : saisie, sélection et correction dans une zone de texte native.
- **Mise en forme** : aperçu natif des titres, gras, italique, souligné, barré, listes et liens. La couleur principale suit le thème de l’application.
- Un collage remplaçant le document complet conserve les styles HTML. Les transformations et corrections continuent d’utiliser les passages formatés, puis **Copier** fournit du texte et du HTML au presse-papiers.
- Les petites modifications manuelles conservent les styles du passage d’origine. Une modification de la structure, comme la suppression d’une séparation de paragraphes, peut réinitialiser les styles ; l’outil le signale et conserve le texte. Les collages partiels peuvent hériter des styles du passage où ils sont insérés.
- Le rendu natif est simplifié : les cellules de tableaux sont séparées dans le texte, les images sont indiquées par leur libellé et les styles CSS avancés ne sont pas reproduits à l’identique. Les données HTML conservées servent à la copie formatée.
- Les collages sont bornés à 20 000 caractères utiles, trois millions de caractères HTML et une complexité limitée. Leur analyse s’effectue hors du thread d’interface. Les aperçus très fragmentés se replient vers du texte pour limiter le nombre de contrôles natifs.

## Réglages / Général

**Autoriser les aperçus WebView dans les outils** est **désactivé par défaut**. Le correcteur et le traducteur restent natifs même si cette option est activée.

Le benchmark de raisonnement et de code fonctionne sans WebView. Les épreuves d’applications HTML interactives ont besoin d’un moteur web pour exécuter et vérifier l’application ; leur lancement demande d’activer cette option sur un poste compatible. Un aperçu HTML conservé peut être affiché comme code source natif lorsque l’option est désactivée, sans exécution.

Le réglage concerne les fenêtres d’outils. Le navigateur Web des conversations conserve son choix dans Réglages / Navigateur, et les rendus Mermaid/mathématiques du chat conservent leurs réglages. Sur un poste qui interdit toute WebView, désactivez aussi ces rendus enrichis et choisissez un navigateur externe ou Désactivé.

Cette modification supprime le besoin de WebView pour les deux éditeurs concernés. La correction du blocage décrit reste à confirmer sur les PC affectés ; elle ne crée pas un mécanisme général de détection des freezes.
