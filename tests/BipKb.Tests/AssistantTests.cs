using BipKb;
using Xunit;
namespace BipKb.Tests;

public class AssistantTests
{
    private static ProjectProfile Profile(string name="iBip")=>new("project",name,"Migrer la facturation",".NET CQRS",["ancien","cible"],["DTO et handlers"],
        [new("billing","Facturation","Factures",["customers"]),new("customers","Clients","Tiers",[])],[],[new("impayé",["reste à payer","unpaid"],"Facture restant à payer","Facturation")]);
    private static void Project(Sandbox s)
    {var path=Path.Combine(s.Root,"project.json");Json.Write(path,Profile());new Projects(s.Workspace).Setup(path,TextReader.Null,TextWriter.Null,false);}
    private static async Task Seed(Sandbox s,string text="La facture impayé possède un solde.")
    {s.Workspace.ConfigureSource("test",s.Source,s.Source,new(SourceRole.Target,new("iBip","Facturation","MPD factures",SourceStatus.Validated)));s.File("factures.md",text);await new Ingestor(s.Workspace,new FakeAnalyzer()).Run(s.Source,null,null,false,default);}
    private sealed class Assistant: IResearchAssistant
    {
        public List<ResearchRequest> Requests {get;}=[];
        public Task<ResearchAdvice> Investigate(ResearchRequest request,CancellationToken ct)
        {
            Requests.Add(request);return Task.FromResult(new ResearchAdvice("Réponse sourcée",request.Findings.Take(1).Select(h=>h.Id).ToArray(),request.Cycle==1?["facture impayé"]:[],["Quel filtre sur les factures annulées ?"]));
        }
    }
    [Fact] public async Task RawEvidenceIsSearchableWhenModelExtractsNothing()
    {
        using var s=new Sandbox();s.File("rules.md","Une facture impayé est un reliquat dû.");
        await new Ingestor(s.Workspace,new FakeAnalyzer{Handler=_=>new([])}).Run(s.Source,null,null,false,default);
        Assert.Empty(s.Workspace.Load().Claims);
        var found=new SearchEngine(s.Workspace).Search(new("reliquat"));Assert.Contains(found.Hits,h=>h.Kind=="evidence"&&h.Text.Contains("reliquat"));
    }
    [Fact] public async Task DomainGraphAndGlossaryExpandSearchWithoutCrossingProjects()
    {
        using var s=new Sandbox();Project(s);await Seed(s);
        s.File("client.md","Le client possède une facture impayé");s.Workspace.ConfigureSource("test",s.Source,Path.Combine(s.Source,"client.md"),new(SourceRole.Current,new("iBip","Clients","Clients",SourceStatus.Observed)));
        await new Ingestor(s.Workspace,new FakeAnalyzer()).Run(s.Source,null,null,false,default);
        var response=new SearchEngine(s.Workspace).Search(new("reste à payer","iBip","Facturation"));
        Assert.Contains(response.Hits,h=>h.Domain=="Clients");Assert.All(response.Hits,h=>Assert.Equal("iBip",h.Project));Assert.Contains(response.Hits,h=>h.Text.Contains("solde"));
    }
    [Fact] public async Task MigrationPreservesIdsDecisionsAndOriginalFiles()
    {
        using var s=new Sandbox();await Seed(s);var original=s.Workspace.Load() with{Version=1};var claim=original.Claims.Single();s.Workspace.Decide(claim.Id,"accept","validé","Alice");
        Directory.Delete(Path.Combine(s.Workspace.Root,"memory"),true);Json.Write(s.Workspace.StatePath,original);var bytes=File.ReadAllText(s.Workspace.StatePath);
        _=s.Workspace.Migrate(true);Assert.Equal(bytes,File.ReadAllText(s.Workspace.StatePath));
        _=s.Workspace.Migrate(false);Assert.True(s.Workspace.IsModern);Assert.Equal(claim.Id,s.Workspace.Load().Claims.Single().Id);Assert.Single(s.Workspace.Decisions());
        Assert.Equal(bytes,File.ReadAllText(Path.Combine(s.Workspace.Local,"migration-backup","knowledge.json")));
        var revision=s.Workspace.Revision;_=s.Workspace.Migrate(false);Assert.Equal(revision,s.Workspace.Revision);
        Directory.Delete(s.Workspace.Local,true);Assert.Equal(claim.Id,s.Workspace.Load().Claims.Single().Id);
    }
    [Fact] public void OrphanPayloadDoesNotPublishAndCorruptPayloadIsRejected()
    {
        using var s=new Sandbox();var before=s.Workspace.Revision;var objects=Path.Combine(s.Workspace.Root,"memory","objects");File.WriteAllText(Path.Combine(objects,"orphan.json"),"[]");Assert.Equal(before,s.Workspace.Revision);Assert.Empty(s.Workspace.Load().Claims);
        foreach(var path in Directory.GetFiles(objects,"*.json").Where(p=>!p.EndsWith("orphan.json")))File.WriteAllText(path,"corrupt");
        Directory.Delete(s.Workspace.Local,true);Assert.Throws<InvalidDataException>(()=>s.Workspace.Load());
    }
    [Fact] public async Task ConcurrentGitEditsRequireResolutionAndInvalidResolutionDoesNotPublish()
    {
        using var s=new Sandbox();await Seed(s);var export=Path.Combine(s.Root,"other");new KnowledgeService(s.Workspace).Export(export);var other=new Workspace(export);
        var left=s.Workspace.Load();var right=other.Load();left.Entities[0]=left.Entities[0] with{Name="Left"};right.Entities[0]=right.Entities[0] with{Name="Right"};s.Workspace.Save(left);other.Save(right);
        foreach(var file in Directory.GetFiles(Path.Combine(export,"memory"),"*.json",SearchOption.AllDirectories)){var dest=Path.Combine(s.Workspace.Root,Path.GetRelativePath(export,file));if(!File.Exists(dest))File.Copy(file,dest);}
        Assert.Throws<RevisionConflictException>(()=>s.Workspace.Load());
        var resolve=Path.Combine(s.Root,"resolve.json");Json.Write(resolve,new[]{new MemoryChange("entity:"+left.Entities[0].Id,System.Text.Json.JsonSerializer.SerializeToElement(left.Entities[0],Json.Options))});
        new MemoryStore(s.Workspace).Resolve(resolve);Assert.Equal("Left",s.Workspace.Load().Entities[0].Name);
        var revision=s.Workspace.Revision;Json.Write(resolve,new[]{new MemoryChange("entity:"+left.Entities[0].Id,null)});
        Assert.Throws<InvalidDataException>(()=>new MemoryStore(s.Workspace).Resolve(resolve));Assert.Equal(revision,s.Workspace.Revision);
    }
    [Fact] public async Task ReadsContinueDuringSlowAnalysisAndConcurrentWritesAreDetected()
    {
        using var s=new Sandbox();await Seed(s);s.File("new.md","Nouvelle facture");
        var entered=new TaskCompletionSource();var release=new TaskCompletionSource();var fake=new BlockingAnalyzer(entered,release);
        var running=new Ingestor(s.Workspace,fake).Run(s.Source,null,null,false,default);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEmpty(new SearchEngine(s.Workspace).Search(new("facture")).Hits);
        s.Workspace.Decide(s.Workspace.Load().Claims[0].Id,"accept","reviewed","Alice");release.SetResult();
        await Assert.ThrowsAsync<RevisionConflictException>(()=>running);
        Assert.DoesNotContain(s.Workspace.Load().Sources,x=>x.RelativePath=="new.md");
    }
    private sealed class BlockingAnalyzer(TaskCompletionSource entered,TaskCompletionSource release):IAnalyzer
    {
        public async Task<Analysis> Analyze(AnalysisRequest r,string? image,CancellationToken ct){entered.TrySetResult();await release.Task;return new([]);}
        public Task<Answer> Ask(string q,Claim[] c,CancellationToken ct)=>throw new NotImplementedException();
    }
    [Fact] public async Task ResearchPersistsEvidenceQuestionsAndExplicitDeclarations()
    {
        using var s=new Sandbox();Project(s);await Seed(s);var assistant=new Assistant();var research=new Research(s.Workspace,assistant);
        var task=research.Start("Consulter les factures impayées","iBip");task=await research.Run(task.Id,null,default);
        Assert.Equal(2,assistant.Requests.Count);Assert.NotEmpty(task.Searches);Assert.NotEmpty(task.Findings);Assert.False(research.IsStale(task));Assert.Single(s.Workspace.Load().Questions);
        Assert.Throws<ArgumentException>(()=>research.Remember("iBip","Une facture annulée est exclue","Alice",false));
        research.Remember("iBip","Une facture annulée est exclue","Alice",true);Assert.True(research.IsStale(task));
        task=await research.Run(task.Id,"Et les factures annulées ?",default);Assert.Contains(assistant.Requests.Last().HumanDeclarations!,q=>q.Answer!.Contains("exclue"));
        Assert.Contains("Réponse sourcée",research.Export(task.Id));Assert.False(research.IsStale(task));
        s.File("extra.md","Une nouvelle exception existe.");await new Ingestor(s.Workspace,new FakeAnalyzer()).Run(s.Source,null,null,false,default);Assert.True(research.IsStale(task));
    }
    [Fact] public void ResearchRejectsInventedCitationsAndEmptyCorpusDoesNotProveAbsence()
    {
        var request=new ResearchRequest("facture",null,[],[],[],1);
        Assert.Throws<InvalidDataException>(()=>Research.Validate(new("Invented",["fake"],[],[]),request));
        Research.Validate(new("Aucune preuve retrouvée ; règle inconnue",[],[],["Quelle référence consulter ?"]),request);
        Assert.All(Research.Coverage([]),c=>Assert.False(c.Supported));
    }
    [Fact] public async Task UnrelatedSourceConfigurationDoesNotReanalyzeExistingSources()
    {
        using var s=new Sandbox();s.File("a.md","Facture");var analyzer=new FakeAnalyzer();var ingestion=new Ingestor(s.Workspace,analyzer);await ingestion.Run(s.Source,null,null,false,default);var count=analyzer.Calls;
        var other=Path.Combine(s.Root,"other");Directory.CreateDirectory(other);s.Workspace.Bind("other",other,SourceRole.Legacy);
        await ingestion.Run(s.Source,null,null,false,default);Assert.Equal(count,analyzer.Calls);
    }
    [Fact] public async Task SqlStructureAndStableEntityRenameArePreserved()
    {
        using var s=new Sandbox();s.File("proc.sql","CREATE PROCEDURE dbo.GetFactures @ClientId int AS SELECT Id FROM dbo.Facture WHERE ClientId=@ClientId;");
        await new Ingestor(s.Workspace,new FakeAnalyzer()).Run(s.Source,null,null,false,default);
        var k=s.Workspace.Load();Assert.Contains(k.Objects,o=>o.Name=="dbo.GetFactures"&&o.Kind=="procedure");Assert.Contains(k.Objects,o=>o.Name=="dbo.Facture"&&o.Kind=="table");Assert.Contains(k.Relations,r=>r.Kind==RelationKind.Reads);
        var entity=k.Entities.Single();k.Entities[0]=entity with{Name="Nouveau nom",Aliases=[entity.Name]};s.Workspace.Save(k);
        await new Ingestor(s.Workspace,new FakeAnalyzer()).Run(s.Source,null,null,true,default);Assert.Single(s.Workspace.Load().Entities);Assert.Equal(entity.Id,s.Workspace.Load().Entities[0].Id);
    }
    [Fact] public async Task EmptyAnalysisStillIndexesSqlParametersAndFilters()
    {
        using var s=new Sandbox();s.File("proc.sql","CREATE PROCEDURE dbo.Lister @Statut int AS SELECT * FROM dbo.Facture WHERE Statut=@Statut;");await new Ingestor(s.Workspace,new FakeAnalyzer{Handler=_=>new([])}).Run(s.Source,null,null,false,default);
        var result=new SearchEngine(s.Workspace).Search(new("Statut"));Assert.Contains(result.Hits,h=>h.Kind=="parameter");Assert.Contains(result.Hits,h=>h.Kind=="evidence"&&h.Text.Contains("WHERE"));
    }

    [Fact] public async Task IndexUpdatesDeletesAndRebuildsWithoutStaleFtsRows()
    {
        using var s=new Sandbox();s.File("raw.md","anciennevaleur");var ingest=new Ingestor(s.Workspace,new FakeAnalyzer{Handler=_=>new([])});
        await ingest.Run(s.Source,null,null,false,default);var engine=new SearchEngine(s.Workspace);
        Assert.Single(engine.Search(new("anciennevaleur")).Hits);
        s.File("raw.md","nouvellevaleur");await ingest.Run(s.Source,null,null,false,default);
        Assert.Empty(engine.Search(new("anciennevaleur")).Hits);Assert.Single(engine.Search(new("nouvellevaleur")).Hits);
        Assert.Single(engine.Search(new("anciennevaleur",IncludeHistorical:true)).Hits);
        File.Delete(Path.Combine(s.Source,"raw.md"));await ingest.Run(s.Source,null,null,false,default);
        Assert.Empty(engine.Search(new("nouvellevaleur")).Hits);
        Directory.Delete(s.Workspace.Local,true);Assert.Single(engine.Search(new("nouvellevaleur",IncludeHistorical:true)).Hits);
    }
    [Fact] public async Task HumanDeclarationsAreRetrievedByTopicBeyondTheRecentFive()
    {
        using var s=new Sandbox();Project(s);await Seed(s);var research=new Research(s.Workspace,new Assistant());
        var important=research.Remember("iBip","La remise exceptionnelle vaut 12 pour cent","Alice",true);
        for(var i=0;i<8;i++)research.Remember("iBip","Autre déclaration "+i,"Bob",true);
        var response=new SearchEngine(s.Workspace).Search(new("remise exceptionnelle","iBip","Facturation"));
        var hit=Assert.Single(response.Hits,h=>h.Id==important.Id);Assert.Equal(KnowledgeOrigin.Declared,hit.Origin);Assert.Contains(hit.Reasons,r=>r.Contains("Alice"));
        Assert.DoesNotContain(new SearchEngine(s.Workspace).Search(new("remise exceptionnelle","Autre projet")).Hits,h=>h.Id==important.Id);
    }
    [Fact] public async Task EvidenceChangesReopenAcceptedKnowledgeInSearch()
    {
        using var s=new Sandbox();s.File("a.md","Facture");var analyzer=new FakeAnalyzer{Handler=r=>new([new(){Entity="Facture",Kind="rule",Statement="La facture est due",Contribution=Contribution.New,EvidenceIds=[r.Units[0].Id]}])};
        var ingest=new Ingestor(s.Workspace,analyzer);await ingest.Run(s.Source,null,null,false,default);var claim=s.Workspace.Load().Claims.Single();
        s.Workspace.Decide(claim.Id,"accept","Vérifié","Alice");var engine=new SearchEngine(s.Workspace);
        Assert.Equal(KnowledgeState.Accepted,engine.Search(new(claim.Id)).Hits.Single(h=>h.Id==claim.Id).State);
        s.File("a.md","Facture, avec exception");await ingest.Run(s.Source,null,null,false,default);
        Assert.Equal(KnowledgeState.Reexamine,engine.Search(new(claim.Id)).Hits.Single(h=>h.Id==claim.Id).State);
    }
    [Fact] public async Task FrenchPluralQueriesFindSingularEvidenceAndEvaluationReportsRecall()
    {
        using var s=new Sandbox();s.File("a.md","Une facture impayé reste due");await new Ingestor(s.Workspace,new FakeAnalyzer{Handler=_=>new([])}).Run(s.Source,null,null,false,default);
        var proof=s.Workspace.Load().Evidence.Single();Assert.Contains(new SearchEngine(s.Workspace).Search(new("factures impayées")).Hits,h=>h.Id==proof.Id);
        var path=Path.Combine(s.Root,"evaluation.json");Json.Write(path,new[]{new EvaluationCase("factures impayées",[proof.Id])});
        var output=System.Text.Json.JsonSerializer.SerializeToElement(Evaluation.Run(s.Workspace,path),Json.Options);Assert.Equal(1,output.GetProperty("recallAt20").GetDouble());
    }
    [Fact] public async Task ImportChecksIntegrityEvenWithWarmMaterializedCache()
    {
        using var s=new Sandbox();await Seed(s);_=s.Workspace.Load();
        var path=Directory.GetFiles(Path.Combine(s.Workspace.Root,"memory","objects"),"*.json")[0];File.WriteAllText(path,"[]");
        Assert.Throws<InvalidDataException>(()=>new KnowledgeService(s.Workspace).Import());
    }

    [Fact] public async Task Utf16SqlExportsRetainStructuralObjects()
    {
        using var s=new Sandbox();File.WriteAllText(Path.Combine(s.Source,"export.sql"),"CREATE TABLE dbo.Facture(Id int PRIMARY KEY);",System.Text.Encoding.Unicode);
        await new Ingestor(s.Workspace,new FakeAnalyzer{Handler=_=>new([])}).Run(s.Source,null,null,false,default);
        Assert.Contains(s.Workspace.Load().Objects,o=>o.Name=="dbo.Facture"&&o.Kind=="table");
    }

    [Fact] public async Task ChangedConditionsAndWithdrawnInterpretationKeepHistoryAndDecisions()
    {
        using var s=new Sandbox();s.File("a.md","La facture est due sous certaines conditions");var condition="statut ouvert";
        var analyzer=new FakeAnalyzer{Handler=r=>new([new(){Entity="Facture",Kind="rule",Statement="La facture est due",Conditions=[condition],Contribution=Contribution.New,EvidenceIds=[r.Units[0].Id]}])};
        var ingest=new Ingestor(s.Workspace,analyzer);await ingest.Run(s.Source,null,null,false,default);
        var old=s.Workspace.Load().Claims.Single();s.Workspace.Decide(old.Id,"accept","Validé avec le métier","Alice");
        condition="statut ouvert, hors facture annulée";await ingest.Run(s.Source,null,null,true,default);
        var updated=s.Workspace.Load();Assert.Equal(2,updated.Claims.Count);Assert.True(updated.Claims.Single(c=>c.Id==old.Id).Obsolete);Assert.Single(s.Workspace.Decisions());
        Assert.Equal(condition,Assert.Single(updated.Claims.Single(c=>!c.Obsolete).Conditions!));
        analyzer.Handler=_=>new([]);await ingest.Run(s.Source,null,null,true,default);Assert.All(s.Workspace.Load().Claims,c=>Assert.True(c.Obsolete));
    }
}
