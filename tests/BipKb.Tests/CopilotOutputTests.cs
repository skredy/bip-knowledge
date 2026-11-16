using System.Text.Json;
using BipKb;
using Xunit;

namespace BipKb.Tests;

public class CopilotOutputTests
{
    [Fact]
    public void RawNewlineAtByte100IsEncodedWithoutChangingItsMeaning()
    {
        var text=new string('a',91)+"\nConnexion disponible";
        var response="{\"text\":\""+text+"\",\"claimIds\":[],\"uncertainties\":[]}";
        var error=Assert.Throws<JsonException>(()=>Json.Read<Answer>(response));Assert.Equal(100,error.BytePositionInLine);
        Assert.Equal(text,CopilotOutput.Read<Answer>(response).Text);
    }
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\t")]
    public void LiteralWhitespaceInsideStringsSurvivesStrictDeserialization(string separator)
    {
        var response="{\"text\":\"un"+separator+"deux\",\"claimIds\":[],\"uncertainties\":[]}";
        Assert.Equal("un"+separator+"deux",CopilotOutput.Read<Answer>(response).Text);
    }
    [Fact]
    public void AlreadyEscapedJsonPathsQuotesAndPrettyPrintingAreUnchanged()
    {
        var original=new Answer("C:\\Dossier\\n.txt ; \"citation\" ; ligne\nsuite",["id"],["À vérifier"]);
        var response="```json\r\n"+Json.Serialize(original).Replace("\n","\r\n")+"\r\n```";
        Assert.Equal(Json.Serialize(original),Json.Serialize(CopilotOutput.Read<Answer>(response)));
    }
    [Theory]
    [InlineData("{\"text\":\"unfinished")]
    [InlineData("Answer: {\"text\":\"ok\",\"claimIds\":[],\"uncertainties\":[]}")]
    [InlineData("{\"text\":\"ok\",\"claimIds\":[],\"uncertainties\":[],\"unexpected\":true}")]
    [InlineData("{\"text\":\"ambiguous\\\nnewline\",\"claimIds\":[],\"uncertainties\":[]}")]
    [InlineData("{\"text\":\"bad\\Uescape\",\"claimIds\":[],\"uncertainties\":[]}")]
    public void OtherMalformedResponsesAndUnknownFieldsRemainRejected(string response)
    {Assert.Throws<InvalidDataException>(()=>CopilotOutput.Read<Answer>(response));}

    [Fact]
    public void NormalizationDoesNotInventOrAuthorizeEvidenceReferences()
    {
        var response="{\"findings\":[{\"entity\":\"Facture\",\"kind\":\"rule\",\"statement\":\"Une ligne\nUne autre\",\"contribution\":\"new\",\"evidenceIds\":[\"invented\"]}]}";
        var analysis=CopilotOutput.Read<Analysis>(response);
        Assert.Throws<InvalidDataException>(()=>CopilotAnalyzer.Validate(analysis,new("target",[new("provided","line:1","source")],[],[],[],"extract")));
    }
    private static string Line(object value)=>JsonSerializer.Serialize(value,Json.Options).Replace("\n","").Replace("\r","");
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MachineTransportUsesOnlyFinalFullAssistantMessage(bool flat)
    {
        var answer=Json.Serialize(new Answer("Réponse\nsur deux lignes",[],[]));
        var message=flat?Line(new{type="assistant.message",content=answer}):Line(new{type="assistant.message",data=new{content=answer}});
        var stream=Line(new{type="session.start",data=new{sessionId="s"}})+"\n"+Line(new{type="tool.execution_complete",data=new{content="not an answer"}})+"\n"+
            Line(new{type="assistant.message",data=new{content="Interim reply"}})+"\n"+Line(new{type="assistant.message_delta",data=new{deltaContent="ignored"}})+"\n"+message+"\n"+Line(new{type="result",exitCode=0});
        Assert.Equal(answer,CopilotOutput.JsonLines(stream));Assert.Equal("Réponse\nsur deux lignes",CopilotOutput.Read<Answer>(CopilotOutput.JsonLines(stream)).Text);
    }
    [Theory]
    [InlineData("{\"type\":\"result\",\"exitCode\":1}")]
    [InlineData("{\"type\":\"result\",\"exitCode\":0}")]
    [InlineData("{\"type\":\"assistant.message\",\"data\":{\"content\":\"{}\"}}")]
    [InlineData("{\"type\":\"session.error\",\"data\":{\"message\":\"failure\"}}")]
    [InlineData("non JSON banner")]
    public void FailedOrIncompleteMachineTransportIsRejected(string stream)
    {Assert.Throws<InvalidDataException>(()=>CopilotOutput.JsonLines(stream));}

    [UnixFact]
    public async Task DoctorLiveParsesJsonLinesAndLiteralNewlineThroughRealProcess()
    {
        using var s=new Sandbox();var script=Path.Combine(s.Root,"copilot-json-fixture");
        var answer="{\"text\":\"Connexion\nconfirmée\",\"claimIds\":[],\"uncertainties\":[]}";
        File.WriteAllText(script,"""
#!/bin/sh
case "$1" in
 --help) echo '--prompt --silent --deny-tool --available-tools --disable-builtin-mcps --no-custom-instructions --no-auto-update --stream --no-color --no-ask-user --output-format'; exit 0;;
 --version) echo fixture; exit 0;;
esac
if [ "$1" != "--output-format" ] || [ "$2" != "json" ]; then exit 7; fi
cat <<'JSONL'
"""+"\n"+Line(new{type="assistant.message",data=new{content=answer}})+"\n"+Line(new{type="result",exitCode=0})+"\nJSONL\n");
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(script,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        Json.Write(Path.Combine(s.Workspace.Root,"bip.json"),s.Workspace.Config with{CopilotExecutable=script});
        var result=JsonSerializer.SerializeToElement(await new CopilotAnalyzer(s.Workspace).Doctor(true,default),Json.Options);
        Assert.Equal("jsonl",result.GetProperty("responseFormat").GetString());Assert.Equal("Connexion\nconfirmée",result.GetProperty("completion").GetProperty("text").GetString());
    }
}
