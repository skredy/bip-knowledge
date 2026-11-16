namespace BipKb;

public sealed record UnitRange(int Start,int Count);
public sealed record UnitReview(string UnitId,string Status,string Reason);
public sealed record RangeExclusion(int Start,int Count,string Reason);
public sealed record CoverageRange(int Start,int Count,string Status,string Reason);
public sealed record DocumentSection(string Name,int Start,int Count,string[] Preview);
public sealed record DocumentMatch(int Number,string Locator,string Preview);
public sealed record VisibleUnit(int Number,Unit Unit);
public sealed record ExplorationRequest(string Protocol,string Document,string Format,int Step,int TotalUnits,
    int RemainingUnits,UnitRange[] Pending,int SectionCount,int CatalogOffset,DocumentSection[] Catalog,
    int MatchCount,DocumentMatch[] Matches,VisibleUnit[] Visible,AnalysisRequest Context,string Plan,string Feedback,string[] Limitations);
public sealed record ExplorationReply(string Action,string Plan,string Summary,UnitRange[] Reads,string Query,int Offset,
    List<Finding> Findings,UnitReview[] Reviews,string[] Questions,string VisualStatus,RangeExclusion[]? Exclusions=null,CoverageRange[]? ReviewRanges=null);
public sealed record ExplorationSummary(string Protocol,string RunId,string Plan,string Summary,int TotalUnits,int AnalyzedUnits,
    int ExcludedUnits,int UnresolvedUnits,int EmptyUnits,int Steps,CoverageRange[] Coverage,string[] Questions,string VisualStatus)
{
    [System.Text.Json.Serialization.JsonIgnore] public bool Complete=>AnalyzedUnits+EmptyUnits==TotalUnits;
}
public sealed record ExplorationStatus(string Source,string RelativePath,string SourceKey,string RunId,int Step,int RemainingUnits,string State,string Plan,string Profile="arguments-v1");
// Only navigation errors may request a bounded correction; provenance errors never use this type.
public sealed class CopilotNavigationException(string message,string correction):Exception("Exploration Copilot refusée : "+message)
{
    public string Correction {get;}=correction;
}
public interface IExplorationAssistant
{
    Task<string> ReadingProfile(CancellationToken ct)=>Task.FromResult(DocumentExplorer.LegacyProfile);
    Task<ExplorationReply> Explore(ExplorationRequest request,string? image,CancellationToken ct);
}
public sealed class ExplorationBudget
{
    public int Limit {get;}
    public int Used {get;private set;}
    public ExplorationBudget(int limit){if(limit is <1 or >100)throw new ArgumentException("maxExplorationCalls doit être compris entre 1 et 100.");Limit=limit;}
    public bool TryUse(){if(Used>=Limit)return false;Used++;return true;}
}

/// <summary>Read-only, model-directed exploration of an immutable extracted document.</summary>
public sealed class DocumentExplorer(Workspace workspace,IExplorationAssistant assistant,Action<string>? progress=null)
{
    public const string Protocol="guided-v1";
    public const string LegacyProfile="arguments-v1",WideProfile="stdin-v1";
    private string StatusPath(string source,string relative)=>Path.Combine(workspace.Local,"exploration","status",Json.Hash(source+"/"+relative)+".json");
    public ExplorationStatus? StatusFor(string source,string relative)
    {
        var path=StatusPath(source,relative);return File.Exists(path)?Json.Read<ExplorationStatus>(File.ReadAllText(path)):null;
    }
    public bool HasPending(string source,string relative,string key,SourceVersion? previous)
    {
        var status=StatusFor(source,relative);
        return status!=null&&status.SourceKey==key&&status.RunId!=previous?.Exploration?.RunId;
    }
    private static string Short(string text,int count)=>text[..Math.Min(text.Length,count)];
    private static string Section(Unit u)
    {
        foreach(var separator in new[]{"; row:","; cell:"})
        {var p=u.Locator.LastIndexOf(separator,StringComparison.Ordinal);if(p>=0)return u.Locator[..p];}
        return "document";
    }
    public static DocumentSection[] Catalog(Unit[] units)=>units.Select((u,i)=>(Unit:u,Number:i+1)).GroupBy(x=>Section(x.Unit))
        .Select(g=>new DocumentSection(Short(g.Key,160),g.First().Number,g.Count(),g.Take(2).Select(x=>Short(x.Unit.Text,160)).ToArray())).ToArray();
    public static void ValidateSummary(ExplorationSummary s,int units)
    {
        if(s.Protocol!=Protocol||!Guid.TryParseExact(s.RunId,"N",out _)||string.IsNullOrWhiteSpace(s.Plan)||s.Plan.Length>1800||string.IsNullOrWhiteSpace(s.Summary)||s.Summary.Length>2000||s.TotalUnits!=units||s.Steps is <1 or >512||s.Coverage==null||s.Questions==null||s.Questions.Length>50||s.Questions.Any(q=>string.IsNullOrWhiteSpace(q)||q.Length>500)||s.VisualStatus is not ("not-applicable" or "observed" or "unreadable" or "unsupported"))throw new InvalidDataException("Invalid exploration summary.");
        long next=1;var totals=new Dictionary<string,int>{{"analyzed",0},{"excluded",0},{"unresolved",0},{"empty",0}};
        foreach(var r in s.Coverage)
        {
            if(r==null||r.Start!=next||r.Count<=0||!totals.ContainsKey(r.Status)||r.Reason==null||r.Reason.Length>500||r.Status!="analyzed"&&string.IsNullOrWhiteSpace(r.Reason)||(long)r.Start+r.Count-1>units)throw new InvalidDataException("Invalid exploration coverage.");
            totals[r.Status]+=r.Count;next+=r.Count;
        }
        if(next!=units+1L||totals["analyzed"]!=s.AnalyzedUnits||totals["excluded"]!=s.ExcludedUnits||totals["unresolved"]!=s.UnresolvedUnits||totals["empty"]!=s.EmptyUnits)throw new InvalidDataException("Inconsistent exploration coverage counts.");
    }
    public static void Validate(ExplorationReply reply,ExplorationRequest request)
    {
        [System.Diagnostics.CodeAnalysis.DoesNotReturn] static void Fail(string reason)=>throw new InvalidDataException("Exploration Copilot refusée : "+reason);
        if(string.IsNullOrWhiteSpace(reply.Plan)||reply.Plan.Length>1800||reply.Summary==null||reply.Summary.Length>2000||reply.Query==null||reply.Query.Length>200)Fail("plan, synthèse ou recherche hors limites.");
        var exclusions=reply.Exclusions??[];
        if(exclusions.Length>4||exclusions.Any(e=>e==null||e.Start<1||e.Count<1||(long)e.Start+e.Count-1>request.TotalUnits||string.IsNullOrWhiteSpace(e.Reason)||e.Reason.Length>440)||reply.Action=="exclude"&&exclusions.Length==0||reply.Action!="exclude"&&exclusions.Length>0)Fail("exclusion de plage invalide ou sans justification.");
        if(reply.Questions==null||reply.Questions.Length>8||reply.Questions.Any(q=>string.IsNullOrWhiteSpace(q)||q.Length>500))Fail("questions hors limites.");
        if(reply.VisualStatus is not ("not-applicable" or "observed" or "unreadable" or "unsupported"))Fail("statut visuel inconnu.");
        var image=request.Format is ".png" or ".jpg" or ".jpeg";
        if(!image&&reply.VisualStatus!="not-applicable")Fail("observation visuelle sans image fournie.");
        if(image&&reply.VisualStatus=="observed"&&request.Visible.Length==0)Fail("image non fournie dans cet échange.");
        CopilotAnalyzer.Validate(new(reply.Findings),request.Context with{Units=request.Visible.Select(v=>v.Unit).ToArray()});
        var visible=request.Visible.Select(v=>v.Unit.Id).ToHashSet();
        if(reply.Reviews==null||reply.Reviews.Length>200||reply.Reviews.Any(r=>r==null||!visible.Contains(r.UnitId)||r.Status is not ("analyzed" or "excluded" or "unresolved")||r.Reason==null||r.Reason.Length>500||r.Status!="analyzed"&&string.IsNullOrWhiteSpace(r.Reason))||reply.Reviews.Select(r=>r.UnitId).Distinct().Count()!=reply.Reviews.Length)Fail("couverture non fournie, dupliquée ou sans justification.");
        var ranges=reply.ReviewRanges??[];var byNumber=request.Visible.ToDictionary(v=>v.Number,v=>v.Unit.Id);var reviewed=reply.Reviews.Select(r=>r.UnitId).ToHashSet();
        if(ranges.Length>200)Fail("trop de plages de couverture.");
        foreach(var range in ranges)
        {
            if(range==null||range.Start<1||range.Count is <1 or >200||(long)range.Start+range.Count-1>request.TotalUnits||range.Status is not ("analyzed" or "excluded" or "unresolved")||range.Reason==null||range.Reason.Length>500||range.Status!="analyzed"&&string.IsNullOrWhiteSpace(range.Reason))Fail("plage de couverture invalide.");
            for(var n=range.Start;n<range.Start+range.Count;n++)
                if(!byNumber.TryGetValue(n,out var id)||!reviewed.Add(id))Fail("la couverture doit citer uniquement des unités visibles, sans doublons ni trous.");
        }
        if(image&&reply.VisualStatus!="observed"&&(reply.Findings.Count>0||reply.Reviews.Any(r=>r.Status=="analyzed")||ranges.Any(r=>r.Status=="analyzed")))Fail("image non observée : aucune conclusion visuelle ni couverture complète autorisée.");
        if(reply.Action=="finish"&&string.IsNullOrWhiteSpace(reply.Summary))Fail("synthèse finale manquante.");
        // Validate all evidence and coverage FIRST, including replies with invalid navigation.
        ValidateNavigation(reply,request);
    }
    private static void ValidateNavigation(ExplorationReply reply,ExplorationRequest request)
    {
        [System.Diagnostics.CodeAnalysis.DoesNotReturn] void Fail(string reason)=>throw new CopilotNavigationException(reason,
            $"Navigation was invalid: {reason} Use action read/search/catalog/exclude/finish. reads must be an array, empty except for action read, which requires 1 to 4 ranges. start is a 1-based UNIT NUMBER from 1 to {request.TotalUnits}, not an Excel row or an evidence ID; count is a positive number of units, NOT an end index. Large reads are safely bounded by bip-kb. offset must be nonnegative; catalog offsets are less than {Math.Max(1,request.SectionCount)}. search requires a nonempty query. Use visible/pending/catalog to choose valid numbers. Return a complete JSON response to the ORIGINAL request, with valid evidence and coverage; nothing from the rejected reply was accepted.");
        if(reply.Action is not ("read" or "search" or "catalog" or "exclude" or "finish"))Fail("action inconnue.");
        if(reply.Reads==null)Fail("reads : tableau requis.");
        if(reply.Reads.Length>4)Fail($"reads : {reply.Reads.Length} plages demandées, maximum 4.");
        if(reply.Action=="read"&&reply.Reads.Length==0||reply.Action!="read"&&reply.Reads.Length!=0)Fail("reads doit être non vide uniquement pour l'action read.");
        for(var i=0;i<reply.Reads.Length;i++)
        {
            var range=reply.Reads[i];
            if(range==null)Fail($"reads[{i}] : plage nulle.");
            if(range.Start<1||range.Start>request.TotalUnits)Fail($"reads[{i}].start={range.Start} : numéro d'unité attendu entre 1 et {request.TotalUnits}.");
            if(range.Count<1)Fail($"reads[{i}].count={range.Count} : nombre d'unités strictement positif requis.");
        }
        if(reply.Offset<0)Fail($"offset={reply.Offset} : valeur négative.");
        if(reply.Action=="search"&&string.IsNullOrWhiteSpace(reply.Query))Fail("query : recherche vide.");
        if(reply.Action=="catalog"&&reply.Offset>=Math.Max(1,request.SectionCount))Fail($"offset={reply.Offset} : page de catalogue inexistante ({request.SectionCount} sections).");
    }
    public static int[] ReadNumbers(UnitRange[] ranges,int totalUnits)
    {
        // Do not materialize an untrusted Count. ValidateNavigation rejects invalid starts/counts.
        return ranges.SelectMany(r=>Enumerable.Range(r.Start,Math.Min(r.Count,totalUnits-r.Start+1))).Distinct().Take(200).ToArray();
    }
    public async Task<ExplorationSummary> Run(string source,string relative,string key,string format,Unit[] units,
        IReadOnlyDictionary<string,UnitHeader> headers,string[] limitations,string? image,bool force,ExplorationBudget budget,
        Func<Unit[],string,int,AnalysisRequest> context,Action<Analysis> accept,CancellationToken ct)
    {
        var catalog=Catalog(units);var positions=units.Select((u,i)=>(u.Id,i)).ToDictionary(p=>p.Id,p=>p.i);
        var coverage=new UnitReview?[units.Length];
        for(var i=0;i<units.Length;i++)
            if(headers.TryGetValue(units[i].Id,out var h)&&units[i].Text==h.Prefix)coverage[i]=new(units[i].Id,"empty","Ligne Excel sans données ; en-têtes conservés ailleurs.");
        var previousStatus=StatusFor(source,relative);
        var profile=!force&&previousStatus?.SourceKey==key?previousStatus.Profile:await assistant.ReadingProfile(ct);
        if(profile is not (LegacyProfile or WideProfile))throw new InvalidDataException("Profil de lecture inconnu ; mettez bip-kb à jour avant de reprendre.");
        var wide=profile==WideProfile;var readBudget=wide?120_000:8500;var requestBudget=wide?160_000:18000;
        VisibleUnit[] Read(IEnumerable<int> numbers)=> (AnalysisBatches.Create(numbers.Select(n=>units[n-1]),headers,readBudget).FirstOrDefault()??[])
            .Select(u=>new VisibleUnit(positions[u.Id]+1,u)).ToArray();
        var visible=image!=null?units.Select((u,i)=>new VisibleUnit(i+1,u)).ToArray():wide?Read(Enumerable.Range(1,Math.Min(200,units.Length))):[];
        var matches=Array.Empty<DocumentMatch>();var matchCount=0;var offset=0;var plan="";var feedback="Vue d'ensemble : les aperçus sont incomplets et ne sont pas des unités analysées.";
        var questions=new HashSet<string>();var visual="not-applicable";
        if(wide)feedback=$"Vue d'ensemble et première lecture : {visible.Length}/{units.Length} unités complètes fournies. Le catalogue décrit les sections ; seuls visible[] sont citables. Poursuivre les sections restantes via read/pending.";
        var statusPath=StatusPath(source,relative);
        var runId=!force&&previousStatus?.SourceKey==key?previousStatus.RunId:Guid.NewGuid().ToString("N");
        void Status(int step,string state)
        {
            Json.Write(statusPath,new ExplorationStatus(source,relative,key,runId,step,coverage.Count(c=>c==null),state,plan,profile));
        }
        for(var step=1;step<=512;step++)
        {
            ct.ThrowIfCancellationRequested();
            var supplied=visible.Select(v=>v.Unit).ToArray();var existing=context(supplied,plan,requestBudget);
            // Context() selects claims and decisions; evidence is supplied only once in Visible.
            existing=existing with{Units=[]};
            var request=new ExplorationRequest(Protocol,relative,format,step,units.Length,coverage.Count(c=>c==null),
                Ranges(coverage.Select((c,i)=>(c,i)).Where(x=>x.c==null).Select(x=>x.i+1)).Take(12).ToArray(),
                catalog.Length,offset,catalog.Skip(offset).Take(8).ToArray(),matchCount,matches,visible,existing,plan,feedback,limitations.Take(8).Select(l=>Short(l,250)).ToArray());
            while(Json.Serialize(request).Length>requestBudget)
            {
                if(request.Context.Existing.Length>0)
                {
                    var keep=request.Context.Existing[..^1];var ids=keep.Select(c=>c.Id).ToHashSet();
                    request=request with{Context=request.Context with{Existing=keep,Entities=request.Context.Entities.Where(e=>keep.Any(c=>c.EntityId==e.Id)).ToArray(),Decisions=request.Context.Decisions.Where(d=>ids.Contains(d.ClaimId)).ToArray()}};
                }
                else if(request.Catalog.Length>0)request=request with{Catalog=request.Catalog[..^1]};
                else if(request.Matches.Length>0)request=request with{Matches=request.Matches[..^1]};
                else if(request.Visible.Length>1)request=request with{Visible=request.Visible[..^1]};
                else throw new InvalidDataException("Le contexte et une unité dépassent le budget d'exploration ; raccourcissez le contexte déclaré.");
            }
            if(request.Visible.Length!=visible.Length)request=request with{Feedback=$"Lecture bornée : {request.Visible.Length} unités transmises intégralement ; seules visible[] sont citables."};
            var path=Path.Combine(workspace.Local,"exploration","responses",Json.Hash(key+source+relative+Json.Serialize(request))+".json");
            ExplorationReply reply;
            if(!force&&File.Exists(path))
            {
                reply=Json.Read<ExplorationReply>(File.ReadAllText(path));Validate(reply,request);
                progress?.Invoke($"Exploration — échange {step} réutilisé");
            }
            else
            {
                if(!budget.TryUse())
                {Status(step,"paused");throw new InvalidOperationException($"Exploration en pause après {budget.Used} nouveaux appels ; {request.RemainingUnits}/{units.Length} unités restent à classer dans ce fichier. Aucune révision inachevée publiée. Relancez avec --analyze, sans --reanalyze, pour reprendre. Budget : --max-calls.");}
                Status(step,"running");progress?.Invoke($"Exploration — échange {step}, appel {budget.Used}/{budget.Limit}, {request.RemainingUnits}/{units.Length} unités à classer, {request.Visible.Length} fournies ({Json.Serialize(request).Length:N0} caractères) — attente de Copilot");
                try
                {
                    reply=await assistant.Explore(request,image!=null&&request.Visible.Length>0?image:null,ct);Validate(reply,request);
                    Json.Write(path,reply);
                }
                catch
                {
                    try{Status(step,"interrupted");}catch(Exception e) when(e is IOException or UnauthorizedAccessException){progress?.Invoke("Le statut local n'a pas pu être actualisé ; les réponses validées restent dans le cache.");}
                    throw;
                }
            }
            accept(new(reply.Findings));plan=reply.Plan;
            foreach(var review in reply.Reviews)coverage[positions[review.UnitId]]=review;
            foreach(var range in reply.ReviewRanges??[])
                for(var n=range.Start;n<range.Start+range.Count;n++)coverage[n-1]=new(units[n-1].Id,range.Status,range.Reason);
            foreach(var exclusion in reply.Exclusions??[])
                for(var n=exclusion.Start;n<exclusion.Start+exclusion.Count;n++)coverage[n-1]=new(units[n-1].Id,"excluded","Exclusion de plage (lecture non garantie) : "+exclusion.Reason);
            foreach(var question in reply.Questions)questions.Add(question);
            if(image!=null&&request.Visible.Length>0)visual=reply.VisualStatus;
            progress?.Invoke($"Exploration — {coverage.Count(c=>c!=null)}/{units.Length} unités classées ; prochaine action : {reply.Action}");
            if(reply.Action=="finish"&&coverage.All(c=>c!=null))
            {
                var ranges=new List<CoverageRange>();
                for(var i=0;i<coverage.Length;i++)
                {
                    var review=coverage[i]!;
                    if(ranges.Count>0&&ranges[^1].Status==review.Status&&ranges[^1].Reason==review.Reason)ranges[^1]=ranges[^1] with{Count=ranges[^1].Count+1};
                    else ranges.Add(new(i+1,1,review.Status,review.Reason));
                }
                var summary=new ExplorationSummary(Protocol,runId,plan,reply.Summary,units.Length,coverage.Count(c=>c?.Status=="analyzed"),coverage.Count(c=>c?.Status=="excluded"),coverage.Count(c=>c?.Status=="unresolved"),coverage.Count(c=>c?.Status=="empty"),step,ranges.ToArray(),questions.Take(50).ToArray(),visual);
                Status(step,"ready");return summary;
            }
            visible=[];matches=[];matchCount=0;
            switch(reply.Action)
            {
                case "read":
                    var numbers=ReadNumbers(reply.Reads,units.Length);
                    visible=Read(numbers);
                    feedback=$"{visible.Length}/{numbers.Length} unités demandées fournies intégralement. Les autres restent à lire ; utiliser les numéros visibles et pending pour poursuivre.";
                    if(reply.Reads.Sum(r=>(long)r.Count)>200||reply.Reads.Any(r=>(long)r.Start+r.Count-1>units.Length))
                        feedback+=" Demande bornée à 200 unités distinctes et à la fin du document ; aucune unité non fournie n'est déclarée analysée.";
                    break;
                case "search":
                    var found=units.Select((u,i)=>(u,i)).Where(x=>x.u.Text.Contains(reply.Query,StringComparison.OrdinalIgnoreCase)||x.u.Locator.Contains(reply.Query,StringComparison.OrdinalIgnoreCase)).ToArray();
                    matchCount=found.Length;matches=found.Skip(reply.Offset).Take(12).Select(x=>new DocumentMatch(x.i+1,Short(x.u.Locator,160),Short(x.u.Text,160))).ToArray();
                    feedback="Résultats de recherche : aperçus seulement. Demander read pour obtenir des preuves citables.";break;
                case "catalog":offset=reply.Offset;feedback="Catalogue paginé ; lire les unités pertinentes avant de les classer.";break;
                case "exclude":feedback="Plages explicitement exclues, non déclarées analysées. L'analyse publiée restera partielle ; poursuivre les autres unités ou synthétiser.";break;
                default:feedback="Synthèse prématurée : des unités restent non lues/non classées. Continuer via pending ; ne pas déclarer une couverture complète.";break;
            }
        }
        Status(512,"limit");throw new InvalidOperationException("Limite de 512 échanges atteinte. Les résultats intermédiaires sont conservés ; aucune nouvelle révision publiée.");
    }
    private static IEnumerable<UnitRange> Ranges(IEnumerable<int> numbers)
    {
        var start=0;var count=0;
        foreach(var n in numbers)
        {
            if(count>0&&n!=start+count){yield return new(start,count);count=0;}
            if(count==0)start=n;count++;
        }
        if(count>0)yield return new(start,count);
    }
}
