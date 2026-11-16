using BipKb;
using Xunit;

namespace BipKb.Tests;

public sealed class NodeFactAttribute:FactAttribute
{
    public NodeFactAttribute(){if(CopilotLaunchTests.NodeExecutable()==null)Skip="Node.js is optional; required only to exercise the npm launcher process boundary.";}
}

public class CopilotLaunchTests
{
    public static string? NodeExecutable()
    {
        var name=OperatingSystem.IsWindows()?"node.exe":"node";
        return (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator).Where(p=>p.Length>0).Select(p=>Path.Combine(p.Trim('"'),name)).FirstOrDefault(File.Exists);
    }
    private static string Put(string root,string name,string text="fixture")
    {
        var file=Path.Combine(root,name);Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,text);return file;
    }
    private static string Npm(string directory,string entry="npm-loader.js")
    {
        Put(directory,"copilot.cmd","This wrapper must never run");Put(directory,"copilot.ps1","throw 'Never execute the wrapper'");
        var package=Path.Combine(directory,"node_modules","@github","copilot");
        Put(package,"package.json",System.Text.Json.JsonSerializer.Serialize(new{name="@github/copilot",bin=new{copilot=entry}}));
        return Put(package,entry,"console.log('fixture')");
    }
    [Fact]
    public void VscodeBootstrapperAloneProducesActionableError()
    {
        using var s=new Sandbox();var shim=Path.Combine(s.Root,"github.copilot-chat","copilotCli");
        Put(shim,"copilot.ps1","throw 'Must not execute VS Code bootstrapper'");Put(shim,"copilot.bat");
        var error=Assert.Throws<FileNotFoundException>(()=>CopilotLaunch.Resolve("copilot",s.Root,true,[shim]));
        Assert.Contains("VS Code",error.Message);Assert.Contains("winget install",error.Message);Assert.Contains("bip-kb doctor",error.Message);
    }
    [Fact]
    public void NativeExecutableAfterVscodeWrapperIsResolvedWithoutRunningTheWrapper()
    {
        using var s=new Sandbox();var shim=Path.Combine(s.Root,"vscode");var native=Path.Combine(s.Root,"Program Files","Copilot");
        Put(shim,"copilot.ps1");var file=Put(native,"copilot.exe");
        var launch=CopilotLaunch.Resolve("copilot",s.Root,true,[shim,native]);Assert.Equal(file,launch.Executable);Assert.Empty(launch.PrefixArguments);
    }
    [Theory]
    [InlineData("copilot")]
    [InlineData("copilot.cmd")]
    [InlineData("copilot.ps1")]
    public void NpmShimsResolveThroughPackageMetadataAndNode(string command)
    {
        using var s=new Sandbox();var npm=Path.Combine(s.Root,"npm spaces & accents é");var node=Path.Combine(s.Root,"Node JS");
        var entry=Npm(npm);var executable=Put(node,"node.exe");
        var launch=CopilotLaunch.Resolve(command,s.Root,true,[npm,node]);Assert.Equal(executable,launch.Executable);Assert.Equal([entry],launch.PrefixArguments);Assert.Equal("npm-node",launch.Kind);
    }
    [Fact]
    public void ExplicitRelativeExecutableIsResolvedBeforeChangingWorkingDirectory()
    {
        using var s=new Sandbox();var exe=Put(s.Root,Path.Combine("tools","copilot.exe"));
        var launch=CopilotLaunch.Resolve(Path.Combine("tools","copilot.exe"),s.Root,true,[]);
        Assert.Equal(exe,launch.Start(s.Workspace.Local).FileName);
    }
    [Fact]
    public void MissingNodeAndEscapingPackageEntryAreNotExecuted()
    {
        using var s=new Sandbox();var npm=Path.Combine(s.Root,"npm");Npm(npm);
        var error=Assert.Throws<FileNotFoundException>(()=>CopilotLaunch.Resolve("copilot",s.Root,true,[npm]));Assert.Contains("node.exe",error.Message);
        var package=Path.Combine(npm,"node_modules","@github","copilot");
        Put(package,"package.json","""{"name":"@github/copilot","bin":{"copilot":"../../../outside.js"}}""");Put(npm,"outside.js");Put(npm,"node.exe");
        Assert.Throws<FileNotFoundException>(()=>CopilotLaunch.Resolve("copilot",s.Root,true,[npm]));
    }
    [Fact]
    public void CustomWrapperWithoutKnownNpmPackageIsNotInterpretedAsShellCode()
    {
        using var s=new Sandbox();var path=Put(s.Root,"copilot.ps1","Invoke-Expression $args[0]");
        Assert.Throws<FileNotFoundException>(()=>CopilotLaunch.Resolve(path,s.Root,true,[s.Root]));
    }
    [NodeFact]
    public async Task NodeLaunchPreservesJsonQuotesNewlinesAndShellCharacters()
    {
        using var s=new Sandbox();var npm=Path.Combine(s.Root,"npm space & é");var script=Npm(npm);
        var node=NodeExecutable();
        if(OperatingSystem.IsWindows())File.Copy(node!,Path.Combine(npm,"node.exe"));else File.CreateSymbolicLink(Path.Combine(npm,"node.exe"),node!);
        File.WriteAllText(script,"process.stdout.write(JSON.stringify(process.argv.slice(2)))");
        var launch=CopilotLaunch.Resolve("copilot",s.Root,true,[npm]);var start=launch.Start(s.Root);
        var payload="{\"text\":\"C:\\\\Chemin avec espaces\\\\; & echo NO $(echo NO) %PATH% `texte`\"}\nDeuxième ligne é";
        foreach(var argument in CopilotAnalyzer.Arguments(payload,null))start.ArgumentList.Add(argument);
        var result=await Processes.Run(start,TimeSpan.FromSeconds(10),default);Assert.Equal(0,result.ExitCode);
        var args=Json.Read<string[]>(result.Output);Assert.Equal(CopilotAnalyzer.Arguments(payload,null),args);
        Assert.False(start.UseShellExecute);Assert.DoesNotContain("-Command",args);
    }
}
