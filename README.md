# bip-kb

Assistant local de connaissance technique : ingestion de documents et de code, mémoire sourcée, recherche, enquêtes avec Copilot CLI et dossiers de travail persistants. .NET 10, SQLite FTS5, Razor Pages, partage par Git et MCP en lecture seule.

**Version 0.3.2.** Les tests automatisés contrôlent les mécanismes ; la compréhension métier et l'intégration Copilot doivent être vérifiées avec votre corpus et votre compte professionnel. Voir [validation](docs/validation.md).

## Installer ou mettre à jour

Prérequis : SDK .NET 10 (incluant ASP.NET Core), Git, Copilot CLI authentifié. Sous Windows, utiliser la distribution native `copilot.exe` ; les wrappers `.cmd` ne sont pas exécutés via un shell.

```powershell
./scripts/install.ps1 -ToolPath "$env:USERPROFILE/.bip-tools"
# Ajouter ce chemin au PATH.
bip-kb --version
```

Sous Linux :

```bash
dotnet restore --locked-mode
dotnet test --no-restore
dotnet pack src/BipKb -c Release --no-restore -o artifacts
dotnet tool install BipKb --version 0.3.2 --add-source ./artifacts --tool-path ./artifacts/tools
```

### Si vous avez une base 0.1 ou 0.2

```powershell
bip-kb migrate --workspace C:/Knowledge/bip --dry-run
bip-kb migrate --workspace C:/Knowledge/bip
bip-kb import --workspace C:/Knowledge/bip
```

La migration conserve les identifiants, preuves et décisions, sauvegarde les originaux dans `.local/migration-backup` et active le nouveau format après validation. Une ancienne version de bip-kb refusera d'écrire ce format. Pour revenir en arrière, restaurer la sauvegarde dans **un autre dossier** avec l'ancien programme. Les connaissances sans contexte restent consultables ; leur contexte doit être renseigné avant nouvelle analyse.

## Démarrer un projet

```powershell
bip-kb init --workspace C:/Knowledge/bip
cd C:/Knowledge/bip
bip-kb project setup
bip-kb doctor
bip-kb doctor --live
```

Le setup explique chaque question avec des exemples. **Seuls le nom et l'objectif sont obligatoires.** Entrée laisse un champ facultatif vide, ou conserve sa valeur si vous modifiez un profil existant. `?` réaffiche l'aide ; `/cancel` annule sans enregistrer. Un récapitulatif précède la confirmation.

| Question | Ce qu'elle signifie | Exemple pour BIP |
|---|---|---|
| Nom du projet | Le nom commun aux documents et au code à relier | Migration BIP |
| Ce que vous voulez réaliser | Le travail que cet assistant doit faciliter | Recréer les endpoints avec le nouveau modèle de données |
| Technologies et organisation du code | Comment l'application est construite | API .NET, SQL Server, DTO et handlers CQRS |
| Applications ou bases concernées | Les logiciels et bases sur lesquels vous travaillez | Ancien BIP ; nouvelle API BIP ; base SQL cible |
| Sujets métier | Les thèmes fonctionnels pour organiser les recherches | Clients ; Commandes ; Facturation |
| Règles de développement | Les conventions explicites de votre équipe | Un handler par query ; réutiliser les DTO existants |

Les sujets métier ne sont pas les couches techniques comme DTO ou Repository. Les applications concernées ne sont pas des chemins de dossiers : vous indiquerez ceux-ci lors des ingestions. Les exemples ne sont pas enregistrés automatiquement. Vous pouvez relancer `bip-kb project setup` pour compléter le même projet ; ses réponses précédentes sont proposées.

Utiliser le [profil complet d'exemple](docs/project.example.json) pour configurer aussi les références faisant autorité, les synonymes et les liens entre sujets :

```powershell
bip-kb project setup --file C:/Config/project.json
bip-kb project show --project "Migration BIP"
bip-kb project summary --project "Migration BIP"
```

Conserver les `id` lors des modifications. Les noms de projet/domaine doivent correspondre au contexte des sources. Les synthèses sont calculées depuis les données et citent leurs dépendances ; elles ne sont pas des preuves indépendantes.

## Ingérer sans préciser le format

```powershell
bip-kb --ingest C:/Documents/Facturation
bip-kb --ingest C:/Documents/Facturation/tables.sql
bip-kb --ingest C:/Documents/Facturation/etude.xls
bip-kb --ingest C:/Documents/Facturation/modele.drawio
bip-kb --ingest C:/Documents/Facturation/schema.png
```

Pas de `source bind` préalable. Le premier import inventorie les fichiers, propose un contexte existant si l'aperçu présente suffisamment de termes communs et demande ce qui manque : nom de source, projet, domaine, rôle (`legacy`, `target`, `current`), statut (`proposal`, `validated`, `observed`, `historical`), description et notes. Vous confirmez avant tout appel Copilot. `/cancel` ou `non` annule la préparation.

Le contexte est réutilisé ensuite. `--describe` le modifie pour le chemin sélectionné uniquement ; une exception sur un fichier ou sous-dossier ne modifie pas ses voisins. `--reanalyze` contourne le cache. Les projets restent séparés ; les domaines explicitement liés peuvent être explorés sans être fusionnés.

```powershell
bip-kb --ingest C:/Documents/Facturation/ancien.sql --describe
```

SQL Server, XLS/XLSX, C#, csproj, JSON, Markdown, texte, Draw.io natif/compressé, PNG et JPEG sont supportés. Les options `--file`, `--path`, `--image`, `--schema` restent des alias. Macros, SQL et code importés ne sont jamais exécutés. `.gitignore`, exclusions, fichiers sensibles usuels et liens symboliques sont traités avant analyse. Un dossier hors Git contenant des `.gitignore` doit être initialisé comme dépôt pour appliquer leurs règles.

### Automatisation et remappage

```powershell
bip-kb --ingest C:/Documents/Facturation --source facturation --context-file C:/Config/facturation.json --non-interactive
bip-kb source bind facturation D:/Equipe/Facturation
```

Pour plusieurs dossiers indépendants, donner un identifiant de source différent à chacun ; `source bind` associe un identifiant à une seule racine.

Projet et sujet métier : **120 caractères maximum** chacun. Description et précisions : **1 200 caractères maximum** chacune. Ces limites sont affichées pendant la saisie. Si une réponse est invalide, seul ce champ est redemandé ; les autres réponses sont conservées. Les erreurs d’un contexte JSON nomment également les champs concernés.

Un [contexte JSON](docs/source-context.example.json) contient `role` et `context` (project, domain, description, status, notes). `--json`, `--non-interactive` et l'entrée redirigée ne posent aucune question : un contexte manquant provoque une erreur avant Copilot. `source bind` remappe une source partagée sans changer sa description.

## Rechercher et comprendre les résultats

```powershell
bip-kb search "factures impayées" --project "Migration BIP" --domain Facturation
bip-kb search "dbo.Facture" --project "Migration BIP" --intent schema
bip-kb search "ancienne règle" --project "Migration BIP" --history
bip-kb context "Préparer la consultation des factures" --project "Migration BIP" --output contexte.md
```

La recherche directe ne consomme aucun appel Copilot. Elle combine identifiants exacts, plein texte, alias et navigation dans les relations. Elle recherche aussi les extraits originaux lorsque l'analyse n'a extrait aucune assertion. Chaque résultat expose son périmètre, ses preuves, son état et la raison de sa sélection. `--role target` filtre les rôles ; `--intent schema` ou `runtime` sélectionne l'autorité applicable. Si plusieurs projets existent, `--project` devient nécessaire.

Les objets SQL/C# et leurs relations sont extraits structurellement. Les appels C# non résolus restent marqués comme inférences. Une citation valide établit une provenance, pas la vérité de l'interprétation.

## Enquêter et discuter

```powershell
bip-kb task start "Préparer l'endpoint des factures impayées" --project "Migration BIP"
bip-kb task resume ID_DOSSIER
bip-kb chat --task ID_DOSSIER
bip-kb chat --task ID_DOSSIER --message "Et les factures annulées ?"
bip-kb task export ID_DOSSIER --output dossier.md
# Raccourci : crée un dossier et lance son enquête.
bip-kb ask "Comment calculer le reste à payer ?" --project "Migration BIP"
```

Une enquête conserve les recherches, résultats, références réellement transmis à Copilot, réponses et questions ouvertes. Par défaut : trois cycles, deux niveaux de relations, contexte borné. La grille de couverture indique les **preuves candidates**, sans annoncer qu'un besoin métier est entièrement validé. Une nouvelle source ou décision du projet marque les dossiers précédents à actualiser.

### Mémoire humaine et arbitrages

```powershell
bip-kb questions --project "Migration BIP"
bip-kb question answer ID_QUESTION --message "Exclure les factures annulées" --actor Alice --confirm
bip-kb remember "Une facture annulée est exclue du calcul" --project "Migration BIP" --actor Alice --confirm
bip-kb review
bip-kb review ID_ASSERTION --action accept --reason "Vérifié avec le métier" --actor Alice
bip-kb entity rename ID_ENTITE --name "Nouveau nom"
```

La conversation ne valide rien automatiquement. `remember` et les réponses confirmées sont des déclarations humaines traçables, distinctes des preuves documentaires. Actions d'arbitrage : `accept`, `reject`, `distinct`, `replace`. Renommer une entité conserve son identité et son ancien nom comme alias. Les modifications historiques restent dans le journal.

## Interface locale et MCP

```powershell
bip-kb serve --port 5267
```

Interface sur la boucle locale : recherche, preuves, ingestions, arbitrages, projets, dossiers et mémoire humaine. Les actions d'écriture utilisent des formulaires antiforgery. Une analyse peut tourner pendant que les lecteurs consultent la dernière révision publiée.

Configuration d'un serveur MCP stdio (l'enveloppe dépend du client) :

```json
{"command":"bip-kb","args":["mcp","--workspace","C:/Knowledge/bip"]}
```

Outils en lecture seule : `search(query, project?)`, `context(query, project?)`, `entity(id)`, `evidence(id)`, `relations(id)`, `project(id)`, `task(id)`, `questions(id)` (nom du projet). Aucun outil MCP ne peut ingérer, arbitrer ou lancer une enquête. Protocole 2024-11-05, transport JSON-RPC ligne par ligne.

## Partager par Git

Versionner `bip.json`, le marqueur `knowledge.json`, `memory/` et `decisions/`. Ne jamais partager `.local/` ou une base SQLite en réseau. Les originaux restent hors de la mémoire ; les extraits partagés peuvent contenir des informations confidentielles.

```powershell
bip-kb export --output C:/Exports/bip
# Sur l'autre poste, après clonage/fusion des fichiers :
bip-kb import --workspace C:/Equipe/bip
bip-kb source bind facturation D:/Documents/Facturation --workspace C:/Equipe/bip
```

Les lots de changements sont immuables et vérifiés par empreinte. Des modifications indépendantes se combinent ; deux versions concurrentes d'un même objet bloquent l'import jusqu'à résolution explicite. Une décision humaine concurrente incompatible reste en arbitrage. `memory resolve --file resolutions.json` attend un tableau de `{ "key": "entity:ID", "value": { ...objet complet... } }` ; `value: null` retire un enregistrement si les références restent valides. Ne pas modifier les anciens lots à la main. Aucun commit/pull/push n'est effectué par l'application.

## Mesurer et diagnostiquer

```powershell
bip-kb evaluate --file C:/Tests/questions.json
bip-kb benchmark --count 100000
```

Un cas d'évaluation contient `question`, `expectedIds`, `project`, éventuellement `domain` et `forbiddenIds`. Les métriques incluent rappel dans les vingt premiers résultats, omissions, résultats interdits et latence. Le benchmark crée puis supprime une base synthétique temporaire ; ses résultats ne mesurent pas la compréhension métier.

Configuration : `exclusions`, `analysisProfile`, `timeoutSeconds`, `maxFileBytes`, `copilotExecutable`, `maxResearchCycles` (1..5), `graphDepth` (0..3), `researchBudget` (6000..18000 caractères). Une source accepte `excelSheets`. Les chemins locaux et caches restent dans `.local/`.

Limites : pas d'embeddings, de service central ou de génération du code applicatif. SQL dynamique et références C# externes peuvent rester incomplets ; aucune évaluation MSBuild n'est exécutée. La vision dépend du modèle Copilot. Les filtres de fichiers sensibles ne remplacent pas vos exclusions. La portée des enquêtes et le budget limitent volontairement l'exploration ; une absence de résultat n'est jamais une preuve d'absence.

Voir [architecture](docs/architecture.md), [validation](docs/validation.md) et les [exemples](examples/).
