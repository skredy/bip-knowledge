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
public sealed class Ingestor(Workspace workspace,IAnalyzer analyzer)
{
    public async Task<IngestionReport> Run(string path,string? sourceId,SourceRole? role,bool reanalyze,CancellationToken ct)
    {
        path=Path.GetFullPath(path);
        if(!File.Exists(path)&&!Directory.Exists(path))throw new IOException("Source inaccessible; existing knowledge remains unchanged.");
        workspace.EnsureWritable();
        var baseline=workspace.SemanticRevision;var configuration=Json.Hash(Json.Serialize(workspace.Config));
        var config=workspace.Config; var bindings=workspace.Bindings(); var k=workspace.Load(); var decisions=workspace.Decisions();
        static bool Under(string p,string root) { var relative=Path.GetRelativePath(root,p); return relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar)&&!Path.IsPathRooted(relative); }
        if(sourceId==null)
        {
            var matches=bindings.Where(b=>Under(path,b.Value)).ToArray();
            if(matches.Length!=1)throw new ArgumentException("Bind the source first: bip-kb source bind <id> <directory> --role legacy|target|current; use --source for overlapping roots.");
            sourceId=matches[0].Key;
        }
        if(!bindings.TryGetValue(sourceId,out var root)||!Under(path,root)) throw new ArgumentException("Path is outside the selected source binding.");
        var definition=config.Sources.Single(s=>s.Id==sourceId);
        var files=await Extractor.Inventory(path,root,config,ct);
        // Preflight every file before invoking Copilot: partial configuration must not start a batch.
        foreach(var file in files)
        {
            var descriptor=definition.Resolve(Path.GetRelativePath(root,file))??throw new ArgumentException("Contexte absent pour "+Path.GetRelativePath(root,file)+". Relancez ingest dans un terminal ou fournissez --context-file.");
            descriptor.Validate();
        }
        var warnings=new List<string>(); var changed=new List<string>(); var unchanged=0; var counts=Enum.GetValues<Contribution>().ToDictionary(c=>c,c=>0);
        var seen=new HashSet<string>(); var groups=new Dictionary<SourceDescriptor,List<Unit>>();
        foreach(var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var relative=Path.GetRelativePath(root,file).Replace('\\','/'); seen.Add(relative);
            var descriptor=definition.Resolve(relative)!;var context=descriptor.Context;var effectiveRole=role??descriptor.Role;
            if(new FileInfo(file).Length>config.MaxFileBytes)throw new InvalidDataException("File exceeds maxFileBytes: "+relative);
            var bytes=await File.ReadAllBytesAsync(file,ct); if(bytes.Length>config.MaxFileBytes)throw new InvalidDataException("File grew beyond maxFileBytes."); var hash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
            var profile=k.Projects.SingleOrDefault(p=>p.Name.Equals(context.Project,StringComparison.OrdinalIgnoreCase));
            var key=Json.Hash(hash+Extractor.Version+config.AnalysisProfile+Json.Serialize(new{descriptor,definition.ExcelSheets,config.MaxFileBytes,project=profile})+effectiveRole);
            var previous=k.Sources.SingleOrDefault(s=>s.Active&&s.SourceId==sourceId&&s.RelativePath==relative);
            if(!reanalyze&&previous?.CacheKey==key) { unchanged++; counts[Contribution.None]++; continue; }
            var versionId=Json.Hash(sourceId+"/"+relative+key);
            // Parse the exact bytes that were hashed, not a second read of a changing source.
            var snapshot=Path.Combine(workspace.Local,"inputs",Guid.NewGuid().ToString("N")+Path.GetExtension(file));
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            await File.WriteAllBytesAsync(snapshot,bytes,ct);
            Extraction extraction;
            try { extraction=Extractor.Read(snapshot,config.MaxFileBytes,config.Sources.Single(s=>s.Id==sourceId).ExcelSheets); }
            finally { File.Delete(snapshot); }
            warnings.AddRange(extraction.Warnings.Select(w=>relative+": "+w));
            var evidence=extraction.Units.Select(u=>new Evidence(Json.Hash(versionId+u.Id),sourceId,relative,hash,u.Locator,u.Text,u.Parent)).ToArray();
            foreach(var e in evidence)if(!k.Evidence.Any(x=>x.Id==e.Id))k.Evidence.Add(e);
            var units=evidence.Select(e=>new Unit(e.Id,$"{sourceId}/{relative} {e.Locator}",e.Excerpt)).ToArray();
            var technical=TechnicalExtraction.Read(bytes,Path.GetExtension(file).ToLowerInvariant(),sourceId,context,evidence);
            foreach(var item in technical.Objects){var oldObject=k.Objects.SingleOrDefault(o=>o.Id==item.Id);k.Objects.RemoveAll(o=>o.Id==item.Id);k.Objects.Add(oldObject==null?item:item with{EvidenceIds=oldObject.EvidenceIds.Concat(item.EvidenceIds).Distinct().ToArray()});}
            foreach(var edge in technical.Relations){var prior=k.Relations.SingleOrDefault(r=>r.Id==edge.Id);k.Relations.RemoveAll(r=>r.Id==edge.Id);k.Relations.Add(prior==null?edge:edge with{EvidenceIds=prior.EvidenceIds.Concat(edge.EvidenceIds).Distinct().ToArray()});}
            var duplicate=k.Sources.FirstOrDefault(s=>s.Active&&s.Hash==hash&&s.Role==effectiveRole&&s.Context==context&&s.Id!=previous?.Id);
            var fileClaims=new HashSet<string>();
            if(duplicate!=null&&!reanalyze)
            {
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
            else
            {
                foreach(var batch in Batches(units,9000))
                {
                    var existing=Retrieval.Select(k,context.Description+" "+context.Notes+" "+string.Join(" ",batch.Select(u=>u.Locator+" "+u.Text)),context:context);
                    var request=Request(k,decisions,effectiveRole,batch,existing,"extract",context);
                    Analysis analysis;
                    if(extraction.Image)
                    {
                        var imageSnapshot=Path.Combine(workspace.Local,"inputs",hash+Path.GetExtension(file));
                        await File.WriteAllBytesAsync(imageSnapshot,bytes,ct);
                        try { analysis=await AnalyzeCached(request,imageSnapshot,key,reanalyze,ct); }
                        finally { File.Delete(imageSnapshot); }
                    }
                    else analysis=await AnalyzeCached(request,null,key,reanalyze,ct);
                    foreach(var f in analysis.Findings)
                    {
                        var claim=Apply(k,f,effectiveRole,counts,context);
                        if(claim!=null){fileClaims.Add(claim.Id);changed.Add(claim.Id);}
                    }
                }
            }
            if(previous!=null) k.Sources[k.Sources.IndexOf(previous)]=previous with{Active=false};
            k.Sources.RemoveAll(s=>s.Id==versionId);
            k.Sources.Add(new(versionId,sourceId,relative,hash,key,effectiveRole,evidence.Select(e=>e.Id).ToArray(),fileClaims.Order().ToArray(),true,extraction.Warnings.ToArray(),context));
            var groupKey=descriptor with{Role=effectiveRole};
            if(!groups.TryGetValue(groupKey,out var groupUnits)){groupUnits=[];groups.Add(groupKey,groupUnits);}
            groupUnits.AddRange(units);
        }
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
        foreach(var group in groups.Where(g=>g.Value.Count>1))
            foreach(var batch in Batches(group.Value.ToArray(),9000))
            {
                var context=group.Key.Context;var effectiveRole=group.Key.Role;
                var existing=Retrieval.Select(k,context.Description+" "+string.Join(" ",batch.Select(u=>u.Text)),30,context);
                var request=Request(k,decisions,effectiveRole,batch,existing,"reconcile",context);
                var analysis=await AnalyzeCached(request,null,"reconcile-"+Json.Hash(string.Join("",group.Value.Select(u=>u.Id))),reanalyze,ct);
                foreach(var f in analysis.Findings.Where(f=>f.RelatedClaimIds.Length>0))
                {
                    var claim=Apply(k,f,effectiveRole,counts,context); if(claim==null)continue; changed.Add(claim.Id);
                    for(var i=0;i<k.Sources.Count;i++) if(k.Sources[i].Active&&k.Sources[i].EvidenceIds.Intersect(claim.EvidenceIds).Any()) k.Sources[i]=k.Sources[i] with{ClaimIds=k.Sources[i].ClaimIds.Append(claim.Id).Distinct().Order().ToArray()};
                }
            }
        RefreshObsolete(k);
        var report=new IngestionReport(Guid.NewGuid().ToString("N"),DateTimeOffset.UtcNow,sourceId,files.Count,unchanged,counts,warnings.ToArray(),changed.Distinct().Order().ToArray());
        k.Reports.Add(report); ct.ThrowIfCancellationRequested();
        using(var publication=workspace.Lock())
        {
            if(workspace.SemanticRevision!=baseline||Json.Hash(Json.Serialize(workspace.Config))!=configuration)throw new RevisionConflictException("Knowledge or context changed during analysis. Retry ingestion; unchanged staged units will be reused.");
            workspace.Save(k);
        }
        // The index is disposable. A process interruption here is repaired by search/import.
        return report;
    }
    public static AnalysisRequest Request(Knowledge k,List<Decision> decisions,SourceRole role,Unit[] units,Claim[] existing,string phase,SourceContext? context=null)
    {
        // Bound large assertions and decision explanations before constructing the prompt.
        existing=existing.Take(20).Select(c=>c with{Statement=c.Statement[..Math.Min(c.Statement.Length,1500)]}).ToArray();
        var ids=existing.Select(c=>c.Id).ToHashSet();
        var request=new AnalysisRequest(role.ToString(),units,existing,k.Entities.Where(e=>existing.Any(c=>c.EntityId==e.Id)).ToArray(),decisions.Where(d=>ids.Contains(d.ClaimId)).TakeLast(20).Select(d=>d with{Reason=d.Reason[..Math.Min(d.Reason.Length,1000)]}).ToArray(),phase,context,k.Projects.SingleOrDefault(p=>p.Name.Equals(context?.Project,StringComparison.OrdinalIgnoreCase)));
        request=request with{HumanDeclarations=k.Questions.Where(q=>q.Project==context?.Project&&q.Answer!=null).TakeLast(5).Select(q=>q with{Answer=q.Answer![..Math.Min(500,q.Answer!.Length)]}).ToArray()};
        if(request.Project is {} project)request=request with{Project=project with{Objective=project.Objective[..Math.Min(600,project.Objective.Length)],Architecture=project.Architecture[..Math.Min(600,project.Architecture.Length)],Conventions=project.Conventions.Take(5).Select(c=>c[..Math.Min(200,c.Length)]).ToArray(),Domains=project.Domains.Where(d=>d.Name==context?.Domain).Take(1).ToArray(),Glossary=project.Glossary.Where(g=>units.Any(u=>u.Text.Contains(g.Term,StringComparison.OrdinalIgnoreCase))).Take(3).ToArray(),Authorities=project.Authorities.Take(5).ToArray(),Systems=project.Systems.Take(5).ToArray()}};
        while(Json.Serialize(request).Length>18000)
        {
            if(request.Existing.Length==0)throw new InvalidDataException("Unit metadata exceeds the analysis budget.");
            var keep=request.Existing.SkipLast(1).ToArray(); var claimIds=keep.Select(c=>c.Id).ToHashSet();
            request=request with{Existing=keep,Entities=request.Entities.Where(e=>keep.Any(c=>c.EntityId==e.Id)).ToArray(),Decisions=request.Decisions.Where(d=>claimIds.Contains(d.ClaimId)).ToArray()};
        }
        return request;
    }
    private async Task<Analysis> AnalyzeCached(AnalysisRequest request,string? image,string salt,bool force,CancellationToken ct)
    {
        var key=Json.Hash(salt+Json.Serialize(request)); var path=Path.Combine(workspace.Local,"staging",key+".json");
        if(!force&&File.Exists(path)) { var saved=Json.Read<Analysis>(File.ReadAllText(path)); CopilotAnalyzer.Validate(saved,request); return saved; }
        var analysis=await analyzer.Analyze(request,image,ct); CopilotAnalyzer.Validate(analysis,request); Json.Write(path,analysis); return analysis;
    }
    private static IEnumerable<Unit[]> Batches(Unit[] units,int max)
    {
        var batch=new List<Unit>(); var size=0;
        foreach(var unit in units) { if(size+Json.Serialize(unit).Length>max&&batch.Count>0){yield return batch.ToArray();batch.Clear();size=0;} batch.Add(unit);size+=Json.Serialize(unit).Length; }
        if(batch.Count>0)yield return batch.ToArray();
    }
    private static Claim? Apply(Knowledge k,Finding finding,SourceRole role,Dictionary<Contribution,int> counts,SourceContext context)
    {
        if(finding.Contribution==Contribution.None){counts[Contribution.None]++;return null;}
        var source=k.Evidence.Single(e=>e.Id==finding.EvidenceIds[0]).SourceId;
        var identity=source+":"+context.Scope+":"+finding.Kind.ToLowerInvariant()+":"+(finding.QualifiedName??finding.Entity).Trim().ToLowerInvariant();
        var entity=k.Entities.SingleOrDefault(e=>e.IdentityKey==identity);
        var entityId=entity?.Id??Json.Hash(identity);
        if(entity==null)k.Entities.Add(new(entityId,finding.QualifiedName??finding.Entity,finding.Kind,context.Project,context.Domain,[],identity));
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
            k.Relations.RemoveAll(r=>r.Id==relation.Id);k.Relations.Add(relation);
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
