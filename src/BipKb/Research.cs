using System.Text;
namespace BipKb;

public sealed class Research(Workspace workspace,IResearchAssistant assistant)
{
    public WorkTask Start(string goal,string? project)
    {
        if(string.IsNullOrWhiteSpace(goal)||goal.Length>4000)throw new ArgumentException("A task goal is required (maximum 4000 characters).");
        var profile=new Projects(workspace).Find(project)??throw new ArgumentException("Set up a project first; choose --project if several exist.");
        var task=new WorkTask(Guid.NewGuid().ToString("N"),profile.Name,goal,DateTimeOffset.UtcNow,[],[],[],[]);
        Store(task);return task;
    }
    public WorkTask Get(string id)=>workspace.Load().Tasks.SingleOrDefault(t=>t.Id==id)??throw new ArgumentException("Unknown task.");
    public static CoverageItem[] Coverage(SearchHit[] hits)
    {
        CoverageItem Item(string name,Func<SearchHit,bool> predicate,string missing)
        {
            var found=hits.Where(predicate).SelectMany(h=>h.EvidenceIds).Distinct().ToArray();
            return new(name,found.Length>0,found,found.Length>0?"Evidence candidates found; inspect their content before claiming completeness.":missing);
        }
        return [Item("Modèle cible",h=>h.Role==SourceRole.Target,"Find the target schema and affected objects."),
            Item("Ancien et correspondances",h=>h.Role==SourceRole.Legacy||h.Reasons.Any(r=>r.Contains("MapsTo")||r.Contains("Evolution")),"Find legacy behavior and validated mappings."),
            Item("Règles et filtres",h=>h.Kind=="claim"||h.Kind is "procedure" or "method","Find conditions, exceptions and business rules."),
            Item("Implémentation de référence",h=>h.Role==SourceRole.Current||h.Kind is "method" or "type","Find a comparable implementation and conventions."),
            Item("Ambiguïtés",h=>h.State==KnowledgeState.Reexamine,"Absence of flagged conflict does not establish absence of ambiguity.")];
    }
    public string Fingerprint(string project)
    {
        var k=workspace.Load();
        var sourceIds=k.Sources.Where(s=>s.Context?.Project.Equals(project,StringComparison.OrdinalIgnoreCase)==true).Select(s=>s.Id).Order().ToArray();
        var claims=k.Claims.Where(c=>c.Context?.Project.Equals(project,StringComparison.OrdinalIgnoreCase)==true).Select(c=>c.Id).ToHashSet();
        // New relevant evidence can invalidate a dossier even when none of its old citations changed.
        return Json.Hash(Json.Serialize(new{sources=k.Sources.Where(s=>sourceIds.Contains(s.Id)).OrderBy(s=>s.Id),claims=k.Claims.Where(c=>claims.Contains(c.Id)).OrderBy(c=>c.Id),relations=k.Relations.OrderBy(r=>r.Id),projects=k.Projects.Where(p=>p.Name==project),declarations=k.Questions.Where(q=>q.Project==project&&q.Answer!=null),decisions=workspace.Decisions().Where(d=>claims.Contains(d.ClaimId))}));
    }
    public bool IsStale(WorkTask task)=>task.ResearchFingerprint!=null&&task.ResearchFingerprint!=Fingerprint(task.Project);
    public async Task<WorkTask> Run(string id,string? message,CancellationToken ct)
    {
        var task=Get(id);var initial=Json.Hash(Json.Serialize(task));var profile=new Projects(workspace).Find(task.Project)!;
        var fingerprint=Fingerprint(task.Project);var findings=new Dictionary<string,SearchHit>();var steps=new List<ResearchStep>();var turns=task.Turns.ToList();
        if(message!=null)
        {
            if(message.Length>4000)throw new ArgumentException("Message exceeds 4000 characters.");
            turns.Add(new(Guid.NewGuid().ToString("N"),"user",message,DateTimeOffset.UtcNow,[]));
        }
        var queryText=message==null ? task.Goal : message+" "+task.Goal;
        var pending=new Queue<string>();pending.Enqueue(queryText[..Math.Min(4000,queryText.Length)]);var asked=new HashSet<string>();ResearchAdvice? advice=null;
        var settings=workspace.Config;var cycles=Math.Clamp(settings.MaxResearchCycles,1,5);var engine=new SearchEngine(workspace);
        for(var cycle=0;cycle<cycles&&pending.Count>0;cycle++)
        {
            var query=pending.Dequeue();if(!asked.Add(query))continue;
            var result=engine.Search(new(query,task.Project,Depth:Math.Clamp(settings.GraphDepth,0,3)));
            steps.Add(new(cycle+1,query,result.Hits.Select(h=>h.Id).ToArray(),result.Milliseconds));foreach(var hit in result.Hits)findings[hit.Id]=hit;
            var selected=findings.Values.OrderByDescending(h=>h.Score).Take(30).ToArray();
            var declarations=workspace.Load().Questions.Where(q=>q.Project==task.Project&&q.Answer!=null).TakeLast(5);
            var request=BuildRequest(task.Goal,profile,selected,turns,declarations,cycle+1,settings.ResearchBudget);
            steps[^1]=steps[^1] with{SuppliedIds=request.Findings.Select(h=>h.Id).ToArray(),RequestHash=Json.Hash(Json.Serialize(request))};
            advice=await assistant.Investigate(request,ct);Validate(advice,request);
            foreach(var next in advice.NextQueries.Where(q=>!asked.Contains(q)).Take(3))pending.Enqueue(next);
            if(advice.NextQueries.Length==0)break;
        }
        if(advice==null)throw new InvalidOperationException("No research step executed.");
        if(Fingerprint(task.Project)!=fingerprint)throw new RevisionConflictException("Evidence changed during research; resume the task to refresh it.");
        turns.Add(new(Guid.NewGuid().ToString("N"),"assistant",advice.Answer,DateTimeOffset.UtcNow,advice.Citations));
        var final=task with{Turns=turns,Searches=task.Searches.Concat(steps).ToList(),Findings=findings.Values.ToArray(),Coverage=Coverage(findings.Values.ToArray()),Revision=workspace.SemanticRevision,ResearchFingerprint=fingerprint};
        using var gate=workspace.Lock();var k=workspace.Load();var current=k.Tasks.Single(t=>t.Id==id);
        if(Json.Hash(Json.Serialize(current))!=initial||Fingerprint(task.Project)!=fingerprint)throw new RevisionConflictException("Task or project changed; resume instead of overwriting concurrent work.");
        k.Tasks.RemoveAll(t=>t.Id==id);k.Tasks.Add(final);
        foreach(var question in advice.Questions)
        {
            var qid=Json.Hash(task.Project+question);
            if(!k.Questions.Any(q=>q.Id==qid))k.Questions.Add(new(qid,task.Project,question,advice.Citations));
        }
        workspace.Save(k);return final;
    }
    public static ResearchRequest BuildRequest(string goal, ProjectProfile profile, SearchHit[] ranked, List<TaskTurn> turns,
        IEnumerable<OpenQuestion> declarations, int cycle, int configuredBudget)
    {
        var budget = Math.Clamp(configuredBudget, 6000, 18000);
        var latest = turns.LastOrDefault();
        var current = latest?.Role == "user" ? latest : null;
        var request = new ResearchRequest(goal, null, [], current == null ? [] : [current with { Citations = [] }], [], cycle, []);
        if (Json.Serialize(request).Length > budget)
            throw new ArgumentException($"La question et l’objectif dépassent à eux seuls la limite de {budget} caractères JSON. Raccourcissez la question ou l’objectif.");

        bool Include(ResearchRequest candidate)
        {
            if (Json.Serialize(candidate).Length > budget) return false;
            request = candidate;
            return true;
        }

        static string Excerpt(string text, int length) => text.Length <= length ? text : text[..length] + " […]";
        static string[]? Conditions(string[]? values)
        {
            if (values == null) return null;
            var excerpts = values.Take(3).Select(value => Excerpt(value, 240)).ToArray();
            return values.Length > excerpts.Length ? [..excerpts, "[conditions supplémentaires omises]"] : excerpts;
        }

        void AddHit(SearchHit hit)
        {
            var excerpt = hit with
            {
                Title = Excerpt(hit.Title, 200), Text = Excerpt(hit.Text, 1000),
                EvidenceIds = hit.EvidenceIds.Take(3).ToArray(), SearchTerms = null, Conditions = Conditions(hit.Conditions),
                Reasons = hit.Reasons.Take(4).Select(r => Excerpt(r, 120)).ToArray()
            };
            bool Add(SearchHit item)
            {
                SearchHit[] hits = [..request.Findings, item];
                return Include(request with { Findings = hits, Coverage = Coverage(hits) });
            }
            if (!Add(excerpt))
                Add(excerpt with { Text = Excerpt(hit.Text, 240), EvidenceIds = hit.EvidenceIds.Take(1).ToArray() });
        }

        var summary = new ProjectProfile(profile.Id, profile.Name, Excerpt(profile.Objective, 300),
            Excerpt(profile.Architecture, 200), [], [], [], [], []);
        Include(request with { Project = summary });
        // Reserve room for the top evidence before optional framing and history.
        foreach (var hit in ranked.Take(2)) AddHit(hit);
        var framing = Compact(profile, current?.Text ?? goal);
        if (!Include(request with { Project = framing }))
            Include(request with { Project = summary with { Brief = framing.Brief } });
        foreach (var declaration in declarations.Reverse())
        {
            var item = declaration with { Text = Excerpt(declaration.Text, 240),
                Answer = Excerpt(declaration.Answer!, 500), EvidenceIds = declaration.EvidenceIds.Take(3).ToArray() };
            Include(request with { HumanDeclarations = [item, ..request.HumanDeclarations!] });
        }
        foreach (var turn in turns.Take(turns.Count - (current == null ? 0 : 1)).TakeLast(5).Reverse())
        {
            var item = turn with { Text = Excerpt(turn.Text, 600), Citations = turn.Citations.Take(3).ToArray() };
            Include(request with { Conversation = [item, ..request.Conversation] });
        }
        foreach (var hit in ranked.Skip(2)) AddHit(hit);
        if (request.Findings.Length == 0) Include(request with { Coverage = Coverage([]) });
        return request;
    }

    public static ProjectProfile Compact(ProjectProfile p,string? goal=null)
    {
        var compact=BusinessInterview.Compact(p) with{Objective=p.Objective[..Math.Min(600,p.Objective.Length)],Architecture=p.Architecture[..Math.Min(600,p.Architecture.Length)],Conventions=p.Conventions.Take(5).Select(c=>c[..Math.Min(c.Length,200)]).ToArray(),Glossary=p.Glossary.Take(5).ToArray(),Domains=p.Domains.Take(5).ToArray(),Authorities=p.Authorities.Take(5).ToArray(),Systems=p.Systems.Take(5).ToArray()};
        if(compact.Brief is {} brief)
        {
            var terms=SearchEngine.Terms(goal??"");
            string Clip(string text,int max)=>text.Length<=max?text:text[..max]+" [extrait ; cadrage complet via project]";
            var items=brief.Content.Items.OrderByDescending(i=>terms.Count(t=>SearchEngine.Normalize(i.Text).Contains(t))+(i.Topic is "constraint" or "decision"?1:0)).Take(6)
                .Select(i=>i with{Text=Clip(i.Text,350),TurnIds=[]}).ToArray();
            compact=compact with{Brief=brief with{Content=brief.Content with{Objective=Clip(brief.Content.Objective,600),Scope=Clip(brief.Content.Scope,600),Items=items,OpenQuestions=brief.Content.OpenQuestions.Take(3).Select(q=>Clip(q,200)).ToArray()}}};
        }
        return compact;
    }
    public static void Validate(ResearchAdvice advice,ResearchRequest request)
    {
        if(string.IsNullOrWhiteSpace(advice.Answer)||advice.Answer.Length>16000||advice.Citations==null||advice.NextQueries==null||advice.Questions==null||advice.NextQueries.Length>3||advice.Questions.Length>5||advice.NextQueries.Concat(advice.Questions).Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>1000))throw new InvalidDataException("Invalid research response.");
        var ids=request.Findings.Select(h=>h.Id).Concat(request.Findings.SelectMany(h=>h.EvidenceIds)).Concat((request.HumanDeclarations??[]).Select(q=>q.Id)).ToHashSet();
        if(advice.Citations.Any(id=>!ids.Contains(id))||request.Findings.Length>0&&advice.Citations.Length==0)throw new InvalidDataException("Research response must cite supplied evidence.");
    }
    public OpenQuestion Remember(string project,string declaration,string actor,bool confirmed)
    {
        if(!confirmed||string.IsNullOrWhiteSpace(declaration)||declaration.Length>4000||string.IsNullOrWhiteSpace(actor))throw new ArgumentException("Use --confirm, --actor and a declaration of at most 4000 characters.");
        var profile=new Projects(workspace).Find(project)??throw new ArgumentException("Unknown project.");
        using var gate=workspace.Lock();workspace.EnsureWritable();var k=workspace.Load();
        var q=new OpenQuestion(Guid.NewGuid().ToString("N"),profile.Name,"Déclaration humaine",[],declaration,actor);k.Questions.Add(q);workspace.Save(k);return q;
    }
    public void AnswerQuestion(string id,string text,string actor,bool confirmed)
    {
        if(!confirmed)throw new ArgumentException("Use --confirm to record this human declaration.");
        if(string.IsNullOrWhiteSpace(text)||text.Length>4000||string.IsNullOrWhiteSpace(actor))throw new ArgumentException("Answer and actor are required.");
        using var gate=workspace.Lock();workspace.EnsureWritable();var k=workspace.Load();var q=k.Questions.Single(x=>x.Id==id);
        // Keep the initial question and each declaration in task history; do not turn a user's answer into extracted evidence.
        k.Questions[k.Questions.IndexOf(q)]=q with{Answer=text,Actor=actor};workspace.Save(k);
    }
    public string Export(string id)
    {
        var task=Get(id);var text=new StringBuilder($"# {task.Goal}\n\nProjet : {task.Project}\nÉtat : {(IsStale(task)?"À actualiser":"Révision consultée à jour")}\n");
        foreach(var c in task.Coverage)text.AppendLine($"- {c.Name} : {(c.Supported?"preuves candidates":"à compléter")} — {c.Explanation}");
        foreach(var turn in task.Turns)text.AppendLine($"\n## {turn.Role}\n{turn.Text}\nRéférences : {string.Join(", ",turn.Citations)}");
        text.AppendLine("\n## Preuves consultées");foreach(var hit in task.Findings)text.AppendLine($"\n### {hit.Title} [{hit.Id}]\n{hit.Text}\nStatut : {hit.State} ; origine : {hit.Origin} ; rôle : {hit.Role} ; preuves : {string.Join(", ",hit.EvidenceIds)}");
        text.AppendLine("\n## Recherches");foreach(var step in task.Searches)text.AppendLine($"- {step.Query}: {step.ResultIds.Length} résultats, {step.Milliseconds:F0} ms ; transmis : {string.Join(", ",step.SuppliedIds??[])} ; empreinte : {step.RequestHash}");
        return text.ToString();
    }
    private void Store(WorkTask task){using var gate=workspace.Lock();workspace.EnsureWritable();var k=workspace.Load();k.Tasks.Add(task);workspace.Save(k);}
}
