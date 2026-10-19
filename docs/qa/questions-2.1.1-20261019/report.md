# Questions desktop — vérification 2.1.1

Date : 2026-10-19. Résultat : **145 contrôles réussis** dans le scénario intégré `SmokeWorkflowQuestions`, exécuté avec le lanceur final `artifacts/GUI/MonolithHarness.exe` (2.1.1).

## Méthode

- Application Uno ouverte dans une vraie fenêtre Windows, avec captures du rendu natif.
- Base SQLite et ressources de test isolées ; aucun appel à un modèle ou fournisseur distant.
- Sélection via les pairs d’automatisation natifs des RadioButton/CheckBox et invocation des boutons de l’application.
- Mesures de géométrie des sélecteurs, des glyphes, du contenu et des boutons après mise en page.
- Cette vérification ne simule pas des clics physiques avec la souris Windows.

## Cas vérifiés

- Choix unique exclusif, choix multiples conservés, option personnalisée et texte libre sans sélecteur redondant.
- Validation des réponses vides, effacement de l’erreur après saisie/sélection valide, conservation des brouillons en navigation.
- Accordéon, réduction de la carte entière, soumission des quatre réponses et annulation distincte d’une approbation.
- Thèmes Fluent clair et Fluent sombre V2 ; fenêtres de 1600, 1200, 720 et 420 pixels de large, hauteur de 1000 pixels.
- Zoom de texte à 150 % : radio, cases à cocher et barre de validation finale.
- Rafraîchissement de langue sans remplacement des contrôles par leurs identifiants d’automatisation.

## Correctifs observés

- Le sélecteur reste centré avec le bloc icône/texte, même avec plusieurs lignes et le texte agrandi.
- La numérotation fournie par le modèle n’est plus affichée une seconde fois dans les en-têtes.
- Les actions passent sur deux lignes lorsque l’espace et le zoom le nécessitent.
- Le questionnaire tient dans la hauteur du chat ; son contenu garde son propre défilement et la navigation révèle la question active.

## Preuves

- [Contrôles détaillés](checks.txt)
- [Mesures de géométrie](question-geometry.json)
- [Fenêtre complète](application-dark-wide.png)
- [Choix en thème sombre](questions-dark-wide.png)
- [Petite largeur en thème clair](questions-fluent-light-420.png)
- [Validation en petite largeur](questions-fluent-dark-v2-free-420.png)
- [Choix multiples](questions-multiple.png)
- [Zoom de texte à 150 %](questions-zoom-150.png)

Les captures et mesures concernent exclusivement les données fictives du scénario. Les résultats des essais intermédiaires restent dans les sous-dossiers dédiés de `.tmp/questions-ui-20261019-*`.

## Exécutable vérifié

SHA-256 GUI : `7b3aa882ce7a59c30d9d175c7c79a98269de4ba08673c9a80dbd20a024f36369`. Les publications locales GUI, CLI et serveur sont alignées en 2.1.1.
