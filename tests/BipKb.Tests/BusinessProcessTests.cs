using BipKb;
using Xunit;

namespace BipKb.Tests;

public class BusinessProcessTests
{
    [NodeFact]
    public async Task FramingQualificationAndWorkbookPassTheRealProcessBoundaryAndRetryBadProofs()
    {
        using var s=new Sandbox();
        var package=Path.Combine(s.Root,"node_modules","@github","copilot");Directory.CreateDirectory(package);
        var script=Path.Combine(package,"business.js");File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","copilot-business.js"),script);
        string executable;
        if(OperatingSystem.IsWindows())
        {
            executable=Path.Combine(s.Root,"copilot.cmd");File.WriteAllText(executable,"wrapper must not be executed");
            Json.Write(Path.Combine(package,"package.json"),new{name="@github/copilot",bin=new{copilot="business.js"}});
        }
        else{executable=script;File.SetUnixFileMode(script,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);}
        Json.Write(Path.Combine(s.Workspace.Root,"bip.json"),s.Workspace.Config with{BusinessMode=true,CopilotExecutable=executable});
        var copilot=new CopilotAnalyzer(s.Workspace);var interview=new BusinessInterview(s.Workspace,copilot);
        await interview.Respond("Comprendre les règles de consultation des contrats é 漢字 👋",default);interview.Confirm("Alice");
        var file=Path.Combine(s.Source,"study.xlsx");File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","study.xlsx"),file);
        var source=await new BusinessSourceSetup(s.Workspace,copilot,TextReader.Null,TextWriter.Null,false).Prepare(file,null,null,"Étude à rapprocher du projet",true,true,default);
        File.WriteAllText(Path.Combine(package,"bad-proof-once"),"");
        await new Ingestor(s.Workspace,copilot).Run(file,source,null,false,default,guided:true);
        var calls=File.ReadAllLines(Path.Combine(package,"business-calls.jsonl"));Assert.Equal(4,calls.Length);Assert.Single(calls,c=>c.Contains("\"retry\":true"));
        var k=s.Workspace.Load();Assert.Single(k.Claims);Assert.DoesNotContain(k.Claims.SelectMany(c=>c.EvidenceIds),id=>id=="unknown");
        Assert.True(Assert.Single(k.Sources).Business!.Complete);
        Assert.Contains("Cadrage métier",new KnowledgeService(s.Workspace).Context("accès contrat"));
    }
    [LiveFact]
    public async Task AuthenticatedBusinessFramingAndExcelHaveSourcedFindings()
    {
        using var s=new Sandbox();Json.Write(Path.Combine(s.Workspace.Root,"bip.json"),s.Workspace.Config with{BusinessMode=true,TimeoutSeconds=90});
        var copilot=new CopilotAnalyzer(s.Workspace);var framing=new BusinessInterview(s.Workspace,copilot);
        var draft=await framing.Respond("Projet iBip. Objectif : comprendre les règles métier des commandes. Périmètre initial : consultation des commandes. Je dispose d'une étude Excel des endpoints. Ces informations suffisent pour commencer ; conservez les autres inconnues ouvertes et proposez le récapitulatif.",default);
        Assert.True(draft.Reply!.Ready);framing.Confirm("Test live");
        var file=Path.Combine(s.Source,"study.xlsx");File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","study.xlsx"),file);
        await new BusinessSourceSetup(s.Workspace,copilot,TextReader.Null,TextWriter.Null,false).Prepare(file,"test",null,"Étude des endpoints de commandes, statut à confirmer.",true,true,default);
        await new Ingestor(s.Workspace,copilot).Run(file,"test",null,false,default,guided:true,maxCalls:6);
        var k=s.Workspace.Load();Assert.NotEmpty(k.Claims);Assert.NotNull(Assert.Single(k.Sources).Business);
        Assert.All(k.Claims,c=>Assert.All(c.EvidenceIds,id=>Assert.Contains(k.Evidence,e=>e.Id==id)));
    }
}
