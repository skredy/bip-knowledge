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
