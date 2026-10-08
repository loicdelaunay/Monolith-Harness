# Tests du stockage des propositions — 1.72.1

## Régression reproduite

Le 8 octobre 2026, le groupe de tests a d’abord échoué avec l’erreur exacte `Unknown portable resource folder. (Parameter 'relative')`, dans `FileProposals.ReadAsync` puis `PortableStorage.Folder`. La lecture est effectuée lors du chargement d’une conversation, même sans proposition en attente.

Le dossier `proposals` manquait dans les ressources portables autorisées. Il est maintenant déclaré, et le stockage utilise le dossier de la base passée à l’API plutôt que la racine globale du host.

## Exécution ciblée

Depuis la racine du dépôt :

```powershell
dotnet run --project tests/MonolithHarness.Tests/MonolithHarness.Tests.csproj -c Release -- --file-proposals
```

Les 31 contrôles sont aussi inclus dans `--portable-storage` et dans la suite par défaut. Ils utilisent uniquement des dossiers temporaires uniques, des fichiers de test et des modèles de données ; ils n’appellent ni modèle IA ni service distant et ne modifient pas les données de l’application.

## Couverture

- Lecture sans proposition : retourne `null` sans créer de dossier ni provoquer l’erreur affichée.
- Dossier autorisé, séparateurs Windows/Unix, refus des chemins inconnus, absolus et des traversées de répertoires.
- Stockage associé à la base explicitement sélectionnée, sans changer la racine globale.
- Isolation entre conversations, entre dossiers de bases et entre bases partageant un dossier.
- Persistance des contenus et octets d’origine ; préparation sans modifier le projet.
- Application uniquement des fichiers cochés ; conservation des propositions restantes.
- Refus d’une revue périmée ou d’un fichier modifié depuis la préparation.
- Migration vers `workspace/proposals`, résolution des anciens chemins et répétition sans perte.
- Collision de dossiers : conservation des propositions historiques et des propositions déjà présentes.

## Résultats du 8 octobre 2026

| Groupe | Contrôles réussis |
| --- | ---: |
| Stockage portable, incluant les 31 nouveaux contrôles | 37 |
| Fichiers et isolation des conversations | 33 |
| Réglages d’apparence et MCP | 37 |
| Total distinct de ces groupes | **107** |

La suite complète n’a pas été exécutée. Ces contrôles valident le code de stockage et ses appelants partagés ; ils ne simulent pas l’affichage de l’interface native.
