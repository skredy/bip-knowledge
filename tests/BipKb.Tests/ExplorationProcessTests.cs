using BipKb;
using Xunit;

namespace BipKb.Tests;

public class ExplorationProcessTests
{
    [LiveFact]
    public async Task AuthenticatedGuidedExplorationCanReadAndCiteAnExcelWorkbook()
    {
        using var s=new Sandbox();var file=Path.Combine(s.Source,"study.xls");File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","study.xls"),file);
        Json.Write(Path.Combine(s.Workspace.Root,"bip.json"),s.Workspace.Config with{TimeoutSeconds=45});
        var report=await new Ingestor(s.Workspace,new CopilotAnalyzer(s.Workspace)).Run(file,null,null,false,default,guided:true,maxCalls:6);
        Assert.Equal("guided",report.Mode);var source=Assert.Single(s.Workspace.Load().Sources);
        Assert.NotNull(source.Exploration);Assert.NotEmpty(source.ClaimIds);
        Assert.All(s.Workspace.Load().Claims,c=>Assert.All(c.EvidenceIds,id=>Assert.Contains(id,source.EvidenceIds)));
    }
    [NodeFact]
    public async Task InstalledAdapterUsesJsonlAndResumesAfterRejectedEvidenceWithoutWideningPermissions()
    {
        using var s=new Sandbox();var file=Path.Combine(s.Source,"study.xls");File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","study.xls"),file);
        var npm=Path.Combine(s.Root,"node fixture");var package=Path.Combine(npm,"node_modules","@github","copilot");Directory.CreateDirectory(package);
        var script=Path.Combine(package,"guided.js");File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","copilot-guided.js"),script);
        string executable;
        if(OperatingSystem.IsWindows())
        {
            executable=Path.Combine(npm,"copilot.cmd");File.WriteAllText(executable,"Do not execute wrapper");
            Json.Write(Path.Combine(package,"package.json"),new{name="@github/copilot",bin=new{copilot="guided.js"}});
        }
        else {executable=script;File.SetUnixFileMode(script,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);}
        Json.Write(Path.Combine(s.Workspace.Root,"bip.json"),s.Workspace.Config with{CopilotExecutable=executable});
        File.WriteAllText(Path.Combine(package,"reject-next"),"");
        var analyzer=new CopilotAnalyzer(s.Workspace);var engine=new Ingestor(s.Workspace,analyzer);
        var error=await Assert.ThrowsAsync<InvalidDataException>(()=>engine.Run(file,null,null,false,default,guided:true));
        Assert.Contains("evidenceIds",error.Message);Assert.DoesNotContain("unknown-proof",error.Message);Assert.Empty(s.Workspace.Load().Claims);
        Assert.Single(Directory.GetFiles(Path.Combine(s.Workspace.Local,"copilot-diagnostics")));
        var report=await engine.Run(file,null,null,false,default,guided:true);Assert.Equal("guided",report.Mode);
        Assert.Equal(3,File.ReadAllLines(Path.Combine(package,"guided-calls.jsonl")).Length);
        Assert.True(Assert.Single(s.Workspace.Load().Sources).Exploration!.Complete);
        var messages="{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"analysis\",\"arguments\":{\"id\":\"test\"}}}\n";
        using var output=new StringWriter();await Mcp.Run(s.Workspace,new StringReader(messages),output,default);
        Assert.Contains("guided-v1",output.ToString());Assert.DoesNotContain("isError\":true",output.ToString());
    }
}
