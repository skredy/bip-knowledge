using BipKb;
using Xunit;
namespace BipKb.Tests;

public sealed class UnixFactAttribute:FactAttribute
{
    public UnixFactAttribute(){if(OperatingSystem.IsWindows())Skip="Unix process fixture; run Windows live validation separately.";}
}
public sealed class LiveFactAttribute:FactAttribute
{
    public LiveFactAttribute(){if(Environment.GetEnvironmentVariable("BIP_KB_LIVE_TESTS")!="1")Skip="Requires authenticated Copilot and BIP_KB_LIVE_TESTS=1.";}
}
public class AdapterTests
{
    [UnixFact] public async Task RealProcessBoundaryValidatesStructuredOutput()
    {
        using var s=new Sandbox();var script=Path.Combine(s.Root,"copilot-test");
        File.WriteAllText(script,"""
#!/bin/sh
case "$1" in
 --help) echo '--prompt --silent --deny-tool --available-tools --disable-builtin-mcps --no-custom-instructions --no-auto-update --stream --attachment --no-color --no-ask-user'; exit 0;;
 --version) echo fixture; exit 0;;
esac
printf '%s\n' '{"findings":[{"entity":"Commande","kind":"rule","statement":"Commande validée","contribution":"new","evidenceIds":["unit"],"relatedClaimIds":[],"uncertain":false}]}'
""");
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(script,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        Json.Write(Path.Combine(s.Workspace.Root,"bip.json"),s.Workspace.Config with{CopilotExecutable=script});
        var result=await new CopilotAnalyzer(s.Workspace).Analyze(new("current",[new("unit","line:1","Commande validée")],[],[],[],"extract"),null,default);
        Assert.Equal("Commande validée",Assert.Single(result.Findings).Statement);
        Assert.Empty(Directory.GetDirectories(Path.Combine(s.Workspace.Local,"copilot")));
    }
    [UnixFact] public async Task ResearchProcessUsesStructuredCitationsAndCleansTemporaryDirectory()
    {
        using var s=new Sandbox();var script=Path.Combine(s.Root,"research-test");
        File.WriteAllText(script,"""
#!/bin/sh
if [ "$1" = "--help" ]; then
 echo '--prompt --silent --deny-tool --available-tools --disable-builtin-mcps --no-custom-instructions --no-auto-update --stream --no-color --no-ask-user'; exit 0
fi
printf '%s\n' '{"answer":"Une facture possède un statut [unit]","citations":["unit"],"nextQueries":[],"questions":[]}'
""");
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(script,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        Json.Write(Path.Combine(s.Workspace.Root,"bip.json"),s.Workspace.Config with{CopilotExecutable=script});
        var hit=new SearchHit("unit","evidence","Statut","La facture possède un statut",null,null,null,["unit"],1,[],KnowledgeState.Proposed);
        var result=await new CopilotAnalyzer(s.Workspace).Investigate(new("Statut",null,[hit],[],[],1),default);
        Assert.Contains("unit",result.Citations);Assert.Empty(Directory.GetDirectories(Path.Combine(s.Workspace.Local,"copilot")));
    }
    [LiveFact] public async Task AuthenticatedCopilotCanIngestSqlAndAnswerWithCitations()
    {
        using var s=new Sandbox();s.File("commande.sql","CREATE TABLE dbo.Commande(Id int PRIMARY KEY, Statut nvarchar(30));");
        var analyzer=new CopilotAnalyzer(s.Workspace);
        var report=await new Ingestor(s.Workspace,analyzer).Run(s.Source,null,null,false,default);
        Assert.NotEmpty(report.ClaimIds);var claims=s.Workspace.Search("Commande");Assert.NotEmpty(claims);
        var answer=await analyzer.Ask("Quelles colonnes possède Commande ?",claims.ToArray(),default);
        Assert.NotEmpty(answer.ClaimIds);Assert.Contains("Commande",new KnowledgeService(s.Workspace).Context("Commande"));
        var hits=new SearchEngine(s.Workspace).Search(new("Commande")).Hits;
        var research=await analyzer.Investigate(new("Quelles colonnes possède Commande ?",null,hits,[],Research.Coverage(hits),1),default);
        Assert.NotEmpty(research.Citations);
    }
}
