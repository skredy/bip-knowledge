using System.Diagnostics;
namespace BipKb;
public sealed record EvaluationCase(string Question,string[] ExpectedIds,string? Project=null,string? Domain=null,string[]? ForbiddenIds=null);
public static class Evaluation
{
    public static object Run(Workspace workspace,string file)
    {
        var cases=Json.Read<EvaluationCase[]>(File.ReadAllText(file));if(cases.Length==0)throw new ArgumentException("Evaluation corpus is empty.");
        var engine=new SearchEngine(workspace);var results=cases.Select(c=>{
            var found=engine.Search(new(c.Question,c.Project,c.Domain));var ids=found.Hits.Select(h=>h.Id).Concat(found.Hits.SelectMany(h=>h.EvidenceIds)).ToHashSet();
            var matched=c.ExpectedIds.Count(ids.Contains);return new{c.Question,recall=c.ExpectedIds.Length==0?(found.Hits.Length==0?1d:0d):(double)matched/c.ExpectedIds.Length,missing=c.ExpectedIds.Where(id=>!ids.Contains(id)).ToArray(),forbidden=(c.ForbiddenIds??[]).Where(ids.Contains).ToArray(),found.Milliseconds};
        }).ToArray();
        return new{count=cases.Length,recallAt20=results.Average(x=>x.recall),p95Milliseconds=results.Select(r=>r.Milliseconds).Order().ElementAt((int)Math.Ceiling(results.Length*.95)-1),results};
    }
    public static object Benchmark(int count)
    {
        if(count is <100 or >500000)throw new ArgumentException("Benchmark size: 100..500000.");
        var root=Path.Combine(Path.GetTempPath(),"bip-benchmark-"+Guid.NewGuid().ToString("N"));var w=new Workspace(root);w.Init();
        try
        {
            var k=new Knowledge();var ids=new List<string>();
            for(var i=0;i<count;i++){var id="proof"+i;ids.Add(id);k.Evidence.Add(new(id,"bench","rules.sql","v1","line:"+i,$"Facture reference REF{i:D8} montant client statut impaye"));}
            k.Sources.Add(new("source","bench","rules.sql","v1","v1",SourceRole.Target,ids.ToArray(),[],true,[],new("Benchmark","Facturation","Synthetic benchmark",SourceStatus.Observed)));
            var prepare=Stopwatch.StartNew();w.Save(k);var engine=new SearchEngine(w);engine.Refresh();var buildMs=prepare.Elapsed.TotalMilliseconds;
            var elapsed=new List<double>();var recovered=0;
            for(var i=0;i<30;i++){var target=(i*7919)%count;var result=engine.Search(new($"REF{target:D8}","Benchmark",Depth:0));elapsed.Add(result.Milliseconds);if(result.Hits.Any(h=>h.Id=="proof"+target))recovered++;}
            return new{units=count,queries=elapsed.Count,recovered,buildMilliseconds=buildMs,p50Milliseconds=elapsed.Order().ElementAt(15),p95Milliseconds=elapsed.Order().ElementAt(28),note="Synthetic lexical lookup only, excludes Copilot and does not measure business understanding."};
        }
        finally{Directory.Delete(root,true);}
    }
}
