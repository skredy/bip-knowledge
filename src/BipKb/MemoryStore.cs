using System.Text.Json;

namespace BipKb;

public sealed record ChangeSet(string Id, string[] Parents, string PayloadHash, DateTimeOffset At);
public sealed record CachedMemory(string Revision, Knowledge Knowledge);
public sealed record MemoryChange(string Key, JsonElement? Value);
public sealed class RevisionConflictException(string message) : InvalidOperationException(message);

/// <summary>Immutable, content-checked change batches. Publishing a manifest is the commit point.</summary>
public sealed class MemoryStore(Workspace workspace)
{
    public string DirectoryPath => Path.Combine(workspace.Root,"memory");
    private string Manifests => Path.Combine(DirectoryPath,"changes");
    public string Revision => Json.Hash(string.Join("\n",Files().Select(Path.GetFileName)));
    private string[] Files() => Directory.Exists(Manifests)?Directory.GetFiles(Manifests,"*.json").Order(StringComparer.Ordinal).ToArray():[];
    public static SortedDictionary<string,JsonElement> Records(Knowledge k)
    {
        var result=new SortedDictionary<string,JsonElement>(StringComparer.Ordinal);
        void Add<T>(string kind,IEnumerable<T> list,Func<T,string> id){foreach(var x in list)result.Add(kind+":"+id(x),JsonSerializer.SerializeToElement(x,Json.Options));}
        Add("source",k.Sources,x=>x.Id);Add("entity",k.Entities,x=>x.Id);Add("evidence",k.Evidence,x=>x.Id);Add("claim",k.Claims,x=>x.Id);Add("report",k.Reports,x=>x.Id);
        Add("project",k.Projects,x=>x.Id);Add("object",k.Objects,x=>x.Id);Add("relation",k.Relations,x=>x.Id);Add("task",k.Tasks,x=>x.Id);Add("question",k.Questions,x=>x.Id);
        return result;
    }
    private static Knowledge Hydrate(Dictionary<string,JsonElement> values)
    {
        List<T> Read<T>(string kind)=>values.Where(p=>p.Key.StartsWith(kind+":",StringComparison.Ordinal)).OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Value.Deserialize<T>(Json.Options)!).ToList();
        return new(){Sources=Read<SourceVersion>("source"),Entities=Read<Entity>("entity"),Evidence=Read<Evidence>("evidence"),Claims=Read<Claim>("claim"),Reports=Read<IngestionReport>("report"),Projects=Read<ProjectProfile>("project"),Objects=Read<TechnicalObject>("object"),Relations=Read<KnowledgeRelation>("relation"),Tasks=Read<WorkTask>("task"),Questions=Read<OpenQuestion>("question")};
    }
    public Knowledge Load(MemoryChange[]? resolutions=null)
    {
        var revision=Revision;var cache=Path.Combine(workspace.Local,"materialized.json");
        if(resolutions==null&&File.Exists(cache))
        {
            try{var cached=Json.Read<CachedMemory>(File.ReadAllText(cache));if(cached.Revision==revision)return cached.Knowledge;}catch(JsonException){ }
        }
        var changes=Files().Select(f=>{
            var c=Json.Read<ChangeSet>(File.ReadAllText(f));
            if(Path.GetFileNameWithoutExtension(f)!=c.Id||!SafeHash(c.PayloadHash))throw new InvalidDataException("Invalid change manifest.");return c;
        }).ToDictionary(c=>c.Id);
        var ancestors=new Dictionary<string,HashSet<string>>();var visiting=new HashSet<string>();var ordered=new List<ChangeSet>();
        HashSet<string> Visit(string id)
        {
            if(ancestors.TryGetValue(id,out var known))return known;
            if(!changes.TryGetValue(id,out var c)||!visiting.Add(id))throw new InvalidDataException("Missing parent or cyclic change history.");
            var set=new HashSet<string>();foreach(var parent in c.Parents){set.Add(parent);set.UnionWith(Visit(parent));}
            visiting.Remove(id);ancestors[id]=set;ordered.Add(c);return set;
        }
        foreach(var id in changes.Keys.Order(StringComparer.Ordinal))Visit(id);
        // Keep concurrent versions until an explicit later batch resolves all parents.
        var versions=new Dictionary<string,List<(string Batch,JsonElement? Value)>>();
        foreach(var c in ordered)
        {
            var path=Path.Combine(DirectoryPath,"objects",c.PayloadHash+".json");
            if(!File.Exists(path))throw new InvalidDataException("Incomplete published batch: "+c.Id);
            var bytes=File.ReadAllText(path);if(Json.Hash(bytes)!=c.PayloadHash)throw new InvalidDataException("Corrupt batch payload: "+c.Id);
            var mutations=Json.Read<MemoryChange[]>(bytes);
            if(mutations.Select(x=>x.Key).Distinct().Count()!=mutations.Length)throw new InvalidDataException("Duplicate mutation keys.");
            foreach(var m in mutations)
            {
                if(m.Value is {} value&&(!value.TryGetProperty("id",out var id)||m.Key[(m.Key.IndexOf(':')+1)..]!=id.GetString()))throw new InvalidDataException("Record identity does not match its key.");
                if(!versions.TryGetValue(m.Key,out var list))versions[m.Key]=list=[];
                list.RemoveAll(v=>ancestors[c.Id].Contains(v.Batch));list.Add((c.Id,m.Value));
            }
        }
        if(resolutions!=null)foreach(var m in resolutions)versions[m.Key]=[("resolution",m.Value)];
        var allowed=new[]{"source","entity","evidence","claim","report","project","object","relation","task","question"};
        if(versions.Keys.Any(key=>!allowed.Contains(key.Split(':')[0])))throw new InvalidDataException("Unknown record type.");
        var conflicts=versions.Where(p=>p.Value.Select(v=>v.Value?.GetRawText()).Distinct().Count()>1).Select(p=>p.Key).ToArray();
        if(conflicts.Length>0)throw new RevisionConflictException("Concurrent knowledge versions require resolution: "+string.Join(", ",conflicts.Take(10))+". Use memory resolve with an explicit record file.");
        var values=versions.Where(p=>p.Value[0].Value!=null).ToDictionary(p=>p.Key,p=>p.Value[0].Value!.Value);
        var k=Hydrate(values);Workspace.Validate(k);
        // Cache publication does not affect the shared commit graph. Competing readers use identical revisions.
        if(resolutions==null)try{Json.Write(cache,new CachedMemory(revision,k));}catch(IOException){ }
        return k;
    }
    public void Publish(Knowledge k)
    {
        var old=Files().Length==0?new Knowledge():Load();var before=Records(old);var after=Records(k);
        var mutations=after.Where(p=>!before.TryGetValue(p.Key,out var v)||v.GetRawText()!=p.Value.GetRawText()).Select(p=>new MemoryChange(p.Key,p.Value))
            .Concat(before.Keys.Except(after.Keys).Select(key=>new MemoryChange(key,null))).ToArray();
        if(mutations.Length==0&&Files().Length>0)return;
        PublishChanges(mutations);
    }
    public void Resolve(string recordFile)
    {
        var changes=Json.Read<MemoryChange[]>(File.ReadAllText(recordFile));
        if(changes.Length==0)throw new ArgumentException("Provide explicit record resolutions.");
        if(changes.Select(c=>c.Key).Distinct().Count()!=changes.Length)throw new InvalidDataException("Duplicate resolution keys.");
        foreach(var change in changes)if(change.Value is {} value&&(!value.TryGetProperty("id",out var id)||change.Key[(change.Key.IndexOf(':')+1)..]!=id.GetString()))throw new InvalidDataException("Resolution identity does not match its key.");
        _=Load(changes);
        PublishChanges(changes);
    }
    private void PublishChanges(MemoryChange[] mutations)
    {
        var manifests=Files().Select(f=>Json.Read<ChangeSet>(File.ReadAllText(f))).ToArray();var parents=manifests.SelectMany(c=>c.Parents).ToHashSet();
        var heads=manifests.Where(c=>!parents.Contains(c.Id)).Select(c=>c.Id).Order(StringComparer.Ordinal).ToArray();
        var payload=Json.Serialize(mutations)+"\n";var hash=Json.Hash(payload);
        var objectPath=Path.Combine(DirectoryPath,"objects",hash+".json");Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
        if(!File.Exists(objectPath))AtomicFile.Write(objectPath,payload);
        var change=new ChangeSet(Guid.NewGuid().ToString("N"),heads,hash,DateTimeOffset.UtcNow);
        Json.Write(Path.Combine(Manifests,change.Id+".json"),change);
    }
    private static bool SafeHash(string s)=>s.Length==64&&s.All(char.IsAsciiHexDigit);
}
