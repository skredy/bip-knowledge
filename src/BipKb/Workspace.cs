using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

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
        Json.Write(StatePath, new Knowledge());
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
        var k = Json.Read<Knowledge>(File.ReadAllText(StatePath));
        Validate(k); return k;
    }
    public static void Validate(Knowledge k)
    {
        if (k.Version != 1) throw new InvalidDataException("Unsupported knowledge format.");
        static void Unique(IEnumerable<string> ids) { var a=ids.ToArray(); if(a.Distinct().Count()!=a.Length) throw new InvalidDataException("Duplicate identifiers."); }
        Unique(k.Claims.Select(x=>x.Id)); Unique(k.Evidence.Select(x=>x.Id)); Unique(k.Entities.Select(x=>x.Id)); Unique(k.Sources.Select(x=>x.Id));
        var evidence=k.Evidence.Select(x=>x.Id).ToHashSet(); var entities=k.Entities.Select(x=>x.Id).ToHashSet(); var claims=k.Claims.Select(x=>x.Id).ToHashSet();
        if(k.Claims.Any(c=>!entities.Contains(c.EntityId)||c.EvidenceIds.Length==0||c.EvidenceIds.Any(e=>!evidence.Contains(e))||c.RelatedClaimIds.Any(r=>!claims.Contains(r)))) throw new InvalidDataException("Broken knowledge references.");
        if(k.Sources.Any(s=>s.EvidenceIds.Any(e=>!evidence.Contains(e))||s.ClaimIds.Any(c=>!claims.Contains(c)))) throw new InvalidDataException("Broken source references.");
        if(k.Sources.Any(s=>Path.IsPathRooted(s.RelativePath)||s.RelativePath.Split('/').Contains(".."))) throw new InvalidDataException("Source paths must be relative.");
        if(k.Sources.Where(s=>s.Active).GroupBy(s=>(s.SourceId,s.RelativePath)).Any(g=>g.Count()>1)) throw new InvalidDataException("Conflicting active source versions; resolve the Git merge.");
    }
    public void Save(Knowledge k)
    {
        Validate(k);
        k = k with { Sources=k.Sources.OrderBy(x=>x.Id).ToList(), Entities=k.Entities.OrderBy(x=>x.Id).ToList(), Evidence=k.Evidence.OrderBy(x=>x.Id).ToList(), Claims=k.Claims.OrderBy(x=>x.Id).ToList(), Reports=k.Reports.OrderBy(x=>x.Id).ToList() };
        Json.Write(StatePath,k);
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
        using var gate=Lock(); var k=Load();
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
        if(!Regex.IsMatch(id,"^[a-zA-Z0-9_-]+$")) throw new ArgumentException("Source ID: letters, digits, underscore or dash.");
        path=Path.GetFullPath(path);
        if(!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        using var gate=Lock(); var config=Config; var existing=config.Sources.SingleOrDefault(s=>s.Id==id);
        if(existing==null&&role==null) throw new ArgumentException("New sources require --role legacy|target|current.");
        if(role==SourceRole.Unknown) throw new ArgumentException("Choose legacy, target or current.");
        config.Sources.RemoveAll(s=>s.Id==id); config.Sources.Add(new(id,role??existing!.Role,existing?.ExcelSheets));
        Json.Write(Path.Combine(Root,"bip.json"),config with {Sources=config.Sources.OrderBy(s=>s.Id).ToList()});
        var bindings=Bindings(); bindings[id]=path; Json.Write(Path.Combine(Local,"bindings.json"),bindings);
    }
    public SortedDictionary<string,string> Bindings() => File.Exists(Path.Combine(Local,"bindings.json"))?Json.Read<SortedDictionary<string,string>>(File.ReadAllText(Path.Combine(Local,"bindings.json"))):new();
    public void Rebuild(Knowledge? knowledge=null)
    {
        var k=knowledge??Load(); Directory.CreateDirectory(Local);
        using var db=OpenDb(); using var tx=db.BeginTransaction();
        using var cmd=db.CreateCommand(); cmd.Transaction=tx;
        cmd.CommandText="CREATE VIRTUAL TABLE IF NOT EXISTS claims USING fts5(id UNINDEXED, entity, statement, tokenize='unicode61 remove_diacritics 2'); DELETE FROM claims;"; cmd.ExecuteNonQuery();
        foreach(var c in k.Claims.Where(c=>!c.Obsolete))
        {
            cmd.CommandText="INSERT INTO claims(id,entity,statement) VALUES($id,$entity,$text)"; cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("$id",c.Id); cmd.Parameters.AddWithValue("$entity",k.Entities.Single(e=>e.Id==c.EntityId).Name); cmd.Parameters.AddWithValue("$text",c.Statement); cmd.ExecuteNonQuery();
        }
        tx.Commit(); File.WriteAllText(Path.Combine(Local,"index.hash"),Json.Hash(File.ReadAllText(StatePath)));
    }
    private SqliteConnection OpenDb() { var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(Local,"index.db"),Pooling=false}.ToString()); db.Open(); return db; }
    public List<Claim> Search(string query,int limit=30) => Query(query,limit).Claims;
    public (Knowledge Knowledge,List<Claim> Claims) Query(string query,int limit=30)
    {
        using var gate=Lock(); var k=Load();
        var hash=Path.Combine(Local,"index.hash");
        if(!File.Exists(Path.Combine(Local,"index.db"))||!File.Exists(hash)||File.ReadAllText(hash)!=Json.Hash(File.ReadAllText(StatePath))) Rebuild(k);
        var words=Regex.Matches(query,@"[\p{L}\p{N}_]+").Select(m=>"\""+m.Value+"\"").Distinct().Take(32).ToArray();
        if(words.Length==0) return (k,[]);
        using var db=OpenDb(); using var cmd=db.CreateCommand(); cmd.CommandText="SELECT id FROM claims WHERE claims MATCH $q ORDER BY rank LIMIT $n";
        cmd.Parameters.AddWithValue("$q",string.Join(" OR ",words)); cmd.Parameters.AddWithValue("$n",Math.Clamp(limit,1,100));
        using var reader=cmd.ExecuteReader(); var result=new List<Claim>();
        var decisions=Decisions();
        while(reader.Read()) { var c=k.Claims.Single(c=>c.Id==reader.GetString(0)); if(!Rejected(decisions,c.Id) && !k.Claims.Any(replacement=>!replacement.Obsolete&&replacement.RelatedClaimIds.Contains(c.Id)&&!Conflict(decisions,replacement.Id)&&Effective(decisions,replacement.Id).Any(d=>d.Action=="replace"))) result.Add(c); }
        return (k,result);
    }
}
