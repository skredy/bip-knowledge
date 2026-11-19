# bip-kb

Business analyste local avec une mémoire sourcée : comprendre le métier, ses processus, règles, exceptions, décisions et la transformation souhaitée. Copilot conduit le cadrage et l'analyse ; bip-kb conserve les preuves, les connaissances et leurs relations, puis les rend consultables rapidement par CLI, interface locale et MCP.

**Version 0.5.3.** `ask` et `chat` ajustent les preuves, le cadrage et l’historique au budget de chaque appel Copilot. Lors du premier ajout interactif, saisissez la description de la source ou tapez `/skip` pour passer. Un projet par workspace, entretien conversationnel, analyse des documents à l'échelle des sections, reprise et fiches sourcées. Les tests logiciels utilisent notamment un processus Copilot simulé ; la compréhension sur un corpus professionnel et la lecture native Excel par le vrai Copilot restent à valider. Voir le [guide complet et les limites](docs/business-analyst-guide.md).

Socle : .NET 10, SQLite FTS5, Razor Pages, outil .NET `bip-kb`. Windows est la plateforme prioritaire ; les tests CI couvrent Windows et Linux. Le partage se fait par Git, sans commit ou push automatique par le produit.

## Application Windows et VDI

Téléchargez l’artefact **bip-kb-desktop-win-x64** de la validation Windows, extrayez le dossier complet et lancez **BipKb.Desktop.exe**. Le client WPF autonome regroupe le cadrage, les sources, les questions, les preuves, les arbitrages et les diagnostics. Choisissez un disque persistant de la VDI pour votre workspace. [Installation et guide Desktop](docs/plan-client-desktop.md)

## Installer ou mettre à jour

Prérequis : SDK .NET 10 avec ASP.NET Core, Git et Copilot CLI authentifié pour les opérations d'analyse. L'indexation locale et la recherche directe ne demandent pas de modèle.

```powershell
./scripts/install.ps1 -ToolPath "$env:USERPROFILE/.bip-tools"
# Ajouter ce dossier au PATH.
bip-kb --version
```

Le script restaure les dépendances verrouillées, compile, teste, crée le paquet puis installe la version du projet. En cas d'échec, consulter la première erreur de l'étape concernée.

Sous Linux :

```bash
dotnet restore --locked-mode
dotnet test --no-restore
dotnet pack src/BipKb -c Release --no-restore -o artifacts
dotnet tool install BipKb --version 0.5.3 --add-source ./artifacts --tool-path ./artifacts/tools
```

Sous Windows, bip-kb détecte la distribution native `copilot.exe` et l'installation npm `@github/copilot` via Node.js. Le lanceur `copilot.ps1` seul ne garantit pas la présence de la CLI autonome. `bip-kb doctor` inspecte l'installation ; `bip-kb doctor --live` effectue une vraie requête.

## Démarrer sur un espace neuf

```powershell
bip-kb init --workspace C:/Knowledge/ibip-metier
cd C:/Knowledge/ibip-metier
```

Dans un terminal, `init` ouvre l'entretien avec Copilot : expliquez ce que vous cherchez à comprendre ou à faire évoluer. Il reformule, conserve vos réponses et pose une question utile à la fois. Vous n'avez pas à définir une liste de domaines ni à répéter le projet pour chaque source.

- `/confirm` retient le cadrage proposé ; les hypothèses restent identifiées.
- `/pause` conserve l'entretien ; `bip-kb project setup` le reprend.
- `project setup --message "…" --json`, puis une commande distincte `project setup --confirm --actor "Prénom"`, permettent un usage automatisé.
- `project show` et `project summary` consultent le projet sans devoir préciser son nom.

Les applications legacy, cible, bases et sujets métier appartiennent au même projet. Les espaces créés avant la 0.5.0 gardent leur [ancien parcours](docs/legacy-guide.md) ; ils ne sont pas convertis automatiquement. Les essais précédents peuvent être laissés de côté.

## Ajouter et comprendre les sources

```powershell
bip-kb --ingest C:/Documents/endpoints.xlsx
bip-kb --ingest C:/Documents/processus.drawio
bip-kb --ingest C:/Documents/architecture.png
bip-kb --ingest C:/Repos/legacy/src
bip-kb --ingest C:/Documents/modele.sql --analyze
```

Le format est détecté automatiquement. Dans un nouveau workspace, **un fichier Excel, Draw.io ou image est analysé par Copilot par défaut**. Les dossiers et les autres formats sont indexés localement ; `--analyze` demande l'analyse. `--index-only` impose une indexation sans appel Copilot, quel que soit le format.

Une description libre peut préciser le rôle de la source. Copilot utilise ensuite le cadrage pour proposer ses sujets et identifier une éventuelle question importante. Les indications enregistrées sont réutilisées ; `--describe` permet de les revoir. En automatisation : `--message "Étude des comportements actuels et propositions cible" --non-interactive`.

Copilot reçoit le document entier lorsque la vue lecteur tient dans le budget ; sinon, il choisit les sections à consulter. Il produit des connaissances métier et leurs relations, avec références exactes aux lectures présentes ou antérieures. Il n'est plus tenu de classer chaque ligne. Une compréhension partielle est signalée comme telle.

Le lecteur `.xlsx` conserve valeurs, coordonnées, formules XML, fusions et commentaires classiques disponibles. Il ne suppose pas que la première ligne porte les en-têtes. La lecture native du fichier original via Copilot est une option à valider, pas une capacité annoncée comme testée. Voir les [limites par format et l'option nativeDocuments](docs/business-analyst-guide.md).

```powershell
# Reprendre, en réutilisant les réponses déjà validées :
bip-kb --ingest C:/Documents/endpoints.xlsx --analyze
# Recommencer l'analyse :
bip-kb --ingest C:/Documents/endpoints.xlsx --reanalyze
# Consulter sa couverture :
bip-kb analysis <identifiant-de-source>
```

Les étapes et une activité périodique sont affichées sur stderr. `--json` garde une sortie exploitable par un programme. `Ctrl+C` interrompt en conservant les échanges validés ; aucune révision inachevée ne devient active.

## Interroger la mémoire

```powershell
bip-kb search "conditions de consultation d'un contrat"
bip-kb context "Que change la suspension d'un contrat ?" --output contexte.md
bip-kb ask "Quelles règles restent à arbitrer ?"
bip-kb questions
bip-kb remember "Cette proposition doit être confirmée par le métier" --actor Alice --confirm
bip-kb review
bip-kb serve
```

`search` et `context` sont locaux. `ask` utilise Copilot pour investiguer les résultats, sans réingérer sa propre réponse comme un fait. Par défaut, `ask` affiche le texte de la réponse. Ajoutez `--json` pour obtenir le dossier complet avec les preuves et les recherches effectuées. Les dossiers persistants restent disponibles avec `task start`, `task resume`, `task export` et `chat --task`.

La recherche privilégie les fiches métier et leurs synonymes, puis un voisinage borné du graphe. Le code et SQL servent à documenter les comportements observés. Les preuves techniques sont rattachées aux déclarations ou instructions concernées, sans rattacher tout le fichier à chaque objet dans le nouveau parcours.

L'interface locale affiche le cadrage, les connaissances, arbitrages, ingestions et couvertures. Elle écoute sur la boucle locale et protège les actions d'écriture par antifalsification.

## Connecter Copilot via MCP

```powershell
bip-kb mcp --workspace C:/Knowledge/ibip-metier
```

Configurer cette commande comme serveur stdio dans votre IDE. Le [guide MCP](docs/mcp.md) couvre VS Code, Visual Studio et le plugin GitHub Copilot de JetBrains.

Le serveur consulte l'index SQLite ; il ne charge pas tout `materialized.json` à chaque appel. `project` et `questions` acceptent `{}` pour l'unique projet. `context` fournit cadrage, fiches pertinentes, preuves et limites. Le MCP ne déclenche ni ingestion, ni arbitrage, ni nouvelle instance de Copilot.

## Partager et maintenir

Les sources originales restent hors de la mémoire partagée. Les extraits utiles, connaissances et déclarations confirmées sont partageables par Git ; les chemins physiques, caches et brouillons de dialogue restent locaux dans `.local/`.

```powershell
bip-kb export --output C:/Knowledge/partage
bip-kb import
bip-kb reindex
bip-kb cache compact --rebuild
```

L'index est reconstructible. Après une fusion Git ou un index obsolète, `import` ou `reindex` le remet à jour sans appel Copilot. Les conflits de décisions ne sont pas résolus silencieusement. Voir [la gestion du cache](docs/cache.md).

## Conception et validation

- [Guide du parcours métier 0.5.0](docs/business-analyst-guide.md).
- [Conception du rôle de business analyste](docs/business-analyst-workspace.md).
- [Refonte de la compréhension et de la recherche](docs/knowledge-redesign.md).
- [Validation et limites connues](docs/validation.md).

Le parcours installé est testé avec `python scripts/test-business.py --tool <chemin-bip-kb>`. Il utilise un modèle simulé pour contrôler le protocole et la reprise, pas pour mesurer la compréhension métier. Les tests authentifiés sont activables avec `BIP_KB_LIVE_TESTS=1` lorsqu'un Copilot réellement opérationnel est disponible.
