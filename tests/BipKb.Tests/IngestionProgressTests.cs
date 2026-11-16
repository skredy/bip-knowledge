using System.Collections.Concurrent;
using System.Text;
using BipKb;
using Xunit;

namespace BipKb.Tests;

public class IngestionProgressTests
{
    private sealed class RecordingWriter : TextWriter
    {
        public override Encoding Encoding=>Encoding.UTF8;
        public ConcurrentQueue<string> Lines {get;}=new();
        public TaskCompletionSource Waiting {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void WriteLine(string? value)
        {
            Lines.Enqueue(value??"");
            if(value?.Contains("attente de Copilot — étape en cours") == true)Waiting.TrySetResult();
        }
    }
    private sealed class WaitingAnalyzer : IAnalyzer
    {
        public TaskCompletionSource<Analysis> Response {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Analysis> Analyze(AnalysisRequest request,string? image,CancellationToken ct)=>Response.Task.WaitAsync(ct);
        public Task<Answer> Ask(string question,Claim[] claims,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class BrokenWriter : TextWriter
    {
        public override Encoding Encoding=>Encoding.UTF8;
        public override void WriteLine(string? value)=>throw new IOException("closed pipe");
    }
    [Fact]
    public async Task ClosedFeedbackStreamDoesNotFailIngestion()
    {
        using var s=new Sandbox();s.File("rules.md","Commande");
        await using var progress=new IngestionProgress(new BrokenWriter());
        var report=await new Ingestor(s.Workspace,new FakeAnalyzer(),progress.Report).Run(s.Source,null,null,false,default);
        Assert.Equal(1,report.Files);Assert.Single(s.Workspace.Load().Claims);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportsHeartbeatWhileAnalyzerIsPendingAndStopsOnSuccessOrCancellation(bool cancel)
    {
        using var s=new Sandbox();s.File("rules.md","CONTENU PRIVE à ne pas afficher dans la progression");
        using var cancellation=new CancellationTokenSource();
        var writer=new RecordingWriter();var analyzer=new WaitingAnalyzer();
        await using var progress=new IngestionProgress(writer,TimeSpan.FromMilliseconds(20));
        var running=new Ingestor(s.Workspace,analyzer,progress.Report).Run(s.Source,null,null,false,cancellation.Token);
        await writer.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(running.IsCompleted);Assert.Empty(s.Workspace.Load().Reports);
        Assert.Contains(writer.Lines,l=>l.Contains("Fichier 1/1")&&l.Contains("lot 1/1"));
        Assert.DoesNotContain(writer.Lines,l=>l.Contains("CONTENU PRIVE"));
        if(cancel)
        {
            cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>running);
            Assert.DoesNotContain(writer.Lines,l=>l.Contains("Ingestion terminée"));Assert.Empty(s.Workspace.Load().Reports);
        }
        else
        {
            analyzer.Response.SetResult(new([]));await running;
            Assert.Contains(writer.Lines,l=>l.Contains("réponse reçue"));
            Assert.Contains(writer.Lines,l=>l.Contains("Ingestion terminée"));Assert.Single(s.Workspace.Load().Reports);
        }
        await progress.DisposeAsync();var count=writer.Lines.Count;
        progress.Report("Ne doit plus apparaître");Assert.Equal(count,writer.Lines.Count);
    }

    [Fact]
    public async Task ResumingReportsCachedWorkWithoutClaimingAnotherModelCall()
    {
        using var s=new Sandbox();s.File("a.md","Commande validée");s.File("b.md","Commande annulée");
        var analyzer=new FakeAnalyzer{FailAt=2};var messages=new List<string>();
        var engine=new Ingestor(s.Workspace,analyzer,messages.Add);
        await Assert.ThrowsAsync<TimeoutException>(()=>engine.Run(s.Source,null,null,false,default));
        Assert.DoesNotContain(messages,m=>m.Contains("Ingestion terminée"));
        analyzer.FailAt=null;messages.Clear();await engine.Run(s.Source,null,null,false,default);
        Assert.Contains(messages,m=>m.Contains("a.md")&&m.Contains("résultat intermédiaire réutilisé"));
        Assert.DoesNotContain(messages,m=>m.Contains("a.md")&&m.Contains("attente de Copilot"));
        Assert.Contains(messages,m=>m.Contains("Rapprochement entre fichiers"));
        messages.Clear();await engine.Run(s.Source,null,null,false,default);
        Assert.DoesNotContain(messages,m=>m.Contains("attente de Copilot"));
        Assert.Equal(2,messages.Count(m=>m.Contains("inchangé, analyse réutilisée")));
    }
}
