using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace BipKb;

public sealed class SearchEngine(Workspace workspace)
{
    public string PathName=>Path.Combine(workspace.Local,"search-v6.db");
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(workspace.Local);
        var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=PathName,Pooling=false,DefaultTimeout=20}.ToString());db.Open();return db;
    }
    public void Refresh(bool force=false,Knowledge? supplied=null,Action<string>? progress=null,CancellationToken ct=default,string? suppliedRevision=null)
    {
        try{RefreshCore(force,supplied,progress,ct,suppliedRevision);}
        catch(SqliteException e) when(e.SqliteErrorCode==9&&ct.IsCancellationRequested){throw new OperationCanceledException("Construction de l'index annulée ; mémoire publiée conservée.",e,ct);}
    }
    private void RefreshCore(bool force,Knowledge? supplied,Action<string>? progress,CancellationToken ct,string? suppliedRevision)
    {
        ct.ThrowIfCancellationRequested();
        progress?.Invoke("Index local — ouverture SQLite");
        var revision=workspace.SemanticRevision;
        using var db=Open();using var cmd=db.CreateCommand();
        using var interrupt=ct.Register(()=>SQLitePCL.raw.sqlite3_interrupt(db.Handle));
        cmd.CommandText="PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-65536; CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY,value TEXT); CREATE TABLE IF NOT EXISTS docs(id TEXT PRIMARY KEY,hash TEXT,kind TEXT,project TEXT,domain TEXT,role TEXT,historical INTEGER,state TEXT,body TEXT,title TEXT); CREATE INDEX IF NOT EXISTS doc_title ON docs(title COLLATE NOCASE); CREATE INDEX IF NOT EXISTS doc_scope ON docs(project,domain); CREATE VIRTUAL TABLE IF NOT EXISTS search USING fts5(id UNINDEXED,title,text,tokenize='unicode61 remove_diacritics 2'); CREATE TABLE IF NOT EXISTS edges(src TEXT,dst TEXT,kind TEXT,PRIMARY KEY(src,dst,kind)); CREATE INDEX IF NOT EXISTS incoming ON edges(dst); CREATE TABLE IF NOT EXISTS proof_members(set_id TEXT,proof_id TEXT,PRIMARY KEY(set_id,proof_id)); CREATE INDEX IF NOT EXISTS proof_incoming ON proof_members(proof_id); CREATE TABLE IF NOT EXISTS node_proofs(node_id TEXT PRIMARY KEY,set_id TEXT); CREATE INDEX IF NOT EXISTS proof_nodes ON node_proofs(set_id); CREATE TABLE IF NOT EXISTS profiles(id TEXT PRIMARY KEY,name TEXT,body TEXT);";cmd.ExecuteNonQuery();
        cmd.CommandText="SELECT value FROM meta WHERE key='revision' AND EXISTS(SELECT 1 FROM meta WHERE key='mcp-schema' AND value='"+McpIndex.Version+"')";
        if(!force&&(string?)cmd.ExecuteScalar()==revision){progress?.Invoke("Index local déjà à jour");return;}
        using var gate=new FileStream(Path.Combine(workspace.Local,"search.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        progress?.Invoke(supplied==null?"Index local — chargement de la mémoire publiée":"Index local — utilisation de la mémoire déjà chargée");
        // A supplied snapshot must never be stamped with a later revision after concurrent publication.
        if(supplied!=null&&suppliedRevision!=null&&revision!=suppliedRevision)throw new RevisionConflictException("La mémoire a changé avant l'indexation ; relancez reindex.");
        Knowledge k;List<Decision> decisions;string after;
        do{ct.ThrowIfCancellationRequested();revision=workspace.SemanticRevision;k=supplied??workspace.Load();decisions=workspace.Decisions();after=workspace.SemanticRevision;}while(supplied==null&&after!=revision);
        if(supplied!=null&&suppliedRevision!=null&&after!=suppliedRevision)throw new RevisionConflictException("La mémoire a changé avant l'indexation ; relancez reindex.");
        ct.ThrowIfCancellationRequested();
        progress?.Invoke($"Index local — préparation : {k.Evidence.Count} preuves, {k.Objects.Count} objets, {k.Relations.Count} relations");
        var claimsByEntity=k.Claims.GroupBy(c=>c.EntityId).ToDictionary(g=>g.Key,g=>g.ToArray());
        var entities=k.Entities.ToDictionary(e=>e.Id);var activeEvidence=k.Sources.Where(s=>s.Active).SelectMany(s=>s.EvidenceIds).ToHashSet();
        var sourceByEvidence=k.Sources.SelectMany(s=>s.EvidenceIds.Select(id=>(id,s))).GroupBy(x=>x.id).ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>x.s.Active).First().s);
        var effective=decisions.GroupBy(d=>d.ClaimId).ToDictionary(g=>g.Key,g=>Workspace.Effective(g,g.Key));
        var distinct=effective.Where(p=>p.Value.Count>0&&p.Value.All(d=>d.Action=="distinct")).Select(p=>p.Key).ToHashSet();
        var replaced=k.Claims.Where(c=>!c.Obsolete&&effective.TryGetValue(c.Id,out var ds)&&ds.Select(d=>d.Action).Distinct().Count()==1&&ds[0].Action=="replace").SelectMany(c=>c.RelatedClaimIds).ToHashSet();
        var docs=new List<SearchHit>();
        foreach(var c in k.Claims)
        {
            ct.ThrowIfCancellationRequested();
            var state=c.Obsolete?KnowledgeState.Reexamine:c.State;
            if(effective.TryGetValue(c.Id,out var ds)){state=ds.Select(d=>d.Action).Distinct().Count()>1?KnowledgeState.Reexamine:ds[0].Action=="reject"?KnowledgeState.Rejected:KnowledgeState.Accepted;}
            if(effective.TryGetValue(c.Id,out var reviewed)&&reviewed.Any(d=>d.EvidenceFingerprint!=Json.Hash(string.Join("",c.EvidenceIds.Where(activeEvidence.Contains).Order()))))state=KnowledgeState.Reexamine;
            if(replaced.Contains(c.Id)&&state!=KnowledgeState.Reexamine)state=KnowledgeState.Superseded;
            docs.Add(new(c.Id,"claim",entities[c.EntityId].Name,c.Statement,c.Context?.Project,c.Context?.Domain,c.Role,c.EvidenceIds,0,[],state,c.Obsolete,c.Origin,c.Context?.Status,c.Conditions));
        }
        foreach(var e in k.Evidence)
        {
            ct.ThrowIfCancellationRequested();
            sourceByEvidence.TryGetValue(e.Id,out var source);
            docs.Add(new(e.Id,"evidence",e.RelativePath+" "+e.Locator,e.Excerpt,source?.Context?.Project,source?.Context?.Domain,source?.Role,[e.Id],0,["source:"+e.SourceId],KnowledgeState.Proposed,!activeEvidence.Contains(e.Id),SourceStatus:source?.Context?.Status));
        }
        foreach(var e in k.Entities)
        {
            ct.ThrowIfCancellationRequested();
            var related=claimsByEntity.GetValueOrDefault(e.Id)??[];var context=related.Select(c=>c.Context).FirstOrDefault(c=>c!=null);
            docs.Add(new(e.Id,"entity",e.Name,string.Join(" ",e.Aliases??[]),e.Project??context?.Project,e.Domain??context?.Domain,null,related.SelectMany(c=>c.EvidenceIds).Distinct().Take(20).ToArray(),0,[],KnowledgeState.Proposed,related.Length>0&&related.All(c=>c.Obsolete)));
        }
        foreach(var o in k.Objects)
        {
            ct.ThrowIfCancellationRequested();
            var sampled=o.EvidenceIds.Take(32).ToArray();
            var reasons=o.EvidenceIds.Length>32?new[]{$"32 références affichées sur {o.EvidenceIds.Length} ; ensemble complet conservé dans le graphe et la mémoire"}:Array.Empty<string>();
            docs.Add(new(o.Id,o.Kind,o.Name,o.Parent??"",o.Project,o.Domain,o.EvidenceIds.Select(id=>sourceByEvidence.GetValueOrDefault(id)).FirstOrDefault(s=>s?.Active==true)?.Role,sampled,0,reasons,KnowledgeState.Proposed,!o.Active));
        }
        foreach(var declaration in k.Questions.Where(q=>q.Answer!=null))docs.Add(new(declaration.Id,"declaration",declaration.Text,declaration.Answer!,declaration.Project,null,null,declaration.EvidenceIds,0,["Déclaré par "+declaration.Actor],KnowledgeState.Proposed,Origin:KnowledgeOrigin.Declared));
        using var tx=db.BeginTransaction();cmd.Transaction=tx;
        if(force)
        {
            ct.ThrowIfCancellationRequested();progress?.Invoke("Index local — reconstruction complète des documents et du texte intégral");
            // A rebuild must repair a missing FTS row even when the document hash still matches.
            // These deletes roll back with the rest of the transaction on cancellation/failure.
            cmd.CommandText="DELETE FROM search; DELETE FROM docs;";cmd.ExecuteNonQuery();
        }
        SqliteCommand Prepared(string sql,params string[] names)
        {
            var c=db.CreateCommand();c.Transaction=tx;c.CommandText=sql;
            foreach(var name in names)c.Parameters.Add(new SqliteParameter(name,SqliteType.Text));
            c.Prepare();return c;
        }
        using var deleteSearch=Prepared("DELETE FROM search WHERE rowid=(SELECT rowid FROM docs WHERE id=$id)","$id");
        using var writeDoc=Prepared("INSERT INTO docs VALUES($id,$hash,$kind,$project,$domain,$role,$old,$state,$body,$title) ON CONFLICT(id) DO UPDATE SET hash=excluded.hash,kind=excluded.kind,project=excluded.project,domain=excluded.domain,role=excluded.role,historical=excluded.historical,state=excluded.state,body=excluded.body,title=excluded.title","$id","$hash","$kind","$project","$domain","$role","$old","$state","$body","$title");
        using var writeSearch=Prepared("INSERT INTO search(rowid,id,title,text) SELECT rowid,$id,$title,$text FROM docs WHERE id=$id","$id","$title","$text");
        using var deleteDoc=Prepared("DELETE FROM docs WHERE id=$id","$id");
        cmd.CommandText="SELECT id,hash FROM docs";var hashes=new Dictionary<string,string>();using(var r=cmd.ExecuteReader()){while(r.Read()){ct.ThrowIfCancellationRequested();hashes[r.GetString(0)]=r.GetString(1);}}
        var processed=0;
        foreach(var d in docs)
        {
            ct.ThrowIfCancellationRequested();
            if(processed%500==0)progress?.Invoke($"Index local — documents {processed}/{docs.Count}");
            processed++;
            var body=Json.Serialize(d);var hash=Json.Hash(body);
            if(hashes.TryGetValue(d.Id,out var previous)&&previous==hash){hashes.Remove(d.Id);continue;}
            if(previous!=null){deleteSearch.Parameters["$id"].Value=d.Id;deleteSearch.ExecuteNonQuery();}
            writeDoc.Parameters["$id"].Value=d.Id;writeDoc.Parameters["$hash"].Value=hash;writeDoc.Parameters["$kind"].Value=d.Kind;
            writeDoc.Parameters["$project"].Value=(object?)d.Project??DBNull.Value;writeDoc.Parameters["$domain"].Value=(object?)d.Domain??DBNull.Value;
            writeDoc.Parameters["$role"].Value=(object?)d.Role?.ToString()??DBNull.Value;writeDoc.Parameters["$old"].Value=d.Historical?"1":"0";
            writeDoc.Parameters["$state"].Value=d.State.ToString();writeDoc.Parameters["$body"].Value=body;writeDoc.Parameters["$title"].Value=d.Title;writeDoc.ExecuteNonQuery();
            writeSearch.Parameters["$id"].Value=d.Id;writeSearch.Parameters["$title"].Value=d.Title;writeSearch.Parameters["$text"].Value=d.Text;writeSearch.ExecuteNonQuery();hashes.Remove(d.Id);
        }
        progress?.Invoke($"Index local — documents {processed}/{docs.Count} ; retrait des entrées obsolètes");
        foreach(var id in hashes.Keys){ct.ThrowIfCancellationRequested();deleteSearch.Parameters["$id"].Value=id;deleteSearch.ExecuteNonQuery();deleteDoc.Parameters["$id"].Value=id;deleteDoc.ExecuteNonQuery();}
        cmd.CommandText="DELETE FROM edges; DELETE FROM node_proofs; DELETE FROM proof_members; DELETE FROM profiles;";cmd.ExecuteNonQuery();
        using var edge=Prepared("INSERT OR IGNORE INTO edges VALUES($a,$b,$k)","$a","$b","$k");
        long links=0;
        void Edge(string from,string to,string kind)
        {
            ct.ThrowIfCancellationRequested();
            edge.Parameters["$a"].Value=from;edge.Parameters["$b"].Value=to;edge.Parameters["$k"].Value=kind;edge.ExecuteNonQuery();
            if(++links%10000==0)progress?.Invoke($"Index local — {links} relations écrites");
        }
        progress?.Invoke("Index local — écriture des relations");
        foreach(var c in k.Claims){Edge(c.EntityId,c.Id,"assertion");foreach(var id in c.EvidenceIds)Edge(c.Id,id,"proof");foreach(var id in c.RelatedClaimIds.Where(_=>!distinct.Contains(c.Id)))Edge(c.Id,id,c.Contribution.ToString());}
        // Store an identical set of source proofs only once, instead of every object × every file unit.
        using var member=Prepared("INSERT OR IGNORE INTO proof_members VALUES($set,$id)","$set","$id");
        using var node=Prepared("INSERT INTO node_proofs VALUES($id,$set)","$id","$set");
        var sets=new HashSet<string>();long memberships=0;var objects=0;
        foreach(var o in k.Objects)
        {
            ct.ThrowIfCancellationRequested();
            var proofIds=o.EvidenceIds.Distinct().Order(StringComparer.Ordinal).ToArray();
            var set=Json.Hash(string.Join("\n",proofIds));
            if(sets.Add(set))
                foreach(var id in proofIds)
                {
                    ct.ThrowIfCancellationRequested();member.Parameters["$set"].Value=set;member.Parameters["$id"].Value=id;member.ExecuteNonQuery();memberships++;
                    if(memberships%10000==0)progress?.Invoke($"Index local — {memberships} références de preuve, {sets.Count} ensembles partagés");
                }
            node.Parameters["$id"].Value=o.Id;node.Parameters["$set"].Value=set;node.ExecuteNonQuery();
            if(++objects%500==0)progress?.Invoke($"Index local — objets reliés aux preuves {objects}/{k.Objects.Count}, {memberships} références stockées");
        }
        foreach(var r in k.Relations.Where(r=>r.State!=KnowledgeState.Rejected&&!distinct.Contains(r.From)&&r.EvidenceIds.Any(activeEvidence.Contains)))Edge(r.From,r.To,r.Kind.ToString());
        using var profile=Prepared("INSERT INTO profiles VALUES($id,$name,$body)","$id","$name","$body");
        foreach(var p in k.Projects){ct.ThrowIfCancellationRequested();profile.Parameters["$id"].Value=p.Id;profile.Parameters["$name"].Value=p.Name;profile.Parameters["$body"].Value=Json.Serialize(p);profile.ExecuteNonQuery();}
        McpIndex.Build(db,tx,k,decisions,progress,ct);
        ct.ThrowIfCancellationRequested();
        if(workspace.SemanticRevision!=revision)throw new RevisionConflictException("La mémoire a changé pendant l'indexation ; relancez reindex. L'ancien index reste intact.");
        progress?.Invoke($"Index local — validation : {docs.Count} documents, {links} relations, {memberships} références partagées");
        cmd.Parameters.Clear();cmd.CommandText="INSERT OR REPLACE INTO meta VALUES('revision',$r)";cmd.Parameters.AddWithValue("$r",revision);cmd.ExecuteNonQuery();
        ct.ThrowIfCancellationRequested();tx.Commit();
        progress?.Invoke("Index local terminé");
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
    public SearchResponse Search(SearchQuery query,bool readOnly=false,CancellationToken ct=default)
    {
        if(query.Text.Length>4000||query.Limit is <1 or >100||query.Depth is <0 or >3)throw new ArgumentException("Search limits: 4000 characters, 1..100 results, graph depth 0..3.");
        ct.ThrowIfCancellationRequested();var timer=Stopwatch.StartNew();
        if(!readOnly)Refresh(ct:ct);
        using var snapshot=readOnly?new McpIndex(workspace,ct,requireRecords:false):null;
        using var ownedDb=readOnly?null:Open();var db=snapshot?.Db??ownedDb!;
        using var ownedTransaction=readOnly?null:db.BeginTransaction();var transaction=snapshot?.Transaction??ownedTransaction!;
        using var interrupt=ct.Register(()=>SQLitePCL.raw.sqlite3_interrupt(db.Handle));
        using var cmd=db.CreateCommand();cmd.Transaction=transaction;
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
                visited.Add(h.Id);cmd.Parameters.Clear();cmd.CommandText="SELECT body,kind FROM (SELECT d.body,'proof' AS kind FROM node_proofs n JOIN proof_members m ON m.set_id=n.set_id JOIN docs d ON d.id=m.proof_id WHERE n.node_id=$id LIMIT 10) UNION ALL SELECT d.body,e.kind FROM edges e JOIN docs d ON d.id=CASE WHEN e.src=$id THEN e.dst ELSE e.src END WHERE e.src=$id OR e.dst=$id UNION ALL SELECT d.body,'proof' FROM proof_members m JOIN node_proofs n ON n.set_id=m.set_id JOIN docs d ON d.id=n.node_id WHERE m.proof_id=$id LIMIT 30";cmd.Parameters.AddWithValue("$id",h.Id);
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
