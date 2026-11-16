using BipKb;
using Xunit;

namespace BipKb.Tests;

public class LocalIngestionTests
{
    [Fact]
    public void LegacySourceVersionsRemainRecognizedAsAnalyzed()
    {
        var source=new SourceVersion("id","source","file.md","hash","key",SourceRole.Current,[],[],true,[]);
        var json=System.Text.Json.Nodes.JsonNode.Parse(Json.Serialize(source))!.AsObject();json.Remove("analyzed");
        Assert.True(Json.Read<SourceVersion>(json.ToJsonString()).Analyzed);
        var report=new IngestionReport("id",DateTimeOffset.UtcNow,"source",1,0,[],[],[]);
        json=System.Text.Json.Nodes.JsonNode.Parse(Json.Serialize(report))!.AsObject();json.Remove("mode");json.Remove("unanalyzedFiles");
        Assert.Equal("analysis",Json.Read<IngestionReport>(json.ToJsonString()).Mode);
    }
    private sealed class NoModel : IAnalyzer
    {
        public Task<Analysis> Analyze(AnalysisRequest request,string? image,CancellationToken ct)=>throw new Exception("Local indexing must not call Copilot");
        public Task<Answer> Ask(string question,Claim[] claims,CancellationToken ct)=>throw new Exception("Local indexing must not call Copilot");
    }
    private static Task<IngestionReport> Index(Sandbox s,string? path=null)=>new Ingestor(s.Workspace,new NoModel()).Run(path??s.Source,null,null,false,default,analyze:false);

    [Fact]
    public async Task LocalIndexKeepsCodeSqlExcelAndDiagramEvidenceWithoutModel()
    {
        using var s=new Sandbox();
        s.File("dto.cs","namespace Api; public record Commande(int Id, string Statut);");
        s.File("proc.sql","CREATE PROCEDURE dbo.Lister AS SELECT * FROM dbo.Commande WHERE Statut=1;");
        s.File("diagram.drawio","<mxGraphModel><root><mxCell id=\"a\" value=\"Commande\"/><mxCell id=\"b\" value=\"Client\"/><mxCell id=\"r\" source=\"a\" target=\"b\"/></root></mxGraphModel>");
        File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","study.xls"),Path.Combine(s.Source,"study.xls"));
        var report=await Index(s);Assert.Equal("index",report.Mode);Assert.Equal(4,report.UnanalyzedFiles);
        var k=s.Workspace.Load();Assert.Empty(k.Claims);Assert.All(k.Sources,source=>Assert.False(source.Analyzed));
        Assert.Contains(k.Relations,r=>r.Kind==RelationKind.Reads);Assert.Contains(k.Objects,o=>o.Name=="Api.Commande");
        Assert.Contains(k.Evidence,e=>e.Locator.Contains("Migration")&&e.Excerpt.Contains("C2: Cible"));
        Assert.Contains(k.Evidence,e=>e.Excerpt.Contains("source: a; target: b"));
        Assert.Contains(new SearchEngine(s.Workspace).Search(new("Statut")).Hits,h=>h.Kind=="evidence"&&h.Text.Contains("WHERE"));
        Assert.Contains("Preuve",new KnowledgeService(s.Workspace).Context("Commande"));
        var again=await Index(s);Assert.Equal(4,again.Unchanged);Assert.Equal(k.Evidence.Count,s.Workspace.Load().Evidence.Count);
    }
    [Fact]
    public async Task ImportantDocumentsCanBeAnalyzedAfterIndexingWithoutReanalyzeOrDuplicateEvidence()
    {
        using var s=new Sandbox();
        var excel=Path.Combine(s.Source,"study.xls");File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","study.xls"),excel);
        var diagram=s.File("model.drawio","<mxGraphModel><root><mxCell id=\"a\" value=\"Commande\"/></root></mxGraphModel>");
        await Index(s);var proofs=s.Workspace.Load().Evidence.Select(e=>e.Id).Order().ToArray();var analyzer=new FakeAnalyzer();
        var engine=new Ingestor(s.Workspace,analyzer);
        var result=await engine.Run(excel,null,null,false,default);Assert.Equal(0,result.UnanalyzedFiles);Assert.NotEmpty(result.ClaimIds);
        Assert.False(s.Workspace.Load().Sources.Single(v=>v.Active&&v.RelativePath=="model.drawio").Analyzed);
        result=await engine.Run(diagram,null,null,false,default);Assert.NotEmpty(result.ClaimIds);
        Assert.Equal(proofs,s.Workspace.Load().Evidence.Select(e=>e.Id).Order().ToArray());
        Assert.All(s.Workspace.Load().Sources.Where(v=>v.Active),v=>Assert.True(v.Analyzed));
    }
    [Fact]
    public async Task LocalModeRetainsCompletedAnalysisAndDecisionsButExpiresChangedEvidence()
    {
        using var s=new Sandbox();var file=s.File("rules.md","Commande validée");
        await new Ingestor(s.Workspace,new FakeAnalyzer()).Run(s.Source,null,null,false,default);
        var claim=Assert.Single(s.Workspace.Load().Claims);s.Workspace.Decide(claim.Id,"accept","Confirmé","Équipe");
        var report=await Index(s);Assert.Equal(1,report.Unchanged);Assert.Equal(0,report.UnanalyzedFiles);Assert.False(Assert.Single(s.Workspace.Load().Claims).Obsolete);
        File.WriteAllText(file,"Commande annulée");report=await Index(s);Assert.Equal(1,report.UnanalyzedFiles);
        Assert.True(Assert.Single(s.Workspace.Load().Claims).Obsolete);Assert.Single(s.Workspace.Decisions());
        Assert.Contains(new SearchEngine(s.Workspace).Search(new("annulée")).Hits,h=>h.Kind=="evidence");
    }
    [Fact]
    public async Task SwitchingAfterInterruptedAnalysisRecoversValidStagedBatchesWithoutModelCalls()
    {
        using var s=new Sandbox();s.File("a.md","Commande validée");s.File("b.md","Commande annulée");
        var analyzer=new FakeAnalyzer{FailAt=2};
        await Assert.ThrowsAsync<TimeoutException>(()=>new Ingestor(s.Workspace,analyzer).Run(s.Source,null,null,false,default));
        Assert.Empty(s.Workspace.Load().Sources);
        var indexed=await Index(s);Assert.Equal(1,indexed.UnanalyzedFiles);Assert.Single(s.Workspace.Load().Claims);
        Assert.True(s.Workspace.Load().Sources.Single(v=>v.RelativePath=="a.md").Analyzed);
        Assert.False(s.Workspace.Load().Sources.Single(v=>v.RelativePath=="b.md").Analyzed);
        var other=Path.Combine(s.Root,"shared");new KnowledgeService(s.Workspace).Export(other);var imported=new Workspace(other);new KnowledgeService(imported).Import();
        Assert.False(imported.Load().Sources.Single(v=>v.RelativePath=="b.md").Analyzed);
    }
    [Fact]
    public async Task DuplicateLocalFilesStillReceiveExplicitModelAnalysis()
    {
        using var s=new Sandbox();s.File("a.md","Commande");s.File("b.md","Commande");await Index(s);
        var analyzer=new FakeAnalyzer();var report=await new Ingestor(s.Workspace,analyzer).Run(s.Source,null,null,false,default);
        Assert.True(analyzer.Calls>0);Assert.Equal(0,report.UnanalyzedFiles);
        Assert.Equal(2,Assert.Single(s.Workspace.Load().Claims).EvidenceIds.Length);
    }
    [Fact]
    public async Task ImagesAreMarkedAsNotVisuallyAnalyzed()
    {
        using var s=new Sandbox();s.File("diagram.png","opaque image bytes");
        var report=await Index(s);Assert.Equal(1,report.UnanalyzedFiles);Assert.Contains(report.Warnings,w=>w.Contains("contenu visuel non analysé"));
        Assert.Empty(s.Workspace.Load().Claims);Assert.False(Assert.Single(s.Workspace.Load().Sources).Analyzed);
        var again=await Index(s);Assert.Equal(1,again.Unchanged);Assert.Contains(again.Warnings,w=>w.Contains("contenu visuel non analysé"));
    }
    private sealed class CitedAssistant : IResearchAssistant
    {
        public ResearchRequest? Request {get;private set;}
        public Task<ResearchAdvice> Investigate(ResearchRequest request,CancellationToken ct)
        {
            Request=request;var proof=request.Findings.First(h=>h.Kind=="evidence");
            return Task.FromResult(new ResearchAdvice("La commande est filtrée par statut ["+proof.Id+"]",[proof.Id],[],[]));
        }
    }
    [Fact]
    public async Task AskUsesRawEvidenceFromLocalIndexWithoutPrecomputedClaims()
    {
        using var s=new Sandbox();s.File("proc.sql","CREATE PROCEDURE dbo.ListerCommandes AS SELECT * FROM dbo.Commande WHERE Statut=1;");
        var profile=Path.Combine(s.Root,"project.json");Json.Write(profile,new ProjectProfile("ibip","iBip","Migrer les commandes",".NET",[],[],[new("commandes","Commandes","Commandes",[])],[],[]));
        new Projects(s.Workspace).Setup(profile,TextReader.Null,TextWriter.Null,false);await Index(s);
        var assistant=new CitedAssistant();var research=new Research(s.Workspace,assistant);var task=research.Start("Commande Statut","iBip");
        var result=await research.Run(task.Id,null,default);
        Assert.Empty(s.Workspace.Load().Claims);Assert.NotEmpty(result.Turns.Last().Citations);
        Assert.Contains(assistant.Request!.Findings,h=>h.Kind=="evidence"&&h.Text.Contains("WHERE"));
    }
}
