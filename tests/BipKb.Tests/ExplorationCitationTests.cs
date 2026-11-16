using BipKb;
using Xunit;

namespace BipKb.Tests;

public class ExplorationCitationTests
{
    private static ExplorationRequest Request()
    {
        var a=Json.Hash("proof-a");var b=Json.Hash("proof-b");var claim=Json.Hash("claim-a");
        var context=new AnalysisRequest("target",[],[new(claim,"entity","Stored claim",Contribution.New,["context-only-proof"],[],SourceRole.Target,false)],[],
            [new("decision",claim,"accept","Human decision","actor",[],DateTimeOffset.UnixEpoch)],"explore",HumanDeclarations:[new("question","project","Human declaration",["declaration-only-proof"],"Answer")]);
        return new(DocumentExplorer.Protocol,"sheet.xlsx",".xlsx",2,566,547,[new(1,566)],1,0,[],0,[],
            [new(143,new(a,"row:143","Source A")),new(146,new(b,"row:146","Source B"))],context,"Plan","",[]);
    }
    private static ExplorationReply Reply()=>new("read","Plan","",[new(287,10)],"",0,
        [new Finding{Entity="E",Kind="rule",Statement="Observed",Contribution=Contribution.New,EvidenceIds=["u143"]}],[],[],"not-applicable",[],[],new("analyzed",""));
    [Fact]
    public void ProjectionHidesContextProofsAndRestoresOnlyExactSuppliedReferences()
    {
        var original=Request();var before=Json.Serialize(original);var map=new ExplorationCitations(original);var wire=map.Request;
        Assert.Equal(new[]{"u143","u146"},wire.Visible.Select(v=>v.Unit.Id));
        Assert.Empty(wire.Context.Existing[0].EvidenceIds);Assert.Empty(wire.Context.HumanDeclarations![0].EvidenceIds);
        Assert.Equal(wire.Context.Existing[0].Id,wire.Context.Decisions[0].ClaimId);
        Assert.DoesNotContain("context-only-proof",Json.Serialize(wire));Assert.DoesNotContain("declaration-only-proof",Json.Serialize(wire));
        Assert.StartsWith("c",wire.Context.Existing[0].Id);Assert.True(wire.Context.Existing[0].Id.Length<original.Context.Existing[0].Id.Length);
        var reply=Reply() with{Findings=[Reply().Findings[0] with{Contribution=Contribution.Complement,RelatedClaimIds=[wire.Context.Existing[0].Id]}],ReviewVisible=null,Reviews=[new("u146","analyzed","")]};
        var restored=map.Restore(reply);
        Assert.Equal(original.Visible[0].Unit.Id,restored.Findings[0].EvidenceIds[0]);Assert.Equal(original.Context.Existing[0].Id,restored.Findings[0].RelatedClaimIds[0]);
        Assert.Equal(original.Visible[1].Unit.Id,restored.Reviews[0].UnitId);Assert.Equal(before,Json.Serialize(original));
        DocumentExplorer.Validate(restored,original);
    }
    [Theory]
    [InlineData("u145")]
    [InlineData("143")]
    [InlineData("U143")]
    [InlineData("u143 ")]
    [InlineData("context-only-proof")]
    public void UnknownOrApproximateLabelsCannotBeRestored(string label)
    {
        var map=new ExplorationCitations(Request());
        Assert.Throws<InvalidDataException>(()=>map.Restore(Reply() with{Findings=[Reply().Findings[0] with{EvidenceIds=[label]}]}));
    }
    [Fact]
    public void ClaimLabelsRemainStableButOldUnitLabelsAreNotAcceptedWhenNoLongerVisible()
    {
        var request=Request();var first=new ExplorationCitations(request);
        var second=new ExplorationCitations(request with{Visible=[request.Visible[1]],Context=request.Context with{Existing=[request.Context.Existing[0] with{Id="another"},request.Context.Existing[0]]}});
        Assert.Equal(first.Request.Context.Existing[0].Id,second.Request.Context.Existing[1].Id);
        Assert.Throws<InvalidDataException>(()=>second.Restore(Reply()));
        Assert.Throws<InvalidDataException>(()=>first.Restore(Reply() with{Findings=[Reply().Findings[0] with{EvidenceIds=[first.Request.Context.Existing[0].Id]}]}));
    }
    [Fact]
    public void TruncatedCanonicalHashNeverMatchesAnEvidenceByPrefix()
    {
        var request=Request();var map=new ExplorationCitations(request);var full=request.Visible[0].Unit.Id;
        var shortened=Reply() with{Findings=[Reply().Findings[0] with{EvidenceIds=[full[..8]]}]};
        Assert.Throws<InvalidDataException>(()=>map.Restore(shortened));
        Assert.Throws<InvalidDataException>(()=>DocumentExplorer.Validate(shortened,request));
        Assert.Throws<InvalidDataException>(()=>map.Restore(Reply() with{Findings=[Reply().Findings[0] with{EvidenceIds=[full]}]}));
    }
    [Fact]
    public async Task InvalidReferenceTriggersOneFreshAnalysisWithoutReinjectingTheBadConclusion()
    {
        using var s=new Sandbox();var map=new ExplorationCitations(Request());var calls=0;
        var invalid=Reply() with{Findings=[Reply().Findings[0] with{EvidenceIds=["invented-private-id"],Statement="private rejected conclusion"}]};
        var reply=await CopilotResponses.Run<ExplorationReply>("original evidence request","explore",s.Workspace.Local,(prompt,_)=>
        {
            if(++calls==2){Assert.StartsWith("REFERENCES RETRY:",prompt);Assert.EndsWith("original evidence request",prompt);}
            Assert.DoesNotContain("invented-private-id",prompt);Assert.DoesNotContain("private rejected conclusion",prompt);
            return Task.FromResult(Json.Serialize(calls==1?invalid:Reply()));
        },r=>DocumentExplorer.Validate(r,map.Request,retryReferences:true),default);
        Assert.Equal(2,calls);Assert.Equal(Request().Visible[0].Unit.Id,map.Restore(reply).Findings[0].EvidenceIds[0]);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedUnknownReferencesNeverBecomeAcceptedEvidence(bool related)
    {
        using var s=new Sandbox();var map=new ExplorationCitations(Request());var calls=0;
        var invalid=Reply() with{Findings=[related?Reply().Findings[0] with{RelatedClaimIds=["invented-private-id"]}:Reply().Findings[0] with{EvidenceIds=["invented-private-id"]}]};
        var error=await Assert.ThrowsAsync<InvalidDataException>(()=>CopilotResponses.Run<ExplorationReply>("original","explore",s.Workspace.Local,(_,_)=>
        {calls++;return Task.FromResult(Json.Serialize(invalid));},r=>DocumentExplorer.Validate(r,map.Request,retryReferences:true),default));
        Assert.Equal(2,calls);Assert.DoesNotContain("invented-private-id",error.Message);Assert.Empty(s.Workspace.Load().Claims);
    }
    [Fact]
    public async Task ReferenceAndCoverageFailuresShareTheSameRetryBudget()
    {
        using var s=new Sandbox();var calls=0;var map=new ExplorationCitations(Request());
        await Assert.ThrowsAsync<InvalidDataException>(()=>CopilotResponses.Run<ExplorationReply>("original","explore",s.Workspace.Local,(_,_)=>
        Task.FromResult(Json.Serialize(++calls==1?Reply() with{Findings=[Reply().Findings[0] with{EvidenceIds=["unknown"]}]}:Reply() with{ReviewVisible=null,ReviewRanges=[new(143,4,"analyzed","")]})),
        r=>DocumentExplorer.Validate(r,map.Request,retryReferences:true),default));Assert.Equal(2,calls);
    }
    [Fact]
    public async Task CancellationPreventsFreshAnalysisAndUnobservedImageCannotRetryAsReferenceFailure()
    {
        using var s=new Sandbox();var calls=0;var map=new ExplorationCitations(Request());using var ct=new CancellationTokenSource();
        var bad=Reply() with{Findings=[Reply().Findings[0] with{EvidenceIds=["unknown"]}]};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>CopilotResponses.Run<ExplorationReply>("original","explore",s.Workspace.Local,(_,_)=>
        {calls++;ct.Cancel();return Task.FromResult(Json.Serialize(bad));},r=>DocumentExplorer.Validate(r,map.Request,retryReferences:true),ct.Token));
        Assert.Equal(1,calls);
        Assert.Throws<InvalidDataException>(()=>DocumentExplorer.Validate(bad with{VisualStatus="unsupported"},map.Request with{Format=".png"},retryReferences:true));
    }
}
