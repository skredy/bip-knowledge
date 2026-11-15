# Architecture et invariants

## Organisation

`Domain` définit les types sérialisés, `Extractor` les lecteurs, `Ingestor` le pipeline, `Workspace` la persistance et FTS5, `CopilotAnalyzer` les processus CLI, `KnowledgeService` les exports et dossiers. Razor Pages et MCP utilisent les mêmes services.

Une mémoire contient des versions de sources, entités, assertions, preuves et rapports. Identifiants SHA-256 déterministes ; horodatage et identifiant unique pour les ingestions et décisions. Les références de fichiers sont `sourceId + relativePath`, indépendantes du poste.

## Publication et reprise

Un verrou local exclusif protège l'ingestion, les décisions et la reconstruction d'index. L'ingestion travaille sur une copie mémoire, écrit les réponses validées dans `.local/staging`, puis remplace atomiquement `knowledge.json`. Le JSON fait autorité ; une panne avant le remplacement n'active aucun résultat partiel. Un index absent ou obsolète est reconstruit par recherche/import.

Le cache inclut le contenu, la configuration, le rôle, la version d'extracteur et le contexte fourni. Changer `analysisProfile` force la prise en compte d'un nouveau modèle ou prompt. `--reanalyze` contourne le cache. Une reprise ne réutilise une réponse qu'avec exactement la même demande.

Les sources identiques ne déclenchent pas une confirmation indépendante. Une suppression n'est inférée qu'après inventaire complet de la racine liée, et absence réelle du fichier. Une source inaccessible ou un fichier simplement exclu ne provoque pas de suppression. Les preuves historiques sont conservées ; une assertion sans preuve active devient obsolète.

Les décisions sont des fichiers immuables. Une nouvelle décision cite les décisions qu'elle remplace. Après fusion, des décisions terminales incompatibles restent visibles au lieu d'appliquer un ordre temporel arbitraire. La modification des preuves déclenche leur réexamen.

## Contrat Copilot

Entrée : rôle, unités extraites, assertions proches, entités et décisions pertinentes. Sortie : tableau `findings` strict, avec entité, nature, assertion, apport, preuves, relations et incertitude. Les champs inconnus, références inventées, relations absentes et réponses malformées sont refusés.

Chaque processus démarre dans un dossier de travail neuf hors des sources. Les données sont encodées en JSON dans le prompt. Pas de shell d'interprétation pour les arguments ; commandes système, écriture, lecture par outils, mémoire et URL sont refusées. La liste d'outils est vide ; les instructions du dépôt et MCP intégrés sont désactivés. Les pièces jointes sont copiées dans le dossier temporaire puis supprimées. Les mécanismes propres à une installation Copilot restent sous le contrôle de l'administrateur du poste.

Les unités sont découpées et les références proches bornées pour limiter chaque requête structurée à 18 000 caractères. Un contrôle supplémentaire borne les arguments encodés à 30 000 caractères, sous la limite de création de processus Windows.

Les codes transitoires 429/502/503/504 font l'objet de deux nouvelles tentatives maximum. Timeout et annulation tuent l'arbre du processus. Les erreurs du fournisseur ne sont pas réimprimées avec le contenu des documents ou des credentials. La limite de sortie est 2 Mo.

## Limites de confiance

Les textes importés ne sont jamais des instructions fiables. Une validation JSON et une preuve existante empêchent les références inventées ; elles ne prouvent pas que le modèle a correctement interprété le contenu. Les interprétations incertaines restent des hypothèses. Les arbitrages humains sont transmis aux analyses et ne sont jamais écrasés par celles-ci.

L'interface web est locale, sans mode multi-utilisateur : la possession de la session locale donne accès à la mémoire. Vérification du Host, absence de CORS permissif et antiforgery sur POST. Les sorties HTML sont encodées par Razor.
