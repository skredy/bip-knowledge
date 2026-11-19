# Démarrer le parcours métier — bip-kb 0.5.0

La 0.5.0 implémente un premier parcours de business analyste : cadrage conversationnel, un projet par workspace, qualification des sources, analyse des documents avec mémoire de lecture et connaissances sourcées consultables via MCP. Commencez dans un **nouveau dossier vide**. Les espaces précédents restent accessibles avec leur ancien parcours ; ils ne sont pas convertis automatiquement.

## Créer et cadrer le projet

```powershell
bip-kb init --workspace C:/Knowledge/ibip-metier
cd C:/Knowledge/ibip-metier
```

Dans un terminal interactif, `init` commence l'entretien avec Copilot. Décrivez votre objectif dans vos mots, par exemple : « Nous refondons notre application de gestion de contrats. Je veux comprendre les règles actuelles, ce qui change dans la cible et les décisions encore ouvertes. Commençons par la consultation des contrats. »

Copilot reformule et pose une question utile à la fois. Les technologies, listes de domaines et rôles de sources ne sont pas des champs obligatoires à remplir. Les applications, bases, versions legacy/cible et sujets métier appartiennent au même projet.

- Répondez librement, corrigez la reformulation ou indiquez « je ne sais pas ».
- `/pause` conserve l'entretien. Relancez `bip-kb project setup` pour reprendre.
- `/confirm` publie le cadrage proposé lorsque l'objectif et le périmètre sont suffisamment établis. Vous confirmez la fidélité de ce cadrage, pas toutes les règles des futurs documents.
- Une panne Copilot conserve la dernière réponse saisie. Relancez `project setup` sans la retaper.
- Pour réviser le cadrage confirmé, relancez `project setup`. Si la base a changé pendant un brouillon, `project setup --restart` archive ce brouillon local et repart du dernier cadrage confirmé.

Le nom du projet reste stable dans cette version pour préserver les rattachements des sources. Les objectifs, périmètres et éléments du cadrage peuvent être révisés.

Pour automatiser ou utiliser une entrée redirigée :

```powershell
bip-kb init --workspace C:/Knowledge/ibip-metier --non-interactive
bip-kb project setup --workspace C:/Knowledge/ibip-metier --message "Notre objectif et notre premier périmètre…" --json
# Examiner la reformulation ; répondre de nouveau avec --message si nécessaire.
bip-kb project setup --workspace C:/Knowledge/ibip-metier --confirm --actor "Prénom"
```

`--message` et `--confirm` sont deux commandes distinctes : la réponse du modèle ne peut pas simuler votre confirmation. Dans le dossier du workspace, `--workspace` devient inutile. Depuis un autre dossier, indiquez-le, notamment dans la configuration MCP.

## Ajouter des sources

```powershell
bip-kb --ingest C:/Documents/endpoints.xlsx
bip-kb --ingest C:/Documents/processus.drawio
bip-kb --ingest C:/Documents/architecture.png
bip-kb --ingest C:/Repos/legacy/src
bip-kb --ingest C:/Documents/modele.sql --analyze
```

Dans un nouveau workspace :

| Entrée | Comportement par défaut |
|---|---|
| Fichier Excel, Draw.io ou image | Qualification puis analyse métier avec Copilot |
| Dossier, code, SQL, JSON, texte | Indexation locale ; ajouter `--analyze` pour demander l'analyse métier |
| Toute entrée avec `--index-only` | Indexation locale, aucun appel Copilot |

Le choix porte sur l'entrée : un dossier contenant des Excel reste indexé localement par défaut. Analysez ensuite les documents importants explicitement. `--analyze` sur un gros dossier analyse ses fichiers successivement et peut rester coûteux ; la priorisation automatique par composants n'est pas encore implémentée.

Lors du premier ajout, une description libre est proposée : « À quoi sert cette source pour votre projet ? ». Depuis la 0.5.1, une ligne vide laisse la question ouverte. Tapez `/skip` pour passer ou `/pause` pour arrêter. Avec `--describe`, saisissez la nouvelle description ou tapez `/keep` pour conserver celle affichée. Copilot reçoit le cadrage, l'inventaire et un aperçu limité pour proposer le rôle ; cela ne constitue pas une analyse complète. Une ambiguïté importante peut entraîner une question supplémentaire. Vous pouvez laisser cette question ouverte.

Le projet est repris automatiquement, les identifiants de source sont proposés et les sujets métier n'exigent pas de saisie manuelle. Un document mixte peut garder un rôle global inconnu ; ses assertions distinguent ensuite `legacy`, `target`, `current` ou `unknown`. Aucune qualification du modèle ne déclare une source approuvée.

Pour fournir la description sans dialogue :

```powershell
bip-kb --ingest C:/Documents/endpoints.xlsx --message "Étude mêlant endpoints actuels et propositions cible" --non-interactive
```

`--describe` permet de refaire la qualification. Le cadrage et les descriptions déjà enregistrés sont réutilisés aux prochains ajouts. Dans ce nouveau parcours, `--context-file` est remplacé par le cadrage et une description libre.

## Comment les documents sont analysés

Copilot reçoit une vue globale et les sections disponibles. Un petit document lui est fourni intégralement dès le premier échange ; pour un document plus grand, il choisit les plages à lire. Le découpage métier est proposé par le modèle, pas imposé par une classification de toutes les lignes.

Pour Excel, le lecteur conserve les cellules et coordonnées sans supposer d'en-têtes à la première ligne ni répéter ces en-têtes sur chaque ligne. En `.xlsx`, il ajoute les formules XML, fusions, états de feuilles et commentaires classiques disponibles. Les formules ne sont pas exécutées. Styles/couleurs, graphiques, macros et commentaires modernes ne sont pas interprétés ; en `.xls`, les formules et commentaires ne sont pas accessibles par ce lecteur. Les limites sont communiquées au modèle et figurent dans le rapport.

**L'envoi natif de `.xlsx` à Copilot n'a pas été validé avec un modèle authentifié dans le cloud.** Le mode par défaut utilise donc la vue lecteur. Pour tester explicitement les pièces jointes originales, le paramètre `nativeDocuments: true` dans `bip.json` ajoute le fichier original à la requête, en plus de cette vue. Une aide CLI annonçant `--attachment` ne garantit pas la compréhension du format. Les images sont toujours jointes ; une image non observée ne produit pas de conclusion visuelle validée. Draw.io fournit sa structure native et ses connexions ; aucun rendu visuel n'est annoncé.

Le journal de lecture autorise les références consultées plus tôt dans la même analyse. Les réponses validées et une synthèse de travail sont conservées sur disque : cette mémoire explicite fonctionne entre processus, sans dépendre d'une session CLI persistante. Si le contenu exact d'un passage n'est plus disponible au modèle, il doit le relire.

La sortie contient des fiches métier, synonymes de recherche, liens typés et questions ouvertes. Les conclusions restent proposées, avec preuves et origine ; l'analyse ne remplace pas un arbitrage humain. La couverture est décrite par sections : interprétée, conservée après lecture, exclue ou non résolue. Une synthèse peut être publiée comme **partielle** ; cela ne signifie pas que le document entier a été compris.

## Reprendre, réanalyser et consulter

```powershell
# Reprendre une interruption, avec les réponses déjà validées :
bip-kb --ingest C:/Documents/endpoints.xlsx --analyze

# Recommencer réellement l'analyse de ce fichier :
bip-kb --ingest C:/Documents/endpoints.xlsx --reanalyze

bip-kb project show
bip-kb project summary
bip-kb search "qui peut consulter un contrat"
bip-kb context "Que change la suspension d'un contrat ?"
bip-kb ask "Quelles règles restent à arbitrer ?"
bip-kb analysis <identifiant-de-source>
bip-kb serve
```

`--max-calls 6` borne les nouveaux échanges d'analyse d'une commande. La qualification et les éventuelles corrections de format ont leurs propres appels ; un appel CLI peut comporter plusieurs requêtes au modèle. Une interruption avant publication conserve l'ancienne révision active. Après une erreur de construction de l'index, `reindex` reprend ce travail sans refaire l'analyse.

La recherche donne priorité aux fiches métier dans un projet cadré, exploite leurs synonymes et limite l'expansion du graphe. Le dossier MCP associe cadrage, connaissances, preuves et limites des sources. Il ne charge pas tout `materialized.json` et ne démarre pas un autre Copilot pour chaque requête. Les outils `project` et `questions` peuvent recevoir `{}` dans un workspace à projet unique. Voir [la configuration MCP](mcp.md), y compris JetBrains.

Les questions/réponses ordinaires ne deviennent pas silencieusement des faits. `remember` et les arbitrages restent des actions explicites. Les changements du cadrage invalident actuellement le cache d'analyse des sources qui seront réingérées ; le calcul plus fin des dépendances entre chaque élément du cadrage et chaque conclusion reste à développer.

## Validation et limites de cette livraison

Les tests couvrent la reprise de l'entretien, sa provenance, les confirmations, les conflits concurrents, la lecture Excel, les citations entre échanges, le refus de preuves inventées, les décisions conservées, les relations précises, le classement métier face à des lignes brutes et les appels MCP sans cache matérialisé. Un processus simulé teste aussi le protocole CLI et ses permissions. `scripts/test-business.py` vérifie le parcours installé sous Windows/Linux ; le CI exécute ce parcours sur les deux systèmes.

Ces tests ne mesurent pas la qualité d'un vrai modèle. Le `doctor --live` tenté dans le cloud le 19 novembre 2026 a échoué avec un code de sortie 1. La lecture native Excel, la pertinence des fiches sur votre corpus et les temps observés sur votre poste ne sont donc pas déclarés validés. Un test authentifié dédié est disponible avec `BIP_KB_LIVE_TESTS=1` ; il utilise une fixture synthétique et doit être complété par des questions métier sur vos documents.
