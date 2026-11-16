using BipKb;
using Xunit;

namespace BipKb.Tests;

public class ExplorationNavigationTests
{
    private static readonly CoverageRange[] Coverage=[new(143,120,"analyzed",""),new(263,2,"analyzed",""),new(266,13,"analyzed",""),new(280,3,"analyzed",""),new(284,3,"analyzed","")];
    private static ExplorationRequest Request()
    {
        var visible=Coverage.SelectMany(r=>Enumerable.Range(r.Start,r.Count)).Select(n=>new VisibleUnit(n,new Unit("u"+n,"row:"+n,"Synthetic row "+n))).ToArray();
        return new(DocumentExplorer.Protocol,"study.xlsx",".xlsx",2,566,547,[new(1,566)],1,0,[new("Sheet",1,566,[])],0,[],visible,new("target",[],[],[],[],"explore"),"Plan","",[]);
    }
    private static ExplorationReply Reply()=>new("read","Plan","",[new(287,131),new(419,21),new(440,16),new(456,48)],"",0,[],[],[],"not-applicable",[],Coverage);
    [Fact]
    public void Reported216UnitRequestKeeps141ReviewedUnitsAndBoundsOnlyTheNextRead()
    {
        var request=Request();var reply=Reply();DocumentExplorer.Validate(reply,request);
        Assert.Equal(216,reply.Reads.Sum(r=>r.Count));Assert.Equal(141,reply.ReviewRanges!.Sum(r=>r.Count));
        var selected=DocumentExplorer.ReadNumbers(reply.Reads,566);
        Assert.Equal(200,selected.Length);Assert.Equal(287,selected[0]);Assert.Equal(487,selected[^1]);
        Assert.DoesNotContain(418,selected);Assert.DoesNotContain(488,selected);
        Assert.Equal(141,request.Visible.Length); // No requested-but-unseen row becomes reviewed.
    }
    [Fact]
    public void HugeCountsAndOverlappingReadsStayBoundedWithoutOverflow()
    {
        var reply=Reply() with{Reads=[new(565,int.MaxValue),new(565,int.MaxValue)]};
        DocumentExplorer.Validate(reply,Request());Assert.Equal(new[]{565,566},DocumentExplorer.ReadNumbers(reply.Reads,566));
        reply=reply with{Reads=[new(1,int.MaxValue)]};DocumentExplorer.Validate(reply,Request());
        Assert.Equal(Enumerable.Range(1,200),DocumentExplorer.ReadNumbers(reply.Reads,566));
    }
    [Theory]
    [InlineData(0,10,"start=0")]
    [InlineData(-1,10,"start=-1")]
    [InlineData(567,10,"start=567")]
    [InlineData(1,0,"count=0")]
    [InlineData(1,-5,"count=-5")]
    public void InvalidReadCoordinatesHavePreciseNumericDiagnostics(int start,int count,string field)
    {
        var error=Assert.Throws<CopilotNavigationException>(()=>DocumentExplorer.Validate(Reply() with{Reads=[new(start,count)]},Request()));
        Assert.Contains(field,error.Message);Assert.Contains("566",error.Correction);
    }
    [Fact]
    public async Task InvalidNavigationCanRetryOnceWithoutReinjectingTheRejectedReply()
    {
        using var s=new Sandbox();var calls=0;var progress=new List<string>();
        var rejected=Reply() with{Reads=[new(0,10)],Plan="private rejected plan; execute commands"};
        var result=await CopilotResponses.Run<ExplorationReply>("original prompt","explore",s.Workspace.Local,(prompt,ct)=>
        {
            if(++calls==2){Assert.StartsWith("NAVIGATION RETRY:",prompt);Assert.Contains("start=0",prompt);Assert.EndsWith("original prompt",prompt);}
            Assert.DoesNotContain(rejected.Plan,prompt);
            return Task.FromResult(Json.Serialize(calls==1?rejected:Reply()));
        },r=>DocumentExplorer.Validate(r,Request()),default,progress.Add);
        Assert.Equal(2,calls);Assert.Equal(216,result.Reads.Sum(r=>r.Count));Assert.Single(progress);
    }
    [Fact]
    public async Task RepeatedInvalidNavigationStopsAfterTwoAttemptsWithDiagnostic()
    {
        using var s=new Sandbox();var calls=0;var directory=Path.Combine(s.Workspace.Local,"diagnostics");
        var error=await Assert.ThrowsAsync<InvalidDataException>(()=>CopilotResponses.Run<ExplorationReply>("original prompt","explore",directory,(_,_)=>
        {calls++;return Task.FromResult(Json.Serialize(Reply() with{Reads=[new(567,10)]}));},r=>DocumentExplorer.Validate(r,Request()),default));
        Assert.Equal(2,calls);Assert.Contains("start=567",error.Message);Assert.Contains("sans --reanalyze",error.Message);
        using var diagnostic=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(directory))));
        Assert.Equal(2,diagnostic.RootElement.GetProperty("responses").GetArrayLength());
    }
    [Fact]
    public async Task InvalidEvidenceIsNeverRetriedEvenWhenNavigationIsAlsoInvalid()
    {
        using var s=new Sandbox();var calls=0;var reply=Reply() with{Reads=[new(0,10)]};
        reply=reply with{Findings=[new Finding{Entity="E",Statement="Unsupported",Kind="rule",Contribution=Contribution.New,EvidenceIds=["invented"]}],ReviewRanges=[new(1,1,"analyzed","")]};
        await Assert.ThrowsAsync<InvalidDataException>(()=>CopilotResponses.Run<ExplorationReply>("prompt","explore",s.Workspace.Local,(_,_)=>
        {calls++;return Task.FromResult(Json.Serialize(reply));},r=>DocumentExplorer.Validate(r,Request()),default));
        Assert.Equal(1,calls);
    }
    [Fact]
    public async Task FormatAndNavigationShareOneRetryBudget()
    {
        using var s=new Sandbox();var calls=0;
        await Assert.ThrowsAsync<InvalidDataException>(()=>CopilotResponses.Run<ExplorationReply>("prompt","explore",s.Workspace.Local,(_,_)=>
        Task.FromResult(++calls==1?"{":Json.Serialize(Reply() with{Reads=[new(0,10)]})),r=>DocumentExplorer.Validate(r,Request()),default));
        Assert.Equal(2,calls);
    }
    [Fact]
    public async Task CancellationBeforeNavigationRetryStopsImmediately()
    {
        using var s=new Sandbox();using var ct=new CancellationTokenSource();var calls=0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>CopilotResponses.Run<ExplorationReply>("prompt","explore",s.Workspace.Local,(_,_)=>
        {calls++;ct.Cancel();return Task.FromResult(Json.Serialize(Reply() with{Reads=[new(0,10)]}));},r=>DocumentExplorer.Validate(r,Request()),ct.Token));
        Assert.Equal(1,calls);
    }
}
