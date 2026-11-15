# iBip / bip-kb

Mémoire technique locale pour rapprocher études Excel, schémas, SQL et code .NET. Copilot CLI analyse les sources ; iBip conserve les preuves, versions et décisions et prépare le contexte de développement.

**État : première implémentation, à valider avec votre corpus et votre session Copilot.** Les tests simulés vérifient les contrats et la persistance ; ils ne prouvent pas la qualité métier du modèle. Aucun code applicatif n'est généré ou modifié.

## Installer

Prérequis : SDK **.NET 10**, Git, Copilot CLI authentifié et autorisé par votre entreprise. La CLI testée pour l'inspection des options est **1.0.111**. Une version plus ancienne doit fournir les capacités vérifiées par `doctor`. Le SDK .NET inclut le runtime ASP.NET Core utilisé par l'interface. Sous Windows, utiliser la distribution native `copilot.exe` ; les wrappers npm `.cmd` ne sont pas exécutés via un shell.

```powershell
./scripts/install.ps1 -ToolPath "$env:USERPROFILE/.bip-tools"
# Ajouter ce répertoire au PATH ou appeler son exécutable directement.
bip-kb init --workspace C:/Knowledge/ibip
bip-kb doctor --workspace C:/Knowledge/ibip
bip-kb doctor --live --workspace C:/Knowledge/ibip
```

Sous Linux :

```bash
dotnet restore --locked-mode
dotnet test --no-restore
dotnet pack src/BipKb -c Release --no-restore -o artifacts
dotnet tool install BipKb --add-source ./artifacts --version 0.1.0 --tool-path ./artifacts/tools
./artifacts/tools/bip-kb init --workspace /path/to/knowledge
```

`doctor` inspecte l'exécutable, sans prétendre vérifier l'authentification. `doctor --live` effectue un appel réel. Aucun secret ne doit être placé dans `bip.json` ni passé comme argument. Utiliser l'authentification normale de Copilot. Un accès Git fonctionnel ne garantit pas l'accès à l'API Copilot.

## Déclarer les sources une fois

Utiliser un espace de connaissance dédié, séparé des sources applicatives.

```bash
bip-kb source bind legacy /repos/ancien --role legacy --workspace /knowledge/ibip
bip-kb source bind target /documents/mpd --role target --workspace /knowledge/ibip
bip-kb source bind current /repos/nouveau/src --role current --workspace /knowledge/ibip
```

Le nom et le rôle sont partageables. Les chemins physiques restent dans `.local/bindings.json`, ignoré par Git. Un collègue réutilise les mêmes noms avec ses chemins. Un dossier peut être hors Git ; s'il contient des `.gitignore`, il doit appartenir à un dépôt Git pour que leur sémantique soit respectée.

Depuis l'espace de connaissance (ou avec `--workspace` sur chaque commande) :

```bash
bip-kb --ingest /documents/mpd/etude.xls
bip-kb --ingest /repos/nouveau/src
bip-kb --ingest /documents/mpd/architecture.png
bip-kb --ingest /documents/mpd/modele.drawio
bip-kb --ingest /documents/mpd/tables.sql
bip-kb ingest /documents/mpd/procedures.sql
bip-kb --ingest /documents/mpd/etude.xls --source target --role target --reanalyze
```

Le type de fichier est détecté automatiquement ; un dossier peut mélanger SQL, code, Excel, JSON, images et schémas. Les anciennes options `--file`, `--path`, `--image`, `--schema` restent des alias facultatifs.

Le rôle peut être surchargé pour un import. Une source ambiguë est refusée avec une action précise. `--reanalyze` ignore les réponses en cache ; la relance normale reprend les analyses terminées. Une ingestion n'est publiée qu'après réussite de toutes ses analyses. Le rapport distingue les sept apports et indique les limites d'extraction.

## Consulter et arbitrer

```bash
bip-kb search "commande client"
bip-kb ask "Comment calcule-t-on le statut d’une commande ?"
bip-kb context "Préparer un endpoint de consultation des commandes" --output contexte.md
bip-kb report ID_INGESTION --json
bip-kb review
bip-kb review ID_CONNAISSANCE --action accept --reason "Vérifié avec le métier" --actor Alice
bip-kb serve --port 5267
```

L'interface est accessible uniquement depuis la machine locale. Elle présente les ingestions, la recherche, le détail des preuves et les arbitrages. Les formulaires d'écriture sont protégés contre les requêtes intersites.

Actions : `accept`, `reject`, `distinct` (concepts différents), `replace` (remplace les connaissances liées). Les deux dernières exigent une relation existante. Les décisions sont conservées ; des décisions concurrentes incompatibles réapparaissent dans les arbitrages. Un changement de preuves demande un réexamen. Un rejet exclut le résultat de la recherche normale ; l'historique reste consultable.

`ask` ne réingère jamais sa réponse. `context` produit un dossier avec sources et incertitudes ; les vérifications génériques sont étiquetées comme suggestions, pas comme exigences métier.

## Partager avec Git

La mémoire est `knowledge.json`, les décisions sont des fichiers immuables dans `decisions/`, la configuration est `bip.json`. SQLite et les chemins locaux ne sont jamais à partager.

```bash
bip-kb export --output /exports/ibip
# Versionner cet export, ou directement l'espace de connaissance, avec votre workflow Git.
# Après clonage / fusion sur un autre poste :
bip-kb import --workspace /repos/ibip-knowledge
bip-kb source bind current C:/repos/nouveau/src --workspace /repos/ibip-knowledge
```

`import` valide les références et reconstruit SQLite depuis les fichiers présents. Ce n'est pas un téléchargement et cela ne remplace pas les fichiers depuis un autre chemin. Aucun commit, pull ou push n'est exécuté par iBip.

`knowledge.json` est un instantané atomique trié pour rendre les différences lisibles. Des ingestions concurrentes sur différentes branches peuvent produire un conflit Git dans ce fichier : le résoudre explicitement avant `import`, sans choisir silencieusement une branche entière. Les décisions indépendantes se fusionnent par leurs fichiers ; deux choix incompatibles sont signalés par iBip. Ne pas partager une base SQLite sur un lecteur réseau.

## MCP

Configurer un serveur **stdio** dans un client compatible :

```json
{
  "command": "bip-kb",
  "args": ["mcp", "--workspace", "C:/Knowledge/ibip"]
}
```

Le conteneur de cette entrée dépend du client/IDE. Outils : `search(query)`, `entity(id)`, `evidence(id)` (ID d'assertion), `relations(id)` (ID d'assertion), `context(query)`. Lecture uniquement ; aucune génération, ingestion ou décision exposée. Transport JSON-RPC ligne par ligne, protocole MCP 2024-11-05.

## Configuration et limites explicites

`bip.json` permet de régler `exclusions` (glob), `analysisProfile` (invalide le cache lorsqu'il change), `timeoutSeconds`, `maxFileBytes` et `copilotExecutable` (par défaut `copilot`). Chaque entrée de `sources` accepte aussi `excelSheets`, une liste optionnelle de noms de feuilles à analyser. Pour éviter des chemins propres au poste dans Git, conserver le nom de l'exécutable dans cette configuration et utiliser le PATH du poste.

- SQL : dialecte SQL Server ; SQL dynamique et erreurs de parsing signalés. Roslyn extrait la syntaxe, les signatures et les usages présents ; pas de résolution interprojets équivalente à une compilation de solution.
- Excel : valeurs enregistrées et coordonnées ; les formules ne sont pas recalculées. Les tables sont transmises sous forme de lignes bornées.
- Images : pièce jointe native, dépendante du support visuel du modèle choisi dans Copilot. Une erreur de lecture bloque la publication, sans résultat artificiel fondé sur le nom de fichier.
- La recherche est lexicale avec FTS5, sans embeddings. Le rapprochement utilise un contexte borné : les relations sémantiquement très éloignées peuvent manquer.
- Une citation prouve une provenance, pas la vérité d'une interprétation. Les extraits et réponses doivent être revus selon les enjeux métier.
- Les originaux restent hors de la mémoire partagée ; leurs extraits peuvent être confidentiels. Utiliser un dépôt de connaissance et un accès Copilot adaptés aux règles de votre entreprise.
- Les noms de fichiers sensibles et quelques signatures de clés sont filtrés ; ce filtre n'est pas un détecteur exhaustif de secrets. Configurer les exclusions avant ingestion.
- Aucun moteur de macros, formule Excel, SQL ou code extrait n'est exécuté. Les fichiers XML avec DTD sont refusés et les liens symboliques ne sont pas suivis.
- Pas de service central, synchronisation automatique, entraînement ou écriture du code applicatif.

Voir [architecture](docs/architecture.md), [validation](docs/validation.md) et [procédure de validation sur poste](scripts/validate.ps1).
