using BipKb;
using Xunit;

namespace BipKb.Tests;

public class ProjectSetupTests
{
    [Fact]
    public void MinimalSetupExplainsTermsAndDoesNotStoreExamples()
    {
        using var s=new Sandbox();var output=new StringWriter();
        var profile=new Projects(s.Workspace).Setup(null,new StringReader("Migration BIP\nRecréer les endpoints\n\n\n\n\noui\n"),output,true);
        Assert.Equal("Recréer les endpoints",profile.Objective);
        Assert.Equal("Non précisée",profile.Architecture);
        Assert.Empty(profile.Systems);Assert.Empty(profile.Domains);Assert.Empty(profile.Conventions);
        Assert.Contains("pas vos dossiers",output.ToString());Assert.Contains("Sujets métier",output.ToString());
        Assert.Single(s.Workspace.Load().Projects);
    }

    [Fact]
    public void EditingWithEnterPreservesProfileAndDomainLinks()
    {
        using var s=new Sandbox();var projects=new Projects(s.Workspace);
        var initial=projects.Setup(null,new StringReader("Migration BIP\nMigrer les endpoints\n.NET\nAncien BIP ; API BIP\nClients ; Facturation\nCQRS\noui\n"),TextWriter.Null,true);
        var k=s.Workspace.Load();initial=initial with{Domains=[initial.Domains[0] with{RelatedDomains=[initial.Domains[1].Id]},initial.Domains[1]]};k.Projects[0]=initial;s.Workspace.Save(k);
        var updated=projects.Setup(null,new StringReader("\n\n\n\n\n\noui\n"),TextWriter.Null,true);
        Assert.Equal(Json.Serialize(initial),Json.Serialize(updated));Assert.Single(s.Workspace.Load().Projects);
    }

    [Fact]
    public void HelpAndBlankRequiredFieldsRepeatWithoutCancelling()
    {
        using var s=new Sandbox();var output=new StringWriter();
        var profile=new Projects(s.Workspace).Setup(null,new StringReader("?\n\nMigration BIP\n?\nMigrer\n\n\n\n\npeut-être\nOUI\n"),output,true);
        Assert.Equal("Migration BIP",profile.Name);Assert.Contains("Ce champ est nécessaire",output.ToString());
        Assert.Contains("Répondez oui",output.ToString());
    }

    [Theory]
    [InlineData("/cancel\n")]
    [InlineData("Migration BIP\nMigrer\n\n\n\n\nnon\n")]
    public void CancellationLeavesMemoryUnchanged(string answers)
    {
        using var s=new Sandbox();var revision=s.Workspace.Revision;
        Assert.Throws<OperationCanceledException>(()=>new Projects(s.Workspace).Setup(null,new StringReader(answers),TextWriter.Null,true));
        Assert.Equal(revision,s.Workspace.Revision);Assert.Empty(s.Workspace.Load().Projects);
    }
}
