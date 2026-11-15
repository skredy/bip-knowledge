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
        if(args.Length==1 && args[0]=="--version"){Console.WriteLine("bip-kb 0.1.0");return 0;}
        using var cancel=new CancellationTokenSource();
        Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
        try
        {
            var parsed=new Arguments(args);var workspace=new Workspace(parsed.Get("workspace")??Directory.GetCurrentDirectory());
            var command=parsed.Has("ingest")?"ingest":parsed.Words.FirstOrDefault()??"help";
            var analyzer=new CopilotAnalyzer(workspace);var service=new KnowledgeService(workspace);object? result=null;
            string Required(int index)=>parsed.Words.ElementAtOrDefault(index)??throw new ArgumentException("Missing argument. Run bip-kb help.");
            SourceRole? role=parsed.Get("role") is {} r? Enum.TryParse<SourceRole>(r,true,out var value)&&value!=SourceRole.Unknown?value:throw new ArgumentException("Role must be legacy, target or current."):null;
            switch(command)
            {
                case "help":
                    Console.WriteLine("""
bip-kb — Mémoire technique sourcée
  init --workspace <empty-directory>
  source bind <id> <directory> --role legacy|target|current
  --ingest <file-or-directory> | ingest <file-or-directory>
      (automatic SQL, Excel, code, JSON, image and Draw.io detection)
  --ingest --file <file> | --path <directory> | --image <image> | --schema <drawio>
      [--source <id>] [--role <role>] [--reanalyze]
  search <query> | ask <question> | context <task> [--output <file>]
  report <ingestion-id> | review
  review <claim-id> --action accept|reject|distinct|replace --reason <text> --actor <name>
  export --output <empty-directory> | import
  doctor [--live] | serve [--port 5267] | mcp
All commands: --workspace <directory>, --json. Quote multiword arguments.
"""); return 0;
                case "init":workspace.Init();result=new{workspace=workspace.Root,status="initialized"};break;
                case "source":
                    if(Required(1)!="bind")throw new ArgumentException("Use source bind.");
                    workspace.Bind(Required(2),Required(3),role);result=new{source=Required(2),status="bound"};break;
                case "doctor":result=await analyzer.Doctor(parsed.Has("live"),cancel.Token);break;
                case "ingest":
                    var input=parsed.IngestionInput();
                    result=await new Ingestor(workspace,analyzer).Run(input,parsed.Get("source"),role,parsed.Has("reanalyze"),cancel.Token);break;
                case "search":result=workspace.Search(Required(1));break;
                case "ask":
                    var claims=workspace.Search(Required(1),15).Select(c=>c with{Statement=c.Statement[..Math.Min(c.Statement.Length,1500)]}).ToArray();
                    result=await analyzer.Ask(Required(1),claims,cancel.Token);break;
                case "context":
                    var context=service.Context(Required(1));if(parsed.Get("output") is {} output){File.WriteAllText(output,context);result=new{output};}else result=context;break;
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
        string[] switches=["json","ingest","reanalyze","live"];string[] values=["workspace","file","path","image","schema","source","role","output","port","action","reason","actor"];
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
