# Architecture et invariants — bip-kb 0.3

## Composants

- `SourceSetup` : inventaire préalable, contexte guidé, confirmation et exceptions par chemin.
- `Projects` : profil global explicite, domaines liés, glossaire, autorités par sujet et synthèse dérivée.
- `Extractor` / `TechnicalExtraction` : unités documentaires et objets/relations SQL et C# observables.
- `Ingestor` : empreintes, extraction, recherche des assertions proches, analyse, rapprochement et publication.
- `MemoryStore` / `Workspace` : mémoire partagée, journal de changements, migration et décisions.
- `SearchEngine` : index FTS5 des assertions **et des extraits originaux**, objets, entités et déclarations humaines.
- `Research` : enquêtes bornées, conversations, questions, dossiers et fraîcheur.
- `CopilotAnalyzer` : contrat JSON, processus non interactif, limites et annulation.
- CLI, Razor Pages et MCP utilisent ces services communs. MCP reste strictement en lecture seule.

## Identité et provenance

Une entité a une identité conservée lors de son renommage ; son ancien nom devient un alias. La clé initiale combine source, périmètre et nom qualifié. Les objets techniques utilisent leur nom qualifié et leur source ; un même nom dans deux sources ne constitue pas automatiquement une équivalence. Les projets/domaines ont des identifiants dans le profil, mais le contexte des sources référence encore leurs **noms** : les maintenir cohérents, ne pas les renommer comme une simple étiquette.

Les assertions distinguent apport d'ingestion, origine (`extracted`, `inferred`, `declared`), état et conditions. Les décisions sont conservées séparément et déterminent l'état consultable. Une décision dont les preuves actives ont changé apparaît à réexaminer. Une source proposée n'est jamais promue automatiquement en exigence validée. Une déclaration humaine reste identifiable par son auteur et ne se transforme pas en preuve extraite.

Les preuves portent source logique, chemin relatif, version, emplacement et extrait. Les unités conservent leur parent ; les lignes Excel incluent l'en-tête. Les objets techniques citent les unités de leur fichier : ce niveau de provenance ne constitue pas une résolution exacte des dépendances d'un projet entier. Les appels externes non résolus sont des inférences ; aucun MSBuild ou code source n'est exécuté.

## Mémoire partagée et publication

`knowledge.json` est désormais un marqueur de format 2. `memory/objects/<empreinte>.json` contient un lot de mutations par identifiant. `memory/changes/<id>.json` indique ses parents, son empreinte et sa date. Le manifeste est publié atomiquement en dernier : un fichier de données isolé ne rend rien visible. Les fichiers immuables se partagent par Git ; SQLite et les chemins physiques restent sous `.local/`.

Le chargement vérifie les parents, cycles, empreintes et références. Deux écritures concurrentes différentes sur un même identifiant nécessitent une résolution explicite ; aucune date ne donne silencieusement priorité à l'une. Une résolution est validée avant publication. Les fichiers `decisions/` conservent leur propre chaîne de remplacement et leurs conflits d'arbitrage.

Un cache local matérialisé évite de rejouer tout le journal. Il suppose que les anciens lots sont immuables ; `import` invalide ce cache et vérifie leur intégrité. Ne pas éditer les lots à la main. Après une fusion Git, exécuter `import`.

L'analyse distante ne garde pas le verrou d'écriture. Elle travaille à partir d'une révision et stocke ses résultats intermédiaires validés sous `.local/staging`. Le verrou est acquis uniquement pour publier ; un changement concurrent de mémoire/configuration interrompt cette publication. Relancer l'ingestion réutilise les réponses dont la demande est identique. La révision précédente reste consultable pendant l'analyse.

Le cache dépend du contenu, de l'extracteur, du profil d'analyse et du contexte effectif du fichier. Une modification de configuration d'une source indépendante ne force pas sa réanalyse. `--reanalyze` réévalue volontairement une source. Une source inaccessible n'est pas supprimée ; seules une inspection complète de sa racine et une absence confirmée rendent ses preuves historiques.

## Index et recherche

SQLite utilise WAL, FTS5, des index de périmètre et un graphe d'adjacence. Une révision déjà indexée n'entraîne aucun chargement du corpus pour une recherche CLI. Le rafraîchissement compare les empreintes des documents et ne réécrit que les lignes FTS modifiées. Les suppressions FTS passent par un `rowid` indexé : aucune recherche linéaire par identifiant pour chaque insertion.

La recherche applique le projet avant de limiter les candidats, puis combine identifiants exacts, texte normalisé, quelques variantes de pluriel, glossaire et relations. La portée est bornée : 200 candidats lexicaux, 30 voisins par nœud, profondeur maximale de trois, vingt résultats par défaut. Les domaines liés sont explicites. La pertinence intègre domaine, validation humaine et autorité selon l'intention (`schema`, `runtime`, etc.). Les raisons de sélection et les preuves restent visibles. Un arbitrage `distinct` retire le rapprochement de la navigation.

Le rafraîchissement lit encore la mémoire matérialisée complète ; les tables d'arêtes et profils sont reconstruites à chaque révision. Le journal partage seulement les changements mais leur calcul parcourt aussi l'état complet. Ces coûts de publication ne sont pas ceux des recherches à chaud. Le benchmark documenté mesure un corpus synthétique et n'établit pas la qualité d'une recherche métier.

## Enquêtes et mémoire humaine

Une enquête charge le profil explicite, recherche, fournit un contexte borné à Copilot puis utilise ses demandes complémentaires. Elle s'arrête après le budget de cycles ou quand aucune recherche supplémentaire n'est proposée. Chaque étape conserve requête, résultats, identifiants effectivement transmis, empreinte de la demande et durée. La conversation n'est pas réingérée automatiquement.

Les dossiers conservent réponses, références et questions ouvertes. Les réponses humaines nécessitent auteur et confirmation. Les déclarations deviennent recherchables par sujet, même lorsqu'elles ne font plus partie des dernières déclarations transmises comme contexte. La couverture indique seulement l'existence de **preuves candidates**. Aucun résultat ne démontre l'absence d'une règle.

La fraîcheur dépend des sources, assertions, profil, décisions et déclarations du projet, ainsi que des relations. Cette invalidation est volontairement conservatrice : une nouvelle source peut rendre un dossier à actualiser même si ses anciennes citations existent toujours. Reprendre relance la recherche ; les réponses précédentes restent dans l'historique.

## Contrat Copilot et limites de confiance

Le lanceur Copilot est résolu une seule fois avant le changement de dossier : exécutable natif ou point d’entrée npm lancé directement avec Node.js. Le chemin relatif éventuel dépend du workspace. Les wrappers PowerShell/batch ne sont pas interprétés et le bootstrapper VS Code n’est pas exécuté. Les arguments restent séparés avec `ArgumentList` et les sorties sont décodées en UTF-8.

Chaque processus d’analyse démarre dans un dossier temporaire hors des sources. Données JSON et pièces jointes sont non fiables et ne constituent jamais des instructions. Pas de shell pour interpréter les arguments. Les commandes, écritures, lectures par outils, mémoire et URL sont refusées ; liste d'outils vide, instructions personnalisées et MCP intégrés désactivés. Aucun mode donnant toutes les permissions. Les fichiers temporaires sont supprimés après usage.

Les unités et références sont bornées à 18 000 caractères de demande structurée ; les arguments encodés sont limités à 30 000 caractères pour Windows. Les sorties dépassant 2 Mo sont rejetées. Timeout/annulation terminent l'arbre du processus ; seules les erreurs transitoires 429/502/503/504 ont deux reprises maximum. Les sorties d'erreur du fournisseur ne sont pas réimprimées avec les documents ou secrets.

La réponse JSON est strictement désérialisée et ses références validées contre les éléments fournis. Cela établit la provenance, pas la vérité métier ni la compréhension visuelle. Une image sans capacité de pièce jointe provoque une erreur explicite.

L'interface écoute uniquement sur 127.0.0.1, vérifie le Host et utilise antiforgery sur les POST. Razor encode les contenus. Ce n'est pas un serveur multi-utilisateur ; le partage concerne les fichiers Git.

## Migration

`migrate --dry-run` inspecte une mémoire de format 1. `migrate` sauvegarde configuration, mémoire et décisions dans `.local/migration-backup`, conserve les identifiants historiques, publie et valide le nouveau journal, puis remplace le marqueur. L'opération est idempotente. Les anciennes sources sans contexte restent consultables, mais doivent être qualifiées avant nouvelle ingestion. Revenir en arrière consiste à restaurer la sauvegarde dans un dossier distinct avec l'ancien programme.
