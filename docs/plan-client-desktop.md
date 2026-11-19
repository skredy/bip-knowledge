# bip-kb Desktop pour VDI Windows

Le client WPF regroupe le cadrage, la collecte de sources, les recherches, les dossiers métier, les arbitrages et les diagnostics dans une seule application. Chaque workspace métier correspond à un projet. La fenêtre travaille sur le même format que la CLI 0.5.3. Les commandes de la CLI restent disponibles dans le paquet pour les IDE et les scripts.

## Installer sur la VDI

1. Choisir une session Windows x64 compatible avec .NET 10. Télécharger la distribution **bip-kb-desktop-win-x64** depuis la validation Windows de la branche utilisée, ou prendre l’archive fournie avec la livraison.
2. Extraire **tout le dossier** dans un emplacement que la politique de la VDI autorise à exécuter, puis ouvrir **BipKb.Desktop.exe**. La distribution inclut WPF, .NET, SQLite et le moteur CLI. Le lancement s’effectue avec les droits de l’utilisateur, sans SDK ni installation d’Office. La distribution produite par ce dépôt est non signée. Pour un parc géré par AppLocker ou WDAC, faire distribuer ou signer cette version par le canal logiciel de l’entreprise.
3. Pour les fonctions d’analyse, utiliser le Copilot CLI approuvé et authentifié dans la même session Windows. « Vérifier Copilot sur ce poste » vérifie le lanceur avant même la création d’une base. **Activité et outils → Tester Copilot en ligne** vérifie aussi la réponse du service avec le proxy et l’identité effectifs de l’utilisateur. Le panneau technique indique l’exécutable résolu et la prise en charge des pièces jointes.
4. Choisir **Créer** et un dossier dédié vide, ou **Ouvrir** et le dossier d’une base existante. Le chemin complet reste affiché. Un espace récemment utilisé est proposé au prochain démarrage si le profil utilisateur est conservé. Un raccourci peut aussi lancer `BipKb.Desktop.exe --workspace "D:\Knowledge\ibip"`.

### Choisir les emplacements

| Données | Emplacement et durée de vie |
|---|---|
| Application | Dossier de distribution en lecture et exécution. Fermer l’application avant de changer de version. Garder le workspace dans un dossier distinct. |
| Projet | Un volume présenté comme local à Windows, persistant et inscriptible. Sur une VDI de pool, vérifier avec l’équipe poste de travail quels volumes ou conteneurs de profil sont effectivement conservés après fermeture de session. |
| Mémoire partageable | Configuration, manifeste, dossiers `memory/` et `decisions/` du workspace. Utiliser export/import ou Git avec une copie de travail par utilisateur. |
| Données de travail | `.local/` du workspace contient à la fois l’index SQLite, le cache matérialisé, les liaisons locales, les brouillons, l’activité Desktop et les points de reprise. Préserver le workspace entier pour reprendre après recyclage de VM. Un export de mémoire suffit pour les révisions publiées. |
| Sources originales | Un ou plusieurs répertoires locaux ou partagés auxquels la session a accès. Ils peuvent être relus après avoir relié leurs emplacements sur un autre poste. Une source momentanément inaccessible reste une source connue. |
| Préférences et temporaires | Espaces récents dans le profil local de l’utilisateur. Répertoires temporaires Copilot dans le temporaire de la session, sous l’identité de l’utilisateur. |

SQLite utilise un journal WAL. Le client oriente les workspaces UNC et les lecteurs réseau vers une copie de travail locale. Choisir pour l’index un volume offrant le verrouillage Windows et une capacité suffisante pendant une reconstruction. Éviter de synchroniser une base SQLite active via OneDrive ou entre deux sessions. Le panneau Stockage donne les tailles de la base, du WAL et du cache. Passer par les commandes de maintenance pour les gérer.

## Première utilisation de bout en bout

1. Dans **Projet**, décrire l’objectif. Cliquer sur **Envoyer à Copilot**, lire sa question, préciser puis confirmer la synthèse lorsque le cadrage est prêt. **Reprendre le cadrage** retrouve le brouillon après une interruption.
2. Dans **Sources**, déposer un XLSX ou choisir un dossier. **Inventorier** compte les formats retenus localement. Donner la finalité de la source et choisir le mode. En automatique, un dépôt est indexé localement. Excel, Draw.io et images sélectionnés individuellement dans un workspace métier déclenchent l’analyse Copilot. Le client présente la qualification proposée pour une nouvelle source et permet de répondre à une question éventuelle. Pour une sélection mixte, indexer le dossier puis analyser les documents importants individuellement.
3. Regarder l’étape courante et le temps écoulé. **Interrompre** demande l’annulation au moteur. **Reprendre la dernière source** retrouve le chemin et les options du dernier traitement lancé depuis le client. Les étapes validées sont réutilisées lorsque les empreintes restent compatibles. **Tout réanalyser** démarre une nouvelle analyse.
4. Dans **Dossiers**, poser une question. La réponse apparaît en texte. Double-cliquer sur une citation ou appuyer sur Entrée pour voir les extraits, leur version et leurs coordonnées dans l’onglet **Preuves**. **Détail technique** donne les recherches effectuées, le nombre de résultats et les identifiants transmis. Poursuivre dans le même dossier ou exporter sa synthèse en Markdown.
5. Dans **Arbitrages**, examiner les assertions rapprochées, leurs conditions et leurs sources. Saisir une décision, son motif et son auteur. Les déclarations et réponses humaines sont enregistrées explicitement.

## Fonctions par écran

| Écran | Actions |
|---|---|
| Projet | Consulter le cadrage confirmé, répondre à l’entretien, reprendre le brouillon, confirmer ou archiver et recommencer. Importer un profil JSON dans un ancien workspace. |
| Sources | Fichier, dossier ou dépôt glissé dans la zone de chemin, inventaire, sources connues, liaisons, qualification, indexation locale, analyse ou réanalyse. Les options permettent de choisir rôle, identifiant, mode guidé/par lots, budget guidé et fichier de contexte des anciens espaces. Le détail d’une source indique sa couverture publiée et les états d’exploration disponibles. |
| Connaissances | Recherche locale, filtres de domaine, rôle et historique, préparation d’un contexte, interrogation paginée des entités, preuves, relations, questions, projets et analyses, renommage d’une entité. |
| Dossiers | Question ponctuelle, conversation suivie, liste paginée, consultation et reprise de dossiers existants, preuves et export Markdown. |
| Arbitrages | Assertions en attente, rapprochements, preuves et historique, acceptation, rejet, distinction ou remplacement, questions ouvertes et déclarations humaines. |
| Activité et outils | Durées et états, historique des opérations, diagnostic poste/Copilot et test en ligne, accès aux diagnostics bruts privés, informations techniques à copier après aperçu, tailles disque, actualisation/reconstruction de l’index, compactage/reconstruction du cache, import/export de mémoire, simulation et exécution de migration, résolution d’un conflit, rapports, évaluation et mesure synthétique. Thème et taille du texte réglables pour la session. |
| Intégrations | Configuration MCP avec les chemins de la VDI pour le plugin Copilot, essai du serveur avec initialisation, liste des outils et lecture réelle du projet. Lancement ou arrêt de la vue web en boucle locale. |

## Diagnostic et reconnexion

| Symptôme | Action |
|---|---|
| Copilot fonctionne dans un terminal mais le client ne le trouve pas | Comparer le lanceur et le répertoire temporaire dans le diagnostic de la session graphique. Vérifier l’installation approuvée, le PATH de la session et le paramètre `copilotExecutable`. Relancer le client après une modification. |
| Index absent ou en retard après import/fusion | Dans Activité, utiliser **Actualiser l’index**. **Reconstruire l’index** effectue une reconstruction complète depuis la mémoire publiée. |
| Exploration interrompue ou référence refusée | Consulter le journal, puis les diagnostics Copilot du workspace. Ces fichiers peuvent contenir des extraits de sources. Le diagnostic court copiable affiche seulement les champs techniques montrés dans l’aperçu. |
| Session distante déconnectée | Si Windows conserve la session et ses processus, se reconnecter à la même session pour retrouver le travail en cours. Une fermeture de session ou un recyclage du poste peut terminer les processus immédiatement. Rouvrir le workspace persistant et reprendre la dernière source. L’historique distingue les opérations dont la fin a été enregistrée. |
| Attente pendant une opération de maintenance | L’opération s’exécute hors du fil d’affichage. La fenêtre traite une opération à la fois. Certaines lectures, migrations et mesures synthétiques ne rendent la main qu’à la fin de leur étape. |
| Partage réseau indisponible ou poste hors ligne | Ouvrir le workspace persistant pour consulter les connaissances déjà publiées. Relier les sources quand elles redeviennent disponibles. La recherche locale fonctionne avec un index à jour. |
| Profil de VDI réinitialisé | Utiliser **Ouvrir** ou le raccourci avec `--workspace`. Si seule la mémoire publiée a été restaurée, relier les sources et rebâtir l’index. Les brouillons et travaux partiels exigent la conservation de `.local/`. |

## Construction et recette

Depuis un poste de construction équipé du SDK .NET 10 et de PowerShell :

```powershell
./scripts/publish-desktop.ps1
./scripts/test-desktop.ps1 -Directory ./artifacts/desktop-win-x64
```

Le script publie en Windows x64 autonome avec des dépendances verrouillées dans `artifacts/desktop-win-x64/`. La CI Windows lance le parcours métier avec un Copilot simulé, ouvre la fenêtre, navigue entre les espaces et lit le diagnostic de stockage. Le workflow propose ensuite la distribution complète comme un seul artefact. Le client utilise directement les services .NET du moteur. La sonde MCP et la vue web démarrent la CLI livrée dans le même dossier.

**Recette VDI de la livraison unique.** Essayer l’archive avec un compte utilisateur standard, la stratégie d’exécution de l’entreprise et son proxy. Créer et rouvrir un workspace persistant, confirmer le cadrage, analyser un classeur avec Copilot authentifié, indexer un dépôt représentatif, vérifier une citation jusqu’à sa cellule et arbitrer une contradiction. Déconnecter puis reconnecter la session pendant l’ingestion. Tester séparément une vraie fermeture de session avec conservation du workspace, un changement de VM, le redimensionnement du bureau distant, la navigation au clavier et le quota disque. Mesurer les temps locaux et Copilot séparément. La qualification finale dépend de cette recette sur l’image Windows réellement déployée.
