namespace BipKb;

public sealed record SourceQualificationRequest(ProjectProfile Project,string Name,string[] Inventory,string Description);
public sealed record SourceQualification(string Purpose,string[] Topics,SourceRole Role,SourceStatus Status,string Question,string Reason);
public sealed record BusinessSection(string Id,string Name,int Start,int Count,string[] Preview);
public sealed record BusinessCoverage(string SectionId,string Status,string Reason,string? Name=null);
public sealed record BusinessLink(string From,string To,RelationKind Kind,string[] EvidenceIds);
public sealed record BusinessReply(string Action,string Summary,UnitRange[] Reads,List<Finding> Findings,BusinessLink[] Relations,BusinessCoverage[] Coverage,string[] Questions,string VisualStatus);
public sealed record BusinessRequest(string Protocol,string Document,string Format,ProjectProfile? Project,SourceContext? Source,
    BusinessSection[] Sections,VisibleUnit[] Visible,Unit[] Journal,Claim[] Existing,Entity[] Entities,Decision[] Decisions,string Memory,string[] Limitations);
public sealed record BusinessSummary(string RunId,string Summary,BusinessCoverage[] Coverage,string[] Questions,int SuppliedUnits,int TotalUnits,int Calls,bool Complete);
public sealed record BusinessRun(string Key,string RunId,bool Ready);

/// <summary>Document-level analysis with a persisted read ledger and semantic, section-level coverage.</summary>
public sealed class BusinessDocuments(Workspace workspace,IBusinessAssistant assistant,Action<string>? progress=null)
{
    public const string Protocol="business-document-v1";
    private string StatusPath(string source,string relative)=>Path.Combine(workspace.Local,"business","documents",Json.Hash(source+"/"+relative)+".json");
    public bool Pending(string source,string relative,string key,SourceVersion? prior)
    {
        var path=StatusPath(source,relative);
        if(!File.Exists(path))return false;
        var run=Json.Read<BusinessRun>(File.ReadAllText(path));return run.Key==key&&run.RunId!=prior?.Business?.RunId;
    }
    public static BusinessSection[] Sections(Unit[] units)
    {
        var result=new List<BusinessSection>();var start=0;
        while(start<units.Length)
        {
            var name=units[start].Parent??"document";var end=start+1;
            while(end<units.Length&&(units[end].Parent??"document")==name)end++;
            result.Add(new("s"+(result.Count+1),name,start+1,end-start,units.Skip(start).Take(Math.Min(2,end-start)).Select(u=>u.Text[..Math.Min(200,u.Text.Length)]).ToArray()));start=end;
        }
        return result.ToArray();
    }
    public static void Validate(BusinessReply r,BusinessRequest request)
    {
        static bool Text(string? value,int limit,bool empty=false)=>value!=null&&value.Length<=limit&&(empty||!string.IsNullOrWhiteSpace(value));
        if(r.Action is not ("read" or "finish")||!Text(r.Summary,8000)||r.Reads==null||r.Reads.Length>8||r.Action=="read"&&r.Reads.Length==0||r.Action=="finish"&&r.Reads.Length!=0||r.Questions==null||r.Questions.Length>12||r.Questions.Any(q=>!Text(q,500)))throw new InvalidDataException("Analyse métier : action, synthèse, lectures ou questions invalides.");
        var total=request.Sections.Sum(s=>s.Count);
        if(r.Reads.Any(x=>x==null||x.Start<1||x.Start>total||x.Count<1))throw new InvalidDataException("Lecture hors du document ; start est un numéro d'unité et count une quantité.");
        var journal=request.Journal.Concat(request.Visible.Select(v=>v.Unit)).DistinctBy(u=>u.Id).ToArray();
        CopilotAnalyzer.Validate(new(r.Findings),new("mixed",journal,request.Existing,request.Entities,request.Decisions,"business",request.Source,request.Project));
        if(r.Relations==null||r.Relations.Length>100||r.Coverage==null||r.Coverage.Length>request.Sections.Length||r.Coverage.Select(c=>c.SectionId).Distinct().Count()!=r.Coverage.Length)throw new InvalidDataException("Relations ou couverture de sections invalides.");
        var names=r.Findings.Where(f=>f.Contribution!=Contribution.None).Select(f=>f.Entity).ToHashSet();var ids=journal.Select(u=>u.Id).ToHashSet();
        foreach(var link in r.Relations)
            if(link==null||!names.Contains(link.From)||!names.Contains(link.To)||!Enum.IsDefined(link.Kind)||link.EvidenceIds==null||link.EvidenceIds.Length==0||link.EvidenceIds.Any(id=>!ids.Contains(id)))throw new InvalidDataException("Une relation métier doit relier deux concepts de cette réponse et citer des preuves consultées.");
        foreach(var c in r.Coverage)
        {
            var section=request.Sections.SingleOrDefault(s=>s.Id==c.SectionId);
            if(section==null||c.Status is not ("interpreted" or "raw" or "unresolved" or "excluded")||!Text(c.Reason,600))throw new InvalidDataException("Couverture de section inconnue ou sans justification.");
            if(c.Status is "interpreted" or "raw"&&Enumerable.Range(section.Start,section.Count).Any(n=>!ids.Contains("e"+n)))throw new InvalidDataException("Une section non intégralement fournie ne peut pas être déclarée interprétée ou conservée après lecture.");
        }
        var image=request.Format is ".png" or ".jpg" or ".jpeg";
        if(r.VisualStatus is not ("not-applicable" or "observed" or "unsupported" or "unreadable")||!image&&r.VisualStatus!="not-applicable"||image&&r.VisualStatus!="observed"&&(r.Findings.Count>0||r.Coverage.Any(c=>c.Status is "interpreted" or "raw")))throw new InvalidDataException("Observation visuelle non établie.");
    }
    public async Task<BusinessSummary> Run(string source,string relative,string key,string format,Unit[] units,string[] limitations,string? attachment,bool force,ExplorationBudget budget,
        Func<Unit[],string,int,AnalysisRequest> context,Action<Analysis,BusinessLink[]> accept,CancellationToken ct)
    {
        var statusPath=StatusPath(source,relative);
        var previous=!force&&File.Exists(statusPath)?Json.Read<BusinessRun>(File.ReadAllText(statusPath)):null;
        var run=previous?.Key==key?previous:new BusinessRun(key,Guid.NewGuid().ToString("N"),false);
        Json.Write(statusPath,run);
        var canonical=units.Select((u,i)=>(Alias:"e"+(i+1),Unit:u)).ToDictionary(x=>x.Alias,x=>x.Unit);
        var labeled=units.Select((u,i)=>u with{Id="e"+(i+1)}).ToArray();
        var sections=Sections(labeled);var journal=new Dictionary<string,Unit>();var coverage=new Dictionary<string,BusinessCoverage>();
        var questions=new HashSet<string>();var memory="";
        VisibleUnit[] Read(IEnumerable<int> numbers)
        {
            var selected=new List<VisibleUnit>();var size=0;
            foreach(var n in numbers.Distinct())
            {
                var unit=labeled[n-1];var length=Json.Serialize(unit).Length+40;
                if(size+length>90000)break;
                selected.Add(new(n,unit));size+=length;
            }
            if(selected.Count==0&&numbers.Any())throw new InvalidDataException("Une unité dépasse le budget de lecture métier ; réduisez la taille de cette cellule ou section.");
            return selected.ToArray();
        }
        // A small complete document is supplied whole. Otherwise the model sees its structure first.
        var wholeSize=labeled.Sum(u=>(long)Json.Serialize(u).Length+40);
        var visible=wholeSize<=90000?Read(Enumerable.Range(1,units.Length)):Array.Empty<VisibleUnit>();
        for(var step=1;step<=64;step++)
        {
            ct.ThrowIfCancellationRequested();
            var selected=context(visible.Select(v=>canonical[v.Unit.Id]).ToArray(),memory,170000);
            var request=new BusinessRequest(Protocol,relative,format,selected.Project,selected.Context,sections,visible,
                journal.Values.Select(u=>u with{Text=""}).ToArray(),selected.Existing,selected.Entities,selected.Decisions,memory,limitations);
            if(Json.Serialize(request).Length>185000)throw new InvalidDataException("Document trop volumineux pour ce parcours : catalogue et journal dépassent le budget. Analysez un sous-ensemble de sources.");
            var path=Path.Combine(workspace.Local,"business","responses",Json.Hash(run.RunId+key+Json.Serialize(request))+".json");
            BusinessReply reply;
            if(File.Exists(path)) {reply=Json.Read<BusinessReply>(File.ReadAllText(path));Validate(reply,request);progress?.Invoke($"Analyse métier — échange {step} réutilisé");}
            else
            {
                if(!budget.TryUse())throw new InvalidOperationException("Budget Copilot atteint ; reprise avec --analyze, sans --reanalyze. Les échanges validés sont conservés.");
                progress?.Invoke($"Analyse métier — échange {step}, {visible.Length} unités fournies, {journal.Count}/{units.Length} déjà consultables ; attente de Copilot");
                reply=await assistant.Understand(request,attachment,ct);Validate(reply,request);Json.Write(path,reply);
            }
            foreach(var v in visible)journal[v.Unit.Id]=v.Unit;
            string[] Restore(string[] ids)=>ids.Select(id=>canonical[id].Id).Distinct().ToArray();
            accept(new(reply.Findings.Select(f=>f with{EvidenceIds=Restore(f.EvidenceIds)}).ToList()),reply.Relations.Select(r=>r with{EvidenceIds=Restore(r.EvidenceIds)}).ToArray());
            foreach(var c in reply.Coverage)coverage[c.SectionId]=c;
            foreach(var q in reply.Questions)questions.Add(q);
            memory=reply.Summary;
            progress?.Invoke($"Compréhension métier — {reply.Findings.Count} fiches, {coverage.Count} sections décrites ; prochaine action : {reply.Action}");
            if(reply.Action=="finish")
            {
                var final=sections.Select(s=>(coverage.GetValueOrDefault(s.Id)??new(s.Id,"unresolved","Section non interprétée ; données consultables dans les preuves.")) with{Name=s.Name}).ToArray();
                Json.Write(statusPath,run with{Ready=true});
                return new(run.RunId,memory,final,questions.Take(30).ToArray(),journal.Count,units.Length,step,final.All(c=>c.Status is "interpreted" or "raw"));
            }
            visible=Read(reply.Reads.SelectMany(r=>Enumerable.Range(r.Start,Math.Min(r.Count,units.Length-r.Start+1))));
        }
        throw new InvalidOperationException("Limite de 64 échanges métier atteinte ; analyse non publiée, échanges conservés.");
    }
}
