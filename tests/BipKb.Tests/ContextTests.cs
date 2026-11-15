using BipKb;
using Xunit;
namespace BipKb.Tests;

public class ContextTests
{
    private static SourceDescriptor Descriptor(string domain="Facturation",SourceStatus status=SourceStatus.Proposal) =>
        new(SourceRole.Target,new("iBip",domain,"Migration des factures vers le MPD cible",status,"Les noms de tables ont changé."));
    private static string Fresh(Sandbox s)
    {
        var path=Path.Combine(s.Root,"documents");Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"tables.sql"),"CREATE TABLE Client(Id int);");return path;
    }
    [Fact] public async Task FirstImportAsksContextAndRegistersOnlyAfterConfirmation()
    {
        using var s=new Sandbox();var path=Fresh(s);var output=new StringWriter();
        var answers=new StringReader("factures\niBip\nFacturation\ntarget\nproposal\nÉtude de migration\nSQL cible non validé\noui\n");
        var id=await new SourceSetup(s.Workspace,answers,output,true).Prepare(path,null,null,null,false,default);
        Assert.Equal("factures",id);Assert.Contains("1 .sql",output.ToString());
        var descriptor=s.Workspace.Config.Sources.Single(x=>x.Id==id).Resolve("tables.sql")!;
        Assert.Equal("Facturation",descriptor.Context.Domain);Assert.Equal(SourceStatus.Proposal,descriptor.Context.Status);
        Assert.Equal(path,s.Workspace.Bindings()[id]);Assert.Empty(s.Workspace.Load().Claims);
    }
    [Theory][InlineData("non")][InlineData("/cancel")]
    public async Task CancelDoesNotSaveContextOrBinding(string answer)
    {
        using var s=new Sandbox();var path=Fresh(s);var before=Json.Serialize(s.Workspace.Config);var bindings=Json.Serialize(s.Workspace.Bindings());
        var input=new StringReader("factures\niBip\nFacturation\ntarget\nproposal\nÉtude\n\n"+answer+"\n");
        await Assert.ThrowsAsync<OperationCanceledException>(()=>new SourceSetup(s.Workspace,input,new StringWriter(),true).Prepare(path,null,null,null,false,default));
        Assert.Equal(before,Json.Serialize(s.Workspace.Config));Assert.Equal(bindings,Json.Serialize(s.Workspace.Bindings()));
    }
    [Fact] public async Task RedirectedInputDoesNotGuessMissingContext()
    {
        using var s=new Sandbox();var path=Fresh(s);
        await Assert.ThrowsAsync<ArgumentException>(()=>new SourceSetup(s.Workspace,new StringReader(""),new StringWriter(),false).Prepare(path,"new",SourceRole.Target,null,false,default));
        Assert.DoesNotContain("new",s.Workspace.Bindings().Keys);
    }
    [Fact] public async Task JsonContextEnablesSingleCommandImportAndReuse()
    {
        using var s=new Sandbox();var path=Fresh(s);var contextFile=Path.Combine(s.Root,"context.json");Json.Write(contextFile,Descriptor());
        var setup=new SourceSetup(s.Workspace,new StringReader(""),new StringWriter(),false);
        var id=await setup.Prepare(path,"factures",null,contextFile,false,default);
        Assert.Equal(id,await setup.Prepare(path,null,null,null,false,default));
        var observed=new List<AnalysisRequest>();
        var fake=new FakeAnalyzer{Handler=r=>{observed.Add(r);return new([new Finding{Entity="Client",Kind="table",Statement="Client possède Id",Contribution=Contribution.New,EvidenceIds=[r.Units[0].Id]}]);}};
        await new Ingestor(s.Workspace,fake).Run(path,id,null,false,default);
        Assert.All(observed,r=>Assert.Equal(Descriptor().Context,r.Context));
        var claim=Assert.Single(s.Workspace.Load().Claims);Assert.True(claim.NeedsReview);Assert.Equal(Descriptor().Context,claim.Context);
        Assert.Equal(Descriptor().Context,Assert.Single(s.Workspace.Load().Sources).Context);
        Assert.Contains("Facturation",new KnowledgeService(s.Workspace).Context("Client"));
    }
    [Fact] public async Task FileOverrideDoesNotChangeSiblingContext()
    {
        using var s=new Sandbox();var a=s.File("a.sql","CREATE TABLE Client(Id int);");s.File("b.sql","CREATE TABLE Client(Id int);");
        s.Workspace.ConfigureSource("test",s.Source,a,Descriptor());
        var definition=s.Workspace.Config.Sources.Single(x=>x.Id=="test");
        Assert.Equal("Facturation",definition.Resolve("a.sql")!.Context.Domain);
        Assert.Equal("Commandes",definition.Resolve("b.sql")!.Context.Domain);
        var seen=new List<AnalysisRequest>();var fake=new FakeAnalyzer{Handler=r=>{seen.Add(r);return new(r.Phase=="reconcile"?[]:[new Finding{Entity="Client",Kind="table",Statement="Client possède Id",Contribution=Contribution.New,EvidenceIds=[r.Units[0].Id]}]);}};
        await new Ingestor(s.Workspace,fake).Run(s.Source,null,null,false,default);
        Assert.Equal(2,s.Workspace.Load().Entities.Count);Assert.Equal(2,s.Workspace.Load().Claims.Count);
        Assert.All(seen,r=>Assert.All(r.Existing,c=>Assert.Equal(r.Context!.Scope,c.Context!.Scope)));
        Assert.Contains(seen,r=>r.Role=="Target");Assert.Contains(seen,r=>r.Role=="Current");
        var target=new SearchEngine(s.Workspace).Search(new("Client",Role:SourceRole.Target));Assert.NotEmpty(target.Hits);Assert.All(target.Hits,h=>Assert.Equal(SourceRole.Target,h.Role));
    }
    [Fact] public async Task ContextChangeInvalidatesAnalysisAndPreservesHistory()
    {
        using var s=new Sandbox();s.File("a.md","Client possède Id");var fake=new FakeAnalyzer();var engine=new Ingestor(s.Workspace,fake);
        await engine.Run(s.Source,null,null,false,default);var old=s.Workspace.Load().Claims.Single();s.Workspace.Decide(old.Id,"accept","Vérifié","Alice");
        var original=s.Workspace.Config.Sources.Single().Context!;
        s.Workspace.ConfigureSource("test",s.Source,s.Source,new(SourceRole.Current,original with{Description="Nouvelle interprétation du document"}));
        var count=fake.Calls;await engine.Run(s.Source,null,null,false,default);
        Assert.True(fake.Calls>count);Assert.Equal(2,s.Workspace.Load().Claims.Count);Assert.True(s.Workspace.Load().Claims.Single(c=>c.Id==old.Id).Obsolete);
        Assert.Single(s.Workspace.Decisions());
    }
    [Fact] public async Task LegacySourcesRequireContextBeforeAnyAnalysis()
    {
        using var s=new Sandbox();s.File("a.md","Client");var config=s.Workspace.Config;config.Sources[0]=config.Sources[0] with{Context=null};Json.Write(Path.Combine(s.Workspace.Root,"bip.json"),config);
        var fake=new FakeAnalyzer();await Assert.ThrowsAsync<ArgumentException>(()=>new Ingestor(s.Workspace,fake).Run(s.Source,null,null,false,default));Assert.Equal(0,fake.Calls);
    }
    [Fact] public async Task FirstFileDoesNotAssignContextToOtherFilesInParent()
    {
        using var s=new Sandbox();var root=Fresh(s);var file=Path.Combine(root,"tables.sql");var contextFile=Path.Combine(s.Root,"context.json");Json.Write(contextFile,Descriptor());
        await new SourceSetup(s.Workspace,new StringReader(""),new StringWriter(),false).Prepare(file,"single",null,contextFile,false,default);
        var definition=s.Workspace.Config.Sources.Single(x=>x.Id=="single");Assert.NotNull(definition.Resolve("tables.sql"));Assert.Null(definition.Resolve("other.sql"));
    }
    [Fact] public void RemappingPreservesSharedContext()
    {
        using var s=new Sandbox();var before=s.Workspace.Config.Sources.Single();var path=Fresh(s);s.Workspace.Bind("test",path,null);
        Assert.Equal(before,s.Workspace.Config.Sources.Single());
    }
    [Fact] public async Task AmbiguousRootsRequireExplicitSelection()
    {
        using var s=new Sandbox();var nested=Path.Combine(s.Source,"child");Directory.CreateDirectory(nested);s.Workspace.Bind("child",nested,SourceRole.Target);
        await Assert.ThrowsAsync<ArgumentException>(()=>new SourceSetup(s.Workspace,new StringReader(""),new StringWriter(),false).Prepare(nested,null,null,null,false,default));
    }
}
