# Refonte : Copilot organise la connaissance avant l'indexation métier

**Statut : proposition de refonte issue de la revue du 19 novembre 2026. Ce document décrit la cible ; ces nouvelles capacités ne sont pas encore implémentées.** Revue du code au commit `ebbff27` (0.4.7). Les mesures précédentes sur des identifiants synthétiques ne valident pas la rapidité d'une recherche métier réelle.

Le besoin confirmé est double : les appels MCP prennent trop de temps et Copilot doit multiplier les recherches pour obtenir une réponse utile. La cible est une base préparée pour comprendre les développements et migrations : le rôle de Copilot à l'ingestion doit inclure l'organisation des connaissances fonctionnelles, leur vocabulaire et leurs relations.

## 1. Ce qui doit changer

La mise à disposition du document doit précéder les décisions de découpage métier. Copilot commence par comprendre le document et choisit ensuite les sections cohérentes à analyser et les connaissances à conserver. bip-kb fournit des moyens fiables de consulter le document, conserve ses preuves et indexe les résultats validés.

Le fichier original constitue une source, pas une instruction. Son accès ne doit pas dépendre d'une découpe arbitraire en lignes ou en blocs de texte. Une limite de taille de transport reste nécessaire : elle ne doit pas décider de ce qu'est un concept, une règle ou un processus métier.

## 2. Constats dans l'implémentation actuelle

| Constat vérifié | Conséquence |
|---|---|
| `Extraction.Read` transforme Excel en lignes et coupe le texte par blocs de 2 000 caractères. La première ligne est systématiquement utilisée comme en-tête. | La structure supposée précède l'analyse ; les titres, en-têtes complexes et tableaux sur une même feuille peuvent être mal représentés. |
| `DocumentExplorer` reçoit déjà ces unités, commence avec au plus 200 unités et exige de classer toutes les unités avant de terminer. | L'exploration reste centrée sur la couverture des fragments ; elle peut demander beaucoup d'échanges administratifs. |
| Chaque échange de l'adaptateur démarre un nouveau processus sans reprise de session ; un plan court et un contexte sélectionné sont retransmis. | La compréhension construite dans les échanges précédents n'est pas une mémoire de travail complète du document. |
| Les preuves citables sont celles de l'appel courant. | Une conclusion transversale exige des relectures et reconstructions de contexte. |
| `TechnicalExtraction` associe toutes les preuves du fichier aux objets et relations qu'il extrait. | Le graphe contient beaucoup de liens de provenance trop larges ; une preuve peut ramener vers de nombreux objets sans rapport précis avec elle. |
| Les identités d'entités produites par `Ingestor.Apply` incluent la source ; les liens de conclusions restent principalement des rapprochements entre assertions. | Un concept commun à plusieurs documents ne devient pas automatiquement une entité fonctionnelle cohérente. Une fusion automatique serait toutefois incorrecte sans arbitrage des ambiguïtés. |
| La recherche combine termes, alias, FTS et voisinage du graphe ; les preuves brutes et objets techniques participent aux résultats. | Des fragments utiles à la vérification peuvent occuper la place d'une réponse fonctionnelle préparée. |
| La recherche peut effectuer des lectures SQLite répétées pour les autorités et explorer plusieurs voisins par résultat. | La rapidité sur un identifiant exact ne garantit pas la rapidité sur une question large. Les coûts doivent être mesurés séparément. |

Sources de la revue : [Extraction.cs](../src/BipKb/Extraction.cs), [Exploration.cs](../src/BipKb/Exploration.cs), [Copilot.cs](../src/BipKb/Copilot.cs), [TechnicalExtraction.cs](../src/BipKb/TechnicalExtraction.cs), [Ingestion.cs](../src/BipKb/Ingestion.cs), [SearchEngine.cs](../src/BipKb/SearchEngine.cs).

Les corrections du stockage, du cache et des lectures MCP restent utiles. Elles ne corrigent pas, à elles seules, la granularité des connaissances ni la qualité des liens de provenance.

## 3. Parcours cible pour un classeur Excel

### A. Donner accès au document complet

Conserver un instantané local identifié par son empreinte, avec le contexte du projet, le rôle de la source, les décisions pertinentes et une vue d'ensemble des feuilles. Le chemin physique reste local.

Premier choix : joindre le `.xlsx` original à Copilot si le couple CLI/modèle sait réellement l'interpréter. L'aide locale de Copilot CLI 1.0.111 annonce `--attachment` pour une image ou un document natif ; elle ne démontre pas la prise en charge effective d'Excel. Ce point doit être validé par un test réel portant sur plusieurs feuilles, titres avant en-têtes, cellules fusionnées, formules, commentaires et feuilles masquées.

Si l'accès natif est insuffisant, fournir à Copilot un lecteur du classeur via des outils dédiés : inventaire des feuilles et tableaux, lecture d'une feuille ou d'une plage, recherche de valeurs, inspection des formules/commentaires et rendu d'une zone lorsque nécessaire. Une vue textuelle complète est possible pour un classeur qui tient dans le budget. Pour les grands classeurs, le document entier demeure consultable même si chaque lecture est bornée.

Cette lecture mécanique doit préserver les coordonnées et la structure. Elle ne transforme pas chaque ligne en une connaissance et ne présume pas que la ligne 1 porte les en-têtes. Les éléments non pris en charge sont signalés ; l'absence de support des formules, couleurs significatives ou commentaires ne doit pas devenir une perte silencieuse.

### B. Faire établir une compréhension globale

Copilot identifie l'objectif du classeur, les sujets couverts, les sections utiles, le sens des colonnes, les correspondances entre feuilles et les informations qui manquent. Il distingue un inventaire, des exemples, des règles, des décisions et des propositions de migration.

Le résultat est un plan de lecture par sujets et sections. Si le document mélange ancien et cible, le rôle est précisé pour les connaissances concernées ; le rôle global déclaré ne doit pas transformer toute une feuille de propositions en règles validées.

Le fractionnement nécessaire intervient alors selon les sections choisies par Copilot : par exemple « consultation des contrats », « calcul du statut », « correspondance ancien/cible ». Les tableaux de données répétitifs peuvent être parcourus et conservés sans créer une assertion par cellule.

### C. Construire la connaissance et les liens

Copilot prépare un ensemble cohérent comprenant :

- Les concepts fonctionnels, leurs définitions et leurs alias.
- Les règles, conditions, exceptions et décisions documentées.
- Les parcours fonctionnels et leurs étapes.
- Les éléments techniques associés : endpoints, services, DTO, tables, procédures.
- Les correspondances ancien/cible et leur degré de validation.
- Les relations typées entre ces éléments, chacune avec sa provenance.
- Les questions non résolues et les limites de l'analyse.

Chaque connaissance conserve son projet, son rôle, son statut, sa version source, ses preuves et sa nature : observation, déclaration ou interprétation. Une synthèse ne devient pas une preuve indépendante. Les questions que cette connaissance permet de retrouver sont proposées comme métadonnées de recherche, pas comme faits supplémentaires.

### D. Rapprocher avec la mémoire existante

Copilot peut rechercher les concepts et artefacts existants pendant l'analyse. Il propose de réutiliser une identité connue, d'ajouter une connaissance, d'apporter un complément, de signaler une contradiction ou une évolution.

Une identité fonctionnelle peut être commune à plusieurs sources d'un même projet. Les artefacts legacy et cible restent distincts, même s'ils implémentent le même concept. Les homonymes, les projets différents et les identités incertaines ne sont pas fusionnés par leur seul nom. Les décisions humaines restent prioritaires ; les nouvelles preuves peuvent ouvrir un réexamen explicite.

### E. Valider puis publier

bip-kb contrôle le contrat, les identités et les références, rattache les citations au véritable instantané et vérifie les emplacements cités. Les liens de code et SQL sont fondés sur les positions des symboles et instructions, sans rattacher par défaut tout le fichier à chaque objet.

Les propositions cohérentes sont publiées dans une révision atomique. Les hypothèses et rapprochements ambigus gardent leur statut ; un classement par le modèle ne les valide pas humainement. La validation structurelle des citations ne prouve pas la vérité métier : une évaluation distincte doit contrôler que les preuves soutiennent les conclusions.

## 4. Une mémoire de travail de document

Une session d'analyse doit conserver la compréhension du document, les lectures déjà effectuées, les candidats et les questions. Deux mécanismes sont à prévoir : reprise de session CLI lorsque la version installée le permet effectivement, et état de travail explicite conservé par bip-kb pour résister à une interruption ou à une perte de session.

La session CLI n'est pas la mémoire durable du produit. Un journal de lectures relie chaque accès au document et à son empreinte. Une conclusion peut citer des éléments réellement consultés plus tôt dans cette analyse ; elle n'est plus obligée de faire tenir toutes ses preuves dans le dernier appel. Le validateur vérifie les références contre ce journal et le contenu source, pas contre le simple souvenir déclaré par le modèle.

Copilot dispose d'outils limités à l'instantané concerné et à la lecture de la base existante. Aucun accès général au shell ou aux écritures applicatives n'est nécessaire. Les propositions restent dans la zone de travail jusqu'à validation. Les budgets portent sur les appels, le temps et le volume consulté ; un dépassement conserve une analyse reprenable.

## 5. Couverture utile et honnête

Remplacer le compteur obligatoire « chaque fragment doit être classé » par une couverture au niveau des feuilles, tableaux, sections et sujets. Distinguer : disponible, consulté, interprété, conservé comme donnée, hors périmètre et non résolu.

Une synthèse globale ne signifie pas que toutes les lignes ont été lues. Une zone résumée par échantillonnage reste signalée comme telle. Les données brutes restent consultables, y compris lorsqu'elles n'ont pas donné lieu à une fiche de connaissance. L'état « complet » doit se rapporter à un périmètre explicite ; une analyse partielle doit rester visible comme partielle.

La progression doit décrire le travail utile : « structure du classeur comprise », « correspondances contrats en cours », « liens avec SQL vérifiés », « questions non résolues ». Les quantités parcourues et le temps restent disponibles pour diagnostiquer une boucle.

## 6. Indexer pour les prochaines questions

Conserver trois niveaux distincts :

| Niveau | Contenu | Usage principal |
|---|---|---|
| Sources et preuves | Instantanés locaux, extraits, coordonnées, versions, positions de code | Vérifier une affirmation ou explorer un détail |
| Connaissances | Fiches fonctionnelles, règles, processus, décisions, correspondances, synthèses | Répondre directement aux demandes de développement |
| Graphe | Concepts et artefacts identifiés, relations typées avec preuves et statut | Suivre les dépendances et le passage ancien → cible |

La recherche métier privilégie les fiches, alias et correspondances puis ajoute un voisinage ciblé du graphe. Une recherche technique exacte garde l'accès direct à un symbole, une table ou une preuve. Les extraits bruts restent un recours explicite quand la connaissance organisée ne suffit pas ; ils ne doivent pas monopoliser les résultats par simple répétition de mots.

Copilot enrichit à l'ingestion le vocabulaire fonctionnel, les synonymes et les questions de recherche possibles. Ces métadonnées restent distinctes des preuves et évaluées pour éviter d'introduire de faux rapprochements. Cela ne rend pas une recherche lexicale universellement sémantique : le rappel sur des reformulations réellement nouvelles doit être mesuré. Des embeddings pourront être évalués ensuite si nécessaire, sans faire dépendre la première livraison d'un autre service de modèle.

Le MCP doit pouvoir renvoyer un dossier utile en un appel : fiches pertinentes, éléments cible, correspondances legacy, règles, implémentations, liens prioritaires, preuves et questions ouvertes. Des appels complémentaires servent à vérifier les détails. Le serveur ne lance pas un nouveau Copilot à chaque requête : le Copilot de l'IDE raisonne à partir des connaissances déjà préparées.

La lecture reste SQLite. Les parcours et filtres utilisent des index dédiés ; les contrôles d'autorité sont groupés et les grands ensembles de preuves ne sont pas parcourus systématiquement. Les réponses ont un budget explicite et des continuations quand nécessaire.

## 7. Exemple de résultat attendu

Exemple illustratif, sans affirmer que ces éléments existent dans le classeur de l'utilisateur :

```text
Connaissance : consulter un contrat
  Définition et vocabulaire métier
  Entrées, résultat attendu, règles de visibilité et exceptions
  Statut : documenté / proposé / à confirmer

Concept « Contrat »
  ← concerné par le parcours « Consultation »
  ← exposé par un endpoint legacy identifié
  → correspond à un DTO cible identifié
  → lu depuis une table ou procédure identifiée

Chaque relation : preuve précise, version, rôle, statut
Points manquants : droits non documentés, correspondance non confirmée…
```

À la question « quels DTO, query et handler pour consulter un contrat ? », le dossier retrouve ces liens déjà préparés. Il distingue les usages de code observés des suggestions et ne déduit aucun détail interne d'un package NuGet à partir de son nom.

## 8. Autres formats

- **Images** : donner l'image entière au modèle visuel ; il identifie les groupes et relations avant de demander des zones détaillées. Une région citée doit correspondre à l'image réelle ; l'OCR seul ne suffit pas à prouver une relation visuelle.
- **Draw.io** : proposer un rendu par page et la structure native, avec les identifiants d'objets et de connexions pour citer précisément. Croiser la lecture visuelle et structurelle ; ne pas prétendre avoir vu un rendu lorsqu'il manque.
- **SQL et C#** : conserver les parseurs pour les symboles, positions et dépendances observables. Copilot choisit les ensembles à comprendre et ajoute la lecture métier. Les références de symboles non résolues restent signalées.
- **Dossiers volumineux** : inventaire local rapide, carte des composants puis priorisation par Copilot selon l'objectif. Ne pas remplacer la boucle actuelle par un appel de modèle pour chacun des milliers de fichiers.

## 9. Livraison et migration

| Étape | Livrable vérifiable |
|---|---|
| 1. Mesurer et valider les capacités | Temps par outil MCP et par étape SQL, nombre d'appels pour une question métier ; vrai test XLSX et reprise de session sur le couple CLI/modèle utilisé. |
| 2. Parcours Excel complet | Instantané consultable, compréhension globale, plan choisi par Copilot, fiches et relations sourcées, reprise après interruption. |
| 3. Recherche métier | Index des fiches et alias, relations précises, dossier MCP direct ; comparaison avec les questions de référence. |
| 4. Rapprochement entre sources | Réutilisation des concepts, conservation des artefacts distincts, conflits et décisions persistants ; actualisation incrémentale. |
| 5. Formats et migration | Images/Draw.io puis dossiers de code ; projection des anciennes données, coexistence et retrait progressif du mode guidé actuel. |

La migration conserve les preuves et décisions existantes. Les anciens résultats ne deviennent pas rétroactivement de nouvelles fiches validées. Une nouvelle analyse est nécessaire pour obtenir une compréhension qui n'a jamais été enregistrée ; elle doit pouvoir cibler les sources utiles et réutiliser leurs preuves, sans forcer la réanalyse du dépôt entier. Les anciens résultats restent consultables tant que leur remplacement n'a pas été publié.

La cible ajoute un modèle de fiches et de relations plus expressif ; elle ne doit pas être présentée comme un simple changement de prompt. Le mode d'indexation locale rapide reste disponible pour les sources où une analyse de modèle n'apporte pas de valeur immédiate.

## 10. Critères de réussite

Évaluer au moins dix questions métier représentatives avec leurs preuves attendues : endpoint à construire, mapping legacy/cible, règle de statut, procédure appelée, exception, droits manquants, décision remplacée, contradiction, usages NuGet observés et question sans réponse. Garder des formulations de test qui n'ont pas été fournies au modèle pendant l'indexation.

Mesurer séparément :

- **Latence d'un outil** : ouverture/vérification de révision, FTS, filtrage, graphe, lecture des preuves et sérialisation. Objectif initial : p95 inférieur à une seconde sur une machine de référence et un corpus documenté ; objectif à valider, pas performance annoncée.
- **Efficacité de la recherche** : fiches et preuves attendues retrouvées, réponses hors sujet, quantité de contexte transmise et nombre d'appels MCP. Viser un premier dossier utile en un appel et un parcours courant en un à trois appels de base, sans interdire les vérifications nécessaires.
- **Qualité de connaissance** : citations réellement probantes, conditions et exceptions conservées, distinction ancien/cible/hypothèse, absence de fusion d'homonymes et de contradiction masquée.
- **Coût d'ingestion** : durée, appels CLI, volume transmis et travail réutilisé lors d'une modification. Ne pas confondre un appel CLI avec une unique requête de modèle.
- **Fiabilité** : reprise, invalidation des seules connaissances dépendantes, aucune révision incomplète activée, décisions humaines conservées.

Un temps correct sur une recherche d'identifiant synthétique ou un modèle simulé ne suffit pas à accepter la refonte. Le premier jalon métier doit passer avec un vrai Copilot sur un classeur représentatif et des questions de développement avant de généraliser le nouveau parcours.
