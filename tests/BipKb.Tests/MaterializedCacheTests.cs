using BipKb;
using Xunit;

namespace BipKb.Tests;

public class MaterializedCacheTests
{
    private static Dictionary<string,string> Shared(Workspace w)=>Directory.GetFiles(w.Root,"*",SearchOption.AllDirectories)
        .Where(p=>!p.StartsWith(w.Local+Path.DirectorySeparatorChar,StringComparison.Ordinal))
        .ToDictionary(p=>Path.GetRelativePath(w.Root,p),p=>Json.Hash(File.ReadAllText(p)));
    [Fact]
    public void CompactionPreservesExactKnowledgeHistoryAndDecisionsWhileReducingRepeatedReferences()
    {
        using var s=new Sandbox();var k=SearchIndexTests.DenseKnowledge(400);
        k.Entities.Add(new("entity","é 漢字 👋","concept"));
        k.Claims.Add(new("claim","entity","Texte conservé",Contribution.New,["proof00001","proof00000","proof00001"],[],SourceRole.Current,true));
        k.Relations.Add(new("rel","object0","object1",RelationKind.References,k.Objects[0].EvidenceIds));
        k.Sources.Add(k.Sources[0] with{Id="historical",Active=false});
        s.Workspace.Save(k);s.Workspace.Decide("claim","accept","Validé","humain");
        var path=MaterializedCache.PathName(s.Workspace);Json.Write(path,new CachedMemory(s.Workspace.Revision,k));
        var shared=Shared(s.Workspace);var semantic=s.Workspace.SemanticRevision;var fingerprint=MaterializedCache.Fingerprint(k);
        var result=MaterializedCache.Compact(s.Workspace);
        Assert.True(result.Verified);Assert.True(result.CurrentBytes<result.PreviousBytes/5);Assert.Equal(result.PreviousBytes-result.CurrentBytes,result.SavedBytes);
        Assert.Equal(fingerprint,MaterializedCache.Fingerprint(s.Workspace.Load()));Assert.Equal(semantic,s.Workspace.SemanticRevision);
        Assert.Equal(shared.OrderBy(p=>p.Key),Shared(s.Workspace).OrderBy(p=>p.Key));
        Assert.Equal(new[]{"proof00001","proof00000","proof00001"},s.Workspace.Load().Claims[0].EvidenceIds);
        var again=MaterializedCache.Compact(s.Workspace);Assert.Equal(result.CurrentBytes,again.CurrentBytes);Assert.Equal(0,again.SavedBytes);
        // Shared arrays on disk must not introduce mutable aliases between independent domain records.
        var restored=s.Workspace.Load();restored.Objects[0].EvidenceIds[0]="changed";
        Assert.DoesNotContain("changed",restored.Objects[2].EvidenceIds);
    }
    [Fact]
    public void ArrayReferencesPreserveOrderDuplicatesEmptySingleNullAndDistinctLists()
    {
        using var s=new Sandbox();var k=new Knowledge();
        string[][] arrays=[[],["one"],["a","b"],["a","b"],["b","a"],["a","a"],["a",null!],["a",null!]];
        for(var i=0;i<arrays.Length;i++)k.Entities.Add(new("e"+i,"Name","concept",Aliases:arrays[i]));
        var path=s.File("cache.json","");MaterializedCache.Write(path,"revision",k);
        var restored=MaterializedCache.Read(path);Assert.Equal(MaterializedCache.Fingerprint(k),MaterializedCache.Fingerprint(restored.Knowledge));
        Assert.Contains("\"$ref\"",File.ReadAllText(path));Assert.NotSame(restored.Knowledge.Entities[2].Aliases,restored.Knowledge.Entities[3].Aliases);
    }
    [Theory]
    [InlineData("{\"cacheFormat\":999}")]
    [InlineData("{\"cacheFormat\":\"bad\"}")]
    [InlineData("{\"cacheFormat\":2,\"revision\":\"r\",\"knowledge\":{\"entities\":[{\"id\":\"e\",\"aliases\":{\"$ref\":12}}]}}")]
    [InlineData("{\"cacheFormat\":2,\"revision\":\"r\",\"knowledge\":{\"entities\":[{\"id\":\"e\",\"aliases\":{\"$ref\":-1}}]}}")]
    [InlineData("{\"cacheFormat\":2,")]
    public void InvalidLocalCacheIsRebuiltFromPublishedMemory(string broken)
    {
        using var s=new Sandbox();var k=SearchIndexTests.DenseKnowledge(10);s.Workspace.Save(k);
        File.WriteAllText(MaterializedCache.PathName(s.Workspace),broken);
        var loaded=s.Workspace.Load();Assert.Equal(10,loaded.Objects.Count);
        Assert.Equal(s.Workspace.Revision,MaterializedCache.Read(MaterializedCache.PathName(s.Workspace)).Revision);
    }
    [Fact]
    public void RebuildIgnoresCacheButDoesNotDeleteHistoryOrAlterSearchRevision()
    {
        using var s=new Sandbox();var k=SearchIndexTests.DenseKnowledge(10);s.Workspace.Save(k);new SearchEngine(s.Workspace).Refresh();
        var original=s.Workspace.SemanticRevision;var shared=Shared(s.Workspace);
        var fake=k with{Objects=[]};Json.Write(MaterializedCache.PathName(s.Workspace),new CachedMemory(s.Workspace.Revision,fake));
        var result=MaterializedCache.Compact(s.Workspace,rebuild:true);
        Assert.True(result.Rebuilt);Assert.Equal(10,s.Workspace.Load().Objects.Count);
        Assert.Equal(original,s.Workspace.SemanticRevision);Assert.Equal(shared.OrderBy(p=>p.Key),Shared(s.Workspace).OrderBy(p=>p.Key));
        using var index=new McpIndex(s.Workspace);Assert.Equal(original,index.Revision);
    }
    [Fact]
    public void CancelledVerificationKeepsPreviousFileAndCleansTemporaryFile()
    {
        using var s=new Sandbox();var k=SearchIndexTests.DenseKnowledge(30);s.Workspace.Save(k);
        var path=MaterializedCache.PathName(s.Workspace);Json.Write(path,new CachedMemory(s.Workspace.Revision,k));var before=File.ReadAllText(path);
        using var ct=new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(()=>MaterializedCache.Compact(s.Workspace,ct:ct.Token,progress:m=>{if(m.StartsWith("Vérification"))ct.Cancel();}));
        Assert.Equal(before,File.ReadAllText(path));Assert.Empty(Directory.GetFiles(s.Workspace.Local,"*.tmp"));
    }
    [Fact]
    public void ConcurrentPublicationDuringVerificationPreventsReplacement()
    {
        using var s=new Sandbox();var k=SearchIndexTests.DenseKnowledge(10);s.Workspace.Save(k);
        var path=MaterializedCache.PathName(s.Workspace);Json.Write(path,new CachedMemory(s.Workspace.Revision,k));var before=File.ReadAllText(path);
        Assert.Throws<RevisionConflictException>(()=>MaterializedCache.Compact(s.Workspace,progress:m=>
        {if(m.StartsWith("Vérification")){k.Evidence[0]=k.Evidence[0] with{Excerpt="new revision"};s.Workspace.Save(k);}}));
        Assert.Equal(before,File.ReadAllText(path));Assert.Empty(Directory.GetFiles(s.Workspace.Local,"*.tmp"));
    }
    [Fact]
    public void FailedStreamValidationDoesNotReplaceDestination()
    {
        using var s=new Sandbox();var path=s.File("cache.json","original");
        Assert.Throws<InvalidDataException>(()=>AtomicFile.WriteStream(path,stream=>stream.WriteByte(1),_=>throw new InvalidDataException("verification failed")));
        Assert.Equal("original",File.ReadAllText(path));Assert.Empty(Directory.GetFiles(s.Source,"*.tmp"));
    }
    [Fact]
    public void RebuildChecksSharedPayloadIntegrityAndKeepsOldCacheOnFailure()
    {
        using var s=new Sandbox();s.Workspace.Save(SearchIndexTests.DenseKnowledge(10));s.Workspace.Load();
        var path=MaterializedCache.PathName(s.Workspace);var before=File.ReadAllText(path);
        var payload=Directory.GetFiles(Path.Combine(s.Workspace.Root,"memory","objects"),"*.json").First();File.AppendAllText(payload," ");
        Assert.Throws<InvalidDataException>(()=>MaterializedCache.Compact(s.Workspace,rebuild:true));
        Assert.Equal(before,File.ReadAllText(path));
    }
    [Fact]
    public void AtomicTextWritesRemainUtf8WithoutBom()
    {
        using var s=new Sandbox();var path=s.File("unicode.json","");const string content="é 漢字 👋\r\n";
        AtomicFile.Write(path,content);Assert.Equal(new System.Text.UTF8Encoding(false).GetBytes(content),File.ReadAllBytes(path));
    }
}
