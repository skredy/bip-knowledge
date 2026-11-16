using BipKb;
using Xunit;

namespace BipKb.Tests;

public class ExplorationCoverageTests
{
    private static ExplorationRequest Request()=>new(DocumentExplorer.Protocol,"sheet.xlsx",".xlsx",2,566,547,[new(1,566)],1,0,[],0,[],
        new[]{143,144,146}.Select(n=>new VisibleUnit(n,new("u"+n,"row:"+n,"private source text"))).ToArray(),new("target",[],[],[],[],"explore"),"Plan","",[]);
    private static ExplorationReply Reply()=>new("read","Plan","",[new(287,100)],"",0,[],[],[],"not-applicable",[],[],new("analyzed",""));
    [Fact]
    public void CoverageHintsContainOnlyActualVisibleNumbersAndNoSourceText()
    {
        var hint=DocumentExplorer.CoverageInstructions(Request());Assert.Contains("143..144, 146..146",hint);
        Assert.DoesNotContain("private source text",hint);Assert.Contains("reviewVisible",hint);
        DocumentExplorer.Validate(Reply(),Request());
    }
    [Theory]
    [InlineData("gap",145)]
    [InlineData("overlap",144)]
    [InlineData("mixed",143)]
    [InlineData("unseen",1)]
    public void RangeFailureNamesTheSpecificMissingOrDuplicateUnit(string mode,int number)
    {
        var reply=Reply() with{ReviewVisible=null,ReviewRanges=mode switch
        {"gap"=>[new(143,4,"analyzed","")],"overlap"=>[new(143,2,"analyzed",""),new(144,1,"analyzed","")],"mixed"=>[new(143,1,"analyzed","")],_=>[new(1,1,"analyzed","")]}};
        if(mode=="mixed")reply=reply with{Reviews=[new("u143","analyzed","")]};
        var error=Assert.Throws<CopilotCoverageException>(()=>DocumentExplorer.Validate(reply,Request()));
        Assert.Contains("unité "+number,error.Message);Assert.DoesNotContain("private",error.Message);
        Assert.Contains("143..144, 146..146",error.Correction);
    }
    [Fact]
    public async Task CoverageRetryUsesExactVisibleNumbersWithoutReinjectingTheRejectedReply()
    {
        using var s=new Sandbox();var calls=0;var progress=new List<string>();
        var bad=Reply() with{Plan="private rejected plan",ReviewVisible=null,ReviewRanges=[new(143,4,"analyzed","")]};
        var result=await CopilotResponses.Run<ExplorationReply>("original request","explore",s.Workspace.Local,(prompt,_)=>
        {
            if(++calls==2){Assert.StartsWith("COVERAGE RETRY:",prompt);Assert.Contains("143..144, 146..146",prompt);Assert.EndsWith("original request",prompt);}
            Assert.DoesNotContain(bad.Plan,prompt);return Task.FromResult(Json.Serialize(calls==1?bad:Reply()));
        },reply=>DocumentExplorer.Validate(reply,Request()),default,progress.Add);
        Assert.Equal(2,calls);Assert.Equal("analyzed",result.ReviewVisible!.Status);Assert.Single(progress);
    }
    [Fact]
    public async Task RepeatedCoverageErrorStopsWithoutAcceptingAnUnseenUnit()
    {
        using var s=new Sandbox();var calls=0;
        var error=await Assert.ThrowsAsync<InvalidDataException>(()=>CopilotResponses.Run<ExplorationReply>("request","explore",s.Workspace.Local,(_,_)=>
        {calls++;return Task.FromResult(Json.Serialize(Reply() with{ReviewVisible=null,ReviewRanges=[new(143,4,"analyzed","")]}));},r=>DocumentExplorer.Validate(r,Request()),default));
        Assert.Equal(2,calls);Assert.Contains("unité 145",error.Message);Assert.Empty(s.Workspace.Load().Claims);
    }
    [Theory]
    [InlineData("mixed")]
    [InlineData("empty")]
    public void WholeVisibleReviewRequiresSuppliedUnitsAndCannotBeCombinedWithOtherReviews(string mode)
    {
        var request=mode=="empty"?Request() with{Visible=[]}:Request();
        var reply=mode=="mixed"?Reply() with{Reviews=[new("u143","analyzed","")]}:Reply();
        Assert.Throws<CopilotCoverageException>(()=>DocumentExplorer.Validate(reply,request));
    }
    [Fact]
    public void WholeVisibleUnresolvedStatusStillRequiresAReason()
    {Assert.Throws<InvalidDataException>(()=>DocumentExplorer.Validate(Reply() with{ReviewVisible=new("unresolved","")},Request()));}
    [Theory]
    [InlineData("image")]
    [InlineData("evidence")]
    [InlineData("reviewId")]
    public async Task CoverageRetryNeverRepairsUnsupportedObservationsOrInventedReferences(string mode)
    {
        using var s=new Sandbox();var calls=0;var request=Request();var reply=Reply();
        if(mode=="image"){request=request with{Format=".png"};reply=reply with{VisualStatus="unsupported"};}
        if(mode=="evidence")reply=reply with{Findings=[new Finding{Entity="E",Kind="rule",Statement="Unsupported",Contribution=Contribution.New,EvidenceIds=["invented"]}],ReviewVisible=null,ReviewRanges=[new(143,4,"analyzed","")]};
        if(mode=="reviewId")reply=reply with{ReviewVisible=null,Reviews=[new("invented","analyzed","")]};
        await Assert.ThrowsAsync<InvalidDataException>(()=>CopilotResponses.Run<ExplorationReply>("request","explore",s.Workspace.Local,(_,_)=>
        {calls++;return Task.FromResult(Json.Serialize(reply));},r=>DocumentExplorer.Validate(r,request),default));Assert.Equal(1,calls);
    }
    [Fact]
    public async Task NavigationAndCoverageShareOneRetryBudget()
    {
        using var s=new Sandbox();var calls=0;
        await Assert.ThrowsAsync<InvalidDataException>(()=>CopilotResponses.Run<ExplorationReply>("request","explore",s.Workspace.Local,(_,_)=>
        Task.FromResult(Json.Serialize(++calls==1?Reply() with{Reads=[new(0,1)]}:Reply() with{ReviewVisible=null,ReviewRanges=[new(143,4,"analyzed","")]})),
        r=>DocumentExplorer.Validate(r,Request()),default));Assert.Equal(2,calls);
    }
}
