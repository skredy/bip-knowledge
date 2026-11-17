# Réduire le cache matérialisé

`.local/materialized.json` est une copie locale des connaissances reconstruites depuis `memory/`. Il évite de rejouer tout le journal lors des opérations qui chargent la mémoire complète. Les décisions humaines sont conservées séparément dans `decisions/`.

Un cache volumineux ne signifie pas nécessairement que ses connaissances sont inutiles : des centaines d'objets et de relations peuvent contenir les mêmes listes de preuves. Recréer le même format reproduirait ces listes. Depuis la **0.4.7**, bip-kb peut les stocker une seule fois dans le cache, sans supprimer aucun lien.

## Compacter une base existante

Après installation de la 0.4.7 ou ultérieure, exécutez avec le chemin de votre base :

```powershell
bip-kb cache compact --workspace "C:/Knowledge/ibip"
```

Depuis le dossier de la base, `bip-kb cache compact` suffit. La commande affiche une progression, puis les tailles avant/après et le pourcentage économisé. `--json` fournit un résultat automatisable, notamment `previousBytes`, `currentBytes`, `savedBytes`, `reductionPercent` et `verified`.

La commande charge l'état courant, écrit un fichier temporaire, le relit et compare les empreintes SHA-256 de la sérialisation complète des connaissances avant/après. Le remplacement n'intervient qu'après cette vérification. L'ordre, les répétitions et les identifiants dans les listes sont préservés à la restitution ; les références du cache ne remplacent pas les identifiants canoniques des preuves.

Le compactage conserve les anciennes versions, les preuves, les relations, les dossiers et les questions. Il ne modifie ni `memory/`, ni `decisions/`, ni SQLite, ni les caches d'analyse Copilot. Aucune réingestion, réanalyse ou réindexation n'est nécessaire du seul fait de cette opération. Le MCP continue à utiliser son index SQLite.

## Reconstruire depuis la mémoire publiée

Pour ignorer le cache existant et reconstruire son contenu à partir des fichiers partagés :

```powershell
bip-kb cache compact --rebuild --workspace "C:/Knowledge/ibip"
```

Cette variante vérifie aussi l'intégrité des lots et les conflits de l'historique partagé. Elle peut être plus longue. Elle n'efface pas l'historique et n'élimine pas les connaissances simplement anciennes. Si les données partagées sont incomplètes ou conflictuelles, le remplacement est refusé.

Les nouvelles reconstructions automatiques du cache utilisent le format compact. Une mise à jour du logiciel ne réécrit pas systématiquement un cache existant : utilisez la commande explicite pour le convertir maintenant.

## Gain attendu et précautions pratiques

Sur le corpus synthétique utilisé pour le test de performance, le cache est passé de **709 512 544 à 18 631 171 octets**, soit **97,37 % de réduction**, avec contenu vérifié et fichiers partagés inchangés. Le gain sur votre base dépend des répétitions ; du texte unique occupe toujours de l'espace. Une seconde exécution sur un cache déjà compact peut ne rien économiser.

La première conversion charge encore l'ancien cache, ce qui peut prendre du temps et consommer de la mémoire. Il faut également de la place pour le nouveau fichier temporaire. Une interruption avant remplacement laisse le cache précédent en place ; `Ctrl+C` est pris en compte entre les étapes et pendant les écritures de listes ou le calcul d'empreinte. Une désérialisation déjà en cours se termine avant le contrôle suivant.

Le format compact est local et reconstructible. Les versions antérieures à 0.4.7 ne le comprennent pas : elles peuvent rejouer la mémoire publiée et régénérer un cache plus volumineux. Les fichiers à partager par Git restent `memory/`, `decisions/` et la configuration, jamais `.local/`.

Si vous souhaitez retirer de vraies connaissances inutiles, cela demande d'identifier les sources concernées et l'effet sur leurs preuves. Le compactage ne prend pas cette décision à votre place.
