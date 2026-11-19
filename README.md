# bip-kb

Assistant local de connaissance technique : ingestion de documents et de code, mémoire sourcée, recherche, enquêtes avec Copilot CLI et dossiers de travail persistants. .NET 10, SQLite FTS5, Razor Pages, partage par Git et MCP en lecture seule.

La cible de la refonte est un **business analyste avec une mémoire sourcée**, avec [un projet par workspace et un cadrage conversationnel par Copilot](docs/business-analyst-workspace.md), puis une [compréhension globale des documents avant l'indexation métier](docs/knowledge-redesign.md). Elle prévoit un départ sur un workspace neuf, sans reprise nécessaire des essais précédents. Ces documents sont des propositions de conception ; le fonctionnement actuel reste décrit ci-dessous.

**Version 0.4.7.** Les tests automatisés contrôlent les mécanismes ; la compréhension métier et l'intégration Copilot doivent être vérifiées avec votre corpus et votre compte professionnel. Voir [validation](docs/validation.md).

La 0.4.2 corrige le décodage des réponses et erreurs Copilot sous Windows : l'entrée et les deux sorties du processus utilisent explicitement UTF-8, indépendamment de la page de codes de la console. Le test de conservation des accents et caractères Unicode reste actif.

La **0.4.3** évite de rejeter une réponse lorsque Copilot demande plus de 200 unités à lire : seules les 200 premières unités distinctes demandées, limitées à la fin du document et au budget de texte, sont fournies. Le reste demeure à lire ; la couverture n'est jamais déduite de la demande. Une navigation invalide (par exemple un numéro de départ inexistant) bénéficie d'une seule nouvelle tentative, partagée avec celle du format JSON. Les preuves invalides restent refusées ; la 0.4.5 ajoute la nouvelle analyse décrite ci-dessous. Pour reprendre une exploration interrompue, utiliser `--analyze`, **sans `--reanalyze`**, depuis le même workspace et avec les mêmes sources et contexte.

La **0.4.4** fournit à Copilot les intervalles exacts des unités visibles et lui permet de déclarer explicitement leur examen avec `reviewVisible`. Cette déclaration s'applique uniquement aux unités effectivement transmises, sans inclure les lignes vides omises ni les unités encore à lire. Une plage de couverture citant une unité absente ou déjà déclarée reçoit un diagnostic précis et une seule nouvelle tentative, dans le même budget que les corrections de navigation/format. Aucune couverture refusée n'est acceptée automatiquement. La progression distingue le chargement de la mémoire locale et la raison d'une nouvelle analyse (reprise, contenu ou contexte modifié, réanalyse demandée).

La **0.4.5** fournit des références courtes (`u143`, par exemple) pour les seules unités transmises à chaque appel guidé. Les assertions de contexte utilisent des références distinctes, et leurs preuves non fournies sont masquées. Une citation absente déclenche au plus une nouvelle analyse avec les données originales, sans réinjecter la conclusion refusée. Ce nouvel essai partage le plafond de deux réponses avec les erreurs de format/navigation/couverture. Aucune référence n'est devinée ni remplacée approximativement : si la réponse reste invalide, l'appel échoue. Les références complètes restent conservées dans la mémoire et les caches validés restent réutilisables.

La **0.4.6** fait lire les outils MCP directement dans SQLite, sans charger `materialized.json` à chaque appel. Après la mise à jour d'une base existante, lancez une fois `bip-kb reindex --workspace "C:/Knowledge/ibip"`, puis redémarrez le serveur MCP dans votre IDE. Cette opération n'appelle pas Copilot et ne réingère pas les sources. Un index absent ou obsolète produit une erreur explicite ; le MCP ne le reconstruit pas pendant une requête. Les grandes listes de preuves sont paginées. Voir le [guide MCP](docs/mcp.md).

La **0.4.7** ajoute `bip-kb cache compact` pour réduire le cache matérialisé sans perdre de données : les listes identiques sont stockées une seule fois, avec vérification intégrale avant remplacement. `--rebuild` permet de repartir de la mémoire publiée. Le compactage ne modifie ni les connaissances partagées, ni les décisions, ni l'index ; il n'appelle pas Copilot. Voir [la gestion du cache local](docs/cache.md).

## Installer ou mettre à jour

Prérequis : SDK .NET 10 (incluant ASP.NET Core) et Git. Copilot CLI authentifié est nécessaire pour `ask`, les enquêtes et `--analyze`, mais pas pour indexer ou rechercher localement. Sous Windows, bip-kb détecte la distribution native `copilot.exe` et les installations npm `@github/copilot` (via Node.js). Le seul lanceur `copilot.ps1` ajouté par l’extension VS Code ne suffit pas : la CLI autonome doit être installée.

```powershell
./scripts/install.ps1 -ToolPath "$env:USERPROFILE/.bip-tools"
# Ajouter ce chemin au PATH.
bip-kb --version
```

Le script lit la version dans le projet et installe le paquet correspondant. Il affiche séparément la restauration, la compilation, les tests, la création du paquet et l'installation. En cas d'échec, conserver la **première erreur détaillée** de l'étape concernée : le message final `Build failed` ou `Tests failed` ne donne pas la cause.

Sous Linux :

```bash
dotnet restore --locked-mode
dotnet test --no-restore
dotnet pack src/BipKb -c Release --no-restore -o artifacts
dotnet tool install BipKb --version 0.4.7 --add-source ./artifacts --tool-path ./artifacts/tools
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

## Choisir entre indexation rapide et analyse complète

**Depuis la 0.3.9, une ingestion sans option indexe localement, sans appeler Copilot.** C'est le parcours conseillé pour un dépôt de plusieurs milliers de fichiers. Les extracteurs conservent les unités sourcées, les coordonnées Excel, les objets/connexions Draw.io, les symboles et relations techniques SQL/C# ; ces éléments sont directement recherchables. Aucune nouvelle interprétation métier n'est présentée comme ayant été produite par le modèle.

```powershell
# Tout le code, sans appel Copilot :
bip-kb --ingest "C:/repos/bip/src"
bip-kb search "commande statut"
bip-kb ask "Comment le statut d'une commande est-il calculé ?"

# Documents importants, à analyser avec Copilot :
bip-kb --ingest "C:/etudes/migration.xlsx" --analyze
bip-kb --ingest "C:/etudes/modele.drawio" --analyze
# Fonctionne aussi pour un fichier ou sous-dossier déjà indexé :
bip-kb --ingest "C:/repos/bip/src/Commandes" --analyze
```

Le choix s'applique à la commande, pas au format : Excel et Draw.io peuvent être indexés rapidement ou analysés entièrement. `ask` et les dossiers de recherche utilisent ensuite Copilot sur des résultats bornés, avec citations et recherches complémentaires ; ils ne lancent pas une analyse du dépôt entier et ne réingèrent pas automatiquement leurs réponses comme des faits. `search` et `context` restent locaux. Un profil de projet est nécessaire pour `ask` (`project setup` si nécessaire).

`--analyze` complète l'analyse des fichiers qui n'ont été qu'indexés, tout en réutilisant les analyses à jour. **`--reanalyze` force une nouvelle analyse et contourne le cache : ne pas le conserver pour une reprise.** Depuis la 0.4.0, la CLI utilise l'exploration guidée décrite ci-dessous. L'ancien parcours reste disponible avec `--analysis-mode batches`.

Depuis la **0.3.11**, les lots Excel partagent les en-têtes identiques une seule fois par feuille dans chaque demande. Les lignes vides contenant seulement une répétition d'en-têtes ne déclenchent plus d'analyse. Les cellules renseignées, leurs coordonnées, les preuves complètes et leurs identifiants sont conservés. Le nombre de lots est affiché avant les appels ; un classeur compressé de 100 Ko peut néanmoins contenir beaucoup de texte. `--analyze` reste une analyse exhaustive en plusieurs appels, pas une indexation locale.

Un fichier isolé ne déclenche plus un second passage de « rapprochement entre fichiers ». Chaque lot compare toujours son contenu aux connaissances pertinentes déjà présentes. Pour reprendre une analyse Excel commencée en 0.3.x, arrêter l'ancien processus, mettre à jour puis relancer **avec `--analyze --analysis-mode batches`, sans `--reanalyze`** : les premiers lots validés sont récupérés si leurs demandes correspondent, puis les lignes restantes sont regroupées. Le contexte déjà renseigné est réutilisé. Les analyses déjà publiées ne sont pas relancées simplement par la mise à jour.

Les connaissances et décisions déjà publiées sont conservées si leurs sources sont inchangées. Les lots d'une analyse interrompue sont récupérés quand leur demande correspond encore exactement. Une modification de source peut rendre les anciennes conclusions historiques, même en indexation locale. Le rapport distingue `mode: "index"` et `mode: "analysis"`, avec `unanalyzedFiles` ; ce compteur n'est pas une erreur du mode local. Les images indexées seules sont explicitement signalées comme non analysées visuellement : utiliser `--analyze` pour leur contenu.

Aucune migration n'est nécessaire depuis la 0.3.x. Les membres d'une équipe doivent mettre à jour leur outil avant de lire une mémoire enrichie avec ces nouveaux indicateurs. Un processus déjà lancé conserve son ancien comportement : l'interrompre avec Ctrl+C puis relancer après mise à jour pour changer de mode.

### Exploration guidée par Copilot — 0.4.0

**Depuis la 0.4.1**, lorsque l'aide de Copilot annonce l'entrée par flux (`piped stdin`), les nouvelles analyses transmettent les demandes par stdin UTF-8. Les lectures peuvent contenir jusqu'à 120 000 caractères de preuves et 200 unités, avec un budget total de 160 000 caractères pour le contexte JSON. La première demande contient déjà les premières unités complètes et le catalogue : un petit document peut donc être analysé en un échange. Les documents plus grands restent explorés par sections choisies par Copilot. La progression indique le nombre d'unités effectivement transmises. Les plages de couverture compactes sont vérifiées unité par unité ; elles n'autorisent pas à déclarer analysées des cellules absentes de la demande.

`bip-kb doctor` indique `promptTransport: "stdin"` ou `"arguments"`. Une CLI qui n'annonce pas stdin garde le parcours compatible. **Une analyse 0.4.0 interrompue conserve son ancien profil pour réutiliser ses échanges** : reprendre avec `--analyze`. Pour bénéficier des lectures élargies sur ce même fichier, relancer volontairement avec `--reanalyze` après mise à jour ; cela recommence l'analyse. Les analyses déjà publiées restent disponibles jusqu'à la publication de la nouvelle. Aucun nettoyage manuel des caches n'est nécessaire.

Chaque échange utilise encore un nouveau processus CLI. Les demandes plus grandes réduisent le nombre d'allers-retours possibles, mais consomment davantage de contexte par appel ; leur durée et la qualité des conclusions dépendent du modèle. Les budgets sont exprimés en caractères, pas en tokens.

Copilot commence par une vue d'ensemble paginée du document (feuilles/pages, nombres d'unités, aperçus). Il construit un plan, choisit les plages à lire, cherche dans le texte extrait et peut rapprocher des unités éloignées. Les limites de transport bornent les réponses de lecture ; elles ne constituent plus un découpage préalable de tout le fichier. L'exploration est **par fichier**, orientée par le contexte projet et les assertions existantes retrouvées ; ce n'est pas un agent ayant accès à tout le poste ou à tous les dépôts.

```powershell
# Exploration guidée : choix des lectures, preuves, couverture et synthèse
bip-kb --ingest "C:/etudes/endpoints.xlsx" --analyze
bip-kb --ingest "C:/etudes/modele.drawio" --analyze
bip-kb --ingest "C:/etudes/architecture.png" --analyze

# Consulter le plan publié et les explorations locales interrompues
bip-kb analysis etudes

# Budget explicite (50 nouveaux échanges par commande par défaut)
bip-kb --ingest "C:/etudes/endpoints.xlsx" --analyze --max-calls 20

# Reprendre les 41 premiers lots d'une ancienne analyse 0.3.x
bip-kb --ingest "C:/etudes/endpoints.xlsx" --analyze --analysis-mode batches
```

`etudes` désigne ici l'identifiant de source configuré pour ces documents. Comme les autres commandes, `analysis` accepte `--workspace` et `--json`. L'interface **Ingestions** affiche aussi les plans, synthèses, questions et plages de couverture publiés. Le MCP propose l'opération de lecture `analysis` avec cet identifiant ; `context` joint les synthèses des sources pertinentes comme interprétations, jamais comme preuves indépendantes.

Les preuves citables proviennent uniquement des unités fournies intégralement dans l'échange. Un aperçu n'est pas une lecture complète. Copilot peut classer une unité lue comme analysée ou non résolue, et exclure explicitement des plages hors sujet avec justification. Les exclusions, même sans lecture intégrale, restent visibles et rendent l'analyse **partielle**. Une synthèse prématurée ne termine pas le parcours : les unités restantes doivent être examinées ou explicitement exclues. La couverture mesure le travail déclaré sur les données fournies, pas la justesse métier du modèle.

Chaque réponse validée est conservée sous `.local/exploration/`. Si le budget est atteint ou qu'une réponse est refusée, aucune révision inachevée n'est publiée : relancer **la même commande sans `--reanalyze`** reprend les échanges dont les demandes sont inchangées. Cela fonctionne aussi après une réanalyse forcée d'une source déjà analysée. Le budget compte les nouveaux échanges d'exploration, hors sonde d'aide et tentatives bornées de format/navigation/couverture/références/erreurs transitoires de l'adaptateur. Il est commun à tous les fichiers de la commande, et configurable dans `bip.json` (`maxExplorationCalls`, de 1 à 100). Un plafond absolu de 512 échanges par document évite les boucles persistantes.

Une analyse terminée avec exclusions ou éléments non résolus est publiée comme partielle ; une réingestion inchangée la conserve. Utiliser `--reanalyze` pour la réexaminer volontairement. Les anciens lots `staging/` et les nouveaux échanges `exploration/` ne sont pas interchangeables : conserver le mode `batches` pour terminer une analyse 0.3.x en cours. Ne supprimer ni l'un ni l'autre pendant une reprise.

Limites par format : Excel conserve les valeurs stockées et coordonnées, sans recalcul de formules ni lecture visuelle de la mise en page. Draw.io fournit les objets, connexions, groupes, styles et géométrie XML, **sans rendu visuel**. PNG/JPEG utilisent la pièce jointe complète si la CLI le permet ; une image illisible ou non prise en charge reste non résolue. Cette version ne fabrique ni recadrage, ni OCR, ni référence de région. Aucun gain de temps ou de compréhension sur un corpus professionnel n'est déduit des tests simulés. Voir le [plan de réalisation](docs/guided-analysis-plan.md) et la [validation](docs/validation.md).

### Construction de l'index local trop longue

La 0.3.10 conserve chaque ensemble identique de preuves une seule fois dans SQLite, avec des liens depuis les objets techniques. Cela évite de matérialiser à nouveau chaque paire objet/preuve quand des centaines d'objets référencent toutes les unités d'un fichier. La mémoire partagée et ses références complètes sont conservées. Les résultats de recherche présentent au maximum 32 références par objet en indiquant quand il y en a davantage ; le graphe complet reste navigable dans les deux sens, et le MCP permet de consulter toutes les références de l'objet technique par pages avec `entity` et `evidence`.

Si une ingestion a atteint « Construction de l'index de recherche local », la mémoire a déjà été publiée. Après arrêt de l'ancien processus et mise à jour, reconstruire uniquement l'index depuis le workspace :

```powershell
bip-kb reindex
# Ou depuis un autre répertoire :
bip-kb reindex --workspace C:/Knowledge/ibip
```

Cette commande ne relit pas les fichiers sources et n'appelle pas Copilot. Elle affiche les étapes et compteurs (chargement, documents, relations, ensembles de preuves), puis la validation. `Ctrl+C` annule les boucles et les opérations SQLite ; un JSON de mémoire déjà en cours de lecture est terminé avant le prochain contrôle d'annulation. Une transaction interrompue ne remplace pas l'index précédemment validé. Ne pas effacer `memory/` ni la mémoire matérialisée pour cette opération.

Le nouvel index est `.local/search-v6.db` ; l'ancien `search-v5.db` est ignoré et n'est pas supprimé automatiquement. Un petit fichier `.db` accompagné d'un gros `-wal` signifie que des écritures se trouvent dans le journal SQLite, pas que rien ne se passe. Après une ingestion locale, la 0.3.10 utilise directement la mémoire déjà chargée, avec contrôle de révision, au lieu de relire immédiatement tout le journal partagé.

### Si PowerShell trouve Copilot mais bip-kb ne peut pas le lancer

```powershell
Get-Command copilot -All | Select-Object CommandType, Source, Path
```

Un résultat sous `Code/User/globalStorage/github.copilot-chat/copilotCli/copilot.ps1` désigne le lanceur de VS Code : il recherche une CLI installée séparément et peut proposer son installation. Ce résultat seul ne confirme pas que la CLI autonome est installée ou accessible dans `PATH`.

Installer la CLI autonome si nécessaire, avec **une** des commandes suivantes :

```powershell
winget install --id GitHub.Copilot --exact
# Ou, si vous utilisez npm :
npm install -g @github/copilot
```

Rouvrir le terminal après installation, puis lancer `copilot --version`, `bip-kb doctor` et `bip-kb doctor --live` depuis le dossier de connaissance. Une authentification Copilot CLI peut être nécessaire ; l'extension VS Code et la CLI sont deux points d'entrée distincts.

`doctor` indique désormais le chemin réellement lancé et le mode (`native`, `npm-node`, etc.). Si l'exécutable est installé hors du `PATH`, renseigner `copilotExecutable` dans `bip.json` avec son chemin exact. Un chemin relatif est résolu depuis le workspace. Les chemins locaux d'installation n'ont pas à être identiques sur les autres postes : conserver `copilot` quand la détection automatique suffit.

bip-kb ne lance pas les scripts d'installation VS Code, ne contourne pas les politiques PowerShell et n'exécute pas les documents via un shell. Pour npm, il lit le point d'entrée déclaré du package installé et le transmet directement à Node.js. Le contexte déjà enregistré est conservé : relancer ensuite la même ingestion sans `--describe`.

### Refus d'accès à `.local/copilot` sous Windows

Les versions antérieures à 0.3.5 créaient le dossier de travail Copilot dans le workspace. Un refus d'accès pendant son nettoyage pouvait masquer la réponse ou l'erreur d'analyse. À partir de la 0.3.5, les appels utilisent un dossier privé `bip-kb-copilot-*` dans le répertoire temporaire système (`%TEMP%` sous Windows), habituellement hors de OneDrive. `doctor` affiche `temporaryRoot` ; `doctor --live` vérifie aussi un appel réel.

Le nettoyage réessaie brièvement les fichiers verrouillés et traite les fichiers en lecture seule sans modifier les droits du workspace. S'il reste impossible, un avertissement indique le dossier restant, qui peut contenir des pièces jointes ; il ne remplace plus le résultat de Copilot. Les contextes et analyses intermédiaires restent dans le workspace. Après mise à jour, relancer la même commande sans `--describe` ni `--reanalyze` pour reprendre le travail disponible.

### Réponse Copilot contenant un retour à la ligne JSON invalide

À partir de la 0.3.6, bip-kb demande la sortie machine `--output-format json` lorsque la CLI la propose. `doctor` indique `responseFormat: "jsonl"` (sinon `"text"` pour les anciennes CLI). Le lecteur utilise le dernier message assistant complet et exige une confirmation de fin réussie ; les événements de progression ne deviennent pas des réponses.

Si le JSON produit par le modèle contient un retour à la ligne ou une tabulation non échappés dans une chaîne, bip-kb en corrige uniquement l'encodage, en conservant le texte. Les champs attendus et références restent contrôlés ; les autres erreurs ne sont pas réparées arbitrairement. Une mise à jour ne nécessite aucune nouvelle saisie du contexte : vérifier `bip-kb doctor --live`, puis relancer la même ingestion.

### Une réponse d'analyse reste non conforme

La 0.3.8 distingue un JSON syntaxiquement invalide d'un JSON lisible dont la structure ne respecte pas le contrat. Pour l'ingestion, une seule nouvelle tentative est faite à partir de la demande originale, avec un rappel du format. La réponse refusée n'est jamais réinjectée comme instruction. Les références inventées restent rejetées, sans tentative de réparation.

Si le refus persiste, le message indique un diagnostic sous `.local/copilot-diagnostics/`. Ce fichier contient les réponses refusées, leur motif et l'empreinte de la demande ; il peut contenir des extraits privés. Il reste local, hors export et ignoré par Git : ne pas le publier ni le partager tel quel. Une impossibilité d'écrire ce diagnostic ne masque pas l'erreur d'analyse. Relancer sans `--reanalyze` permet de réutiliser les lots déjà validés.

`doctor --live` utilise désormais un test de connexion dédié. Sa confirmation signifie qu'un échange et la lecture JSON ont réussi ; elle ne valide pas à elle seule la compréhension du corpus ou le contrat d'ingestion.

### Refus d'accès pendant l'enregistrement d'un JSON

L'écriture conserve un fichier temporaire voisin puis le remplace atomiquement. La 0.3.8 réessaie au maximum quatre fois les refus d'accès et violations de partage, avec 1,5 seconde d'attente cumulée. Aucun droit ni attribut du fichier de destination n'est changé ; il n'est jamais supprimé pour contourner un verrou. Un refus persistant indique le chemin et le code système. Les autres erreurs E/S ne sont pas réessayées. Le script d'installation continue d'exiger des tests réussis.

### Suivre une ingestion longue

L'ingestion affiche automatiquement son étape, le fichier en cours (`3/42`), le lot d'analyse, les résultats réutilisés et le rapprochement entre fichiers. Pendant une étape longue, un rappel toutes les dix secondes indique sa durée ; il confirme que bip-kb attend toujours, sans prétendre mesurer la progression interne de Copilot ni prédire le temps restant.

```text
[ingestion 00:12] Fichier 3/42 — Controllers/Commandes.cs — lot 1/2 — attente de Copilot
[ingestion 00:30] Fichier 3/42 — Controllers/Commandes.cs — lot 1/2 — attente de Copilot — étape en cours depuis 18 s
```

Les messages vont sur stderr, sans contenu des documents, et sont désactivés avec `--json`. Ils s'arrêtent à la fin, à l'erreur ou à l'annulation. `Ctrl+C` interrompt ; relancer la même commande sans `--reanalyze` réutilise les lots terminés lorsque leur demande est inchangée. Une ingestion déjà démarrée avec une ancienne version ne reçoit pas rétroactivement ce suivi.

## Ingérer sans préciser le format

```powershell
bip-kb --ingest C:/Documents/Facturation
bip-kb --ingest C:/Documents/Facturation/tables.sql
bip-kb --ingest C:/Documents/Facturation/etude.xls
bip-kb --ingest C:/Documents/Facturation/modele.drawio
bip-kb --ingest C:/Documents/Facturation/schema.png --analyze
```

Pas de `source bind` préalable. Le premier import inventorie les fichiers, propose un contexte existant si l'aperçu présente suffisamment de termes communs et demande ce qui manque : identifiant de source, projet, sujet métier, rôle (`legacy`, `target`, `current`), statut (`proposal`, `validated`, `observed`, `historical`), description et notes. Vous confirmez avant l'ingestion ; le récapitulatif précise indexation locale ou analyse Copilot. `/cancel` ou `non` annule la préparation.

L'**identifiant de source** est un nom court, par exemple `etude-sql` ou `code_actuel` : uniquement lettres sans accent, chiffres, `-` et `_`. Le nom du dossier sert à proposer un identifiant valide et disponible. Une saisie invalide ou un identifiant déjà utilisé est corrigé immédiatement, avant les questions de contexte. Les noms du projet, les sujets métier et les descriptions acceptent espaces et accents.

Le contexte confirmé est enregistré **avant le traitement**. Si l'analyse échoue, relancer la même commande réutilise les réponses, y compris les descriptions longues. `--describe` sert uniquement à modifier ce contexte. Une faute dans la réponse oui/non est redemandée ; elle n'annule pas le formulaire.

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

Voir le [guide MCP avec Copilot](docs/mcp.md) pour la configuration VS Code, les exemples de demandes, les consignes réutilisables et le dépannage.

```powershell
bip-kb serve --port 5267
```

Interface sur la boucle locale : recherche, preuves, ingestions, arbitrages, projets, dossiers et mémoire humaine. Les actions d'écriture utilisent des formulaires antiforgery. Une analyse peut tourner pendant que les lecteurs consultent la dernière révision publiée.

Configuration d'un serveur MCP stdio (l'enveloppe dépend du client) :

```json
{"command":"bip-kb","args":["mcp","--workspace","C:/Knowledge/bip"]}
```

Outils en lecture seule : `search(query, project?)`, `context(query, project?)`, `entity(id)`, `evidence(id)`, `relations(id)`, `project(id)`, `task(id)`, `questions(id)` (nom du projet), `analysis(id, project?)` (identifiant de source). Aucun outil MCP ne peut ingérer, arbitrer ou lancer une enquête. Protocole 2024-11-05, transport JSON-RPC ligne par ligne.

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
