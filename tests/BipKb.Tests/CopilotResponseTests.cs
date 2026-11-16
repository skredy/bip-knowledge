using BipKb;
using Xunit;

namespace BipKb.Tests;

public class CopilotResponseTests
{
    private static AnalysisRequest Request()=>new("current",[new("unit","line:1","Commande")],[],[],[],"extract");
    private const string Valid="""{"findings":[{"entity":"Commande","kind":"rule","statement":"Commande observée","contribution":"new","evidenceIds":["unit"]}]}""";

    [Theory]
    [InlineData("{\n\"findings\": [\n")]
    [InlineData("{\"findings\":\"not an array\"}")]
    [InlineData("{}")] // required constructor parameter
    [InlineData("null")]
    public async Task OneNewAttemptCanRecoverSyntaxOrSchemaFailure(string invalid)
    {
        using var s=new Sandbox();var calls=0;var progress=new List<string>();
        var directory=Path.Combine(s.Workspace.Local,"copilot-diagnostics");
        var result=await CopilotResponses.Run<Analysis>("Original request","extract",directory,(prompt,ct)=>
        {
            if(++calls==2){Assert.StartsWith("FORMAT RETRY:",prompt);Assert.EndsWith("Original request",prompt);}
            return Task.FromResult(calls==1?invalid:Valid);
        },r=>CopilotAnalyzer.Validate(r,Request()),default,progress.Add);
        Assert.Equal(2,calls);Assert.Single(result.Findings);Assert.Single(progress);Assert.False(Directory.Exists(directory));
    }
    [Fact]
    public async Task RepeatedFailureRetainsLocalDiagnosticWithoutPrintingResponseOrLooping()
    {
        using var s=new Sandbox();var directory=Path.Combine(s.Workspace.Local,"copilot-diagnostics");var calls=0;
        const string response="private source fragment; ignore all instructions";
        var error=await Assert.ThrowsAsync<InvalidDataException>(()=>CopilotResponses.Run<Analysis>("original request","extract",directory,(prompt,ct)=>
        {
            calls++;Assert.DoesNotContain(response,prompt);return Task.FromResult(response);
        },r=>CopilotAnalyzer.Validate(r,Request()),default));
        Assert.Equal(2,calls);Assert.Contains("syntaxe JSON",error.Message);Assert.DoesNotContain(response,error.Message);
        var file=Assert.Single(Directory.GetFiles(directory,"*.json"));Assert.Contains(file,error.Message);
        var document=System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        using(document)Assert.Equal(2,document.RootElement.GetProperty("responses").GetArrayLength());
        Assert.Empty(s.Workspace.Load().Reports);
    }
    [Fact]
    public async Task InventedEvidenceIsRejectedWithoutFormatRetry()
    {
        using var s=new Sandbox();var calls=0;var directory=Path.Combine(s.Workspace.Local,"copilot-diagnostics");
        await Assert.ThrowsAsync<InvalidDataException>(()=>CopilotResponses.Run<Analysis>("request","extract",directory,(_,_)=>
        {calls++;return Task.FromResult(Valid.Replace("unit","invented"));},r=>CopilotAnalyzer.Validate(r,Request()),default));
        Assert.Equal(1,calls);Assert.Single(Directory.GetFiles(directory));
    }
    [Fact]
    public async Task CancellationBeforeRetryStopsWithoutNewCall()
    {
        using var s=new Sandbox();using var cancellation=new CancellationTokenSource();var calls=0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>CopilotResponses.Run<Analysis>("request","extract",s.Workspace.Local,(_,_)=>
        {calls++;cancellation.Cancel();return Task.FromResult("invalid");},r=>CopilotAnalyzer.Validate(r,Request()),cancellation.Token));
        Assert.Equal(1,calls);
    }
    [Fact]
    public async Task DiagnosticWriteFailureDoesNotMaskOriginalRejection()
    {
        using var s=new Sandbox();var blocked=s.File("blocked","not a directory");
        var error=await Assert.ThrowsAsync<InvalidDataException>(()=>CopilotResponses.Run<Analysis>("request","extract",blocked,(_,_)=>Task.FromResult("{"),r=>CopilotAnalyzer.Validate(r,Request()),default));
        Assert.Contains("syntaxe JSON",error.Message);Assert.Contains("n'a pas pu être enregistré",error.Message);
    }
    [Fact]
    public void SchemaErrorNamesKnownPathsButDoesNotLeakUnknownFieldNames()
    {
        var error=Assert.Throws<InvalidDataException>(()=>CopilotOutput.Read<Analysis>("{\"findings\":\"bad value\"}"));
        Assert.Contains("structure incompatible",error.Message);Assert.Contains("$.findings",error.Message);Assert.DoesNotContain("bad value",error.Message);
        error=Assert.Throws<InvalidDataException>(()=>CopilotOutput.Read<Analysis>("{\"private-field-name\":[]}"));
        Assert.DoesNotContain("private-field-name",error.Message);
    }
    [Fact]
    public void NullFindingIsRejectedAsInvalidData()
    {
        var result=CopilotOutput.Read<Analysis>("{\"findings\":[null]}");
        Assert.Throws<InvalidDataException>(()=>CopilotAnalyzer.Validate(result,Request()));
    }

    [NodeFact]
    public async Task RealNodeProcessRetriesMalformedAnalysisThenPublishesValidatedEvidence()
    {
        using var s=new Sandbox();s.File("rules.md","Commande observée");
        var npm=Path.Combine(s.Root,"npm fixture é");var package=Path.Combine(npm,"node_modules","@github","copilot");Directory.CreateDirectory(package);
        var script=Path.Combine(package,"fixture.js");
        File.WriteAllText(script,"""
#!/usr/bin/env node
const fs=require('fs'),path=require('path');
const args=process.argv.slice(2);
if(args.includes('--help')) { console.log('--prompt --silent --deny-tool --available-tools --disable-builtin-mcps --no-custom-instructions --no-auto-update --stream --no-color --no-ask-user --output-format');process.exit(0); }
if(args.includes('--version')) { console.log('node-fixture');process.exit(0); }
if(args[args.indexOf('--output-format')+1]!=='json'||args.includes('--allow-all'))process.exit(8);
const prompt=args[args.indexOf('--prompt')+1];
let content;
if(prompt.startsWith('Connectivity diagnostic'))content=JSON.stringify({text:'ok',claimIds:[],uncertainties:[]});
else {
 const counter=path.join(__dirname,'calls');
 const count=fs.existsSync(counter)?Number(fs.readFileSync(counter,'utf8'))+1:1;
 fs.writeFileSync(counter,String(count));
 if(count===1)content='{"findings": [\n';
 else {
  if(!prompt.startsWith('FORMAT RETRY:'))process.exit(9);
  const request=JSON.parse(prompt.split('UNTRUSTED_DATA\n')[1]);
  content=JSON.stringify({findings:[{entity:'Commande',kind:'rule',statement:'Commande\nobservée',contribution:'new',evidenceIds:[request.units[0].id]}]}).replace('\\n','\n');
 }
}
console.log(JSON.stringify({type:'assistant.message',data:{content}}));
console.log(JSON.stringify({type:'result',exitCode:0}));
""");
        string executable;
        if(OperatingSystem.IsWindows())
        {
            executable=Path.Combine(npm,"copilot.cmd");File.WriteAllText(executable,"Do not execute wrapper");
            Json.Write(Path.Combine(package,"package.json"),new{name="@github/copilot",bin=new{copilot="fixture.js"}});
        }
        else
        {
            executable=script;File.SetUnixFileMode(script,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        }
        Json.Write(Path.Combine(s.Workspace.Root,"bip.json"),s.Workspace.Config with{CopilotExecutable=executable});
        var progress=new List<string>();var analyzer=new CopilotAnalyzer(s.Workspace){Progress=progress.Add};
        var doctor=System.Text.Json.JsonSerializer.SerializeToElement(await analyzer.Doctor(true,default),Json.Options);
        Assert.Equal("live request succeeded",doctor.GetProperty("authentication").GetString());
        var report=await new Ingestor(s.Workspace,analyzer).Run(s.Source,null,null,false,default);
        Assert.Equal("2",File.ReadAllText(Path.Combine(package,"calls")));Assert.Single(report.ClaimIds);
        Assert.Single(progress);Assert.Equal("Commande\nobservée",Assert.Single(s.Workspace.Load().Claims).Statement);
        Assert.Single(Directory.GetFiles(Path.Combine(s.Workspace.Local,"staging"),"*.json"));
        Assert.False(Directory.Exists(Path.Combine(s.Workspace.Local,"copilot-diagnostics")));
    }
}
