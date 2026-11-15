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
        if(args.Length==1 && args[0]=="--version"){Console.WriteLine("bip-kb 0.3.0");return 0;}
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
bip-kb — Mémoire technique sourcée
  init --workspace <empty-directory>
  migrate [--dry-run] | memory resolve --file <resolutions.json>
  project setup [--file <project.json>] | project show [--project <id-or-name>]
  project summary --project <id-or-name>
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
      [--source <id>] [--role <role>] [--reanalyze]
      [--describe] [--context-file <json>] [--non-interactive]
      First import: guided context; /cancel aborts before analysis.
  search <query> | ask <question> | context <task> [--output <file>]
  report <ingestion-id> | review
  review <claim-id> --action accept|reject|distinct|replace --reason <text> --actor <name>
  export --output <empty-directory> | import
  doctor [--live] | serve [--port 5267] | mcp
All commands: --workspace <directory>, --json. Quote multiword arguments.
"""); return 0;
                case "migrate":result=workspace.Migrate(parsed.Has("dry-run"));break;
                case "memory":
                    if(Required(1)!="resolve")throw new ArgumentException("Use memory resolve --file.");
                    using(var gate=workspace.Lock()){new MemoryStore(workspace).Resolve(parsed.Get("file")??throw new ArgumentException("Provide --file."));}result=new{status="resolved"};break;
                case "project":
                    var projects=new Projects(workspace);
                    result=Required(1) switch{"setup"=>projects.Setup(parsed.Get("file"),Console.In,Console.Error,!Console.IsInputRedirected&&!parsed.Has("non-interactive")),"show"=>(object?)projects.Find(parsed.Get("project"))??workspace.Load().Projects,"summary"=>projects.Summary(parsed.Get("project")??throw new ArgumentException("Provide --project.")),_=>throw new ArgumentException("Use project setup/show/summary.")};break;
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
                case "remember":result=research.Remember(parsed.Get("project")??throw new ArgumentException("Provide --project."),Required(1),parsed.Get("actor")??"",parsed.Has("confirm"));break;
                case "entity":
                    if(Required(1)!="rename")throw new ArgumentException("Use entity rename.");
                    using(var gate=workspace.Lock())
                    {workspace.EnsureWritable();var k=workspace.Load();var entity=k.Entities.Single(e=>e.Id==Required(2));var name=parsed.Get("name")??throw new ArgumentException("Provide --name.");k.Entities[k.Entities.IndexOf(entity)]=entity with{Name=name,Aliases=(entity.Aliases??[]).Append(entity.Name).Distinct().ToArray()};workspace.Save(k);result=new{entity.Id,name};}break;
                case "evaluate":result=Evaluation.Run(workspace,parsed.Get("file")??throw new ArgumentException("Provide --file."));break;
                case "benchmark":result=Evaluation.Benchmark(int.Parse(parsed.Get("count")??"100000"));break;
                case "init":workspace.Init();result=new{workspace=workspace.Root,status="initialized"};break;
                case "source":
                    if(Required(1)!="bind")throw new ArgumentException("Use source bind.");
                    workspace.Bind(Required(2),Required(3),role);result=new{source=Required(2),status="bound"};break;
                case "doctor":result=await analyzer.Doctor(parsed.Has("live"),cancel.Token);break;
                case "ingest":
                    var input=parsed.IngestionInput();
                    var source=await new SourceSetup(workspace,Console.In,Console.Error,!Console.IsInputRedirected&&!parsed.Has("non-interactive")&&!parsed.Has("json"))
                        .Prepare(input,parsed.Get("source"),role,parsed.Get("context-file"),parsed.Has("describe"),cancel.Token);
                    result=await new Ingestor(workspace,analyzer).Run(input,source,null,parsed.Has("reanalyze"),cancel.Token);break;
                case "search":result=new SearchEngine(workspace).Search(new(Required(1),parsed.Get("project"),parsed.Get("domain"),role,parsed.Get("intent")??"general",IncludeHistorical:parsed.Has("history")));break;
                case "ask":
                    var quick=research.Start(Required(1),parsed.Get("project"));result=await research.Run(quick.Id,null,cancel.Token);break;
                case "context":
                    var context=service.Context(Required(1),parsed.Get("project"),parsed.Get("domain"));if(parsed.Get("output") is {} output){File.WriteAllText(output,context);result=new{output};}else result=context;break;
                case "report":result=workspace.Load().Reports.SingleOrDefault(r=>r.Id==Required(1))??throw new ArgumentException("Unknown report.");break;
                case "review":
                    result=parsed.Words.Count==1?workspace.Review(workspace.Load()).Select(c=>service.Detail(c.Id)).ToArray():workspace.Decide(Required(1),parsed.Get("action")??"",parsed.Get("reason")??"",parsed.Get("actor")??"");break;
                case "import":service.Import();result=new{status="validated and indexed",decisionConflicts=workspace.Load().Claims.Count(c=>Workspace.Conflict(workspace.Decisions(),c.Id))};break;
                case "export":service.Export(parsed.Get("output")??throw new ArgumentException("Provide --output."));result=new{status="exported"};break;
                case "mcp":await Mcp.Run(workspace,Console.In,Console.Out,cancel.Token);return 0;
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
        string[] switches=["json","ingest","reanalyze","live","describe","non-interactive","dry-run","confirm","history"];string[] values=["workspace","file","path","image","schema","source","role","output","port","action","reason","actor","context-file","project","domain","task","message","name","count","intent"];
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
