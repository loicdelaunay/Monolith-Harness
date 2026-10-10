# Réponses avec images et HTML — 1.88.0

## GUI et Android

- L’IA peut afficher une image avec `![Description](https://serveur-public/image.png)`. PNG, JPEG, GIF et WebP sont pris en charge, avec une limite de 8 Mo. Les URL privées et redirections non HTTPS sont refusées.
- Les images `data:image/...;base64,...` peuvent être affichées sans réseau. Sur desktop, les images locales doivent appartenir aux ressources jointes à la discussion ou aux images et scripts générés dans cette discussion ; les accès sont vérifiés par SourceAccess.
- Pour un aperçu HTML/CSS, demander un bloc de code `html`, avec le CSS dans `<style>`. Une carte de prévisualisation et une section **Voir le code HTML** apparaissent dans le message. Un bloc CSS séparé dans la même réponse complète aussi l’aperçu.
- L’historique conserve le contenu d’origine et reconstitue ses aperçus. Le rendu d’une image ou d’un document invalide affiche un état lisible ; il ne coupe pas la génération du modèle.

## Desktop

La carte HTML utilise un rendu natif simplifié, sans navigateur : texte, titres, listes, images et styles courants (couleur, taille, espacement, bordure). **Ouvrir l’aperçu** ouvre une fenêtre distincte.

Dans **Paramètres / Chat**, **Autoriser les aperçus WebView dans le chat** permet le rendu HTML/CSS complet et le JavaScript dans un aperçu isolé. Ce réglage est indépendant de **Paramètres / Général / Autoriser les aperçus WebView dans les outils**, utilisé notamment par les benchmarks. Les profils existants conservent leur ancienne autorisation sur les deux options jusqu’à leur modification ; les nouveaux profils laissent WebView désactivé.

**Prévisualiser directement le HTML**, désactivé par défaut, affiche l’aperçu interactif dans la bulle dès que le document HTML complet est reçu, sans cliquer sur **Ouvrir l’aperçu**. Cette option nécessite l’autorisation WebView du chat. Le bouton permet toujours d’ouvrir une fenêtre plus grande. Les documents complets en cours de réception ne sont pas exécutés à chaque fragment.

L’ouverture répétée d’une carte réactive son unique fenêtre d’aperçu. À la fermeture ou au retour dans le chat, son aperçu intégré est restauré. Le moteur web est libéré lorsqu’une bulle est durablement retirée de l’écran par la virtualisation ou lorsqu’une fenêtre d’aperçu est fermée. Son état JavaScript est alors réinitialisé. Si WebView échoue ou reste désactivé, le rendu natif simplifié et le code restent disponibles. Les outils correcteur et traducteur restent natifs.

Les scripts restent dans une iframe isolée, sans réseau, accès aux fichiers, API natives ni pont vers l’application, y compris dans les aperçus intégrés au chat.

## Android : Create web app

Activer **Create web app** dans **Paramètres / Skills**, puis écrire par exemple :

> /create-web-app Crée un petit minuteur avec les boutons démarrer et réinitialiser.

La sélection du skill ajoute seulement son nom dans la saisie. À l’envoi, le modèle reçoit les consignes de génération d’un document HTML autonome avec CSS et JavaScript intégrés et une interface adaptée au téléphone.

Toucher la bulle de réponse ou **Ouvrir en plein écran** une fois la génération terminée. **Fermer** ou le retour Android ramène à la discussion.

L’application générée fonctionne hors ligne dans un aperçu isolé. Les dépendances CDN, requêtes réseau, fichiers du téléphone, stockage persistant et API natives ne sont pas disponibles. Son état JavaScript est réinitialisé à la fermeture ; son code reste dans le chat. Les fichiers et le web utilisés par les autres skills continuent de suivre les autorisations existantes.

## Bibliothèques locales : Tailwind CSS et Chart.js

Dans **Paramètres / Chat** sur desktop, et **Paramètres / Général** sur Android :

- **Tailwind CSS dans les réponses HTML** : activé par défaut. Le compilateur navigateur officiel Tailwind CSS **4.3.3** crée les styles des classes présentes dans le HTML généré. Il fonctionne avec les variantes responsive et `<style type="text/tailwindcss">` / `@theme`.
- **Chart.js dans les réponses HTML** : activé par défaut. Le bundle UMD officiel **4.5.1** expose `Chart` avant les scripts du modèle pour dessiner des graphiques dans `<canvas>`.

Sur desktop, **Paramètres / Chat / Tailwind suit le thème de l’application** est activé par défaut. Les variantes `dark:` et les couleurs par défaut de Chart.js suivent le thème de l’application, au lieu du thème du système. Les variables CSS `--monolith-bg`, `--monolith-surface`, `--monolith-text`, `--monolith-muted` et `--monolith-accent` reprennent sa palette. Tailwind expose aussi `bg-monolith-bg`, `bg-monolith-surface`, `text-monolith-text`, `text-monolith-muted` et `border-monolith-accent`. Les consignes du modèle incluent la palette actuelle. Désactiver cette option conserve le thème et les règles de mode sombre de la page. Les styles explicitement définis dans le HTML peuvent toujours personnaliser les couleurs.

**Paramètres / Chat / Taille dynamique du rendu HTML**, désactivé par défaut, adapte l’aperçu intégré entre 80 et 2 000 pixels au contenu de la page. Au-delà, la page reste défilable à l’intérieur de la bulle. Une mesure protégée suit les changements de contenu et de largeur ; les variations de hauteur sont regroupées pour éviter les boucles de redimensionnement. Ce mode retire les contraintes de hauteur de `html` et `body` pour mesurer leur contenu naturel. La fenêtre d’aperçu externe conserve la hauteur disponible de sa fenêtre.

Les deux options sont indépendantes et enregistrées avec les paramètres de chaque interface. Elles s’appliquent aux prochains aperçus ouverts, y compris ceux reconstruits depuis l’historique. Le modèle reçoit leur état à chaque nouvelle demande : CSS classique si Tailwind est désactivé, HTML/CSS/SVG si Chart.js est désactivé. Désactiver une bibliothèque empêche son injection ; cela ne désactive pas le JavaScript propre de la réponse.

Les copies, licences MIT et références de téléchargement sont intégrées à l’assembly partagé, sans CDN ni téléchargement à l’ouverture. Le manifeste `src/MonolithHarness.Core/WebAssets/manifest.json` consigne les versions, sommes SHA-256 des fichiers et intégrités SHA-512 des archives npm, vérifiées lors de l’intégration. Les sources sont [Tailwind browser](https://tailwindcss.com/docs/installation/play-cdn) et [Chart.js UMD](https://www.chartjs.org/docs/latest/getting-started/integration.html). Le compilateur navigateur Tailwind est utilisé pour ces aperçus de HTML dynamique ; il ne remplace pas le rendu natif de l’application.

Les interfaces et graphiques complets sont rendus dans l’aperçu ouvert par l’utilisateur, ou directement dans la bulle si cette option est activée sur desktop. Sur desktop, il faut autoriser les aperçus WebView du chat pour ce rendu. Le repli natif reste simplifié, ne compile pas les classes Tailwind et n’exécute pas Chart.js ; les consignes du modèle demandent alors du CSS classique et une alternative statique aux graphiques. Sur Android, toucher la réponse ouvre son aperçu en plein écran.

Exemple de document (aucun import nécessaire) :

````html
<!doctype html>
<html lang="fr"><head><meta name="viewport" content="width=device-width, initial-scale=1"></head>
<body class="bg-slate-950 text-white p-4 sm:p-8">
  <main class="mx-auto max-w-3xl rounded-3xl bg-slate-900 p-6">
    <h1 class="text-2xl font-semibold">Évolution des ventes</h1>
    <div class="mt-6 h-64"><canvas id="ventes" aria-label="Ventes par mois" role="img"></canvas></div>
  </main>
  <script>
    new Chart(document.getElementById('ventes'), {
      type: 'bar',
      data: {labels: ['Janvier', 'Février', 'Mars'], datasets: [{label: 'Ventes', data: [12, 19, 15], backgroundColor: '#22d3ee'}]},
      options: {responsive: true, maintainAspectRatio: false}
    });
  </script>
</body></html>
````

Les adaptateurs de dates et plugins externes de Chart.js ne sont pas inclus. Les bibliothèques ne donnent aucun accès supplémentaire au réseau, aux fichiers, aux fenêtres externes ou aux API natives ; ces accès restent bloqués dans l’aperçu. Fermer l’aperçu réinitialise son état JavaScript.

## Documents HTML sans bloc de code

Un document commençant par `<!doctype html>` ou `<html>` est regroupé avant le rendu Markdown, même après un paragraphe explicatif. Il garde ensemble ses styles, scripts, commentaires et lignes vides et apparaît comme une seule carte avec son code dépliable. Le texte après `</html>` reste du Markdown ordinaire. Les documents partiels reçus pendant la génération restent regroupés ; le plein écran ne s’ouvre qu’à la demande une fois la génération terminée sur Android. Le message d’origine n’est pas modifié dans l’historique.
