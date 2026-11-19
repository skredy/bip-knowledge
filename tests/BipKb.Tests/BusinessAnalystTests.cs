using BipKb;
using System.IO.Compression;
using Xunit;

namespace BipKb.Tests;

public sealed class BusinessDouble : IBusinessAssistant,IAnalyzer
{
    public int FrameCalls,DocumentCalls,QualificationCalls;
    public string SourceQuestion = "";
    public Func<BriefRequest,BriefReply>? Framing;
    public Func<BusinessRequest,BusinessReply>? Reading;
    public Task<BriefReply> Frame(BriefRequest r,CancellationToken ct)
    {
        FrameCalls++;
        return Task.FromResult(Framing?.Invoke(r)??Ready(r));
    }
    public static BriefReply Ready(BriefRequest r)
    {
        var id=r.Turns.Last(t=>t.Role=="user").Id;
        return new("Comprendre les règles des contrats avant de décider ce qui doit changer.","","",true,
            new(r.Current?.Name??"iBip","Comprendre le métier des contrats","Consultation des contrats",
                [new("objective","Comprendre les règles des contrats","declared",[id]),new("scope","Consultation des contrats","declared",[id])],
                ["Les règles de réactivation sont-elles documentées ?"]));
    }
    public Task<SourceQualification> Qualify(SourceQualificationRequest r,CancellationToken ct)
    {QualificationCalls++;return Task.FromResult(new SourceQualification("Étude des contrats",["Contrats"],SourceRole.Unknown,SourceStatus.Proposal,SourceQuestion,"Rôle mixte à préciser lors de l'analyse."));}
    public Task<BusinessReply> Understand(BusinessRequest r,string? a,CancellationToken ct)
    {DocumentCalls++;return Task.FromResult(Reading?.Invoke(r)??Finish(r));}
    public static BusinessReply Finish(BusinessRequest r)=>new("finish","La consultation des contrats nécessite un contrôle des droits.",[],
        r.Visible.Length==0?[]:[new(){Entity="Consultation des contrats",Kind="process",Statement="Le conseiller consulte un contrat si ses droits le permettent.",Contribution=Contribution.New,EvidenceIds=[r.Visible[0].Unit.Id],Aliases=["accès dossier client"]}],[],
        r.Sections.Select(s=>new BusinessCoverage(s.Id,"interpreted","Les conditions et limites de la section ont été examinées.")).ToArray(),
        ["Qui confirme les droits nécessaires ?"],"not-applicable");
    public Task<Analysis> Analyze(AnalysisRequest r,string? i,CancellationToken ct)=>throw new InvalidOperationException("Old per-batch analysis must not be used.");
    public Task<Answer> Ask(string q,Claim[] c,CancellationToken ct)=>throw new NotImplementedException();
}

public class BusinessAnalystTests
{
    private static void Enable(Sandbox s)=>Json.Write(Path.Combine(s.Workspace.Root,"bip.json"),s.Workspace.Config with{BusinessMode=true});
    private static async Task<ProjectProfile> Frame(Sandbox s,BusinessDouble model)
    {
        Enable(s);var interview=new BusinessInterview(s.Workspace,model);
        await interview.Respond("Nous voulons comprendre les règles de consultation des contrats pour la refonte.",default);
        return interview.Confirm("Analyste");
    }
    [Fact]
    public async Task HumanReplySurvivesFailureAndConfirmationIsExplicitAndIdempotent()
    {
        using var s=new Sandbox();Enable(s);var model=new BusinessDouble{Framing=_=>throw new TimeoutException("offline")};
        var interview=new BusinessInterview(s.Workspace,model);
        await Assert.ThrowsAsync<TimeoutException>(()=>interview.Respond("Comprendre les contrats",default));
        Assert.Equal("Comprendre les contrats",Assert.Single(interview.Draft()!.Turns).Text);Assert.Empty(s.Workspace.Load().Projects);
        model.Framing=null;await interview.Respond("Comprendre les contrats",default);
        Assert.Single(interview.Draft()!.Turns);Assert.Empty(s.Workspace.Load().Projects);
        var profile=interview.Confirm("Alice");Assert.Equal("Alice",profile.Brief!.Actor);Assert.Single(profile.Brief.Declarations);
        var revision=s.Workspace.Revision;Assert.Equal(profile.Id,interview.Confirm("Alice").Id);Assert.Equal(revision,s.Workspace.Revision);
        var snapshot=Path.Combine(s.Workspace.Local,"materialized.json");File.Delete(snapshot);
        Assert.Equal(Json.Serialize(profile),Json.Serialize(Assert.Single(s.Workspace.Load().Projects)));
        var clone=Path.Combine(s.Root,"clone");new KnowledgeService(s.Workspace).Export(clone);
        Assert.Equal(Json.Serialize(profile.Brief),Json.Serialize(new Workspace(clone).Load().Projects.Single().Brief));
    }
    [Fact]
    public async Task FollowupKeepsQuestionAndCorrectionsAndConcurrentChangesCannotBeOverwritten()
    {
        using var s=new Sandbox();Enable(s);var model=new BusinessDouble();var interview=new BusinessInterview(s.Workspace,model);
        var first=await interview.Respond("Comprendre les contrats",default);
        model.Framing=r=>{Assert.Contains(r.Turns,t=>t.Role=="assistant");return BusinessDouble.Ready(r);};
        await interview.Respond("Seulement les contrats actifs",default);
        var k=s.Workspace.Load();k.Questions.Add(new("q","iBip","Question concurrente",[]));s.Workspace.Save(k);
        Assert.Throws<RevisionConflictException>(()=>interview.Confirm("Alice"));
        interview.Restart();Assert.Null(interview.Draft());Assert.Single(Directory.GetFiles(Path.Combine(s.Workspace.Local,"business"),"interview-*.json"));
    }
    [Fact]
    public async Task InvalidHumanReferenceCannotInitializeTheProject()
    {
        using var s=new Sandbox();Enable(s);
        var model=new BusinessDouble{Framing=r=>BusinessDouble.Ready(r) with{Brief=BusinessDouble.Ready(r).Brief with{Items=[new("objective","Inventé","declared",["absent"])]}}};
        await Assert.ThrowsAsync<InvalidDataException>(()=>new BusinessInterview(s.Workspace,model).Respond("Comprendre les contrats",default));
        Assert.Empty(s.Workspace.Load().Projects);
    }
    [Fact]
    public async Task SingleProjectIsEnforcedAndMcpNeedsNoProjectArgument()
    {
        using var s=new Sandbox();var model=new BusinessDouble();var p=await Frame(s,model);
        var k=s.Workspace.Load();k.Projects.Add(p with{Id="other",Name="Other"});Assert.Throws<InvalidDataException>(()=>s.Workspace.Save(k));
        using var output=new StringWriter();
        await Mcp.Run(s.Workspace,new StringReader("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"project\",\"arguments\":{}}}\n"),output,default);
        Assert.Contains("Comprendre le métier",output.ToString());Assert.DoesNotContain("isError\": true",output.ToString());
    }
    [Fact]
    public async Task WorkbookPublishesBusinessCardsAndAliasesThenReingestsWithoutModelCalls()
    {
        using var s=new Sandbox();var model=new BusinessDouble();await Frame(s,model);
        var file=Path.Combine(s.Source,"study.xlsx");File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","study.xlsx"),file);
        var engine=new Ingestor(s.Workspace,model);
        var report=await engine.Run(file,"test",null,false,default,guided:true);
        Assert.Equal("business",report.Mode);Assert.Equal(1,model.DocumentCalls);
        var k=s.Workspace.Load();var source=Assert.Single(k.Sources);Assert.NotNull(source.Business);Assert.True(source.Analyzed);
        Assert.Single(k.Claims);Assert.DoesNotContain(k.Evidence,e=>e.Excerpt.StartsWith("Headers:"));
        var hits=new SearchEngine(s.Workspace).Search(new("accès dossier client"));Assert.Equal("claim",hits.Hits[0].Kind);
        var dossier=(string)new McpQueries(s.Workspace).Call("context","accès dossier client",null,10,0,default);
        Assert.Contains("Cadrage métier",dossier);Assert.Contains("contrôle des droits",dossier);Assert.Contains("Preuve",dossier);
        await engine.Run(file,"test",null,false,default,guided:true);Assert.Equal(1,model.DocumentCalls);
        using var locked=new FileStream(Path.Combine(s.Workspace.Local,"materialized.json"),FileMode.Open,FileAccess.Read,FileShare.None);
        Assert.Contains("iBip",Json.Serialize(new McpQueries(s.Workspace).Call("project","",null,20,0,default)));
    }
    [Fact]
    public async Task DesktopSourceClarificationAndApprovalPrecedeBinding()
    {
        using var s=new Sandbox();
        var model=new BusinessDouble { SourceQuestion="Ce document décrit-il la cible ?" };
        await Frame(s,model);
        var file=s.File("contrats.md","Étude des contrats");
        var initial=Json.Serialize(s.Workspace.Config.Sources);
        var seen=false;
        var setup=new BusinessSourceSetup(s.Workspace,model,TextReader.Null,TextWriter.Null,false,
            clarify:question=>{Assert.Equal(model.SourceQuestion,question);return "Cible proposée";},
            confirm:(id,descriptor)=>{seen=true;Assert.Equal(SourceRole.Target,descriptor.Role);throw new OperationCanceledException();});
        await Assert.ThrowsAsync<OperationCanceledException>(()=>setup.Prepare(file,null,SourceRole.Target,"Étude",true,true,default));
        Assert.True(seen);
        Assert.Equal(2,model.QualificationCalls);
        Assert.Equal(initial,Json.Serialize(s.Workspace.Config.Sources));
    }

    [Fact]
    public async Task QualificationUsesProjectAndDoesNotRequireManualDomainOrRole()
    {
        using var s=new Sandbox();var model=new BusinessDouble();await Frame(s,model);
        var file=s.File("new.md","Une règle métier.");
        var setup=new BusinessSourceSetup(s.Workspace,model,TextReader.Null,TextWriter.Null,false);
        var source=await setup.Prepare(file,null,null,"Document de travail",true,true,default);
        Assert.Equal("test",source);Assert.Equal(1,model.QualificationCalls);
        var descriptor=s.Workspace.Config.Sources.Single().Resolve("new.md")!;Assert.Equal(SourceRole.Current,descriptor.Role);Assert.Equal("iBip",descriptor.Context.Project); // Explicit source binding takes precedence over a model guess.
        await setup.Prepare(file,null,null,null,false,true,default);Assert.Equal(1,model.QualificationCalls);
    }
    [Theory]
    [InlineData("\r\n   \r\nNouvelle description\r\n", "Nouvelle description")]
    [InlineData("/keep\r\n", "Règles et implémentation des commandes")]
    [InlineData("/skip\r\n", "")]
    public async Task DescribeWaitsForExplicitInputBeforeSaving(string lines, string expected)
    {
        using var sandbox = new Sandbox();
        var model = new BusinessDouble();
        await Frame(sandbox, model);
        var file = sandbox.File("rules.md", "Contrats");
        var setup = new BusinessSourceSetup(sandbox.Workspace, model, new StringReader(lines), TextWriter.Null, true);
        await setup.Prepare(file, null, null, null, describe: true, analyze: false, default);
        var draft = Directory.GetFiles(Path.Combine(sandbox.Workspace.Local, "business", "source-notes")).Single();
        Assert.Equal(expected, Json.Read<string>(File.ReadAllText(draft)));
        Assert.Equal(0, model.QualificationCalls);
    }

    [Fact]
    public async Task LargeDocumentUsesModelChosenReadingsAndResumesWithEarlierCitations()
    {
        using var s=new Sandbox();var model=new BusinessDouble();await Frame(s,model);
        var units=Enumerable.Range(1,3).Select(n=>new Unit("proof"+n,"sheet:A; row:"+n,new string((char)('a'+n),50000),"A")).ToArray();
        model.Reading=r=>r.Visible.Length==0?new("read","Comprendre les trois règles.",[new(1,1)],[],[],[],[],"not-applicable"):
            r.Visible[0].Number==1?new("read","Première règle consultée ; poursuivre.",[new(2,2)],[],[],[],[],"not-applicable"):
            new("finish","Deux règles interprétées ; la troisième reste à examiner.",[],[new(){Entity="Contrat",Kind="rule",Statement="Rapprochement proposé entre deux règles.",Contribution=Contribution.Hypothesis,EvidenceIds=["e1","e2"],Uncertain=true}],[],[],["Troisième règle à consulter"],"not-applicable");
        AnalysisRequest Context(Unit[] read,string plan,int budget)=>new("Unknown",read,[],[],[],"business",null,null);
        var accepted=new List<Analysis>();var reader=new BusinessDocuments(s.Workspace,model);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>reader.Run("test","large.xlsx","key",".xlsx",units,[],null,false,new(1),Context,(a,_)=>accepted.Add(a),default));
        accepted.Clear();var result=await reader.Run("test","large.xlsx","key",".xlsx",units,[],null,false,new(3),Context,(a,_)=>accepted.Add(a),default);
        Assert.False(result.Complete);Assert.Equal(2,result.SuppliedUnits);Assert.Equal(3,model.DocumentCalls);
        Assert.Equal(new[]{"proof1","proof2"},Assert.Single(accepted.SelectMany(a=>a.Findings)).EvidenceIds);
    }
    [Fact]
    public async Task InvalidProofNeverPublishesKnowledgeAndValidatedRepliesCanBeReused()
    {
        using var s=new Sandbox();var model=new BusinessDouble();await Frame(s,model);
        var file=s.File("rules.md","Un contrat suspendu n'est pas consultable.");
        model.Reading=r=>BusinessDouble.Finish(r) with{Findings=[new(){Entity="Contrat",Kind="rule",Statement="Erreur",Contribution=Contribution.New,EvidenceIds=["invented"]}]};
        var engine=new Ingestor(s.Workspace,model);var baseline=s.Workspace.Revision;
        await Assert.ThrowsAsync<InvalidDataException>(()=>engine.Run(file,"test",null,false,default,guided:true));
        Assert.Equal(baseline,s.Workspace.Revision);Assert.Empty(s.Workspace.Load().Claims);
        model.Reading=null;await engine.Run(file,"test",null,false,default,guided:true);Assert.Single(s.Workspace.Load().Claims);
    }
    [Fact]
    public async Task DecisionsSurviveReanalysisAndRelationsHavePreciseEvidence()
    {
        using var s=new Sandbox();var model=new BusinessDouble();await Frame(s,model);var file=s.File("rules.md","Le conseiller consulte le contrat.\n");
        model.Reading=r=>BusinessDouble.Finish(r) with
        {
            Findings=[new(){Entity="Conseiller",Kind="actor",Statement="Le conseiller consulte le contrat.",Contribution=Contribution.New,EvidenceIds=["e1"]},new(){Entity="Consultation",Kind="process",Statement="La consultation porte sur le contrat.",Contribution=Contribution.New,EvidenceIds=["e1"]}],
            Relations=[new("Conseiller","Consultation",RelationKind.Performs,["e1"])]
        };
        var engine=new Ingestor(s.Workspace,model);await engine.Run(file,"test",null,false,default,guided:true);
        var claim=s.Workspace.Load().Claims[0];var decision=s.Workspace.Decide(claim.Id,"accept","Confirmé pour ce périmètre","Alice");
        await engine.Run(file,"test",null,true,default,guided:true);
        Assert.Contains(s.Workspace.Decisions(),d=>d.Id==decision.Id);Assert.Single(s.Workspace.Load().Relations);
    }
    [Fact]
    public async Task InteractivePauseResumesWithoutRepeatingTheAnswer()
    {
        using var s=new Sandbox();Enable(s);var model=new BusinessDouble();var interview=new BusinessInterview(s.Workspace,model);var output=new StringWriter();
        await interview.Interactive(new StringReader("Comprendre les contrats\n/pause\n"),output,default);
        Assert.Empty(s.Workspace.Load().Projects);Assert.Contains("/confirm",output.ToString());
        await interview.Interactive(new StringReader("/confirm\n"),output,default);
        Assert.Single(s.Workspace.Load().Projects);Assert.Equal(1,model.FrameCalls);
    }
    [Fact]
    public async Task MixedDocumentKeepsCurrentAndTargetClaimsSeparate()
    {
        using var s=new Sandbox();var model=new BusinessDouble();await Frame(s,model);var file=s.File("migration.md","Ancien : contrat suspendu masqué. Cible proposée : contrat suspendu visible avec avertissement.");
        model.Reading=r=>BusinessDouble.Finish(r) with{Findings=[
            new(){Entity="Visibilité",Kind="rule",Statement="Le contrat suspendu est masqué dans l'ancien système.",Contribution=Contribution.New,EvidenceIds=["e1"],Role=SourceRole.Legacy},
            new(){Entity="Visibilité",Kind="rule",Statement="La cible propose un avertissement pour le contrat suspendu.",Contribution=Contribution.New,EvidenceIds=["e1"],Role=SourceRole.Target,Uncertain=true}]};
        await new Ingestor(s.Workspace,model).Run(file,"test",null,false,default,guided:true);
        var claims=s.Workspace.Load().Claims;Assert.Contains(claims,c=>c.Role==SourceRole.Legacy);Assert.Contains(claims,c=>c.Role==SourceRole.Target&&c.NeedsReview);
        Assert.All(new SearchEngine(s.Workspace).Search(new("Visibilité",Role:SourceRole.Target)).Hits,h=>Assert.Equal(SourceRole.Target,h.Role));
    }
    [Fact]
    public async Task BusinessSearchPrefersAReusableRuleOverHundredsOfMatchingRows()
    {
        using var s=new Sandbox();var model=new BusinessDouble();await Frame(s,model);
        var file=s.File("contrats.md","Le conseiller vérifie les droits avant la consultation des contrats.");
        await new Ingestor(s.Workspace,model).Run(file,"test",null,false,default,guided:true);
        var k=s.Workspace.Load();var source=k.Sources[0];
        var raw=Enumerable.Range(1,1000).Select(i=>new Evidence("raw"+i,"test","inventaire.xlsx","raw-version","row:"+i,"Contrat consultation droits conseiller contrat contrat "+i)).ToArray();
        k.Evidence.AddRange(raw);k.Sources.Add(new("raw-source","test","inventaire.xlsx","raw-version","raw-key",SourceRole.Current,raw.Select(e=>e.Id).ToArray(),[],true,[],source.Context,false));s.Workspace.Save(k);s.Workspace.Rebuild();
        var result=new SearchEngine(s.Workspace).Search(new("droits consultation contrat"),readOnly:true);
        Assert.Equal("claim",result.Hits[0].Kind);Assert.Contains("conseiller",result.Hits[0].Text);
    }
    [Theory]
    [InlineData("study.xls")]
    [InlineData("study.xlsx")]
    public void WorkbookReaderPreservesCoordinatesWithoutAssumingHeaders(string name)
    {
        var result=BusinessExtraction.Read(Path.Combine(AppContext.BaseDirectory,"Fixtures",name),10_000_000);
        Assert.NotEmpty(result.Units);Assert.Contains(result.Units,u=>u.Locator.Contains("row:1"));Assert.DoesNotContain(result.Units,u=>u.Text.StartsWith("Headers:"));
    }
    [Fact]
    public void LongCellAndFormulaAndMergedRangeArePreserved()
    {
        using var s=new Sandbox();var path=Path.Combine(s.Source,"book.xlsx");
        using(var template=ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","study.xlsx")))
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
        foreach(var entry in template.Entries)
        {
            using var output=zip.CreateEntry(entry.FullName).Open();
            if(entry.FullName=="xl/worksheets/sheet1.xml")
            {
                using var writer=new StreamWriter(output);writer.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><dimension ref=\"A1:B3\"/><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>"+new string('é',3500)+"</t></is></c></row><row r=\"3\"><c r=\"A3\"><f>1+2</f><v>3</v></c></row></sheetData><mergeCells><mergeCell ref=\"A1:B1\"/></mergeCells></worksheet>");
            }
            else{using var input=entry.Open();input.CopyTo(output);}
        }
        var result=BusinessExtraction.Read(path,10_000_000);Assert.Contains(result.Units,u=>u.Text.Contains(new string('é',3500)));
        Assert.Contains(result.Units,u=>u.Locator.Contains("formula")&&u.Text.Contains("1+2"));Assert.Contains(result.Units,u=>u.Text.Contains("A1:B1"));
    }
    [Fact]
    public void ResearchKeepsRelevantFramingWithoutSendingTheWholeInterview()
    {
        var items=Enumerable.Range(0,20).Select(i=>new BriefItem("process",(i==19?"Suspension des contrats ":"Autre processus ")+new string('x',450),"declared",["turn"])).ToArray();
        var brief=new ConfirmedBrief(new("iBip","Comprendre les règles","Contrats",items,[]),[new("turn","user",new string('y',4000),DateTimeOffset.UtcNow,[])],"Alice",DateTimeOffset.UtcNow);
        var profile=new ProjectProfile("p","iBip","Comprendre","À préciser",[],[],[],[],[],brief);
        var compact=Research.Compact(profile,"Suspension des contrats");
        Assert.Empty(compact.Brief!.Declarations);Assert.Contains(compact.Brief.Content.Items,i=>i.Text.StartsWith("Suspension"));Assert.True(Json.Serialize(compact).Length<6000);
        Assert.Equal(20,profile.Brief!.Content.Items.Length);Assert.Single(profile.Brief.Declarations);
    }
    [Fact]
    public void PreciseTechnicalProofsDoNotAttachWholeFileToEveryMethod()
    {
        using var s=new Sandbox();var path=s.File("Sample.cs","class Sample {\n public int First() { return 1; }\n public int Second() { return 2; }\n}\n");
        var bytes=File.ReadAllBytes(path);var extraction=Extractor.Read(path,10000);var proofs=extraction.Units.Select(u=>new Evidence(u.Id,"test","Sample.cs","v",u.Locator,u.Text)).ToArray();
        var result=TechnicalExtraction.Read(bytes,".cs","test",new("iBip","Contrats","Code",SourceStatus.Observed),proofs,true);
        var first=result.Objects.Single(o=>o.Name.Contains("First()"));Assert.NotEmpty(first.EvidenceIds);
        Assert.All(first.EvidenceIds,id=>Assert.DoesNotContain("Second()",proofs.Single(e=>e.Id==id).Excerpt));
    }
}
