using System.Text.Json;
using BipKb;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BipKb.Tests;

public class McpIndexTests
{
    private static Knowledge Data(int size=100)
    {
        var k=SearchIndexTests.DenseKnowledge(size);
        k.Entities.Add(new("entity","Commande","concept","iBip","Commandes"));
        k.Claims.Add(new("claim","entity","Commande validée",Contribution.New,["proof00000","proof00001"],[],SourceRole.Current,true,Context:new("iBip","Commandes","Contrats",SourceStatus.Observed)));
        k.Claims.Add(new("related","entity","Exception documentée",Contribution.Complement,["proof00002"],["claim"],SourceRole.Current,false));
        k.Relations.Add(new("relation","object0","entity",RelationKind.MapsTo,["proof00000"]));
        k.Projects.Add(new("project","iBip","Migrer les contrats","API .NET",[],["Un handler par query"],[],[],[]));
        k.Questions.Add(new("question","iBip","Quels droits ?",["proof00000"]));
        k.Tasks.Add(new("task","iBip","Consulter les contrats",DateTimeOffset.UnixEpoch,[],[],[],[]));
        return k;
    }
    private static async Task<JsonElement> Call(Workspace workspace,string name,object args)
    {
        var line=JsonSerializer.Serialize(new{jsonrpc="2.0",id=1,method="tools/call",@params=new{name,arguments=args}})+"\n";
        using var output=new StringWriter();await Mcp.Run(workspace,new StringReader(line),output,default);
        using var doc=JsonDocument.Parse(output.ToString());return doc.RootElement.Clone();
    }
    private static JsonElement Content(JsonElement result)
    {
        Assert.False(result.GetProperty("result").GetProperty("isError").GetBoolean(),result.ToString());
        using var doc=JsonDocument.Parse(result.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!);return doc.RootElement.Clone();
    }
    private static void Sql(Workspace workspace,string sql)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=new SearchEngine(workspace).PathName,Pooling=false}.ToString());db.Open();using var cmd=db.CreateCommand();cmd.CommandText=sql;cmd.ExecuteNonQuery();
    }
    [Fact]
    public async Task AllToolsUsePublishedIndexEvenWithLockedMaterializedAndUnavailablePayloads()
    {
        using var s=new Sandbox();var k=Data();s.Workspace.Save(k);new SearchEngine(s.Workspace).Refresh();
        var cache=Path.Combine(s.Workspace.Local,"materialized.json");
        using var locked=new FileStream(cache,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        var objects=Path.Combine(s.Workspace.Root,"memory","objects");Directory.Move(objects,objects+"-offline");
        var stamp=new FileInfo(new SearchEngine(s.Workspace).PathName).LastWriteTimeUtc;
        foreach(var (name,args) in new (string,object)[]{("search",new{query="preuveunique0"}),("context",new{query="preuveunique0"}),
            ("entity",new{id="entity"}),("evidence",new{id="claim"}),("relations",new{id="claim"}),("project",new{id="iBip"}),
            ("questions",new{id="iBip"}),("task",new{id="task"}),("analysis",new{id="test"})})
        {
            var response=await Call(s.Workspace,name,args);
            Assert.False(response.GetProperty("result").GetProperty("isError").GetBoolean(),response.ToString());
            if(name=="context")Assert.Contains("model.cs",response.ToString());
        }
        Assert.Equal(stamp,new FileInfo(new SearchEngine(s.Workspace).PathName).LastWriteTimeUtc);
    }
    [Fact]
    public async Task MissingOrStaleIndexReturnsActionableErrorWithoutRebuildAndServerKeepsAnswering()
    {
        using var s=new Sandbox();
        var missing=await Call(s.Workspace,"search",new{query="contrat"});
        Assert.True(missing.GetProperty("result").GetProperty("isError").GetBoolean());Assert.Contains("reindex",missing.ToString());Assert.False(File.Exists(new SearchEngine(s.Workspace).PathName));
        var k=Data();s.Workspace.Save(k);new SearchEngine(s.Workspace).Refresh();
        k.Evidence[0]=k.Evidence[0] with{Excerpt="nouveauterm"};s.Workspace.Save(k);
        var stale=await Call(s.Workspace,"search",new{query="preuveunique0"});Assert.Contains("reindex",stale.ToString());
        new SearchEngine(s.Workspace).Refresh();Assert.Contains("nouveauterm",Content(await Call(s.Workspace,"search",new{query="nouveauterm"})).ToString());
    }
    [Fact]
    public async Task ProofPaginationPreservesEveryCanonicalReferenceWithoutQuadraticPayload()
    {
        using var s=new Sandbox();s.Workspace.Save(Data(1000));new SearchEngine(s.Workspace).Refresh();
        using(var index=new McpIndex(s.Workspace))
        {
            using var cmd=index.Command("SELECT count(*) FROM mcp_proofs");Assert.True(Convert.ToInt32(cmd.ExecuteScalar())<1100);
        }
        var entity=Content(await Call(s.Workspace,"entity",new{id="object0",limit=10,offset=900}));
        Assert.Equal(1000,entity.GetProperty("proofs").GetProperty("total").GetInt32());
        Assert.Equal("proof00900",entity.GetProperty("proofs").GetProperty("items")[0].GetString());
        Assert.Equal(910,entity.GetProperty("proofs").GetProperty("nextOffset").GetInt32());
        var last=Content(await Call(s.Workspace,"evidence",new{id="object0",limit=10,offset=995}));
        Assert.Equal(5,last.GetProperty("items").GetArrayLength());Assert.Equal(JsonValueKind.Null,last.GetProperty("nextOffset").ValueKind);
        Assert.Equal("proof00999",last.GetProperty("items")[4].GetProperty("id").GetString());
        Assert.Contains("related",Content(await Call(s.Workspace,"relations",new{id="claim"})).ToString());
    }
    [Fact]
    public async Task DecisionChangesInvalidateIndexAndReindexExposesCurrentStateAndHistory()
    {
        using var s=new Sandbox();s.Workspace.Save(Data());new SearchEngine(s.Workspace).Refresh();
        var decision=s.Workspace.Decide("claim","reject","Mauvaise interprétation","test");
        Assert.Contains("reindex",(await Call(s.Workspace,"search",new{query="Commande"})).ToString());
        new SearchEngine(s.Workspace).Refresh();
        var hits=Content(await Call(s.Workspace,"search",new{query="Commande"}));
        Assert.DoesNotContain(hits.GetProperty("hits").EnumerateArray(),h=>h.GetProperty("id").GetString()=="claim");
        Assert.Contains(decision.Id,Content(await Call(s.Workspace,"relations",new{id="claim"})).ToString());
    }
    [Fact]
    public async Task ExistingSearchIndexCanServeSearchButRequiresMigrationForRecordTools()
    {
        using var s=new Sandbox();s.Workspace.Save(Data());new SearchEngine(s.Workspace).Refresh();Sql(s.Workspace,"DELETE FROM meta WHERE key='mcp-schema'");
        Assert.NotEmpty(Content(await Call(s.Workspace,"search",new{query="Commande"})).GetProperty("hits").EnumerateArray());
        Assert.Contains("reindex",(await Call(s.Workspace,"entity",new{id="entity"})).ToString());
        new SearchEngine(s.Workspace).Refresh();Assert.Contains("Commande",Content(await Call(s.Workspace,"entity",new{id="entity"})).ToString());
    }
    [Theory]
    [InlineData(0,0)] [InlineData(51,0)] [InlineData(20,-1)] [InlineData(20,1000001)]
    public async Task InvalidPaginationIsRejectedBeforeReadingKnowledge(int limit,int offset)
    {
        using var s=new Sandbox();Assert.True((await Call(s.Workspace,"evidence",new{id="proof",limit,offset})).TryGetProperty("error",out _));
    }
    [Fact]
    public async Task HandshakePingAndToolDiscoveryDoNotRequireKnowledgeOrIndex()
    {
        using var s=new Sandbox();File.Delete(s.Workspace.StatePath);
        using var output=new StringWriter();await Mcp.Run(s.Workspace,new StringReader("{\"id\":1,\"method\":\"initialize\"}\n{\"id\":2,\"method\":\"ping\"}\n{\"id\":3,\"method\":\"tools/list\"}\n"),output,default);
        var lines=output.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries);Assert.Equal(3,lines.Length);Assert.All(lines,l=>Assert.Contains("\"result\"",l));
    }
    [Fact]
    public void CancelledReadDoesNotOpenOrCreateIndex()
    {
        using var s=new Sandbox();using var ct=new CancellationTokenSource();ct.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>new McpIndex(s.Workspace,ct.Token));Assert.False(File.Exists(new SearchEngine(s.Workspace).PathName));
    }
    [Fact]
    public void SqlExecutionCanBeInterruptedWithoutHoldingTheServerIndefinitely()
    {
        using var s=new Sandbox();s.Workspace.Save(Data());new SearchEngine(s.Workspace).Refresh();
        using var ct=new CancellationTokenSource();using var index=new McpIndex(s.Workspace,ct.Token);
        using var cmd=index.Command("WITH RECURSIVE count(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM count WHERE x<100000000) SELECT sum(x) FROM count");
        ct.CancelAfter(50);var error=Assert.Throws<SqliteException>(()=>cmd.ExecuteScalar());Assert.Equal(9,error.SqliteErrorCode);
    }
    [Fact]
    public async Task OversizedRecordReturnsToolErrorInsteadOfSendingAnUnboundedResponse()
    {
        using var s=new Sandbox();var k=Data();k.Tasks[0]=k.Tasks[0] with{Goal=new string('x',300000)};
        s.Workspace.Save(k);new SearchEngine(s.Workspace).Refresh();
        var result=await Call(s.Workspace,"task",new{id="task"});Assert.True(result.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("volumineux",result.ToString());Assert.True(result.ToString().Length<2000);
        Assert.NotEmpty(Content(await Call(s.Workspace,"search",new{query="Commande"})).GetProperty("hits").EnumerateArray());
    }
    [Fact]
    public void CancelledReadProjectionBuildRollsBackTogetherWithSearch()
    {
        using var s=new Sandbox();var k=Data(500);s.Workspace.Save(k);var engine=new SearchEngine(s.Workspace);engine.Refresh();
        k.Evidence[0]=k.Evidence[0] with{Excerpt="newcontent"};s.Workspace.Save(k);using var ct=new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(()=>engine.Refresh(force:true,ct:ct.Token,progress:m=>{if(m.StartsWith("Index MCP — 500"))ct.Cancel();}));
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=engine.PathName,Pooling=false}.ToString());db.Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT body FROM mcp_records WHERE kind='evidence' AND id='proof00000'";Assert.DoesNotContain("newcontent",(string)cmd.ExecuteScalar()!);
        cmd.CommandText="SELECT body FROM docs WHERE id='proof00000'";Assert.DoesNotContain("newcontent",(string)cmd.ExecuteScalar()!);
        engine.Refresh();using var index=new McpIndex(s.Workspace);Assert.Equal("newcontent",index.Get<Evidence>("evidence","proof00000")!.Excerpt);
    }
}
