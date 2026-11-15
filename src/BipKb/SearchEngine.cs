using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace BipKb;

public sealed class SearchEngine(Workspace workspace)
{
    private string PathName=>Path.Combine(workspace.Local,"search-v5.db");
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(workspace.Local);
        var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=PathName,Pooling=false,DefaultTimeout=20}.ToString());db.Open();return db;
    }
    public void Refresh(bool force=false,Knowledge? supplied=null)
    {
        var revision=workspace.SemanticRevision;
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY,value TEXT); CREATE TABLE IF NOT EXISTS docs(id TEXT PRIMARY KEY,hash TEXT,kind TEXT,project TEXT,domain TEXT,role TEXT,historical INTEGER,state TEXT,body TEXT,title TEXT); CREATE INDEX IF NOT EXISTS doc_title ON docs(title COLLATE NOCASE); CREATE INDEX IF NOT EXISTS doc_scope ON docs(project,domain); CREATE VIRTUAL TABLE IF NOT EXISTS search USING fts5(id UNINDEXED,title,text,tokenize='unicode61 remove_diacritics 2'); CREATE TABLE IF NOT EXISTS edges(src TEXT,dst TEXT,kind TEXT,PRIMARY KEY(src,dst,kind)); CREATE INDEX IF NOT EXISTS incoming ON edges(dst); CREATE TABLE IF NOT EXISTS profiles(id TEXT PRIMARY KEY,name TEXT,body TEXT);";cmd.ExecuteNonQuery();
        cmd.CommandText="SELECT value FROM meta WHERE key='revision'";
        if(!force&&(string?)cmd.ExecuteScalar()==revision)return;
        using var gate=new FileStream(Path.Combine(workspace.Local,"search.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        // Read a consistent published revision, never partially staged ingestion work.
        Knowledge k;List<Decision> decisions;string after;
        do{revision=workspace.SemanticRevision;k=supplied??workspace.Load();decisions=workspace.Decisions();after=workspace.SemanticRevision;}while(supplied==null&&after!=revision);
        var claimsByEntity=k.Claims.GroupBy(c=>c.EntityId).ToDictionary(g=>g.Key,g=>g.ToArray());
        var entities=k.Entities.ToDictionary(e=>e.Id);var evidence=k.Evidence.ToDictionary(e=>e.Id);var activeEvidence=k.Sources.Where(s=>s.Active).SelectMany(s=>s.EvidenceIds).ToHashSet();
        var sourceByEvidence=k.Sources.SelectMany(s=>s.EvidenceIds.Select(id=>(id,s))).GroupBy(x=>x.id).ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>x.s.Active).First().s);
        var effective=decisions.GroupBy(d=>d.ClaimId).ToDictionary(g=>g.Key,g=>Workspace.Effective(g,g.Key));
        var distinct=effective.Where(p=>p.Value.Count>0&&p.Value.All(d=>d.Action=="distinct")).Select(p=>p.Key).ToHashSet();
        var replaced=k.Claims.Where(c=>!c.Obsolete&&effective.TryGetValue(c.Id,out var ds)&&ds.Select(d=>d.Action).Distinct().Count()==1&&ds[0].Action=="replace").SelectMany(c=>c.RelatedClaimIds).ToHashSet();
        var docs=new List<SearchHit>();
        foreach(var c in k.Claims)
        {
            var state=c.Obsolete?KnowledgeState.Reexamine:c.State;
            if(effective.TryGetValue(c.Id,out var ds)){state=ds.Select(d=>d.Action).Distinct().Count()>1?KnowledgeState.Reexamine:ds[0].Action=="reject"?KnowledgeState.Rejected:KnowledgeState.Accepted;}
            if(effective.TryGetValue(c.Id,out var reviewed)&&reviewed.Any(d=>d.EvidenceFingerprint!=Json.Hash(string.Join("",c.EvidenceIds.Where(activeEvidence.Contains).Order()))))state=KnowledgeState.Reexamine;
            if(replaced.Contains(c.Id)&&state!=KnowledgeState.Reexamine)state=KnowledgeState.Superseded;
            docs.Add(new(c.Id,"claim",entities[c.EntityId].Name,c.Statement,c.Context?.Project,c.Context?.Domain,c.Role,c.EvidenceIds,0,[],state,c.Obsolete,c.Origin,c.Context?.Status,c.Conditions));
        }
        foreach(var e in k.Evidence)
        {
            sourceByEvidence.TryGetValue(e.Id,out var source);
            docs.Add(new(e.Id,"evidence",e.RelativePath+" "+e.Locator,e.Excerpt,source?.Context?.Project,source?.Context?.Domain,source?.Role,[e.Id],0,["source:"+e.SourceId],KnowledgeState.Proposed,!activeEvidence.Contains(e.Id),SourceStatus:source?.Context?.Status));
        }
        foreach(var e in k.Entities)
        {
            var related=claimsByEntity.GetValueOrDefault(e.Id)??[];var context=related.Select(c=>c.Context).FirstOrDefault(c=>c!=null);
            docs.Add(new(e.Id,"entity",e.Name,string.Join(" ",e.Aliases??[]),e.Project??context?.Project,e.Domain??context?.Domain,null,related.SelectMany(c=>c.EvidenceIds).Distinct().Take(20).ToArray(),0,[],KnowledgeState.Proposed,related.Length>0&&related.All(c=>c.Obsolete)));
        }
        foreach(var o in k.Objects)docs.Add(new(o.Id,o.Kind,o.Name,o.Parent??"",o.Project,o.Domain,o.EvidenceIds.Select(id=>sourceByEvidence.GetValueOrDefault(id)).FirstOrDefault(s=>s?.Active==true)?.Role,o.EvidenceIds,0,[],KnowledgeState.Proposed,!o.Active));
        foreach(var declaration in k.Questions.Where(q=>q.Answer!=null))docs.Add(new(declaration.Id,"declaration",declaration.Text,declaration.Answer!,declaration.Project,null,null,declaration.EvidenceIds,0,["Déclaré par "+declaration.Actor],KnowledgeState.Proposed,Origin:KnowledgeOrigin.Declared));
        using var tx=db.BeginTransaction();cmd.Transaction=tx;
        cmd.CommandText="SELECT id,hash FROM docs";var hashes=new Dictionary<string,string>();using(var r=cmd.ExecuteReader()){while(r.Read())hashes[r.GetString(0)]=r.GetString(1);}
        foreach(var d in docs)
        {
            var body=Json.Serialize(d);var hash=Json.Hash(body);
            if(hashes.TryGetValue(d.Id,out var previous)&&previous==hash){hashes.Remove(d.Id);continue;}
            cmd.Parameters.Clear();cmd.CommandText="DELETE FROM search WHERE rowid=(SELECT rowid FROM docs WHERE id=$id); INSERT INTO docs VALUES($id,$hash,$kind,$project,$domain,$role,$old,$state,$body,$title) ON CONFLICT(id) DO UPDATE SET hash=excluded.hash,kind=excluded.kind,project=excluded.project,domain=excluded.domain,role=excluded.role,historical=excluded.historical,state=excluded.state,body=excluded.body,title=excluded.title; INSERT INTO search(rowid,id,title,text) SELECT rowid,$id,$title,$text FROM docs WHERE id=$id";
            cmd.Parameters.AddWithValue("$id",d.Id);cmd.Parameters.AddWithValue("$title",d.Title);cmd.Parameters.AddWithValue("$text",d.Text);cmd.Parameters.AddWithValue("$hash",hash);cmd.Parameters.AddWithValue("$kind",d.Kind);cmd.Parameters.AddWithValue("$project",(object?)d.Project??DBNull.Value);cmd.Parameters.AddWithValue("$domain",(object?)d.Domain??DBNull.Value);cmd.Parameters.AddWithValue("$role",(object?)d.Role?.ToString()??DBNull.Value);cmd.Parameters.AddWithValue("$old",d.Historical?1:0);cmd.Parameters.AddWithValue("$state",d.State.ToString());cmd.Parameters.AddWithValue("$body",body);cmd.ExecuteNonQuery();hashes.Remove(d.Id);
        }
        foreach(var id in hashes.Keys){cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$id",id);cmd.CommandText="DELETE FROM search WHERE rowid=(SELECT rowid FROM docs WHERE id=$id); DELETE FROM docs WHERE id=$id";cmd.ExecuteNonQuery();}
        cmd.Parameters.Clear();cmd.CommandText="DELETE FROM edges; DELETE FROM profiles;";cmd.ExecuteNonQuery();
        void Edge(string from,string to,string kind){cmd.Parameters.Clear();cmd.CommandText="INSERT OR IGNORE INTO edges VALUES($a,$b,$k)";cmd.Parameters.AddWithValue("$a",from);cmd.Parameters.AddWithValue("$b",to);cmd.Parameters.AddWithValue("$k",kind);cmd.ExecuteNonQuery();}
        foreach(var c in k.Claims){Edge(c.EntityId,c.Id,"assertion");foreach(var id in c.EvidenceIds)Edge(c.Id,id,"proof");foreach(var id in c.RelatedClaimIds.Where(_=>!distinct.Contains(c.Id)))Edge(c.Id,id,c.Contribution.ToString());}
        foreach(var o in k.Objects)foreach(var id in o.EvidenceIds)Edge(o.Id,id,"proof");
        foreach(var r in k.Relations.Where(r=>r.State!=KnowledgeState.Rejected&&!distinct.Contains(r.From)&&r.EvidenceIds.Any(activeEvidence.Contains)))Edge(r.From,r.To,r.Kind.ToString());
        foreach(var p in k.Projects){cmd.Parameters.Clear();cmd.CommandText="INSERT INTO profiles VALUES($id,$name,$body)";cmd.Parameters.AddWithValue("$id",p.Id);cmd.Parameters.AddWithValue("$name",p.Name);cmd.Parameters.AddWithValue("$body",Json.Serialize(p));cmd.ExecuteNonQuery();}
        cmd.Parameters.Clear();cmd.CommandText="INSERT OR REPLACE INTO meta VALUES('revision',$r)";cmd.Parameters.AddWithValue("$r",revision);cmd.ExecuteNonQuery();tx.Commit();
    }
    public static string Normalize(string s)=>string.Concat(s.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
    public static string[] Terms(string text)
    {
        var split=Regex.Replace(text,"([a-z])([A-Z])","$1 $2");
        var stop=new HashSet<string>{"les","des","une","pour","dans","avec","comment","quelles","quel","est","que","qui","sur","the","and","from","this"};
        var words=Regex.Matches(Normalize(split),@"[\p{L}\p{N}_]+").Select(m=>m.Value).Where(t=>t.Length>1&&!stop.Contains(t)).Distinct().Take(24).ToArray();
        // Small, explicit inflection expansion; this is lexical retrieval, not semantic equivalence.
        return words.SelectMany(t=>new[]{t,t.Length>4&&t.EndsWith('s')?t[..^1]:t}).SelectMany(t=>new[]{t,t.Length>5&&t.EndsWith("ee")?t[..^1]:t}).Distinct().Take(48).ToArray();
    }
    public SearchResponse Search(SearchQuery query)
    {
        if(query.Text.Length>4000||query.Limit is <1 or >100||query.Depth is <0 or >3)throw new ArgumentException("Search limits: 4000 characters, 1..100 results, graph depth 0..3.");
        var timer=Stopwatch.StartNew();Refresh();using var db=Open();using var transaction=db.BeginTransaction();using var cmd=db.CreateCommand();cmd.Transaction=transaction;
        cmd.CommandText="SELECT body FROM profiles";var profiles=new List<ProjectProfile>();using(var r=cmd.ExecuteReader()){while(r.Read())profiles.Add(Json.Read<ProjectProfile>(r.GetString(0)));}
        var profile=query.Project==null?(profiles.Count==1?profiles[0]:null):profiles.SingleOrDefault(p=>p.Id==query.Project||p.Name.Equals(query.Project,StringComparison.OrdinalIgnoreCase));
        var project=profile?.Name??query.Project;
        if(query.Project==null){cmd.CommandText="SELECT DISTINCT project FROM docs WHERE project IS NOT NULL";var names=new List<string>();using(var r=cmd.ExecuteReader()){while(r.Read())names.Add(r.GetString(0));}names=names.Concat(profiles.Select(p=>p.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();if(names.Count>1)throw new ArgumentException("Several projects are indexed; select --project.");project=names.SingleOrDefault();}
        var expanded=query.Text;
        if(profile!=null)foreach(var term in profile.Glossary)
            if((term.Domain==null||query.Domain==null||term.Domain==query.Domain)&&term.Aliases.Append(term.Term).Any(a=>Normalize(query.Text).Contains(Normalize(a))))expanded+=" "+term.Term+" "+string.Join(" ",term.Aliases);
        var terms=Terms(expanded);var hits=new Dictionary<string,SearchHit>();var allowed=query.Domain==null?null:Projects.Related(profile,query.Domain,query.Depth);
        bool Allowed(SearchHit h)=> (project==null||h.Project?.Equals(project,StringComparison.OrdinalIgnoreCase)==true)&&(query.IncludeHistorical||(!h.Historical&&h.State is not KnowledgeState.Rejected and not KnowledgeState.Superseded))&&(query.Role==null||h.Role==query.Role||h.Kind=="declaration")&&(allowed==null||h.Kind=="declaration"||h.Domain!=null&&allowed.Contains(h.Domain));
        void Add(SearchHit h,double score,string reason)
        {
            if(!Allowed(h))return;
            if(query.Domain!=null&&h.Domain==query.Domain)score+=3;
            if(h.State==KnowledgeState.Accepted)score+=1;
            if(h.State==KnowledgeState.Reexamine)reason+="; requires review";
            if(profile!=null&&profile.Authorities.Any(a=>(a.Topic==query.Intent||a.Topic=="general")&&h.EvidenceIds.Any(id=>AuthorityEvidence(id,a.SourceId)))){score+=3;reason+="; scoped authority";}
            if(hits.TryGetValue(h.Id,out var previous)){hits[h.Id]=previous with{Score=Math.Max(previous.Score,score),Reasons=previous.Reasons.Append(reason).Distinct().ToArray()};}
            else hits[h.Id]=h with{Score=score,Reasons=h.Reasons.Where(r=>!r.StartsWith("source:")).Append(reason).ToArray()};
        }
        // Authority checks use indexed source labels, not the entire corpus.
        bool AuthorityEvidence(string id,string source){using var c=db.CreateCommand();c.Transaction=transaction;c.CommandText="SELECT body FROM docs WHERE id=$id";c.Parameters.AddWithValue("$id",id);var body=c.ExecuteScalar() as string;return body!=null&&Json.Read<SearchHit>(body).Reasons.Contains("source:"+source);}
        cmd.CommandText="SELECT body FROM docs WHERE (id=$q OR title=$q COLLATE NOCASE) AND ($p IS NULL OR project=$p COLLATE NOCASE) LIMIT 100";cmd.Parameters.AddWithValue("$q",query.Text);cmd.Parameters.AddWithValue("$p",(object?)project??DBNull.Value);
        var candidates=new List<SearchHit>();using(var r=cmd.ExecuteReader()){while(r.Read())candidates.Add(Json.Read<SearchHit>(r.GetString(0)));}foreach(var h in candidates)Add(h,20,"exact identifier or name");
        if(terms.Length>0)
        {
            cmd.Parameters.Clear();cmd.CommandText="SELECT d.body,bm25(search,0,5,1) FROM search JOIN docs d ON d.id=search.id WHERE search MATCH $q AND ($p IS NULL OR d.project=$p COLLATE NOCASE) AND ($old=1 OR (d.historical=0 AND d.state NOT IN ('Rejected','Superseded'))) AND ($domains IS NULL OR d.kind='declaration' OR d.domain IN (SELECT value FROM json_each($domains))) AND ($role IS NULL OR d.role=$role OR d.kind='declaration') ORDER BY bm25(search,0,5,1) LIMIT 200";
            cmd.Parameters.AddWithValue("$q",string.Join(" OR ",terms.Select(t=>"\""+t+"\"")));cmd.Parameters.AddWithValue("$p",(object?)project??DBNull.Value);cmd.Parameters.AddWithValue("$old",query.IncludeHistorical?1:0);cmd.Parameters.AddWithValue("$domains",allowed==null?DBNull.Value:Json.Serialize(allowed));cmd.Parameters.AddWithValue("$role",(object?)query.Role?.ToString()??DBNull.Value);
            candidates.Clear();using(var r=cmd.ExecuteReader()){while(r.Read())candidates.Add(Json.Read<SearchHit>(r.GetString(0)));}
            var rank=0;foreach(var h in candidates)Add(h,10.0/(1+rank++*.05),"lexical / alias match");
        }
        var visited=new HashSet<string>();
        for(var depth=0;depth<query.Depth;depth++)
        {
            var frontier=hits.Values.OrderByDescending(h=>h.Score).Where(h=>!visited.Contains(h.Id)).Take(30).ToArray();
            foreach(var h in frontier)
            {
                visited.Add(h.Id);cmd.Parameters.Clear();cmd.CommandText="SELECT d.body,e.kind FROM edges e JOIN docs d ON d.id=CASE WHEN e.src=$id THEN e.dst ELSE e.src END WHERE e.src=$id OR e.dst=$id LIMIT 30";cmd.Parameters.AddWithValue("$id",h.Id);
                var adjacent=new List<(SearchHit Hit,string Kind)>();using(var r=cmd.ExecuteReader()){while(r.Read())adjacent.Add((Json.Read<SearchHit>(r.GetString(0)),r.GetString(1)));}
                foreach(var n in adjacent)Add(n.Hit,Math.Max(1,h.Score-4-depth),$"{n.Kind} via {h.Id}");
            }
        }
        // Reserve room for both primary evidence and structured knowledge; identical text is not independent corroboration.
        var sorted=hits.Values.OrderByDescending(h=>h.Score).ThenBy(h=>h.Id).ToArray();var selected=new List<SearchHit>();var seen=new HashSet<string>();
        foreach(var h in sorted){var key=h.Kind+":"+h.Role+":"+h.Domain+":"+Normalize(h.Text);if(seen.Add(key))selected.Add(h);if(selected.Count==query.Limit)break;}
        if(selected.Count>0&&!selected.Any(h=>h.Kind=="evidence")&&sorted.FirstOrDefault(h=>h.Kind=="evidence") is {} raw)selected[^1]=raw;
        cmd.Parameters.Clear();cmd.CommandText="SELECT value FROM meta WHERE key='revision'";var revision=(string)cmd.ExecuteScalar()!;
        return new(revision,selected.ToArray(),selected.Count==0?["No result does not prove that a rule is absent."]:[],timer.Elapsed.TotalMilliseconds);
    }
}
