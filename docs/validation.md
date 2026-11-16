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
