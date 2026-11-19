using System.Text.RegularExpressions;

namespace BipKb;

public static class Retrieval
{
    public static Claim[] Select(Knowledge k,string text,int count=20,SourceContext? context=null)
    {
        var words=Regex.Matches(text.ToLowerInvariant(),@"[\p{L}\p{N}_]{3,}").Select(m=>m.Value).Distinct().Take(100).ToArray();
        var profile=context==null?null:k.Projects.SingleOrDefault(p=>p.Name.Equals(context.Project,StringComparison.OrdinalIgnoreCase));
        var domains=context==null?null:Projects.Related(profile,context.Domain);
        var entities=k.Entities.ToDictionary(e=>e.Id);
        return k.Claims.Where(c=>!c.Obsolete&&(context==null||c.Context!=null&&c.Context.Project.Equals(context.Project,StringComparison.OrdinalIgnoreCase)&&domains!.Contains(c.Context.Domain))).Select(c=>new{Claim=c,Score=words.Count(w=>(c.Statement+" "+entities[c.EntityId].Name).Contains(w,StringComparison.OrdinalIgnoreCase))})
            .Where(x=>x.Score>0).OrderByDescending(x=>x.Score).ThenBy(x=>x.Claim.Id).Take(count).Select(x=>x.Claim).ToArray();
    }
}
public sealed class Ingestor(Workspace workspace,IAnalyzer analyzer,Action<string>? progress=null)
{
    public async Task<IngestionReport> Run(string path,string? sourceId,SourceRole? role,bool reanalyze,CancellationToken ct,bool analyze=true,bool guided=false,int? maxCalls=null)
    {
        path=Path.GetFullPath(path);
        if(!File.Exists(path)&&!Directory.Exists(path))throw new IOException("Source inaccessible; existing knowledge remains unchanged.");
        progress?.Invoke("Préparation de l'ingestion (Ctrl+C pour interrompre et reprendre plus tard)");
        var business=workspace.Config.BusinessMode&&analyze&&guided;
        progress?.Invoke(business?"Mode business analyste — compréhension du document puis connaissances sourcées":analyze?(guided?"Mode exploration guidée par Copilot":"Mode analyse par lots avec Copilot"):"Mode indexation locale — aucun appel Copilot ; analyse ciblée avec ask ou --analyze");
        if(business&&analyzer is not IBusinessAssistant)throw new NotSupportedException("Adaptateur métier requis.");
        if(analyze&&guided&&!business&&analyzer is not IExplorationAssistant)throw new NotSupportedException("Cet adaptateur ne prend pas en charge l'exploration guidée.");
        progress?.Invoke("Chargement de la mémoire locale et des décisions");
        workspace.EnsureWritable();
        var baseline=workspace.SemanticRevision;var configuration=Json.Hash(Json.Serialize(workspace.Config));
        var config=workspace.Config; var bindings=workspace.Bindings(); var k=workspace.Load(); var decisions=workspace.Decisions();
        var explorationBudget=analyze&&guided?new ExplorationBudget(maxCalls??config.MaxExplorationCalls):null;
        static bool Under(string p,string root) { var relative=Path.GetRelativePath(root,p); return relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar)&&!Path.IsPathRooted(relative); }
        if(sourceId==null)
        {
            var matches=bindings.Where(b=>Under(path,b.Value)).ToArray();
            if(matches.Length!=1)throw new ArgumentException("Bind the source first: bip-kb source bind <id> <directory> --role legacy|target|current; use --source for overlapping roots.");
            sourceId=matches[0].Key;
        }
        if(!bindings.TryGetValue(sourceId,out var root)||!Under(path,root)) throw new ArgumentException("Path is outside the selected source binding.");
        var definition=config.Sources.Single(s=>s.Id==sourceId);
        progress?.Invoke("Inventaire des fichiers");
        var files=await Extractor.Inventory(path,root,config,ct);
        progress?.Invoke($"Inventaire terminé : {files.Count} fichier(s) ; vérification des contextes");
        // Preflight every file before invoking Copilot: partial configuration must not start a batch.
        foreach(var file in files)
        {
            var descriptor=definition.Resolve(Path.GetRelativePath(root,file))??throw new ArgumentException("Contexte absent pour "+Path.GetRelativePath(root,file)+". Relancez ingest dans un terminal ou fournissez --context-file.");
            descriptor.Validate(config.BusinessMode);
        }
        var warnings=new List<string>(); var changed=new List<string>(); var unchanged=0; var counts=Enum.GetValues<Contribution>().ToDictionary(c=>c,c=>0);
        var seen=new HashSet<string>(); var groups=new Dictionary<SourceDescriptor,List<Unit>>();
        var groupFiles=new Dictionary<SourceDescriptor,HashSet<string>>();var headers=new Dictionary<string,UnitHeader>();
        var evidenceIds=k.Evidence.Select(e=>e.Id).ToHashSet();
        var objectPositions=k.Objects.Select((o,i)=>(o.Id,i)).ToDictionary(p=>p.Id,p=>p.i);
        var relationPositions=k.Relations.Select((r,i)=>(r.Id,i)).ToDictionary(p=>p.Id,p=>p.i);
        var hasStaging=Directory.Exists(Path.Combine(workspace.Local,"staging"));
        var fileNumber=0;
        foreach(var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var relative=Path.GetRelativePath(root,file).Replace('\\','/'); seen.Add(relative);
            var fileLabel=$"Fichier {++fileNumber}/{files.Count} — {relative}";
            progress?.Invoke(fileLabel+" — lecture et empreinte");
            var descriptor=definition.Resolve(relative)!;var context=descriptor.Context;var effectiveRole=role??descriptor.Role;
            if(new FileInfo(file).Length>config.MaxFileBytes)throw new InvalidDataException("File exceeds maxFileBytes: "+relative);
            var bytes=await File.ReadAllBytesAsync(file,ct); if(bytes.Length>config.MaxFileBytes)throw new InvalidDataException("File grew beyond maxFileBytes."); var hash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
            var profile=k.Projects.SingleOrDefault(p=>p.Name.Equals(context.Project,StringComparison.OrdinalIgnoreCase));
            var key=Json.Hash(hash+Extractor.VersionFor(file)+(config.BusinessMode?BusinessExtraction.Version+BusinessDocuments.Protocol:"")+config.AnalysisProfile+Json.Serialize(new{descriptor,definition.ExcelSheets,config.MaxFileBytes,project=profile})+effectiveRole);
            var previous=k.Sources.SingleOrDefault(s=>s.Active&&s.SourceId==sourceId&&s.RelativePath==relative);
            var pending=business?new BusinessDocuments(workspace,(IBusinessAssistant)analyzer).Pending(sourceId,relative,key,previous):analyze&&guided&&new DocumentExplorer(workspace,(IExplorationAssistant)analyzer).HasPending(sourceId,relative,key,previous);
            if(!reanalyze&&!pending&&previous?.CacheKey==key&&(!analyze||previous.Analyzed||guided&&(previous.Exploration!=null||previous.Business!=null)))
            {
                unchanged++;counts[Contribution.None]++;warnings.AddRange(previous.Warnings.Select(w=>relative+": "+w));
                progress?.Invoke(fileLabel+(previous.Analyzed?" — inchangé, analyse réutilisée":previous.Exploration!=null?" — inchangé, analyse partielle conservée ; --reanalyze pour la réexaminer":" — inchangé, indexation réutilisée"));continue;
            }
            var versionId=Json.Hash(sourceId+"/"+relative+key);
            if(analyze&&guided)
                progress?.Invoke(fileLabel+" — "+(reanalyze?"réanalyse demandée":pending?"reprise d'une exploration non publiée":previous==null?"première analyse de cette source":previous.Hash!=hash?"contenu du fichier modifié":previous.CacheKey!=key?"contexte ou profil d'extraction modifié":"analyse des données précédemment indexées"));
            // Parse the exact bytes that were hashed, not a second read of a changing source.
            var snapshot=Path.Combine(workspace.Local,"inputs",Guid.NewGuid().ToString("N")+Path.GetExtension(file));
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            await File.WriteAllBytesAsync(snapshot,bytes,ct);
            progress?.Invoke(fileLabel+" — extraction");
            Extraction extraction;
            try { extraction=config.BusinessMode?BusinessExtraction.Read(snapshot,config.MaxFileBytes,definition.ExcelSheets):Extractor.Read(snapshot,config.MaxFileBytes,definition.ExcelSheets); }
            finally { File.Delete(snapshot); }
            warnings.AddRange(extraction.Warnings.Select(w=>relative+": "+w));
            var evidence=extraction.Units.Select(u=>new Evidence(Json.Hash(versionId+u.Id),sourceId,relative,hash,u.Locator,u.Text,u.Parent)).DistinctBy(e=>e.Id).ToArray();
            foreach(var e in evidence)if(evidenceIds.Add(e.Id))k.Evidence.Add(e);
            var units=evidence.Select(e=>new Unit(e.Id,$"{sourceId}/{relative} {e.Locator}",e.Excerpt,config.BusinessMode?e.Parent:null)).ToArray();
            if(extraction.RepeatedHeaders!=null)
                foreach(var (id,header) in extraction.RepeatedHeaders)headers[Json.Hash(versionId+id)]=header with{Scope=sourceId+"/"+relative+" "+header.Scope};
            var technical=TechnicalExtraction.Read(bytes,Path.GetExtension(file).ToLowerInvariant(),sourceId,context,evidence,config.BusinessMode);
            foreach(var item in technical.Objects)
                if(objectPositions.TryGetValue(item.Id,out var index))k.Objects[index]=item with{EvidenceIds=k.Objects[index].EvidenceIds.Concat(item.EvidenceIds).Distinct().ToArray()};
                else {objectPositions[item.Id]=k.Objects.Count;k.Objects.Add(item);}
            foreach(var edge in technical.Relations)
                if(relationPositions.TryGetValue(edge.Id,out var index))k.Relations[index]=edge with{EvidenceIds=k.Relations[index].EvidenceIds.Concat(edge.EvidenceIds).Distinct().ToArray()};
                else {relationPositions[edge.Id]=k.Relations.Count;k.Relations.Add(edge);}
            var duplicate=k.Sources.FirstOrDefault(s=>s.Active&&s.Hash==hash&&s.Role==effectiveRole&&s.Context==context&&s.Id!=previous?.Id&&(!analyze||s.Analyzed));
            var fileClaims=new HashSet<string>();
            void Accept(Analysis analysis)
            {
                foreach(var f in analysis.Findings)
                {
                    var claim=Apply(k,f,effectiveRole,counts,context);
                    if(claim!=null){fileClaims.Add(claim.Id);changed.Add(claim.Id);}
                }
            }
            var analysisComplete=analyze;
            ExplorationSummary? exploration=null;BusinessSummary? businessSummary=null;
            if(duplicate!=null&&!reanalyze&&!pending)
            {
                analysisComplete=duplicate.Analyzed;
                exploration=duplicate.Exploration;businessSummary=duplicate.Business;
                progress?.Invoke(fileLabel+" — contenu dupliqué, connaissances réutilisées");
                foreach(var id in duplicate.ClaimIds)
                {
                    var i=k.Claims.FindIndex(c=>c.Id==id); var old=k.Claims[i];
                    var corresponding=old.EvidenceIds.Select(eid=>k.Evidence.Single(e=>e.Id==eid)).Where(e=>e.Version==hash)
                        .SelectMany(oldEvidence=>evidence.Where(e=>e.Locator==oldEvidence.Locator&&e.Excerpt==oldEvidence.Excerpt)).Select(e=>e.Id).ToArray();
                    if(corresponding.Length==0)continue;
                    k.Claims[i]=old with{EvidenceIds=old.EvidenceIds.Concat(corresponding).Distinct().Order().ToArray(),Obsolete=false}; fileClaims.Add(id);
                }
                counts[Contribution.None]++; warnings.Add(relative+": duplicate content; not independent corroboration.");
            }
            else if(business)
            {
                var attached=extraction.Image||config.NativeDocuments&&Path.GetExtension(file).ToLowerInvariant() is ".xls" or ".xlsx" or ".drawio";
                var original=attached?Path.Combine(workspace.Local,"inputs",Guid.NewGuid().ToString("N")+Path.GetExtension(file)):null;
                if(original!=null)await File.WriteAllBytesAsync(original,bytes,ct);
                try
                {
                    businessSummary=await new BusinessDocuments(workspace,(IBusinessAssistant)analyzer,progress).Run(sourceId,relative,key,Path.GetExtension(file).ToLowerInvariant(),units,extraction.Warnings.ToArray(),original,reanalyze,explorationBudget!,
                        (read,plan,budget)=>Request(k,decisions,effectiveRole,read,Retrieval.Select(k,context.Description+" "+plan+" "+string.Join(" ",read.Select(u=>u.Text)),context:context),"business",context,budget),
                        (analysis,links)=>
                        {
                            Accept(analysis);
                            foreach(var link in links)
                            {
                                var from=analysis.Findings.First(f=>f.Entity==link.From);var to=analysis.Findings.First(f=>f.Entity==link.To);
                                string Entity(Finding f)=>k.Entities.Single(e=>e.IdentityKey==Identity(k,f,context)).Id;
                                var a=Entity(from);var b=Entity(to);var id=Json.Hash(a+link.Kind+b+versionId);
                                k.Relations.RemoveAll(r=>r.Id==id);k.Relations.Add(new(id,a,b,link.Kind,link.EvidenceIds,KnowledgeOrigin.Inferred));
                            }
                        },ct);
                    analysisComplete=businessSummary.Complete;
                    foreach(var question in businessSummary.Questions)
                    {
                        var id=Json.Hash(versionId+question);if(!k.Questions.Any(q=>q.Id==id))k.Questions.Add(new(id,context.Project,question,[]));
                    }
                    if(!analysisComplete){extraction.Warnings.Add("Compréhension partielle : consulter analysis pour les sections non interprétées.");warnings.Add(relative+": compréhension partielle.");}
                }
                finally{if(original!=null)File.Delete(original);}
            }
            else if(analyze&&guided)
            {
                var imageSnapshot=extraction.Image?Path.Combine(workspace.Local,"inputs",Guid.NewGuid().ToString("N")+Path.GetExtension(file)):null;
                if(imageSnapshot!=null)await File.WriteAllBytesAsync(imageSnapshot,bytes,ct);
                try
                {
                    exploration=await new DocumentExplorer(workspace,(IExplorationAssistant)analyzer,progress).Run(sourceId,relative,key,Path.GetExtension(file).ToLowerInvariant(),units,headers,extraction.Warnings.ToArray(),imageSnapshot,reanalyze,explorationBudget!,
                        (read,plan,budget)=>Request(k,decisions,effectiveRole,read,Retrieval.Select(k,context.Description+" "+context.Notes+" "+plan+" "+string.Join(" ",read.Select(u=>u.Text)),context:context),"explore",context,budget),Accept,ct);
                    analysisComplete=exploration.Complete;
                    if(!analysisComplete)
                    {var warning=$"Analyse guidée partielle — {exploration.ExcludedUnits} unités exclues, {exploration.UnresolvedUnits} non résolues. Consulter analysis {sourceId}.";extraction.Warnings.Add(warning);warnings.Add(relative+": "+warning);}
                }
                finally{if(imageSnapshot!=null)File.Delete(imageSnapshot);}
            }
            else if(analyze||hasStaging)
            {
                // Reuse the completed prefix of pre-0.3.11 Excel batches before planning the remainder.
                // Keep the original requests and evidence IDs so an interrupted analysis is not discarded.
                var recovered=0;
                if(extraction.RepeatedHeaders is {Count:>0}&&hasStaging&&!reanalyze)
                    foreach(var legacyBatch in AnalysisBatches.Create(units))
                    {
                        ct.ThrowIfCancellationRequested();
                        var existing=Retrieval.Select(k,context.Description+" "+context.Notes+" "+string.Join(" ",legacyBatch.Select(u=>u.Locator+" "+u.Text)),context:context);
                        var saved=ReadCached(Request(k,decisions,effectiveRole,legacyBatch,existing,"extract",context),key);
                        if(saved==null)break;
                        Accept(saved);recovered+=legacyBatch.Length;
                        progress?.Invoke(fileLabel+$" — anciens lots validés récupérés : {recovered}/{units.Length} unités");
                    }
                var batches=AnalysisBatches.Create(units.Skip(recovered),headers).ToArray();var batchNumber=0;
                var omitted=units.Length-recovered-batches.Sum(b=>b.Length);
                progress?.Invoke(fileLabel+$" — {units.Length} unités extraites, {batches.Length} lot(s) à analyser ou réutiliser"+(omitted>0?$", {omitted} ligne(s) sans données écartée(s) de l'analyse":""));
                analysisComplete=analyze||batches.Length>0||recovered>0;
                foreach(var batch in batches)
                {
                    var label=$"{fileLabel} — lot {++batchNumber}/{batches.Length}";
                    progress?.Invoke(label+" — recherche du contexte");
                    var existing=Retrieval.Select(k,context.Description+" "+context.Notes+" "+string.Join(" ",batch.Select(u=>u.Locator+" "+u.Text)),context:context);
                    var request=Request(k,decisions,effectiveRole,batch,existing,"extract",context);
                    Analysis? analysis;
                    if(!analyze)
                    {
                        analysis=ReadCached(request,key);
                        if(analysis==null){analysisComplete=false;continue;}
                        progress?.Invoke(label+" — résultat intermédiaire réutilisé");
                    }
                    else if(extraction.Image)
                    {
                        var imageSnapshot=Path.Combine(workspace.Local,"inputs",hash+Path.GetExtension(file));
                        await File.WriteAllBytesAsync(imageSnapshot,bytes,ct);
                        try { analysis=await AnalyzeCached(request,imageSnapshot,key,reanalyze,ct,label); }
                        finally { File.Delete(imageSnapshot); }
                    }
                    else analysis=await AnalyzeCached(request,null,key,reanalyze,ct,label);
                    Accept(analysis);
                }
            }
            if(extraction.Image&&!analysisComplete)
            {
                var warning=exploration==null?"Image référencée uniquement, contenu visuel non analysé. Utilisez --analyze sur cette image.":"Analyse visuelle incomplète : consulter le plan et les limites avec analysis.";
                extraction.Warnings.Add(warning);warnings.Add(relative+": "+warning);
            }
            if(previous!=null) k.Sources[k.Sources.IndexOf(previous)]=previous with{Active=false};
            k.Sources.RemoveAll(s=>s.Id==versionId);
            k.Sources.Add(new(versionId,sourceId,relative,hash,key,effectiveRole,evidence.Select(e=>e.Id).ToArray(),fileClaims.Order().ToArray(),true,extraction.Warnings.ToArray(),context,analysisComplete,exploration,businessSummary));
            var groupKey=descriptor with{Role=effectiveRole};
            if(analyze&&!guided)
            {
                if(!groups.TryGetValue(groupKey,out var groupUnits)){groupUnits=[];groups.Add(groupKey,groupUnits);}
                groupUnits.AddRange(units);
                if(!groupFiles.TryGetValue(groupKey,out var paths)){paths=[];groupFiles.Add(groupKey,paths);}paths.Add(relative);
            }
            progress?.Invoke(fileLabel+(analyze?" — traité":" — indexé localement"));
        }
        progress?.Invoke("Vérification des sources supprimées et des preuves");
        // Deletions are inferred only from a successful full-root inventory and actual absence.
        if(Directory.Exists(path)&&Path.GetFullPath(path)==Path.GetFullPath(root))
            for(var i=0;i<k.Sources.Count;i++)
            {
                var s=k.Sources[i];
                if(s.Active&&s.SourceId==sourceId&&!seen.Contains(s.RelativePath))
                {
                    var original=Path.Combine(root,s.RelativePath);
                    try { _=File.GetAttributes(original); }
                    catch(FileNotFoundException) { k.Sources[i]=s with{Active=false}; warnings.Add(s.RelativePath+": source deleted; evidence retained as historical."); }
                    catch(DirectoryNotFoundException) { k.Sources[i]=s with{Active=false}; warnings.Add(s.RelativePath+": source deleted; evidence retained as historical."); }
                }
            }
        RefreshObsolete(k);
        // Reconcile bounded groups; each group also retrieves the closest existing relationships.
        var reconciliation=groups.Where(g=>groupFiles[g.Key].Count>1).SelectMany(g=>AnalysisBatches.Create(g.Value,headers).Select(batch=>(Group:g,Batch:batch))).ToArray();
        var reconciliationNumber=0;
        foreach(var (group,batch) in reconciliation)
            {
                var label=$"Rapprochement entre fichiers — lot {++reconciliationNumber}/{reconciliation.Length}";
                progress?.Invoke(label+" — recherche du contexte");
                var context=group.Key.Context;var effectiveRole=group.Key.Role;
                var existing=Retrieval.Select(k,context.Description+" "+string.Join(" ",batch.Select(u=>u.Text)),30,context);
                var request=Request(k,decisions,effectiveRole,batch,existing,"reconcile",context);
                var analysis=await AnalyzeCached(request,null,"reconcile-"+Json.Hash(string.Join("",group.Value.Select(u=>u.Id))),reanalyze,ct,label);
                foreach(var f in analysis.Findings.Where(f=>f.RelatedClaimIds.Length>0))
                {
                    var claim=Apply(k,f,effectiveRole,counts,context); if(claim==null)continue; changed.Add(claim.Id);
                    for(var i=0;i<k.Sources.Count;i++) if(k.Sources[i].Active&&k.Sources[i].EvidenceIds.Intersect(claim.EvidenceIds).Any()) k.Sources[i]=k.Sources[i] with{ClaimIds=k.Sources[i].ClaimIds.Append(claim.Id).Distinct().Order().ToArray()};
                }
            }
        RefreshObsolete(k);
        var unanalyzed=k.Sources.Count(s=>s.Active&&s.SourceId==sourceId&&seen.Contains(s.RelativePath)&&!s.Analyzed);
        var report=new IngestionReport(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,sourceId,files.Count,unchanged,counts,warnings.ToArray(),changed.Distinct().Order().ToArray(),business?"business":analyze?(guided?"guided":"analysis"):"index",unanalyzed);
        k.Reports.Add(report); ct.ThrowIfCancellationRequested();
        progress?.Invoke("Enregistrement des connaissances et du rapport");
        string publishedRevision;
        using(var publication=workspace.Lock())
        {
            if(workspace.SemanticRevision!=baseline||Json.Hash(Json.Serialize(workspace.Config))!=configuration)throw new RevisionConflictException("Knowledge or context changed during analysis. Retry ingestion; unchanged staged units will be reused.");
            workspace.Save(k);
            publishedRevision=workspace.SemanticRevision;
        }
        // Publish the read projection before reporting completion, including guided/model analysis.
        // An interrupted rebuild can be resumed with reindex without repeating Copilot calls.
        progress?.Invoke("Construction de l'index de recherche local");
        new SearchEngine(workspace).Refresh(supplied:k,progress:progress,ct:ct,suppliedRevision:publishedRevision);
        progress?.Invoke($"Ingestion terminée : {files.Count} fichier(s), dont {unchanged} inchangé(s)");
        return report;
    }
    public static AnalysisRequest Request(Knowledge k,List<Decision> decisions,SourceRole role,Unit[] units,Claim[] existing,string phase,SourceContext? context=null,int budget=18000)
    {
        // Bound large assertions and decision explanations before constructing the prompt.
        existing=existing.Take(20).Select(c=>c with{Statement=c.Statement[..Math.Min(c.Statement.Length,1500)]}).ToArray();
        var ids=existing.Select(c=>c.Id).ToHashSet();
        var request=new AnalysisRequest(role.ToString(),units,existing,k.Entities.Where(e=>existing.Any(c=>c.EntityId==e.Id)).ToArray(),decisions.Where(d=>ids.Contains(d.ClaimId)).TakeLast(20).Select(d=>d with{Reason=d.Reason[..Math.Min(d.Reason.Length,1000)]}).ToArray(),phase,context,k.Projects.SingleOrDefault(p=>p.Name.Equals(context?.Project,StringComparison.OrdinalIgnoreCase)));
        request=request with{HumanDeclarations=k.Questions.Where(q=>q.Project==context?.Project&&q.Answer!=null).TakeLast(5).Select(q=>q with{Answer=q.Answer![..Math.Min(500,q.Answer!.Length)]}).ToArray()};
        if(request.Project is {} project)request=request with{Project=BusinessInterview.Compact(project) with{Objective=project.Objective[..Math.Min(600,project.Objective.Length)],Architecture=project.Architecture[..Math.Min(600,project.Architecture.Length)],Conventions=project.Conventions.Take(5).Select(c=>c[..Math.Min(200,c.Length)]).ToArray(),Domains=project.Domains.Where(d=>d.Name==context?.Domain).Take(1).ToArray(),Glossary=project.Glossary.Where(g=>units.Any(u=>u.Text.Contains(g.Term,StringComparison.OrdinalIgnoreCase))).Take(3).ToArray(),Authorities=project.Authorities.Take(5).ToArray(),Systems=project.Systems.Take(5).ToArray()}};
        while(Json.Serialize(request).Length>budget)
        {
            if(request.Existing.Length==0)throw new InvalidDataException("Unit metadata exceeds the analysis budget.");
            var keep=request.Existing.SkipLast(1).ToArray(); var claimIds=keep.Select(c=>c.Id).ToHashSet();
            request=request with{Existing=keep,Entities=request.Entities.Where(e=>keep.Any(c=>c.EntityId==e.Id)).ToArray(),Decisions=request.Decisions.Where(d=>claimIds.Contains(d.ClaimId)).ToArray()};
        }
        return request;
    }
    private async Task<Analysis> AnalyzeCached(AnalysisRequest request,string? image,string salt,bool force,CancellationToken ct,string label)
    {
        var key=Json.Hash(salt+Json.Serialize(request)); var path=Path.Combine(workspace.Local,"staging",key+".json");
        if(!force&&File.Exists(path)) { var saved=Json.Read<Analysis>(File.ReadAllText(path)); CopilotAnalyzer.Validate(saved,request); progress?.Invoke(label+" — résultat intermédiaire réutilisé"); return saved; }
        progress?.Invoke(label+" — attente de Copilot");
        var analysis=await analyzer.Analyze(request,image,ct);
        progress?.Invoke(label+" — réponse reçue, validation des preuves");
        CopilotAnalyzer.Validate(analysis,request); Json.Write(path,analysis); return analysis;
    }
    private Analysis? ReadCached(AnalysisRequest request,string salt)
    {
        var path=Path.Combine(workspace.Local,"staging",Json.Hash(salt+Json.Serialize(request))+".json");
        if(!File.Exists(path))return null;
        var saved=Json.Read<Analysis>(File.ReadAllText(path));CopilotAnalyzer.Validate(saved,request);return saved;
    }
    private static string Identity(Knowledge k,Finding finding,SourceContext context)
    {
        var source=k.Evidence.Single(e=>e.Id==finding.EvidenceIds[0]).SourceId;
        if(!finding.Uncertain&&finding.Contribution is Contribution.Corroboration or Contribution.Complement)
        {
            var ids=k.Claims.Where(c=>finding.RelatedClaimIds.Contains(c.Id)).Select(c=>c.EntityId).Distinct().ToArray();
            if(ids.Length==1&&k.Entities.Single(e=>e.Id==ids[0]) is {} known&&known.Name.Equals(finding.QualifiedName??finding.Entity,StringComparison.OrdinalIgnoreCase)&&known.Kind==finding.Kind&&known.Project==context.Project)return known.IdentityKey??known.Id;
        }
        return source+":"+context.Scope+":"+finding.Kind.ToLowerInvariant()+":"+(finding.QualifiedName??finding.Entity).Trim().ToLowerInvariant()+(finding.Role!=null&&finding.Kind is "table" or "endpoint" or "dto" or "method" or "component"?":"+finding.Role:"");
    }
    private static Claim? Apply(Knowledge k,Finding finding,SourceRole role,Dictionary<Contribution,int> counts,SourceContext context)
    {
        role=finding.Role??role;
        if(finding.Contribution==Contribution.None){counts[Contribution.None]++;return null;}
        var identity=Identity(k,finding,context);
        var entity=k.Entities.SingleOrDefault(e=>e.IdentityKey==identity);
        var entityId=entity?.Id??Json.Hash(identity);
        if(entity==null)k.Entities.Add(new(entityId,finding.QualifiedName??finding.Entity,finding.Kind,context.Project,context.Domain,finding.Aliases,identity));
        else if(finding.Aliases.Length>0)k.Entities[k.Entities.IndexOf(entity)]=entity with{Aliases=(entity.Aliases??[]).Concat(finding.Aliases).Distinct().Take(24).ToArray()};
        var contribution=finding.Uncertain?Contribution.Hypothesis:finding.Contribution;
        var related=finding.RelatedClaimIds.Distinct().Order().ToArray();
        // A cross-role difference is an evolution proposal, not an automatic contradiction.
        if(contribution==Contribution.Contradiction&&related.Any(id=>k.Claims.Single(c=>c.Id==id).Role!=role))contribution=Contribution.Evolution;
        var id=Json.Hash(entityId+finding.Statement.Trim()+role+Json.Serialize(context)+contribution+string.Join("",related)+Json.Serialize(finding.Conditions));
        var claim=new Claim(id,entityId,finding.Statement.Trim(),contribution,finding.EvidenceIds.Distinct().Order().ToArray(),related,role,
            finding.Uncertain||context.Status==SourceStatus.Proposal||contribution is Contribution.Hypothesis or Contribution.Contradiction or Contribution.Evolution,Context:context,Origin:finding.Uncertain||contribution==Contribution.Hypothesis?KnowledgeOrigin.Inferred:KnowledgeOrigin.Extracted,State:KnowledgeState.Proposed,Conditions:finding.Conditions);
        var index=k.Claims.FindIndex(c=>c.Id==id);
        if(index>=0)
        {
            var old=k.Claims[index]; claim=old with{EvidenceIds=old.EvidenceIds.Concat(claim.EvidenceIds).Distinct().Order().ToArray(),Obsolete=false}; k.Claims[index]=claim;
            counts[Contribution.None]++;
        }
        else { k.Claims.Add(claim); counts[contribution]++; }
        foreach(var target in related)
        {
            var kind=contribution==Contribution.Contradiction?RelationKind.Contradicts:contribution==Contribution.Evolution?RelationKind.Replaces:RelationKind.MapsTo;
            var relation=new KnowledgeRelation(Json.Hash(id+kind+target),id,target,kind,claim.EvidenceIds,KnowledgeOrigin.Inferred);
            var relationIndex=k.Relations.FindIndex(r=>r.Id==relation.Id);
            if(relationIndex>=0)k.Relations[relationIndex]=relation;else k.Relations.Add(relation);
        }
        return claim;
    }
    private static void RefreshObsolete(Knowledge k)
    {
        var active=k.Sources.Where(s=>s.Active).SelectMany(s=>s.EvidenceIds).ToHashSet();
        var activeClaims=k.Sources.Where(s=>s.Active).SelectMany(s=>s.ClaimIds).ToHashSet();
        for(var i=0;i<k.Objects.Count;i++)k.Objects[i]=k.Objects[i] with{Active=k.Objects[i].EvidenceIds.Any(active.Contains)};
        for(var i=0;i<k.Claims.Count;i++)k.Claims[i]=k.Claims[i] with{Obsolete=!activeClaims.Contains(k.Claims[i].Id)||!k.Claims[i].EvidenceIds.Any(active.Contains)};
    }
}
