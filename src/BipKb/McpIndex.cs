using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace BipKb;

public sealed class McpIndexUnavailableException(string message):InvalidOperationException(message);

/// <summary>Published, transactional read projection. Never opens the materialized knowledge on a read.</summary>
public sealed class McpIndex : IDisposable
{
    public const string Version="2";
    public const string PrepareMessage="Index MCP absent, ancien ou obsolète. Exécutez bip-kb reindex --workspace <chemin-de-votre-base> dans un terminal, puis réessayez. Aucune réingestion ni analyse Copilot nécessaire.";
    public SqliteConnection Db {get;}
    public SqliteTransaction Transaction {get;}
    public string Revision {get;}
    private readonly CancellationTokenRegistration interrupt;
    public McpIndex(Workspace workspace,CancellationToken ct=default,bool requireRecords=true)
    {
        ct.ThrowIfCancellationRequested();
        var path=new SearchEngine(workspace).PathName;
        if(!File.Exists(path))throw new McpIndexUnavailableException(PrepareMessage);
        Db=new(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false,DefaultTimeout=2}.ToString());
        try
        {
            Db.Open();interrupt=ct.Register(()=>SQLitePCL.raw.sqlite3_interrupt(Db.Handle));
            Transaction=Db.BeginTransaction(deferred:true);
            using var cmd=Command("SELECT value FROM meta WHERE key='revision'");
            Revision=cmd.ExecuteScalar() as string??"";
            if(Revision!=workspace.SemanticRevision)throw new McpIndexUnavailableException(PrepareMessage);
            if(requireRecords)
            {
                cmd.CommandText="SELECT value FROM meta WHERE key='mcp-schema'";
                if(cmd.ExecuteScalar() as string!=Version)throw new McpIndexUnavailableException(PrepareMessage);
            }
        }
        catch{interrupt.Dispose();Db.Dispose();throw;}
    }
    public SqliteCommand Command(string sql,params (string Key,object? Value)[] values)
    {
        var cmd=Db.CreateCommand();cmd.Transaction=Transaction;cmd.CommandText=sql;
        foreach(var (key,value) in values)cmd.Parameters.AddWithValue(key,value??DBNull.Value);
        return cmd;
    }
    public void Dispose(){interrupt.Dispose();Transaction.Dispose();Db.Dispose();}

    // Rebuilt in the search transaction, so metadata, records and FTS always describe one revision.
    public static void Build(SqliteConnection db,SqliteTransaction tx,Knowledge k,List<Decision> decisions,Action<string>? progress,CancellationToken ct)
    {
        SqliteCommand Prepared(string sql,params string[] names)
        {
            var c=db.CreateCommand();c.Transaction=tx;c.CommandText=sql;
            foreach(var name in names)c.Parameters.Add(new SqliteParameter(name,SqliteType.Text));
            c.Prepare();return c;
        }
        using var schema=db.CreateCommand();schema.Transaction=tx;
        schema.CommandText="""
CREATE TABLE IF NOT EXISTS mcp_records(kind TEXT,id TEXT,parent TEXT,aux TEXT,project TEXT,body TEXT,PRIMARY KEY(kind,id));
CREATE INDEX IF NOT EXISTS mcp_parent ON mcp_records(kind,parent,id);
CREATE INDEX IF NOT EXISTS mcp_aux ON mcp_records(kind,aux,id);
CREATE INDEX IF NOT EXISTS mcp_project ON mcp_records(kind,project,id);
CREATE TABLE IF NOT EXISTS mcp_refs(kind TEXT,id TEXT,set_id TEXT,PRIMARY KEY(kind,id));
CREATE TABLE IF NOT EXISTS mcp_proofs(set_id TEXT,proof_id TEXT,PRIMARY KEY(set_id,proof_id));
DELETE FROM mcp_records; DELETE FROM mcp_refs; DELETE FROM mcp_proofs;
""";schema.ExecuteNonQuery();
        using var record=Prepared("INSERT INTO mcp_records VALUES($kind,$id,$parent,$aux,$project,$body)","$kind","$id","$parent","$aux","$project","$body");
        using var link=Prepared("INSERT INTO mcp_refs VALUES($kind,$id,$set)","$kind","$id","$set");
        using var member=Prepared("INSERT INTO mcp_proofs VALUES($set,$proof)","$set","$proof");
        var sets=new HashSet<string>();var count=0;
        void Add<T>(string kind,string id,T value,string? parent=null,string? aux=null,string? project=null,string[]? proofs=null)
        {
            ct.ThrowIfCancellationRequested();
            if(count++%500==0)progress?.Invoke($"Index MCP — {count-1} enregistrements préparés");
            record.Parameters["$kind"].Value=kind;record.Parameters["$id"].Value=id;
            record.Parameters["$parent"].Value=(object?)parent??DBNull.Value;record.Parameters["$aux"].Value=(object?)aux??DBNull.Value;
            record.Parameters["$project"].Value=(object?)project??DBNull.Value;record.Parameters["$body"].Value=Json.Serialize(value);record.ExecuteNonQuery();
            if(proofs==null)return;
            var ids=proofs.Distinct().Order(StringComparer.Ordinal).ToArray();var set=Json.Hash(string.Join("\n",ids));
            if(sets.Add(set))foreach(var proof in ids)
            {ct.ThrowIfCancellationRequested();member.Parameters["$set"].Value=set;member.Parameters["$proof"].Value=proof;member.ExecuteNonQuery();}
            link.Parameters["$kind"].Value=kind;link.Parameters["$id"].Value=id;link.Parameters["$set"].Value=set;link.ExecuteNonQuery();
        }
        foreach(var e in k.Evidence)Add("evidence",e.Id,e,parent:e.SourceId,aux:e.RelativePath);
        foreach(var e in k.Entities)Add("entity",e.Id,e,project:e.Project);
        foreach(var c in k.Claims)Add("claim",c.Id,c with{EvidenceIds=[]},parent:c.EntityId,project:c.Context?.Project,proofs:c.EvidenceIds);
        foreach(var o in k.Objects)Add("object",o.Id,o with{EvidenceIds=[]},project:o.Project,proofs:o.EvidenceIds);
        foreach(var r in k.Relations)Add("relation",r.Id,r with{EvidenceIds=[]},parent:r.From,aux:r.To,proofs:r.EvidenceIds);
        foreach(var q in k.Questions)Add("question",q.Id,q with{EvidenceIds=[]},project:q.Project,proofs:q.EvidenceIds);
        foreach(var t in k.Tasks)Add("task",t.Id,t,project:t.Project);
        foreach(var d in decisions)Add("decision",d.Id,d,parent:d.ClaimId);
        foreach(var s in k.Sources.Where(s=>s.Active))Add("source",s.Id,s with{EvidenceIds=[],ClaimIds=[]},parent:s.SourceId,aux:s.RelativePath,project:s.Context?.Project);
        schema.CommandText="INSERT OR REPLACE INTO meta VALUES('mcp-schema',$version)";schema.Parameters.AddWithValue("$version",Version);schema.ExecuteNonQuery();
        progress?.Invoke($"Index MCP — {count} enregistrements, preuves partagées conservées");
    }

    public JsonElement? Get(string kind,string id)
    {
        using var cmd=Command("SELECT substr(body,1,262145) FROM mcp_records WHERE kind=$kind AND id=$id",("$kind",kind),("$id",id));
        return cmd.ExecuteScalar() is string body?Parse(body):null;
    }
    public static JsonElement Parse(string body)
    {
        if(body.Length>262144)throw new InvalidOperationException("Enregistrement trop volumineux pour MCP ; consultez-le dans bip-kb ou précisez la recherche.");
        using var doc=JsonDocument.Parse(body);return doc.RootElement.Clone();
    }
    public T? Get<T>(string kind,string id)=>Get(kind,id) is {} value?value.Deserialize<T>(Json.Options):default;
    public IndexedPage<JsonElement> Records(string kind,string where,int limit,int offset,params (string Key,object? Value)[] values)
    {
        var args=values.Append(("$kind",(object?)kind)).ToArray();
        using var cmd=Command("SELECT count(*) FROM mcp_records WHERE kind=$kind AND ("+where+")",args);
        var total=Convert.ToInt32(cmd.ExecuteScalar());
        cmd.CommandText="SELECT substr(body,1,262145) FROM mcp_records WHERE kind=$kind AND ("+where+") ORDER BY id LIMIT $limit OFFSET $offset";
        cmd.Parameters.AddWithValue("$limit",limit);cmd.Parameters.AddWithValue("$offset",offset);
        var items=new List<JsonElement>();using(var reader=cmd.ExecuteReader())while(reader.Read())items.Add(Parse(reader.GetString(0)));
        return new(items,total,offset,offset+items.Count<total?offset+items.Count:null);
    }
    public IndexedPage<string> ProofIds(string kind,string id,int limit=20,int offset=0)
    {
        using var cmd=Command("SELECT count(*) FROM mcp_refs r JOIN mcp_proofs p ON p.set_id=r.set_id WHERE r.kind=$kind AND r.id=$id",("$kind",kind),("$id",id));
        var total=Convert.ToInt32(cmd.ExecuteScalar());
        cmd.CommandText="SELECT p.proof_id FROM mcp_refs r JOIN mcp_proofs p ON p.set_id=r.set_id WHERE r.kind=$kind AND r.id=$id ORDER BY p.proof_id LIMIT $limit OFFSET $offset";
        cmd.Parameters.AddWithValue("$limit",limit);cmd.Parameters.AddWithValue("$offset",offset);
        var items=new List<string>();using(var reader=cmd.ExecuteReader())while(reader.Read())items.Add(reader.GetString(0));
        return new(items,total,offset,offset+items.Count<total?offset+items.Count:null);
    }
}
public sealed record IndexedPage<T>(IReadOnlyList<T> Items,int Total,int Offset,int? NextOffset);
