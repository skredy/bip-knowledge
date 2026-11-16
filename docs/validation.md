# Validation — bip-kb 0.3.0

Vérifications réalisées le 15 novembre 2026 dans l'environnement Linux avec .NET SDK 10.0.404. Cette page distingue les mécanismes testés de la validation métier à effectuer avec Copilot sur le poste professionnel.

## Suite automatisée

**57 tests réussis, 1 test Copilot authentifié ignoré par défaut, aucune défaillance.** Dépendances restaurées avec `--locked-mode`, compilation, tests xUnit et paquet .NET tool Release.

Couverture :

- XLS/XLSX, Draw.io natif/compressé, SQL Server et SQL dynamique, SQL UTF-16, C#, JSON, Markdown, pièces jointes image, ignores Git et liens symboliques.
- Premier import guidé, confirmation/annulation, contexte JSON, refus non interactif sans contexte, exceptions par fichier et conservation du contexte au remappage.
- Incrémentalité, réanalyse, suppression, source inaccessible, reprise après interruption, déduplication, absence de publication partielle et lectures pendant une analyse lente.
- Contradiction/évolution, décisions concurrentes, préservation des décisions et état à réexaminer après changement des preuves.
- Migration avec sauvegarde, identifiants conservés, reconstruction sans `.local/`, lots orphelins, corruption, import avec cache local et résolution explicite de conflits Git.
- Recherche d'extraits sans assertions, paramètres/filtres SQL, pluriels français, domaines liés, glossaire, filtres de rôle et isolement des projets.
- Dossiers persistants, cycles de recherche, références inventées refusées, questions, déclarations confirmées retrouvables au-delà des cinq dernières et invalidation après nouvelle source.
- Contrats JSON et processus réels utilisant des **exécutables simulés**, restrictions CLI, erreurs, timeout, MCP en lecture seule et limites portables des arguments.

Commandes reproductibles :

```bash
dotnet restore --locked-mode
dotnet test --no-restore --logger "trx;LogFileName=tests.trx"
dotnet pack src/BipKb -c Release --no-restore -o artifacts
```

## Parcours du paquet installé

Le paquet 0.3.0 a été installé comme outil, puis utilisé sur trois sources distinctes : étude `.xls` de test, SQL cible avec procédure et filtre, code C#. **Copilot est simulé pour ce parcours** : aucune conclusion sur sa compréhension métier n'en découle.

Parcours vérifié : `init`, profil global, `doctor`, ingestion sans type explicite et avec contexte, réingestion inchangée, recherche, contexte Markdown, création/reprise de dossier, conversation, réponse à une question, déclaration humaine, détection d'un dossier à actualiser, export, import dans un second espace, évaluation de recherche et appel MCP. Les huit outils MCP sont annoncés en lecture seule.

Le premier import guidé a également été exécuté dans un pseudo-terminal pendant la réalisation ; les scénarios interactifs sont couverts par les tests à entrées injectées.

## Interface web

Tests dans Chromium avec Playwright sur l'application réellement lancée depuis le paquet :

- Ouverture des six vues principales ; recherche et consultation des preuves.
- Enregistrement d'un arbitrage, création d'un dossier, recherche avec réponse simulée.
- Enregistrement d'une déclaration et réponse à une question.
- Contenu HTML de déclaration affiché comme texte, sans exécution de script.
- POST sans jeton antiforgery refusé avec 400 ; Host étranger refusé avec 400.
- Aucune erreur JavaScript relevée dans les parcours exécutés.

Il s'agit de l'interface locale Linux ; les IDE clients et Windows n'ont pas été exécutés ici.

## Mesure de recherche locale

Commande : `bip-kb benchmark --count 100000 --json`, paquet Release installé. Corpus synthétique de 100 000 extraits SQL, 30 recherches d'identifiants uniques, sans Copilot :

| Mesure | Résultat observé |
|---|---:|
| Sauvegarde initiale et construction de l'index | 4,92 s |
| Latence médiane à chaud | 17,2 ms |
| Latence p95 à chaud | 27,4 ms |
| Identifiants attendus retrouvés | 30 / 30 |

La mesure précédente en Debug donnait 5,30 s de construction et 15,3 ms de p95. La charge de la machine fait varier ces chiffres ; ce sont des observations, pas un SLA. Le benchmark a permis de supprimer un parcours quadratique lors des insertions FTS. Le calcul des changements et le rafraîchissement des arêtes parcourent encore la mémoire complète lors d'une nouvelle révision.

`evaluate` produit rappel dans les vingt premiers résultats, références manquantes/interdites et latences. Un petit cas du parcours installé et les tests de récupération passent. **Aucun score de pertinence métier sur votre corpus n'est encore établi.** Construire un jeu de questions avec les preuves attendues de votre migration pour cette mesure.

## Copilot réel : blocage réseau constaté

Copilot CLI **1.0.111** est installé. `doctor` inspecte effectivement son aide et détecte le mode non interactif ainsi que les pièces jointes. `doctor --live` a été retenté sur la 0.3.0 : échec du processus. Une requête HTTPS indépendante vers `api.github.com/copilot_internal/user` confirme un refus du tunnel par le proxy (403).

Aucune compatibilité authentifiée, analyse visuelle ou qualité métier n'est annoncée comme validée. Aucun secret supplémentaire ni changement de politique réseau n'a été effectué. Les tests simulés vérifient le contrat et les parcours, pas la disponibilité ou la compréhension du modèle.

## Validation sur votre poste

1. Installer avec `scripts/install.ps1`. Pour une ancienne mémoire, lancer `migrate --dry-run`, puis `migrate` et `import`.
2. Authentifier Copilot normalement et exécuter `scripts/validate.ps1 -Workspace <espace> -Live`.
3. Lancer le test réel ; il couvre ingestion, réponse citée et contrat d'enquête :

```powershell
$env:BIP_KB_LIVE_TESTS = '1'
dotnet test --filter FullyQualifiedName~AuthenticatedCopilot
```

4. Configurer votre profil et ingérer une étude Excel, le SQL cible et une implémentation représentative. Contrôler le contexte, les preuves, les rapprochements et les hypothèses.
5. Tester Draw.io et image ; vérifier les relations visuelles et les limites annoncées.
6. Préparer un endpoint dans un dossier, préciser une ambiguïté, confirmer une déclaration puis modifier une source : vérifier le réexamen et la reprise.
7. Évaluer quelques questions métier avec des références attendues, puis tester le client MCP dans votre IDE.

La CI Linux/Windows est fournie ; son résultat après push doit être consulté sur GitHub. Le script PowerShell n'a pas été exécuté sous Windows dans cette session.

## Correctif 0.3.1 — setup expliqué

Le 16 novembre 2026 : **62 tests réussis, 1 test Copilot réel ignoré**. Les nouveaux scénarios vérifient le profil minimal, les champs facultatifs sans valeurs inventées, la conservation du profil et des liens entre sujets lors d'une modification, l'aide `?`, la nouvelle saisie d'un champ requis et l'annulation sans écriture. Le paquet Release 0.3.1 est construit. Un parcours en pseudo-terminal vérifie également les explications, l'aide, les champs laissés vides et le récapitulatif avant confirmation. Le blocage de la validation Copilot réelle décrit ci-dessus reste inchangé.

## Correctif 0.3.2 — validation du contexte

Le 16 novembre 2026 : **71 tests réussis, 1 test Copilot réel ignoré**. Le message « Contexte requis » était aussi utilisé pour un champ rempli mais trop long. Chaque erreur nomme désormais le champ et indique, le cas échéant, sa longueur et sa limite. La saisie guidée redemande uniquement la réponse invalide ; un contexte enregistré invalide peut être corrigé en conservant les autres valeurs. Les notes JSON nulles sont acceptées comme facultatives.

Les tests couvrent chaque limite de longueur, le statut inconnu, les notes facultatives, l'absence d'écriture avant validation, la réparation d'un contexte enregistré et le dépassement du budget après encodage. Un parcours avec le paquet 0.3.2 installé vérifie un contexte dont tous les champs requis sont remplis mais la description trop longue, le diagnostic précis et l'absence d'enregistrement, puis l'ingestion après correction avec Copilot simulé. Cela reproduit une cause du message signalé ; la cause exacte sur le poste utilisateur dépend de sa commande et de ses valeurs.


## Correctif 0.3.3 — vérification du parcours complet

**81 tests xUnit réussis, 1 test Copilot réel ignoré.** L'identifiant de source est maintenant validé dès sa saisie et avant tout inventaire pour une option `--source` non interactive invalide. La même règle s'applique à `source bind` et à l'enregistrement du contexte. Les propositions dérivées des noms de dossiers sont valides et évitent les identifiants déjà utilisés. Un identifiant saisi n'est pas renommé silencieusement. Les confirmations acceptent les majuscules et redemandent une réponse en cas de faute.

Le script reproductible `scripts/test-terminal.py` pilote de vrais processus dans un pseudo-terminal Linux, avec un processus Copilot simulé. Il couvre :

1. `init` puis setup interactif du projet.
2. Import d'un dossier contenant SQL, XLS et C#, avec corrections d'identifiant, rôle, statut et description trop longue.
3. Descriptions et précisions longues, faute à la confirmation, puis confirmation en majuscules.
4. Échec volontaire du processus Copilot après enregistrement du contexte ; relance dans un autre processus sans aucune nouvelle saisie.
5. Réingestion inchangée sans appel au modèle, recherche et dossier de contexte.
6. Modification du contexte d'un seul fichier, sans changement de ses voisins, puis annulation d'une autre modification sans enregistrement.
7. Refus immédiat d'un `--source` invalide en mode automatisé.
8. Export/import vers un second espace, remappage et réingestion inchangée.

Le parcours a été exécuté sur la compilation Debug puis sur le **paquet 0.3.3 installé**. Il est ajouté à la CI Linux ; les tests xUnit des formulaires restent inclus dans la matrice Linux/Windows. L'exécution réelle sous Windows et l'analyse métier par Copilot authentifié ne sont pas établies par ces tests.

```bash
# Après dotnet build, sans dépendance Python supplémentaire :
python3 scripts/test-terminal.py
# Avec un paquet déjà installé :
python3 scripts/test-terminal.py --tool /chemin/vers/bip-kb
```

## Correctif 0.3.4 — résolution de Copilot sous Windows

**90 tests xUnit réussis, 1 test Copilot authentifié ignoré.** La capture utilisateur montrait uniquement le lanceur `github.copilot-chat/copilotCli/copilot.ps1` de VS Code. L'examen du [bootstrapper amont](https://github.com/microsoft/vscode-copilot-chat/blob/5863f5a/src/extension/chatSessions/vscode-node/copilotCLIShim.ps1) confirme qu'il cherche une installation autonome et peut proposer de l'installer. La présence de ce script ne prouve pas que cette installation existe ou soit accessible dans PATH.

La résolution prend en charge `copilot.exe` et les shims npm `.cmd`/`.ps1` associés au package installé `@github/copilot`. Dans ce dernier cas, bip-kb lit `bin` dans `package.json`, vérifie le chemin et lance directement Node.js avec le point d'entrée ; aucune commande PowerShell/batch ni installation automatique. `doctor` affiche l'exécutable résolu, le mode de lancement et les arguments de démarrage fixes.

Les tests de résolution couvrent le lanceur VS Code seul, un exécutable situé après celui-ci dans PATH, npm, les chemins avec espaces/accents, les installations incomplètes et un point d'entrée sortant du package. Un test avec un véritable processus Node vérifie que JSON, guillemets, retours à la ligne et caractères de shell arrivent inchangés. Ce test est ignoré si Node n'est pas installé : Node reste facultatif pour une installation native de Copilot.

Le paquet 0.3.4 installé passe également tout le scénario `scripts/test-terminal.py`. `doctor` inspecte l'installation Linux réelle de Copilot CLI 1.0.111 et indique correctement son chemin. Les scénarios de résolution Windows sont exercés sur des arborescences de test et intégrés à la CI Windows ; cette session ne constitue pas une validation authentifiée sur le poste professionnel. Le blocage réseau de l'analyse réelle reste celui documenté plus haut.


## Correctif 0.3.5 — répertoire temporaire et nettoyage sous Windows

**97 tests réussis, 2 tests ignorés sur Linux** : Copilot authentifié et un test propre aux verrous Windows. L'analyse utilisait `.local/copilot/<id>` dans le workspace, puis appelait `Directory.Delete` sans protection dans un `finally`. Un échec de cette suppression pouvait remplacer une réponse valide, une erreur fournisseur ou une annulation. La capture de refus d'accès ne permet pas à elle seule de distinguer création et suppression ; ce défaut de masquage a été reproduit par injection de refus d'accès.

Les appels Copilot utilisent désormais un dossier privé créé dans le répertoire temporaire système. `doctor` expose `temporaryRoot`. Le nettoyage traite les attributs de lecture seule uniquement dans ce dossier, ne suit pas les liens et réessaie trois fois les erreurs d'accès/E/S. Un échec persistant est signalé avec le chemin restant, sans modifier le résultat de l'analyse. Aucun droit du workspace ni paramètre OneDrive n'est modifié. Les pièces jointes éventuellement conservées sont signalées dans l'avertissement.

Les tests couvrent succès et erreur fournisseur préservés malgré un refus de nettoyage, annulation, verrou transitoire, fichiers/dossiers en lecture seule, pièce jointe absente et non-traversée des liens. Un test spécifique Windows garde un fichier ouvert sans autoriser sa suppression ; il est inclus dans la CI Windows et ignoré dans cette session Linux. Après timeout, le processus est attendu pendant un délai borné avant de nettoyer.

Le paquet 0.3.5 installé passe le parcours terminal complet avec l'ancien emplacement `.local/copilot` remplacé par un fichier, donc inutilisable comme dossier. Cela vérifie que l'analyse n'en dépend plus. Les nouvelles erreurs éventuelles de `doctor --live` doivent être traitées selon leur nature ; la résolution du nettoyage ne démontre ni une authentification valide ni l'accès au modèle sur le poste utilisateur.

## Correctif 0.3.6 — lecture des réponses Copilot

**116 tests réussis, 2 ignorés sur Linux** (Copilot authentifié et verrou Windows). Un nouveau test reproduit l'erreur de la capture : LF littéral dans `$.text`, position 100. Le lecteur encode uniquement les LF/CR/tabulations à l'intérieur des chaînes, puis désérialise strictement. Les tests vérifient la conservation exacte des valeurs, les chemins et guillemets déjà échappés, les blocs Markdown CRLF et le rejet des autres JSON malformés, des champs inconnus et des références inventées.

L'adaptateur choisit `--output-format json` quand l'aide l'annonce. Les tests de transport couvrent les messages complets, les deltas et événements ignorés, les erreurs, les flux incomplets et la confirmation finale. Un vrai processus simulant la CLI vérifie `doctor --live` avec une réponse JSONL contenant un JSON métier mal échappé. Le parcours terminal complet passe avec la sortie JSONL simulée, sur Debug puis sur le **paquet 0.3.6 installé** : SQL/XLS/C#, reprise sans nouvelle saisie, recherche, contexte et partage.

L'installation réelle de Copilot CLI **1.0.111** annonce l'option JSONL ; `doctor` du paquet installé affiche `responseFormat: "jsonl"`. Une sonde réelle constate le record final `result` avec `exitCode: 1` lors du refus réseau. L'accès au modèle reste bloqué ici : la forme d'un message de succès authentifié et le parcours complet sous Windows avec la CLI 1.0.112 du poste utilisateur restent à confirmer. Les fixtures ne remplacent pas cette validation. Aucun document professionnel n'a été nécessaire à ces tests.

## Version 0.3.7 — suivi d'ingestion

**120 tests réussis, 2 ignorés sur Linux** (Copilot authentifié et verrou Windows). L'ingestion signale ses étapes, les fichiers et lots en cours, les réutilisations et la publication. Un rappel périodique toutes les dix secondes affiche le temps écoulé pendant une étape longue, y compris l'attente de Copilot. Il n'indique pas un pourcentage d'avancement du modèle. Le suivi va sur stderr et est désactivé avec `--json`.

Les tests maintiennent volontairement l'analyse en attente pour vérifier que le rappel arrive avant sa résolution, puis vérifient succès et annulation sans publication partielle. Ils contrôlent la reprise depuis les lots enregistrés, l'absence d'annonce d'appel Copilot pour des fichiers inchangés et la conservation du résultat si le flux d'affichage est fermé. Le contenu documentaire n'est pas recopié dans le suivi.

Le **paquet 0.3.7 installé** passe le parcours terminal SQL/XLS/C# complet avec Copilot simulé, en vérifiant l'affichage des fichiers, des attentes, du rapprochement et de la fin. Le même parcours vérifie que les commandes `--json` n'émettent pas ces messages. Le suivi sera visible au prochain lancement après mise à jour ; il ne s'ajoute pas à un processus déjà démarré.

## Version 0.3.8 — refus d'accès et réponses non conformes

Les captures du poste professionnel confirment un `doctor --live` réussi en 0.3.6 avec Copilot CLI 1.0.112, puis une réponse d'ingestion refusée. Sans cette réponse, la cause exacte de son refus n'est pas établie. La tentative d'installation 0.3.7 s'arrête dans un test lors du remplacement atomique de `bip.json`, avant la mise à jour de l'outil. La capture ne permet pas d'attribuer le refus d'accès à un logiciel particulier.

**135 tests réussis, 4 ignorés sur Linux** : Copilot authentifié et trois tests de verrouillage Windows. Les refus d'accès temporaires et violations de partage sont injectés pour vérifier la reprise bornée, la conservation intégrale de la destination et le nettoyage. Les erreurs permanentes sont signalées après cinq tentatives, les autres erreurs E/S immédiatement. Deux nouveaux tests utilisent de vrais handles Windows, libérés ou maintenus ; ils sont inclus dans la matrice Windows mais n'ont pas été exécutés dans cette session Linux. Le script d'installation exige toujours une suite réussie.

Les tests de réponse distinguent syntaxe JSON et désérialisation, imposent les paramètres de constructeur requis, exercent une seule nouvelle tentative, l'annulation et l'échec définitif. Un diagnostic local conserve les réponses refusées ; son échec d'écriture ne masque pas le refus initial. Les chemins de champs non prévus et le contenu des réponses ne sont pas affichés dans le message d'erreur. Les références inventées restent refusées sans réparation. Une entrée `null` dans `findings` est rejetée proprement.

Un test multiplateforme avec un véritable processus Node simule la CLI : test de connexion dédié, premier JSON d'analyse tronqué, seconde réponse JSONL complète avec LF mal échappé dans le JSON métier, validation des preuves puis publication. Il passe ici ; il est exécuté sous Windows si Node est installé. Ce test simule le modèle et ne remplace pas une analyse professionnelle réelle.

Le paquet final **0.3.8 installé**, dont l'assembly est comparé à la construction Release, passe le parcours terminal complet. Celui-ci inclut maintenant un premier échec fournisseur, puis une relance sans saisie avec réponse mal formée et seconde tentative valide, le suivi, l'incrémentalité et l'export/import. L'accès API GitHub de cette session refuse la consultation des résultats CI distants ; aucune réussite de la CI Windows n'est revendiquée ici.

## Version 0.3.9 — indexation locale et analyse au choix

L'utilisateur a choisi l'indexation locale du gros dépôt, avec analyse Copilot à la demande et analyse complète explicite des documents importants. La CLI indexe désormais sans modèle par défaut ; `--analyze` active le parcours exhaustif, et `--reanalyze` le force. Un changement de mode ne renouvelle pas les identifiants de preuve. Les rapports et l'interface distinguent les deux modes ; les anciennes mémoires restent reconnues comme issues d'une analyse.

**143 tests validés**, dont huit scénarios propres au mode local. La suite générale passe avec quatre tests ignorés ici (Copilot authentifié et trois tests Windows) ; le scénario supplémentaire de compatibilité des anciennes mémoires passe aussi. Les nouveaux tests couvrent SQL/C#/Excel/Draw.io sans modèle, recherche des filtres SQL bruts, question avec citations à partir d'un index sans assertions, analyse ultérieure d'un Excel et d'un Draw.io, absence de doublons de preuves, reprise des lots d'une analyse interrompue, export/import des statuts, préservation des décisions, changement de source et avertissement visuel sur les images.

Le **paquet 0.3.9 installé** passe le parcours terminal complet avec l'analyse explicite, puis un parcours d'indexation par défaut avec un exécutable Copilot inexistant. La recherche fonctionne sans modèle ; remettre la CLI simulée et utiliser `--analyze` enrichit le fichier déjà indexé sans nouvelle saisie du contexte. La validation authentifiée d'un corpus professionnel reste distincte de ces tests.

Mesure reproductible avec `python3 scripts/benchmark-ingestion.py --dotnet /workspace/.dotnet/dotnet`, sous Linux, compilation Debug :

| Mesure | Résultat |
|---|---:|
| Fichiers synthétiques C#/SQL | 2 212 |
| Taille totale des sources | 753 074 octets |
| Documents indexés | 45 778 |
| Arêtes de recherche | 284 417 |
| Première ingestion et construction de SQLite | 41,003 s |
| Réingestion inchangée | 8,202 s |
| Recherche, processus compris | 0,430 s |
| Recherche mesurée dans l'outil | 253 ms |
| Appels Copilot | 0 |

Ce corpus contient de petits modèles C# et des procédures/tables SQL ; ces temps ne prédisent pas ceux d'un dépôt professionnel plus volumineux ou d'un poste Windows. Le gain principal vérifiable est la suppression de tous les appels au modèle pendant l'indexation. Le coût et la couverture du mode `--analyze` restent ceux d'une analyse exhaustive ; l'indexation seule ne constitue pas une interprétation métier ou visuelle préalable de tous les documents.

## Version 0.3.10 — reconstruction d'un index dense

Le poste professionnel a signalé 2 400 secondes à la construction SQLite, avec un fichier principal de 4 Ko, un WAL de 899 Mo et une mémoire matérialisée de 683 Mo. Le petit corpus du benchmark précédent ne représentait pas cette densité. Le code reliait chaque objet technique à toutes les unités de son fichier, répétait ces listes dans chaque résultat indexé et écrivait toutes les paires dans SQLite. Après publication, l'ingestion rechargeait également toute la mémoire au lieu de réutiliser son snapshot. Ces amplifications sont corrigées ; le temps exact sur le poste Windows/OneDrive n'a pas été mesuré ici.

`search-v6.db` stocke des ensembles de preuves partagés, sans perte des associations, avec navigation dans les deux directions. Les listes de références des résultats sont bornées et signalées comme telles, mais les références complètes restent en mémoire et dans le graphe. Les commandes SQLite sont préparées, les paramètres réutilisés et les nouvelles entrées FTS ne font plus l'objet d'une suppression préalable inutile. Une ingestion utilise son snapshot publié avec contrôle de révision ; `reindex` reconstruit séparément sans fichiers sources ni Copilot. La mémoire JSON n'est pas compactée ni supprimée.

**148 tests réussis, 4 ignorés sur Linux** (Copilot authentifié et trois tests Windows). Les nouveaux tests vérifient : stockage unique de listes équivalentes dans un ordre différent, navigation vers une preuve hors des 32 références affichées, accès aux preuves malgré de nombreuses relations techniques, absence de modification de mémoire, compteurs, annulation avec rollback et reconstruction ultérieure, publication concurrente détectée, absence de relecture d'un cache lorsque le snapshot publié est fourni, et réparation d'une entrée de texte intégral manquante malgré des empreintes de documents inchangées.

Le **paquet 0.3.10 installé** passe le parcours terminal complet. Le scénario `reindex` supprime temporairement un fichier source et configure un exécutable Copilot inexistant : la reconstruction et la recherche fonctionnent, les fichiers de mémoire restent identiques et aucun appel Copilot n'a lieu. Les sorties JSON restent sans progression textuelle.

Comparaison reproductible sous Linux, Release, avec `python3 scripts/benchmark-index.py --dotnet /workspace/.dotnet/dotnet --baseline-tool artifacts/verify-039/bip-kb` :

| Mesure | Résultat |
|---|---:|
| Mémoire matérialisée synthétique | 645 004 504 octets |
| Objets techniques / preuves | 20 000 / 20 000 |
| Associations objet–preuve logiques | 4 000 000 |
| Reconstruction 0.3.9, recherche incluse | 48,958 s |
| Reconstruction optimisée, processus compris | 9,043 s |
| Documents indexés | 40 000 |
| Relations techniques stockées | 19 900 |
| Appartenances aux ensembles de preuves | 20 000 |
| Liens objet–ensemble | 20 000 |
| Taille du nouvel index | 133 582 848 octets |
| Recherche après reconstruction, processus compris | 0,288 s |

Ce benchmark reproduit une forte répétition de références avec des identifiants de 64 caractères et une mémoire de plusieurs centaines de Mo. Il ne reproduit ni le contenu métier, ni le matériel, ni le comportement de synchronisation du poste utilisateur. L'annulation est contrôlée dans les boucles et transmise à SQLite ; la lecture/désérialisation synchrone d'un JSON en cours se termine avant le contrôle suivant. Le WAL peut croître pendant une transaction ; sa taille seule ne mesure pas l'avancement.

## Version 0.3.11 — analyse des classeurs Excel

La capture utilisateur montre un seul `endpoints.xlsx` avec `--analyze`, 49 lots, et des attentes de plusieurs dizaines de secondes par appel. Le fichier professionnel n'a pas été fourni : sa taille compressée ne permet pas de connaître le texte extrait. Deux surcoûts du code ont été reproduits : les en-têtes répétés dans chaque ligne occupaient le budget de chaque demande ; un fichier isolé recevait aussi un second passage complet de rapprochement. Les lignes Excel vides auxquelles l'extracteur ajoute les en-têtes sont désormais exclues de l'analyse, sans réécrire les preuves.

**160 tests réussis, 4 ignorés sous Linux** (Copilot authentifié et trois tests Windows). Les nouveaux scénarios couvrent coordonnées et données complètes, en-têtes multilignes/longs contenant des instructions, limites de découpage, cellules fortement échappées en JSON, isolation des feuilles, lignes vides, absence de second passage sur un fichier unique, maintien du rapprochement multi-fichiers, incrémentalité, interruption/reprise du nouveau découpage et récupération des anciens lots validés. L'indexation locale récupère les lots disponibles sans nouvel appel. Les fixtures `.xls` et `.xlsx` existantes restent couvertes.

Mesure reproductible avec `python3 scripts/benchmark-excel.py --dotnet /workspace/.dotnet/dotnet --baseline-tool artifacts/verify-0310-final/bip-kb`. Le script génère un `.xlsx` synthétique, utilise un processus Copilot simulé et vérifie que les mêmes identifiants des 600 lignes renseignées et de l'en-tête sont envoyés une fois pendant l'extraction :

| Mesure | 0.3.10 | 0.3.11 |
|---|---:|---:|
| Taille du classeur | 110 858 octets | 110 858 octets |
| Lots d'extraction | 146 | 38 |
| Lots de rapprochement | 146 | 0 |
| Total d'appels | 292 | 38 |
| Lignes avec en-tête couvertes | 601 | 601 |
| Taille maximale du prompt | 11 537 caractères | 12 077 caractères |

Cette mesure porte sur le nombre d'appels et la préservation des données, pas sur la durée d'une analyse réelle ni sur sa qualité métier. Aucun parallélisme ou permission Copilot supplémentaire n'a été introduit. Le gain sur le classeur utilisateur dépend de son contenu et de ses en-têtes ; il n'est pas établi par ce benchmark.

Le paquet final **0.3.11 installé**, dont l'assembly correspond à la compilation Release, passe le parcours terminal complet SQL/XLS/C#, reprise sans nouvelle saisie, erreur de format, incrémentalité, recherche/contexte, reconstruction et export/import. Ce parcours utilise Copilot simulé sous Linux ; aucune validation authentifiée Windows n'est revendiquée pour cette version.

## Version 0.4.0 — exploration guidée et mémoire de compréhension

Le [plan réalisé](guided-analysis-plan.md) remplace, pour la CLI `--analyze`, l'analyse systématique de lots par un protocole d'exploration par fichier. Copilot choisit catalogue, recherche, plages de lecture, exclusions justifiées et synthèse. Les preuves restent contrôlées ; les plans/synthèses sont des interprétations partagées avec une couverture explicite. L'ancien parcours demeure accessible via `--analysis-mode batches`, notamment pour reprendre une analyse 0.3.x interrompue. Aucune reprise automatique des lots anciens dans un nouveau plan guidé n'est annoncée.

**183 tests automatisés réussis, 5 ignorés dans la suite Linux** (deux tests Copilot authentifiés et trois tests Windows). Les tests supplémentaires couvrent : vue d'ensemble avant lecture Excel XLS/XLSX, recherches et regroupement de sections éloignées, pagination des pages Draw.io, géométrie et identifiants des objets enveloppés, images observées/illisibles/non prises en charge avec adaptateur simulé, SQL/C#/JSON, preuves répétées, couverture fictive refusée, exclusions explicites conservées comme partielles, budget partagé entre fichiers, annulation/reprise, changement de source/contexte, réanalyse forcée interrompue puis reprise sans forcer, et import refusant des compteurs/plages incohérents. Les plans et preuves exportés ne contiennent pas les chemins locaux de la fixture.

Un vrai processus **Node simulant Copilot** passe par le lanceur et le transport JSONL : plan valide, réponse contenant une preuve inconnue refusée, puis reprise du plan sans nouvel appel d'orientation et publication d'une réponse correctement citée. Les arguments de permission sont contrôlés. Ce test est multiplateforme lorsque Node est disponible et ne remplace pas un modèle authentifié.

`scripts/test-guided.py` exerce les commandes réelles pour XLS, XLSX, Draw.io, PNG et SQL : pause au budget, absence de nouvelle révision, reprise, statut `analysis`, couverture, réingestion sans appel, `context`, export/import et absence de chemins physiques dans la mémoire partagée. `scripts/test-terminal.py` continue à exercer le parcours interactif historique en mode `batches`. Le premier parcours a aussi révélé et fait corriger le texte de configuration qui précédait l'erreur JSON lors d'une première ingestion automatisée. Les deux scripts sont inclus dans la CI Linux ; les tests xUnit sont inclus dans la matrice Windows/Linux.

La CLI réelle installée **1.0.111** a été inspectée (`--help`, `--version`, aide des variables d'environnement) ; elle annonce les prompts non interactifs, JSONL et pièces jointes. Le test authentifié guidé a été tenté séparément sur le petit `.xls` synthétique : **échec, sortie CLI 1 avant réponse exploitable**. Une sonde sans document classe l'erreur dans l'accès réseau/authentification, sans recopier les informations de connexion. L'analyse réelle de feuilles ou d'images n'est donc pas validée dans cette session. Le test est rejouable avec `BIP_KB_LIVE_TESTS=1` sur un poste disposant d'un accès Copilot fonctionnel.

Aucun facteur d'accélération n'est annoncé : le modèle choisit les lectures et peut encore avoir besoin de nombreux échanges pour une analyse exhaustive. Le budget de 50 nouveaux échanges est réglable et n'inclut pas les tentatives internes bornées de l'adaptateur. La couverture n'atteste pas la vérité métier. Draw.io reste structurel (pas de rendu) et les images sont jointes entières (pas de recadrage/OCR local). L'exploration ne dispose ni de shell ni d'accès arbitraire au poste.

Les deux parcours passent sur le **paquet 0.4.0 installé**, avec vérification que son assembly correspond à la compilation Release. Le parcours guidé démarre aussi le serveur local, charge la vue Ingestions en HTTP sur la boucle locale et vérifie la présence du plan publié et de sa couverture. Le MCP borne l'aperçu des plans (20 sources, 100 plages par source, huit questions) et expose les indicateurs de troncature/nombre total ; la CLI `analysis` conserve la vue détaillée.


## Version 0.4.1 — demandes par stdin et lectures de sections élargies

Validation Linux du 16 novembre 2026 : **195 tests réussis, cinq ignorés** (deux tests avec compte Copilot authentifié et trois spécifiques à Windows). Les tests Node de transport sont également exécutables sous Windows ; aucun résultat Windows réel n'est revendiqué ici.

Les nouvelles régressions vérifient :

- Transport UTF-8 d'une demande dépassant la limite Windows des arguments, avec accents, caractères non latins, retours à la ligne et caractères de shell préservés.
- Délai et annulation pendant une écriture vers un processus qui ne lit pas stdin ; rejet anticipé du processus sans masquage de son code de sortie.
- Ingestion d'un XLSX de 566 lignes à travers l'adaptateur et un processus Node simulant Copilot, interruption après un appel, reprise et conservation de toutes les preuves.
- Couverture compacte refusée si elle cite une unité absente, une plage avec un trou, un doublon ou une image non observée.
- Reprise d'un statut ancien sans champ `profile`, puis réanalyse forcée avec le nouveau profil.

Le benchmark reproductible utilise un XLSX **synthétique de 111 009 octets**, 566 unités, et un Copilot simulé qui demande les sections suivantes :

```bash
python3 scripts/benchmark-guided.py --tool artifacts/verify-041/bip-kb --baseline-tool artifacts/verify-040/bip-kb
```

| Parcours | Appels | Unités transmises distinctes | Demande maximale, caractères |
|---|---:|---:|---:|
| 0.4.0 | 31 | 566 | 21 751 |
| 0.4.1 | 3 | 566 | 130 911 |

Les ensembles d'identifiants des preuves sont identiques. Ce résultat mesure le transport et le nombre d'appels pour ce scénario ; il ne mesure ni le temps du modèle, ni sa compréhension, ni son comportement sur le classeur privé de l'utilisateur.

Le paquet installé passe le parcours terminal historique et le parcours guidé CLI/web sur XLS, XLSX, Draw.io, PNG et SQL. Le parcours guidé est aussi exécuté avec `scripts/test-guided.py --stdin`, y compris la transmission d'images et la lecture initiale des petits documents. L'assembly du paquet final installé est comparé à celui de la compilation Release.

L'aide de Copilot CLI 1.0.111 annonce `piped stdin` ; son implémentation accepte stdin en mode non interactif et avec pièces jointes. Une sonde réelle avec stdin ne signale pas de demande vide, mais échoue avec code 1 et une erreur de réseau/authentification. **Aucune analyse métier réelle ni accélération chronométrée avec Copilot n'est déclarée validée.** Les échanges restent des processus séparés et les demandes plus grandes peuvent avoir une latence supérieure par appel.
