using BipKb;
using Xunit;
namespace BipKb.Tests;

public class ContextTests
{
    [Theory]
    [InlineData("project",121,120)]
    [InlineData("domain",121,120)]
    [InlineData("description",1201,1200)]
    [InlineData("notes",1201,1200)]
    public void FilledButTooLongContextIdentifiesTheActualField(string field,int length,int maximum)
    {
        var context=new SourceContext("BIP","Facturation","Étude complète",SourceStatus.Validated);
        context=field switch{"project"=>context with{Project=new('p',length)},"domain"=>context with{Domain=new('d',length)},"description"=>context with{Description=new('x',length)},_=>context with{Notes=new('n',length)}};
        var error=Assert.Throws<ArgumentException>(context.Validate);
        Assert.Contains("("+field+")",error.Message);Assert.Contains(length+" caractères saisis",error.Message);Assert.Contains("maximum "+maximum,error.Message);
        Assert.DoesNotContain("Contexte requis",error.Message);
    }
    [Fact]
    public void OptionalJsonNotesCanBeOmittedOrNull()
    {
        var omitted=Json.Read<SourceDescriptor>("""{"role":"target","context":{"project":"BIP","domain":"Facturation","description":"Étude","status":"validated"}}""");
        omitted.Validate();(omitted with{Context=omitted.Context with{Notes=null}}).Validate();
    }
    [Fact]
    public async Task GuidedInputRetriesOnlyInvalidFieldsAndPreservesPreviousAnswers()
    {
        using var s=new Sandbox();var path=Fresh(s);var output=new StringWriter();
        var input="factures\nBIP\nFacturation\ntarget\nunknown\nvalidated\n"+new string('x',1201)+"\nÉtude corrigée\n\noui\n";
        var id=await new SourceSetup(s.Workspace,new StringReader(input),output,true).Prepare(path,null,null,null,false,default);
        var descriptor=s.Workspace.Config.Sources.Single(d=>d.Id==id).Resolve("tables.sql")!;
        Assert.Equal("BIP",descriptor.Context.Project);Assert.Equal("Facturation",descriptor.Context.Domain);Assert.Equal(SourceStatus.Validated,descriptor.Context.Status);
        Assert.Equal("Étude corrigée",descriptor.Context.Description);Assert.Contains("1201 caractères saisis",output.ToString());Assert.Contains("Statut invalide",output.ToString());
    }
    [Fact]
    public async Task InvalidJsonContextDoesNotSaveAnythingAndNamesTheProblem()
    {
        using var s=new Sandbox();var path=Fresh(s);var contextFile=Path.Combine(s.Root,"context.json");
        Json.Write(contextFile,Descriptor() with{Context=Descriptor().Context with{Description=new string('x',1201)}});
        var before=Json.Serialize(s.Workspace.Config);
        var error=await Assert.ThrowsAsync<ArgumentException>(()=>new SourceSetup(s.Workspace,TextReader.Null,TextWriter.Null,false).Prepare(path,"factures",null,contextFile,false,default));
        Assert.Contains("Description (description)",error.Message);Assert.Equal(before,Json.Serialize(s.Workspace.Config));Assert.DoesNotContain("factures",s.Workspace.Bindings().Keys);
    }
    [Fact]
    public async Task InvalidSavedContextCanBeRepairedWithoutReenteringOtherFields()
    {
        using var s=new Sandbox();s.File("a.md","Une commande");var config=s.Workspace.Config;var original=config.Sources[0].Context!;
        config.Sources[0]=config.Sources[0] with{Context=original with{Notes=new string('n',1201)}};Json.Write(Path.Combine(s.Workspace.Root,"bip.json"),config);
        var output=new StringWriter();
        var id=await new SourceSetup(s.Workspace,new StringReader("\n\n\n\n\nNotes corrigées\noui\n"),output,true).Prepare(s.Source,null,null,null,false,default);
        Assert.Equal("test",id);Assert.Equal(original with{Notes="Notes corrigées"},s.Workspace.Config.Sources[0].Context);
        Assert.Contains("Précisions (notes)",output.ToString());
    }
    [Fact]
    public async Task EncodedBudgetFailureCanBeCorrectedBeforePublication()
    {
        using var s=new Sandbox();var path=Fresh(s);var output=new StringWriter();
        var input="factures\nBIP\nFacturation\ntarget\nvalidated\n"+new string('"',1200)+"\n"+new string('"',1200)+"\nDescription courte\nNotes courtes\noui\n";
        await new SourceSetup(s.Workspace,new StringReader(input),output,true).Prepare(path,null,null,null,false,default);
        Assert.Contains("trop volumineux après encodage",output.ToString());
        Assert.Equal("Description courte",s.Workspace.Config.Sources.Single(d=>d.Id=="factures").Context!.Description);
    }
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
