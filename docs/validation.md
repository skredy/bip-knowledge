# Validation de la version 0.1.0

## Vérifications réalisées dans l'environnement Linux

- SDK .NET 10.0.404 téléchargé avec vérification SHA-512 depuis les métadonnées Microsoft.
- Compilation et restauration des dépendances verrouillées.
- Dernier résultat : **27 tests réussis, 1 test Copilot réel ignoré**, aucune défaillance.
- Tests xUnit : extraction XLS/XLSX, Draw.io natif/compressé, SQL Server et SQL dynamique, C#, JSON, Markdown, pièces jointes image, ignores Git et liens symboliques.
- Tests de persistance : incrémentalité, suppression, source inaccessible, reprise après interruption, absence de publication partielle, déduplication, reconstruction SQLite, export vers un second espace.
- Tests d'arbitrage : contradiction/évolution, décisions concurrentes, conservation des décisions après réanalyse, réexamen après perte de preuves.
- Tests des contrats : références inventées et JSON incorrect refusés, restrictions CLI, délai de processus, MCP en lecture seule, entrée d'ingestion sans type explicite et limites de taille des arguments sous Windows.
- Création et installation du paquet .NET tool, puis parcours CLI complet sur un corpus SQL/C#/Markdown avec **un processus CLI simulé** : ingestion, recherche, question et dossier de contexte.
- Requêtes HTTP réelles sur les vues recherche, ingestions, détail et arbitrages. POST sans jeton antiforgery refusé (400), POST avec jeton et cookie accepté, Host étranger refusé (400).

## Vérification Copilot réelle : bloquée par le réseau

Copilot CLI **1.0.111** installé ; aide et options effectivement inspectées. La syntaxe des restrictions a été corrigée après un appel réel : `--deny-tool '*'` n'est pas une règle valide ; l'adaptateur utilise les catégories documentées et une liste d'outils vide.

Une tentative réelle atteint la vérification d'authentification mais échoue sur `https://api.github.com/copilot_internal/user`. Une requête HTTPS indépendante confirme le refus du tunnel par le proxy (403). Une variable d'authentification existe déjà ; cela ne suffit pas à établir sa validité ni l'accès au modèle. Aucun secret supplémentaire n'a été demandé ou enregistré.

Il faut permettre l'accès à `api.github.com` dans l'environnement de validation et aux destinations Copilot requises par la configuration de l'entreprise, puis relancer `doctor --live`. Aucun changement réseau ou publication cloud n'a été effectué par cette implémentation.

Le test `AuthenticatedCopilotCanIngestSqlAndAnswerWithCitations` reste explicitement ignoré par défaut. Pour l'exécuter sur un poste disposant de l'accès :

```powershell
$env:BIP_KB_LIVE_TESTS = '1'
dotnet test --filter FullyQualifiedName~AuthenticatedCopilot
```

```bash
BIP_KB_LIVE_TESTS=1 dotnet test --filter FullyQualifiedName~AuthenticatedCopilot
```

Les tests simulés ne valident ni la compréhension métier, ni la qualité visuelle, ni le fonctionnement d'une session Copilot autorisée.

## À vérifier sur le poste professionnel

1. Installer avec `scripts/install.ps1`, authentifier Copilot normalement et exécuter `scripts/validate.ps1 -Workspace <espace> -Live`.
2. Lancer le test d'intégration réel ci-dessus.
3. Ingestérer une étude Excel, le SQL cible et une implémentation représentative ; vérifier les références et le classement des apports.
4. Ingestérer un Draw.io et une image lisible ; contrôler les entités et relations reconnues.
5. Arbitrer un conflit, modifier sa source puis réingérer : la décision reste conservée et le changement des preuves est visible.
6. Produire un dossier de contexte pour un endpoint réel ; juger s'il évite la reconstruction manuelle du contexte.
7. Tester le client MCP dans l'IDE retenu. Le serveur est validé au niveau protocole local ; les trois IDE n'ont pas été exécutés dans cet environnement.

Une CI Linux/Windows est fournie mais n'a pas été exécutée sur GitHub pendant cette tâche. Le script PowerShell n'a pas été exécuté sous Windows ici.
