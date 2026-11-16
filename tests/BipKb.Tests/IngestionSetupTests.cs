using BipKb;
using Xunit;

namespace BipKb.Tests;

public class IngestionSetupTests
{
    private static string NewSource(Sandbox s)
    {
        var path=Path.Combine(s.Root,"Étude SQL");Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path,"tables.sql"),"CREATE TABLE dbo.Facture(Id int);");return path;
    }
    private const string ContextAnswers="BIP\nFacturation\ntarget\nvalidated\nÉtude détaillée du modèle cible\nLes tables décrivent la cible\n";

    [Theory]
    [InlineData("Étude SQL")]
    [InlineData("etude.sql")]
    [InlineData("C:/documents")]
    [InlineData("source\n")]
    public void AllEntryPointsRejectTheSameInvalidIdentifiers(string id)
    {
        using var s=new Sandbox();Assert.NotNull(SourceIdentifier.Error(id));
        Assert.Throws<ArgumentException>(()=>s.Workspace.Bind(id,s.Source,SourceRole.Target));
        var descriptor=new SourceDescriptor(SourceRole.Target,new("BIP","Facturation","Modèle cible",SourceStatus.Validated));
        Assert.Throws<ArgumentException>(()=>s.Workspace.ConfigureSource(id,s.Source,s.Source,descriptor));
    }
    [Fact]
    public async Task InvalidAndDuplicateIdsAreRetriedBeforeAskingForContext()
    {
        using var s=new Sandbox();var path=NewSource(s);var output=new StringWriter();
        var input=new StringReader("Étude SQL\netude.sql\ntest\netude-sql\n"+ContextAnswers+"peut-être\nOUI\n");
        var id=await new SourceSetup(s.Workspace,input,output,true).Prepare(path,null,null,null,false,default);
        Assert.Equal("etude-sql",id);var transcript=output.ToString();
        Assert.True(transcript.IndexOf("Identifiant de source invalide.",StringComparison.Ordinal)<transcript.IndexOf("Projet auquel",StringComparison.Ordinal));
        Assert.Contains("Identifiant déjà utilisé",transcript);Assert.Contains("Répondez oui ou non",transcript);Assert.Contains("Contexte enregistré",transcript);
        Assert.Equal("Étude détaillée du modèle cible",s.Workspace.Config.Sources.Single(x=>x.Id==id).Context!.Description);
    }
    [Fact]
    public async Task InvalidExplicitIdFailsBeforeInventoryOrPromptsInAutomation()
    {
        using var s=new Sandbox();var path=NewSource(s);var output=new StringWriter();var before=Json.Serialize(s.Workspace.Config);
        var error=await Assert.ThrowsAsync<ArgumentException>(()=>new SourceSetup(s.Workspace,TextReader.Null,output,false).Prepare(path,"Étude SQL",null,null,false,default));
        Assert.Contains("Identifiant de source invalide",error.Message);Assert.Empty(output.ToString());Assert.Equal(before,Json.Serialize(s.Workspace.Config));
    }
    [Fact]
    public async Task ExplicitInvalidIdCanBeCorrectedBeforeContextWithoutSilentRenaming()
    {
        using var s=new Sandbox();var path=NewSource(s);var output=new StringWriter();
        var id=await new SourceSetup(s.Workspace,new StringReader("\n"+ContextAnswers+"oui\n"),output,true).Prepare(path,"Étude SQL",null,null,false,default);
        Assert.Equal("etude-sql",id);Assert.Contains("[etude-sql]",output.ToString());Assert.Contains("Source : etude-sql",output.ToString());
    }
    [Fact]
    public async Task AccentedFolderGetsValidUniqueDefaultIdentifier()
    {
        using var s=new Sandbox();var path=NewSource(s);s.Workspace.Bind("etude-sql",s.Source,SourceRole.Target);
        var id=await new SourceSetup(s.Workspace,new StringReader("\n"+ContextAnswers+"oui\n"),TextWriter.Null,true).Prepare(path,null,null,null,false,default);
        Assert.Equal("etude-sql-2",id);
    }
    [Fact]
    public async Task ConfirmedLongContextSurvivesAnalysisFailureAndIsReusedWithoutInput()
    {
        using var s=new Sandbox();var path=NewSource(s);var description=new string('d',1100);var notes=new string('n',1100);
        var answers=$"etude-sql\nBIP\nFacturation\ntarget\nvalidated\n{description}\n{notes}\noui\n";
        var id=await new SourceSetup(s.Workspace,new StringReader(answers),TextWriter.Null,true).Prepare(path,null,null,null,false,default);
        var analyzer=new FakeAnalyzer{FailAt=1};var ingestion=new Ingestor(s.Workspace,analyzer);
        await Assert.ThrowsAsync<TimeoutException>(()=>ingestion.Run(path,id,null,false,default));Assert.Empty(s.Workspace.Load().Sources);
        var output=new StringWriter();var repeated=await new SourceSetup(s.Workspace,TextReader.Null,output,false).Prepare(path,null,null,null,false,default);
        Assert.Equal(id,repeated);Assert.Contains("Contexte réutilisé",output.ToString());
        var context=s.Workspace.Config.Sources.Single(x=>x.Id==id).Context!;Assert.Equal(description,context.Description);Assert.Equal(notes,context.Notes);
        analyzer.FailAt=null;await ingestion.Run(path,id,null,false,default);var calls=analyzer.Calls;
        Assert.Equal(1,(await ingestion.Run(path,id,null,false,default)).Unchanged);Assert.Equal(calls,analyzer.Calls);
    }
    [Fact]
    public async Task CancellationAfterIdCorrectionDoesNotBindOrPublishAnything()
    {
        using var s=new Sandbox();var path=NewSource(s);var before=Json.Serialize(s.Workspace.Config);
        await Assert.ThrowsAsync<OperationCanceledException>(()=>new SourceSetup(s.Workspace,new StringReader("Étude SQL\netude-sql\n"+ContextAnswers+"non\n"),TextWriter.Null,true).Prepare(path,null,null,null,false,default));
        Assert.Equal(before,Json.Serialize(s.Workspace.Config));Assert.DoesNotContain("etude-sql",s.Workspace.Bindings().Keys);
    }
}
