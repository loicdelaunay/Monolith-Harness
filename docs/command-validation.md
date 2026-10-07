# Validation des commandes

## Choisir le comportement

Dans **Paramètres / Autorisations**, ou dans le menu du bouclier du chat :

- **Demander** (par défaut) : demander pour les accès sans autorisation mémorisée.
- **Automatique** : analyser chaque demande d’exécution avec le validateur configuré ; demander une autorisation ponctuelle si elle est risquée, incertaine ou impossible à analyser.
- **⚠ Tout autoriser** : accepter les demandes sans analyse par un modèle. Ce choix est signalé en orange.
- **Tout refuser** : refuser les demandes d’accès.

Les règles explicites du projet continuent de s’appliquer : une règle Demander déclenche une confirmation, une règle Refuser interdit l’accès. Aucun modèle de validation ne garantit l’innocuité d’une commande. Les autorisations mémorisées ne contournent pas la validation du mode Automatique.

## Installer LANCET

Dans la section **Validation des commandes**, sélectionner **LANCET Nano · local**, puis **Télécharger et installer**. Environ 116 Mo de modèle sont téléchargés, suivis des composants CPU ; les fichiers du modèle sont vérifiés avant préparation. L’installation ne démarre pas lors du choix du mode Automatique. Tant que LANCET n’est pas prêt, l’exécution demande votre accord.

### Installation manuelle

1. Télécharger le [bundle complet LANCET Nano 0.4.3](https://huggingface.co/fingerthief/lancet-nano/tree/2450cfbea514baef810f4087d6d31854783f3d2e/bundle), avec ses sous-dossiers, scripts et licences.
2. Choisir le dossier contenant `classify.py` et `model/` (ou le parent contenant `bundle/`).
3. Cliquer sur **Installer depuis ce dossier**. Monolith copie uniquement les fichiers attendus, vérifie leur taille et leur empreinte, puis prépare le modèle. Le dossier source est conservé.

Les composants CPU viennent normalement de PyPI. Pour une installation hors ligne, placer des roues Python compatibles avec le système et le runtime Python intégré, ainsi que leurs dépendances, dans `wheels/` **à l’intérieur du bundle** : NumPy 2.2.6, Tokenizers 0.22.2 et ONNX Runtime 1.23.2. Leur présence désactive le recours à l’index en ligne ; un lot incomplet provoque un échec explicite.

## Utiliser un modèle existant

Sélectionner **Modèle existant**, son fournisseur et son modèle, puis enregistrer les réglages. Les fournisseurs déjà configurés sont proposés. Un modèle local doit avoir été préparé auparavant dans **Fournisseurs / Local**.

Pour un fournisseur API, chaque analyse transmet la commande, le shell et le dossier de travail ; des frais peuvent s’appliquer. Le validateur ne reçoit aucun outil d’exécution. Seule une réponse structurée valide correspondant à la commande exacte peut autoriser une opération déclarée peu risquée. Une réponse invalide, un délai dépassé ou une indisponibilité demande une confirmation humaine. Les moteurs externes incapables d’analyser sans outils restent soumis à cette confirmation.

Le CLI propose les mêmes choix dans **Réglages / Autorisations / Validation des commandes** et partage les réglages avec la GUI.
