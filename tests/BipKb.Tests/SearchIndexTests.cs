using BipKb;
using Microsoft.Data.Sqlite;
using Xunit;

namespace BipKb.Tests;

public class SearchIndexTests
{
    public static Knowledge DenseKnowledge(int size)
    {
        var k=new Knowledge();var ids=Enumerable.Range(0,size).Select(i=>$"proof{i:D5}").ToArray();
        for(var i=0;i<size;i++)
        {
            k.Evidence.Add(new(ids[i],"test","model.cs","v1","line:"+i,"preuveunique"+i));
            k.Objects.Add(new("object"+i,"Objet"+i,"type",i%2==0?ids:ids.Reverse().ToArray(),Project:"iBip",Domain:"Commandes"));
        }
        k.Sources.Add(new("version","test","model.cs","v1","key",SourceRole.Current,ids,[],true,[],new("iBip","Commandes","Modèle",SourceStatus.Observed)));
        return k;
    }
    private static object? Scalar(SearchEngine engine,string sql)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=engine.PathName,Pooling=false}.ToString());db.Open();
        using var command=db.CreateCommand();command.CommandText=sql;return command.ExecuteScalar();
    }
    [Fact]
    public void SharedProofsAreStoredOnceAndNavigableInBothDirectionsWithoutKnowledgeLoss()
    {
        using var s=new Sandbox();var k=DenseKnowledge(1000);
        for(var i=0;i<40;i++)if(i!=37)k.Relations.Add(new("relation"+i,"object37","object"+i,RelationKind.References,["proof00000"]));
        s.Workspace.Save(k);var revision=s.Workspace.SemanticRevision;
        var engine=new SearchEngine(s.Workspace);var messages=new List<string>();engine.Refresh(progress:messages.Add);
        Assert.Equal(1000L,Scalar(engine,"SELECT count(*) FROM proof_members"));
        Assert.Equal(1000L,Scalar(engine,"SELECT count(*) FROM node_proofs"));
        Assert.Equal(1L,Scalar(engine,"SELECT count(DISTINCT set_id) FROM node_proofs"));
        Assert.Equal(39L,Scalar(engine,"SELECT count(*) FROM edges"));
        var outward=engine.Search(new("Objet37",Depth:1));Assert.Contains(outward.Hits,h=>h.Kind=="evidence");
        var objectHit=outward.Hits.First(h=>h.Id=="object37");Assert.Equal(32,objectHit.EvidenceIds.Length);Assert.Contains(objectHit.Reasons,r=>r.Contains("1000"));
        // This proof is outside the 32 projected references; the complete graph must still resolve it.
        var inward=engine.Search(new("proof00999",Depth:1));Assert.Contains(inward.Hits,h=>h.Kind=="type"&&h.Reasons.Any(r=>r.Contains("proof via proof00999")));
        Assert.Equal(revision,s.Workspace.SemanticRevision);Assert.All(s.Workspace.Load().Objects,o=>Assert.Equal(1000,o.EvidenceIds.Length));
        Assert.Contains(messages,m=>m.Contains("documents 2000/2000"));Assert.Contains(messages,m=>m=="Index local terminé");
    }
    [Fact]
    public void CancelledRebuildPreservesPreviousCommittedIndexAndCanResume()
    {
        using var s=new Sandbox();var k=DenseKnowledge(300);s.Workspace.Save(k);var engine=new SearchEngine(s.Workspace);engine.Refresh();
        var prior=s.Workspace.SemanticRevision;k.Evidence[0]=k.Evidence[0] with{Excerpt="nouvellevaleur"};s.Workspace.Save(k);
        using var cancellation=new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(()=>engine.Refresh(force:true,ct:cancellation.Token,progress:m=>{if(m.Contains("documents 500/"))cancellation.Cancel();}));
        Assert.Equal(prior,Scalar(engine,"SELECT value FROM meta WHERE key='revision'"));
        Assert.DoesNotContain("nouvellevaleur",(string)Scalar(engine,"SELECT body FROM docs WHERE id='proof00000'")!);
        engine.Refresh();Assert.Equal(s.Workspace.SemanticRevision,Scalar(engine,"SELECT value FROM meta WHERE key='revision'"));
        Assert.Contains(engine.Search(new("nouvellevaleur")).Hits,h=>h.Id=="proof00000");
    }
    [Fact]
    public void ConcurrentPublicationRollsBackIndexInsteadOfStampingStaleData()
    {
        using var s=new Sandbox();var k=DenseKnowledge(10);s.Workspace.Save(k);var engine=new SearchEngine(s.Workspace);engine.Refresh();var prior=s.Workspace.SemanticRevision;
        var changed=false;
        Assert.Throws<RevisionConflictException>(()=>engine.Refresh(force:true,progress:m=>
        {
            if(!changed&&m.Contains("documents 0/")){changed=true;k.Evidence[0]=k.Evidence[0] with{Excerpt="concurrent"};s.Workspace.Save(k);}
        }));
        Assert.True(changed);Assert.Equal(prior,Scalar(engine,"SELECT value FROM meta WHERE key='revision'"));
        Assert.Throws<RevisionConflictException>(()=>engine.Refresh(force:true,supplied:k,suppliedRevision:prior));
    }
    [Fact]
    public void PublishedSnapshotCanBeIndexedWithoutRereadingLargeMemoryCache()
    {
        using var s=new Sandbox();var k=DenseKnowledge(10);s.Workspace.Save(k);var revision=s.Workspace.SemanticRevision;
        File.WriteAllText(Path.Combine(s.Workspace.Local,"materialized.json"),"intentionally unreadable JSON");
        var engine=new SearchEngine(s.Workspace);var messages=new List<string>();engine.Refresh(supplied:k,suppliedRevision:revision,progress:messages.Add);
        Assert.Equal(revision,Scalar(engine,"SELECT value FROM meta WHERE key='revision'"));
        Assert.Contains(messages,m=>m.Contains("mémoire déjà chargée"));
        Assert.Equal("intentionally unreadable JSON",File.ReadAllText(Path.Combine(s.Workspace.Local,"materialized.json")));
    }
    [Fact]
    public void ForcedReindexRepairsMissingFullTextRowsDespiteMatchingDocumentHashes()
    {
        using var s=new Sandbox();s.Workspace.Save(DenseKnowledge(10));var engine=new SearchEngine(s.Workspace);engine.Refresh();
        Scalar(engine,"DELETE FROM search WHERE id='proof00000'");
        Assert.Empty(engine.Search(new("preuveunique0",Depth:0)).Hits);
        engine.Refresh(force:true);
        Assert.Contains(engine.Search(new("preuveunique0",Depth:0)).Hits,h=>h.Id=="proof00000");
    }
}
