using Microsoft.Data.Sqlite;

namespace BipKb;

public sealed class Workspace(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string Local => Path.Combine(Root, ".local");
    public string StatePath => Path.Combine(Root, "knowledge.json");
    public Settings Config => Json.Read<Settings>(File.ReadAllText(Path.Combine(Root, "bip.json")));
    public void Init()
    {
        Directory.CreateDirectory(Root);
        if (File.Exists(Path.Combine(Root, "bip.json"))) throw new InvalidOperationException("Workspace already initialized.");
        if (Directory.EnumerateFileSystemEntries(Root).Any()) throw new InvalidOperationException("Use an empty, dedicated knowledge directory.");
        Json.Write(Path.Combine(Root, "bip.json"), new Settings());
        Json.Write(StatePath, new {version=2});
        new MemoryStore(this).Publish(new Knowledge());
        File.WriteAllText(Path.Combine(Root, ".gitignore"), ".local/\n*.tmp\n");
        Directory.CreateDirectory(Local);
    }
    public FileStream Lock()
    {
        Directory.CreateDirectory(Local);
        try { return new FileStream(Path.Combine(Local, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new InvalidOperationException("Another operation is writing this workspace."); }
    }
    public Knowledge Load()
    {
        if (!File.Exists(StatePath)) throw new InvalidOperationException("Run bip-kb init in a dedicated workspace first.");
        var content=File.ReadAllText(StatePath);
        using var document=System.Text.Json.JsonDocument.Parse(content);
        var version=document.RootElement.GetProperty("version").GetInt32();
        var k=version==2?new MemoryStore(this).Load():Json.Read<Knowledge>(content);
        Validate(k); return k;
    }
    public static void Validate(Knowledge k)
    {
        if (k.Version is not 1 and not 2) throw new InvalidDataException("Unsupported knowledge format.");
        static void Unique(IEnumerable<string> ids) { var a=ids.ToArray(); if(a.Distinct().Count()!=a.Length) throw new InvalidDataException("Duplicate identifiers."); }
        Unique(k.Claims.Select(x=>x.Id)); Unique(k.Evidence.Select(x=>x.Id)); Unique(k.Entities.Select(x=>x.Id)); Unique(k.Sources.Select(x=>x.Id));
        Unique(k.Objects.Select(x=>x.Id));Unique(k.Relations.Select(x=>x.Id));Unique(k.Projects.Select(x=>x.Id));Unique(k.Tasks.Select(x=>x.Id));Unique(k.Questions.Select(x=>x.Id));
        var evidence=k.Evidence.Select(x=>x.Id).ToHashSet(); var entities=k.Entities.Select(x=>x.Id).ToHashSet(); var claims=k.Claims.Select(x=>x.Id).ToHashSet();
        if(k.Claims.Any(c=>!entities.Contains(c.EntityId)||c.EvidenceIds.Length==0||c.EvidenceIds.Any(e=>!evidence.Contains(e))||c.RelatedClaimIds.Any(r=>!claims.Contains(r)))) throw new InvalidDataException("Broken knowledge references.");
        var endpoints=entities.Concat(claims).Concat(k.Objects.Select(x=>x.Id)).ToHashSet();
        if(k.Relations.Any(r=>!endpoints.Contains(r.From)||!endpoints.Contains(r.To)||r.EvidenceIds.Any(e=>!evidence.Contains(e))))throw new InvalidDataException("Broken relation references.");
        if(k.Objects.Any(o=>o.EvidenceIds.Any(e=>!evidence.Contains(e))))throw new InvalidDataException("Broken technical object evidence.");
        if(k.Sources.Any(s=>s.EvidenceIds.Any(e=>!evidence.Contains(e))||s.ClaimIds.Any(c=>!claims.Contains(c)))) throw new InvalidDataException("Broken source references.");
        foreach(var source in k.Sources.Where(s=>s.Exploration!=null))
        {
            DocumentExplorer.ValidateSummary(source.Exploration!,source.EvidenceIds.Length);
            if(source.Analyzed!=source.Exploration!.Complete)throw new InvalidDataException("Exploration completeness conflicts with source status.");
        }
        if(k.Sources.Any(s=>Path.IsPathRooted(s.RelativePath)||s.RelativePath.Split('/').Contains(".."))) throw new InvalidDataException("Source paths must be relative.");
        if(k.Sources.Where(s=>s.Active).GroupBy(s=>(s.SourceId,s.RelativePath)).Any(g=>g.Count()>1)) throw new InvalidDataException("Conflicting active source versions; resolve the Git merge.");
    }
    public void Save(Knowledge k)
    {
        Validate(k);
        k = k with { Sources=k.Sources.OrderBy(x=>x.Id).ToList(), Entities=k.Entities.OrderBy(x=>x.Id).ToList(), Evidence=k.Evidence.OrderBy(x=>x.Id).ToList(), Claims=k.Claims.OrderBy(x=>x.Id).ToList(), Reports=k.Reports.OrderBy(x=>x.Id).ToList() };
        if(IsModern)new MemoryStore(this).Publish(k with{Version=2});else Json.Write(StatePath,k);
    }
    public bool IsModern
    {
        get { using var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(StatePath));return doc.RootElement.GetProperty("version").GetInt32()==2; }
    }
    public string Revision => IsModern?new MemoryStore(this).Revision:Json.Hash(File.ReadAllText(StatePath));
    public string SemanticRevision => Json.Hash(Revision+Json.Serialize(Decisions()));
    public void EnsureWritable(){if(!IsModern)throw new InvalidOperationException("Run bip-kb migrate --dry-run, then bip-kb migrate before writing this legacy workspace.");}
    public object Migrate(bool dryRun)
    {
        using var gate=Lock();
        if(IsModern)return new{status="already migrated",revision=Revision};
        var old=Load();var decisions=Decisions();
        if(decisions.Any(d=>!old.Claims.Any(c=>c.Id==d.ClaimId)))throw new InvalidDataException("Orphan decision.");
        var summary=new{status=dryRun?"preview":"migrated",entities=old.Entities.Count,claims=old.Claims.Count,evidence=old.Evidence.Count,unqualified=old.Claims.Count(c=>c.Context==null),decisions=decisions.Count};
        if(dryRun)return summary;
        var backup=Path.Combine(Local,"migration-backup");Directory.CreateDirectory(backup);
        foreach(var file in new[]{StatePath,Path.Combine(Root,"bip.json")})if(!File.Exists(Path.Combine(backup,Path.GetFileName(file))))File.Copy(file,Path.Combine(backup,Path.GetFileName(file)));
        if(Directory.Exists(Path.Combine(Root,"decisions")))foreach(var file in Directory.GetFiles(Path.Combine(Root,"decisions"),"*.json")){var dest=Path.Combine(backup,"decisions",Path.GetFileName(file));Directory.CreateDirectory(Path.GetDirectoryName(dest)!);if(!File.Exists(dest))File.Copy(file,dest);}
        var k=old with{Version=2,Claims=old.Claims.Select(c=>c with{Origin=c.Contribution==Contribution.Hypothesis?KnowledgeOrigin.Inferred:KnowledgeOrigin.Extracted,State=c.Obsolete?KnowledgeState.Reexamine:KnowledgeState.Proposed}).ToList()};
        new MemoryStore(this).Publish(k);var restored=new MemoryStore(this).Load();Validate(restored);
        Json.Write(Path.Combine(backup,"id-map.json"),old.Claims.Select(c=>new{oldId=c.Id,newId=c.Id}).ToArray());
        Json.Write(StatePath,new{version=2});return summary;
    }
    public List<Decision> Decisions()
    {
        var directory=Path.Combine(Root,"decisions");
        var decisions=Directory.Exists(directory)? Directory.GetFiles(directory,"*.json").Order().Select(p=>Json.Read<Decision>(File.ReadAllText(p))).ToList():[];
        var ids=decisions.Select(d=>d.Id).ToHashSet();
        if(decisions.Any(d=>!Actions.Contains(d.Action)||d.Supersedes.Any(s=>s==d.Id||!ids.Contains(s)))) throw new InvalidDataException("Invalid decision or missing predecessor.");
        if(decisions.Select(d=>d.Id).Distinct().Count()!=decisions.Count)throw new InvalidDataException("Duplicate decisions.");
        foreach(var decision in decisions)
        {
            var visited=new HashSet<string>(); var pending=new Stack<string>(decision.Supersedes);
            while(pending.TryPop(out var predecessor))
            {
                if(predecessor==decision.Id)throw new InvalidDataException("Cyclic decisions.");
                if(!visited.Add(predecessor))continue;
                var previous=decisions.Single(d=>d.Id==predecessor);
                if(previous.ClaimId!=decision.ClaimId)throw new InvalidDataException("Decision supersedes an unrelated claim.");
                foreach(var id in previous.Supersedes)pending.Push(id);
            }
        }
        return decisions;
    }
    public static readonly string[] Actions=["accept","reject","distinct","replace"];
    public static List<Decision> Effective(IEnumerable<Decision> all, string claim)
    {
        var a=all.ToArray(); var superseded=a.SelectMany(x=>x.Supersedes).ToHashSet();
        return a.Where(x=>x.ClaimId==claim&&!superseded.Contains(x.Id)).ToList();
    }
    public static bool Conflict(IEnumerable<Decision> decisions,string claim) => Effective(decisions,claim).Select(d=>d.Action).Distinct().Count()>1;
    public Decision Decide(string claimId,string action,string reason,string actor)
    {
        using var gate=Lock(); EnsureWritable();var k=Load();
        var claim=k.Claims.SingleOrDefault(c=>c.Id==claimId) ?? throw new ArgumentException("Unknown claim.");
        if(!Actions.Contains(action)||string.IsNullOrWhiteSpace(reason)||string.IsNullOrWhiteSpace(actor)||reason.Length>4000||actor.Length>200) throw new ArgumentException("Action, reason and actor are required.");
        if(action is "distinct" or "replace" && claim.RelatedClaimIds.Length==0) throw new ArgumentException("This action requires a related claim.");
        var d=new Decision(Guid.NewGuid().ToString("N"),claimId,action,reason,actor,Effective(Decisions(),claimId).Select(x=>x.Id).ToArray(),DateTimeOffset.UtcNow, EvidenceFingerprint(k,claim));
        Json.Write(Path.Combine(Root,"decisions",d.Id+".json"),d); return d;
    }
    public IEnumerable<Claim> Review(Knowledge k)
    {
        var decisions=Decisions();
        return k.Claims.Where(c=>Conflict(decisions,c.Id)||((c.NeedsReview||c.Obsolete)&&Effective(decisions,c.Id).Count==0)||Effective(decisions,c.Id).Any(d=>d.EvidenceFingerprint!=EvidenceFingerprint(k,c)));
    }
    public static string EvidenceFingerprint(Knowledge k,Claim c) => Json.Hash(string.Join("", k.Sources.Where(s=>s.Active).SelectMany(s=>s.EvidenceIds).Intersect(c.EvidenceIds).Order()));
    public static bool Rejected(IEnumerable<Decision> ds,string id) => !Conflict(ds,id)&&Effective(ds,id).Any(d=>d.Action=="reject");
    public void Bind(string id,string path,SourceRole? role)
    {
        SourceIdentifier.Validate(id);
        path=Path.GetFullPath(path);
        if(!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        using var gate=Lock(); var config=Config; var existing=config.Sources.SingleOrDefault(s=>s.Id==id);
        if(existing==null&&role==null) throw new ArgumentException("New sources require --role legacy|target|current.");
        if(role==SourceRole.Unknown) throw new ArgumentException("Choose legacy, target or current.");
        config.Sources.RemoveAll(s=>s.Id==id); config.Sources.Add(new(id,role??existing!.Role,existing?.ExcelSheets,existing?.Context,existing?.Overrides));
        Json.Write(Path.Combine(Root,"bip.json"),config with {Sources=config.Sources.OrderBy(s=>s.Id).ToList()});
        var bindings=Bindings(); bindings[id]=path; Json.Write(Path.Combine(Local,"bindings.json"),bindings);
    }
    public void ConfigureSource(string id,string root,string selectedPath,SourceDescriptor descriptor)
    {
        SourceIdentifier.Validate(id);
        descriptor.Validate();
        root=Path.GetFullPath(root);selectedPath=Path.GetFullPath(selectedPath);
        if(!Directory.Exists(root)||!SourceSetup.Under(selectedPath,root))throw new ArgumentException("Chemin hors de la source.");
        using var gate=Lock();var config=Config;var bindings=Bindings();
        if(bindings.TryGetValue(id,out var bound)&&Path.GetFullPath(bound)!=root)throw new ArgumentException("Source déjà liée ailleurs ; utilisez source bind pour la déplacer.");
        var existing=config.Sources.SingleOrDefault(s=>s.Id==id)??new SourceDefinition(id,descriptor.Role);
        var relative=Path.GetRelativePath(root,selectedPath).Replace('\\','/');
        if(relative==".")existing=existing with{Role=descriptor.Role,Context=descriptor.Context};
        else
        {
            var overrides=new SortedDictionary<string,SourceDescriptor>(existing.Overrides??new());
            overrides[relative]=descriptor;existing=existing with{Overrides=overrides};
        }
        config.Sources.RemoveAll(s=>s.Id==id);config.Sources.Add(existing);
        Json.Write(Path.Combine(Root,"bip.json"),config with{Sources=config.Sources.OrderBy(s=>s.Id).ToList()});
        bindings[id]=root;Json.Write(Path.Combine(Local,"bindings.json"),bindings);
    }
    public SortedDictionary<string,string> Bindings() => File.Exists(Path.Combine(Local,"bindings.json"))?Json.Read<SortedDictionary<string,string>>(File.ReadAllText(Path.Combine(Local,"bindings.json"))):new();
    public void Rebuild(Knowledge? knowledge=null) => new SearchEngine(this).Refresh(true,knowledge);
    public List<Claim> Search(string query,int limit=30) => Query(query,limit).Claims;
    public (Knowledge Knowledge,List<Claim> Claims) Query(string query,int limit=30)
    {
        var k=Load();var hits=new SearchEngine(this).Search(new(query,Limit:limit));var ids=hits.Hits.Select(h=>h.Id).ToHashSet();
        return (k,k.Claims.Where(c=>ids.Contains(c.Id)).ToList());
    }
}
