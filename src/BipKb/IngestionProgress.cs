using System.Diagnostics;
using System.Globalization;

namespace BipKb;

/// <summary>Human-readable stderr feedback; never includes document contents or provider output.</summary>
public sealed class IngestionProgress : IAsyncDisposable
{
    private readonly TextWriter? output;
    private readonly string label;
    private readonly CancellationTokenSource stop=new();
    private readonly object gate=new();
    private readonly Stopwatch elapsed=Stopwatch.StartNew();
    private readonly Stopwatch stageElapsed=Stopwatch.StartNew();
    private readonly Task heartbeat;
    private string? stage;
    private bool disposed;
    private bool outputUnavailable;

    public IngestionProgress(TextWriter? output,TimeSpan? interval=null,string label="ingestion")
    {
        this.output=output;
        this.label=label;
        heartbeat=output==null?Task.CompletedTask:Task.Run(async ()=>
        {
            using var timer=new PeriodicTimer(interval??TimeSpan.FromSeconds(10));
            try
            {
                while(await timer.WaitForNextTickAsync(stop.Token))
                    lock(gate)
                        if(!disposed&&!outputUnavailable&&stage!=null)Write($"{stage} — étape en cours depuis {(int)stageElapsed.Elapsed.TotalSeconds} s");
            }
            catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
        });
    }

    public void Report(string message)
    {
        lock(gate)
        {
            if(disposed||outputUnavailable||output==null)return;
            // Filenames can contain terminal control characters. Keep feedback on one plain line.
            stage=new string(message.Where(c=>!char.IsControl(c)&&char.GetUnicodeCategory(c)!=UnicodeCategory.Format).Take(300).ToArray());
            stageElapsed.Restart();Write(stage);
        }
    }
    private void Write(string message)
    {
        try
        {
            output!.WriteLine($"[{label} {(int)elapsed.Elapsed.TotalMinutes:00}:{elapsed.Elapsed.Seconds:00}] {message}");
            output.Flush();
        }
        // A closed output pipe must not replace the ingestion result or its original error.
        catch(Exception e) when(e is IOException or ObjectDisposedException){outputUnavailable=true;}
    }
    public async ValueTask DisposeAsync()
    {
        lock(gate){if(disposed)return;disposed=true;}
        stop.Cancel();await heartbeat;stop.Dispose();
    }
}
