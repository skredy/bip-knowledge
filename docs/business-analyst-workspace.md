# Cible produit : un business analyste avec une mémoire vérifiable

**Statut : conception, non implémentée.** Orientation confirmée le 19 novembre 2026 : un projet par workspace, cadrage conversationnel par Copilot, priorité à la compréhension métier. Le workspace de validation sera neuf ; les données et paramétrages des essais précédents ne sont pas à migrer. Ce choix ne supprime aucun workspace existant.

Ce document complète et oriente la [refonte de l'analyse et de la recherche](knowledge-redesign.md). Il remplace l'idée d'un outil de code enrichi d'un profil de projet par celle d'un analyste métier qui conserve ses sources, ses raisonnements explicités et ses questions ouvertes. « Raisonnement explicité » désigne une justification vérifiable par l'utilisateur, pas une trace interne du modèle.

## 1. Le rôle du produit

bip-kb aide l'équipe à comprendre son activité, décrire l'existant, préciser la transformation souhaitée et évaluer les conséquences d'une décision. Il entretient une compréhension commune à partir des échanges humains, documents, diagrammes et implémentations observées.

Ses résultats principaux sont une fiche de cadrage, un glossaire, une carte des acteurs et processus, des règles avec leurs conditions et exceptions, des correspondances existant/cible, un journal de décisions et des questions prioritaires. Les références techniques permettent de vérifier où ces comportements sont implémentés. Un dossier pour développer un endpoint est un usage de cette mémoire, pas l'objet central du modèle de connaissance.

Copilot conduit l'analyse et propose l'organisation de la connaissance. bip-kb conserve l'état durable, donne accès aux sources, applique les limites des outils, vérifie les références et construit l'index. Le modèle ne décide pas seul qu'une règle est approuvée ou qu'une ambiguïté métier est résolue.

## 2. Un projet, plusieurs points de vue

Un workspace contient exactement un projet avec un identifiant stable. Son nom peut changer sans casser les liens. Ajouter une source, poser une question et appeler le MCP ne demandent pas de sélectionner ce projet.

Un projet peut contenir plusieurs applications, bases, équipes, domaines métier, versions et objectifs intermédiaires. « Legacy », « cible souhaitée » et « implémentation actuelle » sont des points de vue sur le même projet, pas des projets séparés. Ils peuvent coexister dans un même document et être précisés au niveau d'une règle ou relation.

Le domaine n'est plus un champ obligatoire à inventer avant l'ingestion. Un document peut traiter plusieurs sujets ; le classement peut rester inconnu et évoluer après lecture. Le rôle temporel, le statut d'approbation et l'autorité d'une source sont trois informations distinctes. Un schéma cible récent n'est pas automatiquement approuvé ; une observation du code n'est pas automatiquement une exigence métier.

Le parcours vise une création simple avec `bip-kb init --workspace <chemin>` puis le cadrage interactif dans le même flux. Une commande de reprise du cadrage permet ensuite de le compléter. Ces comportements sont une cible ; le guide d'utilisation courant reste celui de la version publiée.

## 3. Un entretien, pas un questionnaire de configuration

L'ouverture est une question simple : « Que cherchez-vous à comprendre ou à faire évoluer avec ce projet ? » L'utilisateur peut répondre librement et donner plusieurs informations à la fois. Copilot les organise, conserve les termes métier employés et propose des définitions compréhensibles. Il ne demande pas à l'utilisateur de connaître le schéma interne de la base.

L'entretien explore progressivement, selon les réponses :

| Sujet | Ce que l'analyste cherche à comprendre |
|---|---|
| Finalité | Pourquoi le projet existe, ce qui doit s'améliorer et comment juger sa réussite |
| Activité | Qui fait quoi, pour qui, avec quels objets métier et quelles exceptions |
| Transformation | Ce qui existe, ce qui est souhaité, ce qui doit être conservé et ce qui reste à décider |
| Périmètre | Ce qui est inclus, explicitement exclu ou encore indéterminé |
| Informations disponibles | Documents, diagrammes, systèmes, exemples, interlocuteurs ; ce qu'ils peuvent établir et leurs limites |
| Décisions et contraintes | Ce qui a été décidé, par qui si connu, dans quel périmètre et à quelle date |
| Questions utiles | Les premières questions auxquelles la base devrait permettre de répondre |

Ce tableau n'est pas une liste à remplir intégralement. Une question principale par échange est la règle par défaut. Copilot pose la suivante en fonction de son effet sur l'analyse : une ambiguïté sur le sens de « cible validée » compte davantage qu'une liste exhaustive de technologies.

Une question indique brièvement pourquoi la réponse sera utile, propose des choix quand cela réduit l'effort et autorise « je ne sais pas », « plus tard » et une réponse libre. Il ne redemande pas une information déjà fournie et ne force pas la même séquence pour une refonte et pour un projet sans legacy. Une réponse peut corriger plusieurs éléments du cadrage.

L'entretien peut finir dès que l'objectif et un périmètre de travail initial sont compris et confirmés. Les autres inconnues restent visibles. Il ne faut pas attendre de connaître tous les processus ni de disposer de toutes les sources pour commencer. L'utilisateur peut suspendre et reprendre ; ses réponses sont enregistrées avant le prochain appel de modèle. L'indisponibilité de Copilot ne doit pas lui faire retaper son texte : le cadrage reste en brouillon et l'inventaire local reste possible.

### Exemple illustratif

> Utilisateur : On refond un legacy, je veux comprendre les règles à conserver.
>
> Copilot : Le premier objectif est donc de distinguer le comportement actuel de celui que vous souhaitez conserver. Quels parcours métier faut-il traiter en premier ?
>
> Utilisateur : La consultation des contrats. J'ai un Excel des endpoints, le code ancien et le nouveau modèle de données.
>
> Copilot : Je retiens « consultation des contrats » comme premier périmètre. L'Excel décrit-il les comportements actuels, des propositions pour la cible, ou les deux ?
>
> Utilisateur : Les deux ; le modèle de données est encore discuté.
>
> Copilot : Je garderai les propositions distinctes des règles approuvées. Le code pourra montrer le fonctionnement observé ; il ne suffira pas à décider ce qui doit être conservé. Voici le cadrage que je propose…

Cet exemple n'impose aucune règle au vrai projet. Copilot ne déduit pas d'une « refonte » que tout le comportement ancien doit être reproduit.

## 4. Une mémoire de projet explicite

La fiche de cadrage est une vue lisible d'enregistrements structurés, pas un long prompt opaque qui ferait autorité. Elle contient l'objectif, le périmètre initial, les acteurs et objets connus, les points de vue existant/cible, les sources attendues, les décisions déclarées et les inconnues prioritaires.

Chaque élément conserve une identité, sa formulation, son origine, ses références, son périmètre et son statut. Les distinctions indispensables sont :

- **Déclaration humaine** : l'utilisateur a indiqué ceci, dans tel échange. Une déclaration portant sur une décision reste attribuée ; elle ne simule pas une approbation par toute l'équipe.
- **Observation sourcée** : une version précise d'un document ou du code montre ceci, avec un emplacement vérifiable.
- **Interprétation proposée** : Copilot rapproche ou déduit ceci à partir de références identifiées ; l'incertitude est explicitée.
- **Décision confirmée** : un arbitrage humain a retenu ceci pour un périmètre donné, avec sa justification et les éléments qu'il remplace.
- **Question ouverte** : information manquante ou divergence, avec son effet sur le travail et les sources susceptibles d'aider.

La provenance et l'approbation sont indépendantes : confirmer que la reformulation d'un propos est fidèle ne valide pas toutes les règles du projet. Une absence de réponse ne vaut jamais confirmation. Le modèle ne produit pas de pourcentage de confiance qui serait présenté comme une mesure de vérité.

La confirmation initiale porte sur un récapitulatif lisible et corrigeable. Les éléments proposés restent tels quels s'ils n'ont pas été confirmés. Les formulations d'origine restent accessibles pour vérifier qu'une synthèse n'a pas perdu une exception ou changé le sens d'un terme.

La mémoire organise le vocabulaire sans forcer une ontologie figée : mêmes identités de concepts entre sources quand le rapprochement est établi, alias conservés, homonymes distincts. Les relations métier initiales restent peu nombreuses et définies : acteur réalisant une activité, activité utilisant ou produisant un objet, règle contraignant une activité, décision modifiant une règle, cible remplaçant un comportement. Les extensions sont motivées par un besoin réel.

## 5. Des échanges cadrés avec Copilot

L'utilisateur voit une conversation et des fiches. L'adaptateur échange des messages validés et versionnés ; du texte libre du modèle ne déclenche aucune modification directe de la base.

| Élément du contrat | Responsabilité |
|---|---|
| `schemaVersion`, `sessionId`, `baseRevision`, `turnId` | Version du format, reprise, détection des réponses dupliquées et changements concurrents |
| `phase` | Cadrage, qualification d'une source, analyse ou proposition de révision du cadrage |
| `understanding` | Reformulation courte avec références aux éléments utilisés |
| `questions` | Identifiant stable, question, raison, suggestions facultatives, caractère bloquant ou différable |
| `proposals` | Ajouts ou modifications typés, références d'origine, statut proposé et identifiants concernés |
| `openIssues` | Incertitudes, divergences et effet sur la suite |
| `nextAction` | Question, consultation d'une source autorisée, présentation d'un récapitulatif ou pause |

Ce contrat est une spécification à formaliser en JSON Schema lors de l'implémentation. Il ne contient ni commande shell, ni chemin à écrire choisi par le modèle, ni permission d'approuver ses propres propositions. Les identifiants durables sont attribués par bip-kb ; les alias temporaires et références d'échanges sont résolus par le validateur.

Les transitions du brouillon sont : réponse humaine enregistrée → proposition du modèle vérifiée → récapitulatif ou question → confirmation lorsqu'une décision est nécessaire → révision active. Les observations documentaires valides peuvent enrichir automatiquement la base avec leur statut d'observation, sans confirmation par ligne. Une modification d'objectif, d'autorité, de décision humaine ou une fusion ambiguë reste proposée.

Une réponse de modèle invalide reçoit une correction bornée avec un diagnostic précis. L'échec conserve l'état antérieur et les réponses humaines ; aucune boucle indéfinie ni réinitialisation de l'entretien. Un changement concurrent de révision entraîne une réconciliation explicite avant application. Les confirmations sont des événements utilisateurs, jamais un champ « confirmé » accepté depuis le modèle.

## 6. Ajouter une source dans un projet déjà compris

Le cadrage peut noter « étude des endpoints attendue » sans demander immédiatement un chemin ni prétendre que le fichier a été lu. Lorsqu'un chemin est fourni, bip-kb inspecte le format et sa structure ; Copilot consulte le contenu selon les capacités validées, avec la mémoire du projet et les décisions pertinentes.

Il propose une fiche de source : à quoi elle sert, les sujets concernés, son point de vue, les questions qu'elle peut éclairer et les limites constatées. Un rapprochement avec une source attendue est justifié par son contenu et les indications de l'utilisateur, pas uniquement par son nom ou son extension. Il peut s'agir d'une source nouvelle ou hors périmètre ; le rattachement n'est pas forcé.

Les noms, identifiants techniques et classifications sont proposés automatiquement. L'utilisateur n'a plus à répéter le nom du projet, inventer un domaine unique ou réexpliquer le legacy à chaque import. Une ambiguïté importante donne lieu à une question ciblée, par exemple « ces mappings ont-ils été approuvés ou restent-ils des propositions ? ». Sans réponse, ils restent proposés et l'analyse non dépendante peut continuer.

L'ajout produit un bilan métier : ce que cette source apprend, confirme, nuance, contredit, remplace ou laisse inconnu. Les nouvelles questions sont regroupées et priorisées par leur effet sur le périmètre courant ; elles ne deviennent pas une file obligatoire à traiter intégralement. Les nouvelles connaissances peuvent remettre en cause le cadrage : Copilot propose alors une révision et explique les conséquences, sans écraser une décision humaine.

## 7. Un analyste utile dans la durée

À une question telle que « que se passe-t-il lorsqu'un contrat est suspendu ? », la réponse attendue décrit les acteurs, étapes, conditions et exceptions connues, distingue le fonctionnement observé du comportement souhaité, cite les sources et nomme les inconnues. S'il manque la règle de réactivation, l'analyste doit l'indiquer plutôt que compléter avec une pratique plausible.

À « que changerait cette décision ? », il suit les dépendances connues et indique où la couverture est insuffisante. Une analyse d'impact ne prétend pas être exhaustive parce qu'un graphe a été parcouru. Les critères d'acceptation proposés restent distincts des exigences documentées. Les contradictions sont présentées avec leurs périmètres, dates et statuts avant de conclure qu'un arbitrage est nécessaire.

La mémoire active du projet comprend un résumé court révisé, les décisions pertinentes et les questions prioritaires. Les détails sont recherchés à la demande dans les fiches et les sources. Chaque ingestion reçoit la révision de cadrage utilisée ; une modification de ce cadrage marque les analyses dépendantes à réexaminer sans imposer une réanalyse de tous les fichiers.

Le MCP reste une interface de consultation rapide de cette mémoire. Il fournit le cadrage et un dossier métier sourcé, sans démarrer un second Copilot derrière chaque recherche. Les échanges ordinaires de questions/réponses ne sont pas enregistrés silencieusement comme connaissances. Une correction destinée à la mémoire passe par un parcours explicite de proposition et de décision ; une réponse de l'assistant ne devient pas sa propre preuve.

## 8. Architecture minimale et départ neuf

Conserver un seul moteur de mémoire, une base SQLite locale avec index textuel et relations typées, un adaptateur Copilot et les vues CLI/MCP. Pas de base de graphe séparée, de plusieurs agents spécialisés ni de plateforme de workflow dans le premier parcours. Les sources et décisions durables restent partageables par Git ; les chemins et brouillons de conversation restent locaux. Les déclarations confirmées utiles au projet sont publiées avec leur provenance, sans imposer le partage du dialogue complet. Les extraits de déclaration nécessaires à cette provenance accompagnent l'enregistrement publié : une autre installation ne doit pas recevoir une référence inexploitable vers un échange resté local.

Les caches et index sont reconstructibles ; les lectures MCP interrogent des projections bornées, jamais un chargement de toute la mémoire. Les enregistrements métier réutilisent les preuves précises au lieu de recopier des ensembles d'extraits. Le résumé de projet est une vue, pas une deuxième vérité à maintenir manuellement.

Le nouveau format peut rompre avec les anciens espaces de test. Aucune migration de leurs profils, caches ou ingestions n'est nécessaire pour cette livraison. L'application doit reconnaître un format ancien et orienter vers la création d'un nouvel espace, sans le modifier silencieusement. Les tests logiciels utiles restent conservés ou adaptés ; repartir de données neuves ne signifie pas abandonner les garanties de fiabilité.

## 9. Premier parcours à livrer et à évaluer

Construire d'abord un parcours vertical court : workspace neuf → entretien de cadrage → un classeur représentatif → carte métier et questions ouvertes → dossier MCP utile. Valider la vraie lecture du document et l'adaptateur Copilot dès ce parcours ; une conversation simulée ne suffit pas à juger la compréhension. Ajouter ensuite SQL/code pour vérifier les comportements et les correspondances, puis les autres formats et les cas de volumétrie.

Les vérifications doivent couvrir :

- Un novice décrit son objectif sans connaître les mots « domaine », « autorité » ou « source logique » ; Copilot produit un cadrage fidèle et ne transforme pas les exemples en faits.
- Une réponse riche remplit plusieurs informations, une inconnue ne bloque pas tout, une interruption ne fait rien retaper et une correction ne laisse pas deux objectifs actifs contradictoires.
- Deux applications, plusieurs versions et un document mixte restent dans un seul projet ; les affirmations legacy/cible et leurs statuts ne sont pas fusionnés.
- Une nouvelle source est qualifiée avec peu de questions utiles ; une indication humaine peut corriger une interprétation du modèle et reste conservée.
- Une règle contredite, un terme ambigu et une décision remplacée sont traités sans inventer d'arbitrage ; les changements pertinents entraînent un réexamen ciblé.
- Des instructions dans un document restent du contenu ; aucun outil hors périmètre n'est exécuté et aucun statut humain n'est accepté depuis la sortie du modèle.
- Les réponses expliquent correctement les processus, conditions et exceptions sur des questions de test qui n'ont pas servi à construire l'index. Mesurer aussi la latence MCP, le nombre d'appels, les corrections demandées à l'utilisateur et le coût des analyses.

Critère central : l'utilisateur reconnaît son métier dans la base et obtient une compréhension utile, justifiable et corrigible sans refaire lui-même l'analyse des documents à chaque question. La sophistication du graphe, le nombre de faits et la longueur des synthèses ne sont pas des critères de réussite.
