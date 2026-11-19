using BipKb;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;

return await Cli.Run(args);

public static class Cli
{
    public static async Task<int> Run(string[] args)
    {
        if(args.Length==1 && args[0] is "--help" or "-h")args=["help"];
        if(args.Length==1 && args[0]=="--version"){Console.WriteLine("bip-kb 0.5.0");return 0;}
        using var cancel=new CancellationTokenSource();
        Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
        try
        {
            var parsed=new Arguments(args);var workspace=new Workspace(parsed.Get("workspace")??Directory.GetCurrentDirectory());
            var command=parsed.Has("ingest")?"ingest":parsed.Words.FirstOrDefault()??"help";
            var analyzer=new CopilotAnalyzer(workspace);var service=new KnowledgeService(workspace);object? result=null;var research=new Research(workspace,analyzer);
            string Required(int index)=>parsed.Words.ElementAtOrDefault(index)??throw new ArgumentException("Missing argument. Run bip-kb help.");
            SourceRole? role=parsed.Get("role") is {} r? Enum.TryParse<SourceRole>(r,true,out var value)&&Enum.IsDefined(value)&&value!=SourceRole.Unknown?value:throw new ArgumentException("Role must be legacy, target or current."):null;
            switch(command)
            {
                case "help":
                    Console.WriteLine("""
bip-kb — Analyste métier et mémoire sourcée
  init --workspace <empty-directory>
  migrate [--dry-run] | memory resolve --file <resolutions.json>
  project setup [--file <project.json>] | project show [--project <id-or-name>]
  project summary
  project setup [--message <answer> | --confirm] [--restart]
      New workspace: adaptive business interview; /pause and /confirm in interactive mode.
  task start <goal> --project <name> | task resume <id> | task export <id> [--output <file>]
  chat --task <id> | chat --task <id> --message <text>
  questions [--project <name>] | question answer <id> --message <answer> --actor <name> --confirm
  remember <declaration> --project <name> --actor <name> --confirm
  entity rename <id> --name <new-name>
  evaluate --file <cases.json> | benchmark [--count 100000]
  source bind <id> <directory> --role legacy|target|current
  --ingest <file-or-directory> | ingest <file-or-directory>
      (automatic SQL, Excel, code, JSON, image and Draw.io detection)
  --ingest --file <file> | --path <directory> | --image <image> | --schema <drawio>
      [--source <id>] [--role <role>] [--analyze | --reanalyze]
      [--describe] [--context-file <json>] [--non-interactive]
      New workspace: Excel/Draw.io/images use Copilot by default; --index-only keeps local indexing.
      Code/folders: local indexing by default; --analyze requests model analysis.
      --message <description> supplies source context without a form.
      --reanalyze: force full model analysis, bypassing cached answers.
      First import: guided context; /cancel aborts before ingestion.
  search <query> | ask <question> | context <task> [--output <file>]
  report <ingestion-id> | analysis <source-id> | review
  --ingest <path> --analyze [--analysis-mode guided|batches] [--max-calls 50]
      guided explores first; batches resumes older analyses. --reanalyze bypasses caches.
  review <claim-id> --action accept|reject|distinct|replace --reason <text> --actor <name>
  export --output <empty-directory> | import | reindex
      reindex rebuilds local search from published memory, without re-ingestion or Copilot.
  cache compact [--rebuild]
      losslessly compact the local memory cache; --rebuild ignores the old cache.
  doctor [--live] | serve [--port 5267] | mcp
All commands: --workspace <directory>, --json. Quote multiword arguments.
"""); return 0;
                case "migrate":result=workspace.Migrate(parsed.Has("dry-run"));break;
                case "memory":
                    if(Required(1)!="resolve")throw new ArgumentException("Use memory resolve --file.");
                    using(var gate=workspace.Lock()){new MemoryStore(workspace).Resolve(parsed.Get("file")??throw new ArgumentException("Provide --file."));}result=new{status="resolved"};break;
                case "project":
                    if(Required(1)=="setup"&&workspace.Config.BusinessMode)
                    {
                        if(parsed.Has("file"))throw new ArgumentException("Le nouveau cadrage utilise project setup ou --message, puis --confirm.");
                        var interview=new BusinessInterview(workspace,analyzer);
                        if(parsed.Has("restart"))interview.Restart();
                        await using var framingProgress=new IngestionProgress(parsed.Has("json")?null:Console.Error,label:"cadrage");
                        analyzer.Progress=framingProgress.Report;
                        if(parsed.Has("confirm"))
                        {
                            if(parsed.Has("message"))throw new ArgumentException("Envoyez votre réponse, examinez le récapitulatif puis confirmez dans une commande distincte.");
                            result=interview.Confirm(parsed.Get("actor")??Environment.UserName);
                        }
                        else if(parsed.Get("message") is {} framingMessage) {framingProgress.Report("Compréhension de votre réponse — attente de Copilot");result=await interview.Respond(framingMessage,cancel.Token);}
                        else if(!Console.IsInputRedirected&&!parsed.Has("non-interactive")&&!parsed.Has("json"))result=await interview.Interactive(Console.In,Console.Error,cancel.Token);
                        else result=interview.Draft() is {Reply:null,Published:false}?await interview.Respond(null,cancel.Token):(object?)interview.Draft()??new{status="awaiting-answer",question="Que cherchez-vous à comprendre ou à faire évoluer avec ce projet ?",next="project setup --message <réponse>"};
                        analyzer.Progress=null;break;
                    }
                    var projects=new Projects(workspace);
                    result=Required(1) switch{"setup"=>projects.Setup(parsed.Get("file"),Console.In,Console.Error,!Console.IsInputRedirected&&!parsed.Has("non-interactive")&&!parsed.Has("json")),"show"=>(object?)projects.Find(parsed.Get("project"))??workspace.Load().Projects,"summary"=>projects.Summary(parsed.Get("project")??projects.Find(null)?.Name??throw new ArgumentException("Configurez le projet avec project setup.")),_=>throw new ArgumentException("Use project setup/show/summary.")};break;
                case "task":
                    switch(Required(1))
                    {
                        case "start":result=research.Start(Required(2),parsed.Get("project"));break;
                        case "resume":result=await research.Run(Required(2),parsed.Get("message"),cancel.Token);break;
                        case "export":var dossier=research.Export(Required(2));if(parsed.Get("output") is {} target){File.WriteAllText(target,dossier);result=new{output=target};}else result=dossier;break;
                        default:throw new ArgumentException("Use task start/resume/export.");
                    }break;
                case "chat":
                    var taskId=parsed.Get("task")??throw new ArgumentException("Provide --task <id>.");
                    if(parsed.Get("message") is {} message){result=await research.Run(taskId,message,cancel.Token);break;}
                    if(Console.IsInputRedirected||parsed.Has("json"))throw new ArgumentException("Use --message for non-interactive chat.");
                    Console.WriteLine("Conversation bip-kb. /quit pour quitter ; remember enregistre une déclaration confirmée.");
                    while(true){Console.Write("> ");var messageLine=Console.ReadLine();if(messageLine==null||messageLine=="/quit")break;if(string.IsNullOrWhiteSpace(messageLine))continue;try{var updated=await research.Run(taskId,messageLine,cancel.Token);Console.WriteLine(updated.Turns.Last().Text);}catch(Exception e) when(e is not OperationCanceledException){Console.Error.WriteLine(e.Message);}}
                    return 0;
                case "questions":result=workspace.Load().Questions.Where(q=>parsed.Get("project")==null||q.Project==parsed.Get("project")).ToArray();break;
                case "question":if(Required(1)!="answer")throw new ArgumentException("Use question answer.");research.AnswerQuestion(Required(2),parsed.Get("message")??"",parsed.Get("actor")??"",parsed.Has("confirm"));result=new{status="recorded"};break;
                case "remember":result=research.Remember(parsed.Get("project")??new Projects(workspace).Find(null)?.Name??throw new ArgumentException("Configurez le projet avec project setup."),Required(1),parsed.Get("actor")??"",parsed.Has("confirm"));break;
                case "entity":
                    if(Required(1)!="rename")throw new ArgumentException("Use entity rename.");
                    using(var gate=workspace.Lock())
                    {workspace.EnsureWritable();var k=workspace.Load();var entity=k.Entities.Single(e=>e.Id==Required(2));var name=parsed.Get("name")??throw new ArgumentException("Provide --name.");k.Entities[k.Entities.IndexOf(entity)]=entity with{Name=name,Aliases=(entity.Aliases??[]).Append(entity.Name).Distinct().ToArray()};workspace.Save(k);result=new{entity.Id,name};}break;
                case "evaluate":result=Evaluation.Run(workspace,parsed.Get("file")??throw new ArgumentException("Provide --file."));break;
                case "benchmark":result=Evaluation.Benchmark(int.Parse(parsed.Get("count")??"100000"));break;
                case "init":
                    workspace.Init(business:true);
                    if(!Console.IsInputRedirected&&!parsed.Has("non-interactive")&&!parsed.Has("json"))
                    {
                        await using var initProgress=new IngestionProgress(Console.Error,label:"cadrage");analyzer.Progress=initProgress.Report;
                        result=await new BusinessInterview(workspace,analyzer).Interactive(Console.In,Console.Error,cancel.Token);analyzer.Progress=null;
                    }
                    else result=new{workspace=workspace.Root,status="initialized",next="bip-kb project setup --workspace "+workspace.Root};
                    break;
                case "cache":
                    if(Required(1)!="compact")throw new ArgumentException("Use cache compact [--rebuild].");
                    await using(var cacheProgress=new IngestionProgress(parsed.Has("json")?null:Console.Error,label:"cache"))
                        result=MaterializedCache.Compact(workspace,parsed.Has("rebuild"),cacheProgress.Report,cancel.Token);
                    break;
                case "source":
                    if(Required(1)!="bind")throw new ArgumentException("Use source bind.");
                    workspace.Bind(Required(2),Required(3),role);result=new{source=Required(2),status="bound"};break;
                case "doctor":result=await analyzer.Doctor(parsed.Has("live"),cancel.Token);break;
                case "ingest":
                    var input=parsed.IngestionInput();
                    if(parsed.Has("index-only")&&(parsed.Has("analyze")||parsed.Has("reanalyze")))throw new ArgumentException("Choisissez --index-only ou --analyze.");
                    var businessWorkspace=workspace.Config.BusinessMode;
                    var businessDocument=Path.GetExtension(input).ToLowerInvariant() is ".xlsx" or ".xls" or ".drawio" or ".png" or ".jpg" or ".jpeg";
                    var analyze=parsed.Has("analyze")||parsed.Has("reanalyze")||businessWorkspace&&businessDocument&&!parsed.Has("index-only");
                    var analysisMode=parsed.Get("analysis-mode")??"guided";
                    if(analysisMode is not ("guided" or "batches"))throw new ArgumentException("--analysis-mode : guided ou batches.");
                    if(!analyze&&(parsed.Has("analysis-mode")||parsed.Has("max-calls")))throw new ArgumentException("--analysis-mode et --max-calls accompagnent --analyze ou --reanalyze.");
                    int? maxCalls=null;
                    if(parsed.Get("max-calls") is {} callsText)
                    {if(analysisMode!="guided"||!int.TryParse(callsText,out var calls)||calls is <1 or >100)throw new ArgumentException("--max-calls : 1 à 100, en mode guided.");maxCalls=calls;}
                    await using(var progress=new IngestionProgress(parsed.Has("json")?null:Console.Error))
                    {
                        analyzer.Progress=progress.Report;
                        try
                        {
                            var interactive=!Console.IsInputRedirected&&!parsed.Has("non-interactive")&&!parsed.Has("json");
                            var setupOutput=parsed.Has("json")?TextWriter.Null:Console.Error;
                            if(businessWorkspace&&parsed.Has("context-file"))throw new ArgumentException("Le cadrage est réutilisé automatiquement ; utilisez --message pour préciser le rôle de cette source.");
                            progress.Report("Préparation et qualification de la source");
                            var source=businessWorkspace
                                ?await new BusinessSourceSetup(workspace,analyzer,Console.In,setupOutput,interactive).Prepare(input,parsed.Get("source"),role,parsed.Get("message"),parsed.Has("describe"),analyze,cancel.Token)
                                :await new SourceSetup(workspace,Console.In,setupOutput,interactive).Prepare(input,parsed.Get("source"),role,parsed.Get("context-file"),parsed.Has("describe"),cancel.Token,analyze);
                            result=await new Ingestor(workspace,analyzer,progress.Report).Run(input,source,null,parsed.Has("reanalyze"),cancel.Token,analyze,analysisMode=="guided",maxCalls);
                        }
                        finally{analyzer.Progress=null;}
                    }
                    break;
                case "search":result=new SearchEngine(workspace).Search(new(Required(1),parsed.Get("project"),parsed.Get("domain"),role,parsed.Get("intent")??"general",IncludeHistorical:parsed.Has("history")));break;
                case "ask":
                    var quick=research.Start(Required(1),parsed.Get("project"));result=await research.Run(quick.Id,null,cancel.Token);break;
                case "context":
                    var context=service.Context(Required(1),parsed.Get("project"),parsed.Get("domain"));if(parsed.Get("output") is {} output){File.WriteAllText(output,context);result=new{output};}else result=context;break;
                case "report":result=workspace.Load().Reports.SingleOrDefault(r=>r.Id==Required(1))??throw new ArgumentException("Unknown report.");break;
                case "analysis":
                    var analysisSource=Required(1);var analysisStatus=Path.Combine(workspace.Local,"exploration","status");
                    var publishedSources=workspace.Load().Sources.Where(s=>s.Active&&s.SourceId==analysisSource).ToArray();
                    var localStatuses=Directory.Exists(analysisStatus)?Directory.GetFiles(analysisStatus,"*.json").Select(f=>Json.Read<ExplorationStatus>(File.ReadAllText(f))).Where(s=>s.Source==analysisSource).ToArray():[];
                    result=new{published=publishedSources.Where(s=>s.Exploration!=null||s.Business!=null).Select(s=>new{s.RelativePath,s.Analyzed,s.Exploration,s.Business}),
                        local=localStatuses.Select(s=>new{s.RelativePath,s.Step,s.RemainingUnits,s.Plan,s.Profile,state=publishedSources.Any(p=>p.Exploration?.RunId==s.RunId)?"published":s.State,
                            stale=publishedSources.Any(p=>p.RelativePath==s.RelativePath&&p.CacheKey!=s.SourceKey)})};break;
                case "reindex":
                    await using(var indexProgress=new IngestionProgress(parsed.Has("json")?null:Console.Error,label:"index"))
                        new SearchEngine(workspace).Refresh(force:true,progress:indexProgress.Report,ct:cancel.Token);
                    result=new{status="indexed",index=new SearchEngine(workspace).PathName};break;
                case "review":
                    result=parsed.Words.Count==1?workspace.Review(workspace.Load()).Select(c=>service.Detail(c.Id)).ToArray():workspace.Decide(Required(1),parsed.Get("action")??"",parsed.Get("reason")??"",parsed.Get("actor")??"");break;
                case "import":service.Import();result=new{status="validated and indexed",decisionConflicts=workspace.Load().Claims.Count(c=>Workspace.Conflict(workspace.Decisions(),c.Id))};break;
                case "export":service.Export(parsed.Get("output")??throw new ArgumentException("Provide --output."));result=new{status="exported"};break;
                case "mcp":
                    Console.InputEncoding=new System.Text.UTF8Encoding(false);Console.OutputEncoding=new System.Text.UTF8Encoding(false);
                    await Mcp.Run(workspace,Console.In,Console.Out,cancel.Token);return 0;
                case "serve":
                    var port=int.Parse(parsed.Get("port")??"5267");if(port is <1024 or >65535)throw new ArgumentException("Port must be 1024..65535.");
                    _=workspace.Load();
                    var builder=WebApplication.CreateBuilder(new WebApplicationOptions{Args=[],ApplicationName=typeof(Cli).Assembly.FullName});
                    builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
                    builder.Services.AddSingleton(workspace);builder.Services.AddRazorPages();
                    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(workspace.Local,"web-keys")));
                    var app=builder.Build();
                    app.Use(async(ctx,next)=>{
                        if(ctx.Request.Host.Host is not "127.0.0.1" and not "localhost"){ctx.Response.StatusCode=400;return;}
                        ctx.Response.Headers.ContentSecurityPolicy="default-src 'self'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; form-action 'self'";
                        ctx.Response.Headers.XContentTypeOptions="nosniff"; await next();});
                    app.MapRazorPages();await app.RunAsync(cancel.Token);return 0;
                default:throw new ArgumentException("Unknown command: "+command);
            }
            if(result is string text&&!parsed.Has("json"))Console.WriteLine(text);else Console.WriteLine(Json.Serialize(result));return 0;
        }
        catch(OperationCanceledException){Console.Error.WriteLine("Cancelled; staged analysis retained.");return 130;}
        catch(Exception ex) when(ex is not OutOfMemoryException)
        {
            if(args.Contains("--json")) Console.Error.WriteLine(Json.Serialize(new{error=ex.Message,type=ex.GetType().Name}));
            else Console.Error.WriteLine("Error: "+ex.Message);
            return 1;
        }
    }
}
public sealed class Arguments
{
    private readonly Dictionary<string,string?> flags=[];
    public List<string> Words {get;}=[];
    public Arguments(string[] args)
    {
        string[] switches=["json","ingest","analyze","reanalyze","live","describe","non-interactive","dry-run","confirm","history","rebuild","restart","index-only"];string[] values=["workspace","file","path","image","schema","source","role","output","port","action","reason","actor","context-file","project","domain","task","message","name","count","intent","analysis-mode","max-calls"];
        for(var i=0;i<args.Length;i++)
        {
            if(!args[i].StartsWith("--")){Words.Add(args[i]);continue;}
            var name=args[i][2..];if(flags.ContainsKey(name))throw new ArgumentException("Duplicate option: "+name);
            if(switches.Contains(name))flags[name]=null;
            else if(values.Contains(name)&&i+1<args.Length&&!args[i+1].StartsWith("--"))flags[name]=args[++i];
            else throw new ArgumentException("Unknown option or missing value: "+name);
        }
    }
    public string IngestionInput()
    {
        var inputs=new[]{"file","path","image","schema"}.Where(x=>Get(x)!=null).ToArray();
        var positional=Has("ingest")?Words:Words.Skip(1).ToList();
        if(inputs.Length+positional.Count!=1)throw new ArgumentException("Provide exactly one path: bip-kb --ingest <file-or-directory>.");
        var path=inputs.Length==1?Get(inputs[0])!:positional[0];
        if(inputs.Length==1)
        {
            if(inputs[0]=="image"&&!Extractor.IsImage(path))throw new ArgumentException("--image requires PNG or JPEG.");
            if(inputs[0]=="schema"&&!Path.GetExtension(path).Equals(".drawio",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("--schema requires .drawio.");
            if(inputs[0]=="path"&&!Directory.Exists(path))throw new DirectoryNotFoundException("Source directory unavailable.");
        }
        return path;
    }
    public bool Has(string name)=>flags.ContainsKey(name);
    public string? Get(string name)=>flags.GetValueOrDefault(name);
}
