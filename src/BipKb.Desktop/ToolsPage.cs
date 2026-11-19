using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace BipKb.Desktop;

public partial class MainWindow
{
    private static string CliPath => Path.Combine(AppContext.BaseDirectory,"BipKb.exe");

    private void ToolsPage()
    {
        Info("Affichage de la session");
        var appearance=Row();
        Button("Affichage…",()=>{
            var options=Dialog.Show(this,"Appliquer",new("theme","Thème",Choices:["Système","Clair","Sombre"]),
                new("size","Taille du texte",Choices:["14","16","18","20"]));
            if(options!=null){
                // WPF marks runtime switching as experimental; the distribution pins its desktop runtime.
#pragma warning disable WPF0001
                Application.Current.ThemeMode=options["theme"] switch{"Sombre"=>ThemeMode.Dark,"Clair"=>ThemeMode.Light,_=>ThemeMode.System};
#pragma warning restore WPF0001
                FontSize=int.Parse(options["size"]);Answer.FontSize=FontSize+1;Proof.FontSize=FontSize;
            }
            return Task.CompletedTask;
        },appearance);
        Info("Poste et intégrations");
        var diagnostics=Row();
        Button("Historique des opérations",()=>Run("Historique",ct=>new Display(DesktopSession.History(Space))),diagnostics);
        foreach(var live in new[]{false,true})
            Button(live?"Tester Copilot en ligne":"Diagnostiquer le poste",()=>Execute("Diagnostic Copilot",async ct=>{
                var report=await new CopilotAnalyzer(Space).Doctor(live,ct);return new Display(Render(report),report);}),diagnostics);
        Button("Stockage",()=>Run("Stockage",ct=>{
            var db=new SearchEngine(Space).PathName;
            var paths=new[]{db,db+"-wal",db+"-shm",Path.Combine(Space.Local,"materialized.json")};
            return new Display($"{Space.Root}\nTemporaire Copilot · {Path.GetTempPath()}\n\n"+string.Join("\n",paths.Select(p=>
                File.Exists(p)?$"{Path.GetFileName(p)} · {new FileInfo(p).Length/1048576d:N1} Mio":$"{Path.GetFileName(p)} · absent")));
        }),diagnostics);
        Button("Diagnostics Copilot…",()=>
        {
            var dir=Path.Combine(Space.Local,"copilot-diagnostics");
            if(!Directory.Exists(dir))throw new IOException("Aucun diagnostic Copilot enregistré.");
            Process.Start(new ProcessStartInfo("explorer.exe"){ArgumentList={dir},UseShellExecute=false});return Task.CompletedTask;
        },diagnostics);
        Button("Copier un diagnostic",()=>
        {
            var text=$"bip-kb Desktop {typeof(MainWindow).Assembly.GetName().Version}\nWindows {Environment.OSVersion.Version}\nProcessus 64 bits : {Environment.Is64BitProcess}\nCLI livrée : {File.Exists(CliPath)}\nIndex présent : {File.Exists(new SearchEngine(Space).PathName)}";
            if(Confirm(text+"\n\nCopier ces informations ?"))Clipboard.SetText(text);return Task.CompletedTask;
        },diagnostics);
        Button("Configuration IDE",()=>Run("Configuration MCP",ct=>{
            var server=new{command=CliPath,args=new[]{"mcp","--workspace",Space.Root}};
            var servers=new Dictionary<string,object>{{"bip-kb",server}};
            return new Display("GitHub Copilot · JetBrains / VS Code\n"+Json.Serialize(new{servers}));
        }),diagnostics);
        Button("Tester MCP",()=>Execute("Connexion MCP",ProbeMcp),diagnostics);
        Button("Vue web locale",ToggleWeb,diagnostics);
        Info("Mémoire du workspace");
        var maintenance=Row();
        Button("Actualiser l’index",()=>Run("Actualisation",ct=>{new SearchEngine(Space).Refresh(progress:Progress,ct:ct);return new Display("Index à jour.");}),maintenance);
        Button("Reconstruire l’index",()=>Run("Reconstruction",ct=>{new SearchEngine(Space).Refresh(force:true,progress:Progress,ct:ct);return new Display("Index reconstruit.");}),maintenance);
        foreach(var rebuild in new[]{false,true})
            Button(rebuild?"Rebâtir le cache":"Compacter le cache",()=>Run("Cache",ct=>{
                var r=MaterializedCache.Compact(Space,rebuild,Progress,ct);return new Display(Render(r),r);}),maintenance);
        Button("Exporter la mémoire…",()=>{
            var dir=Dialog.Pick(this,"folder");return dir==null?Task.CompletedTask:Run("Export",ct=>{new KnowledgeService(Space).Export(dir);return new Display("Mémoire exportée dans "+dir);});},maintenance);
        Button("Importer après fusion",()=>Run("Validation et indexation",ct=>{new KnowledgeService(Space).Import();return new Display("Mémoire validée et indexée.");}),maintenance);
        foreach(var dry in new[]{true,false})
            Button(dry?"Simuler la migration":"Migrer…",()=>!dry&&!Confirm("Migrer ce workspace et enregistrer la sauvegarde du moteur ?")?Task.CompletedTask:
                Run("Migration",ct=>{var r=Space.Migrate(dry);return new Display(Render(r),r);}),maintenance);
        Button("Résoudre un conflit…",()=>{
            var file=Dialog.Pick(this,"file");return file==null||!Confirm("Appliquer les résolutions de ce fichier à la mémoire ?")?Task.CompletedTask:
                Run("Résolution",ct=>{new MemoryStore(Space).Resolve(file);return new Display("Résolutions appliquées.");});},maintenance);
        Info("Rapports et mesures");
        var quality=Row();
        Button("Ingestions récentes",()=>Run("Rapports",ct=>{
            var reports=Space.Load().Reports.OrderByDescending(r=>r.At).Take(50);
            return new Display(string.Join("\n\n",reports.Select(r=>Report(r).Text)));}),quality);
        Button("Rapport par identifiant…",()=>{
            var f=Dialog.Show(this,"Lire le rapport",new Field("id","Identifiant d’ingestion"));
            return f==null?Task.CompletedTask:Run("Rapport",ct=>Report(Space.Load().Reports.SingleOrDefault(r=>r.Id==f["id"])
                ??throw new ArgumentException("Rapport introuvable.")));},quality);
        Button("Évaluer les recherches…",()=>{
            var file=Dialog.Pick(this,"file");return file==null?Task.CompletedTask:Run("Évaluation",ct=>{var r=Evaluation.Run(Space,file);return new Display(Render(r),r);});},quality);
        Button("Mesurer les performances…",()=>{
            var f=Dialog.Show(this,"Mesurer",new Field("count","Nombre d’éléments synthétiques (100 à 500 000)","1000"));
            return f==null?Task.CompletedTask:Run("Mesure synthétique",ct=>{var r=Evaluation.Benchmark(int.Parse(f["count"]));return new Display(Render(r),r);});},quality);
    }

    private ProcessStartInfo CliStart(string command)
    {
        if(!File.Exists(CliPath))throw new FileNotFoundException("Installez le dossier complet de la distribution Windows (BipKb.exe).");
        var start=new ProcessStartInfo(CliPath){WorkingDirectory=Space.Root,UseShellExecute=false,CreateNoWindow=true};
        start.ArgumentList.Add(command);start.ArgumentList.Add("--workspace");start.ArgumentList.Add(Space.Root);
        return start;
    }
    private async Task<Display> ProbeMcp(CancellationToken ct)
    {
        var start=CliStart("mcp");start.RedirectStandardInput=start.RedirectStandardOutput=start.RedirectStandardError=true;
        start.StandardInputEncoding=new UTF8Encoding(false);start.StandardOutputEncoding=Encoding.UTF8;
        using var process=Process.Start(start)??throw new IOException("Démarrage MCP impossible.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(25));
        var token=timeout.Token;var watch=Stopwatch.StartNew();
        var stderr=process.StandardError.ReadToEndAsync();
        async Task<JsonElement> Request(int id,string method,object parameters)
        {
            var line=JsonSerializer.Serialize(new{jsonrpc="2.0",id,method,@params=parameters});
            await process.StandardInput.WriteLineAsync(line.AsMemory(),token);
            var reply=await process.StandardOutput.ReadLineAsync(token);
            if(reply==null)throw new IOException("MCP fermé avant la réponse. "+await stderr);
            using var doc=JsonDocument.Parse(reply);
            if(doc.RootElement.TryGetProperty("error",out var error))throw new IOException(error.ToString());
            var result=doc.RootElement.GetProperty("result").Clone();
            if(result.TryGetProperty("isError",out var failed)&&failed.GetBoolean())throw new IOException(result.GetProperty("content").ToString());
            return result;
        }
        try
        {
            var init=await Request(1,"initialize",new{protocolVersion="2024-11-05",capabilities=new{},clientInfo=new{name="bip-kb-desktop",version="1"}});
            await process.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            var list=await Request(2,"tools/list",new{});await Request(3,"tools/call",new{name="project",arguments=new{}});
            return new Display($"MCP opérationnel · {list.GetProperty("tools").GetArrayLength()} outils · projet lu en {watch.ElapsedMilliseconds} ms",init);
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){throw new TimeoutException("MCP a dépassé le délai de 25 s.");}
        finally{if(!process.HasExited)process.Kill(entireProcessTree:true);}
    }
    private async Task ToggleWeb()
    {
        if(web is {HasExited:false}){StopWeb();Status.Text="Vue web arrêtée";return;}
        var f=Dialog.Show(this,"Ouvrir la vue web",new Field("port","Port local (1024 à 65535)","5267"));
        if(f==null)return;
        var port=int.Parse(f["port"]);if(port is <1024 or >65535)throw new ArgumentException("Port invalide.");
        await Execute("Ouverture de la vue web",async ct=>{
            var start=CliStart("serve");start.ArgumentList.Add("--port");start.ArgumentList.Add(port.ToString());
            // A successful HTTP response from an existing process must not look like our server.
            var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,port);
            try{listener.Start();}catch(System.Net.Sockets.SocketException){throw new IOException("Ce port local est déjà utilisé. Choisissez un autre port.");}
            finally{listener.Stop();}
            StopWeb();web=Process.Start(start)??throw new IOException("Démarrage du serveur impossible.");
            try
            {
                var url=$"http://127.0.0.1:{port}";
                using var http=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromSeconds(1)};
                for(var attempt=0;attempt<120;attempt++)
                {
                    await Task.Delay(500,ct);
                    if(web.HasExited)throw new IOException($"Le serveur a quitté (code {web.ExitCode}). Vérifiez le port.");
                    try{using var reply=await http.GetAsync(url,ct);if(reply.IsSuccessStatusCode){
                        Process.Start(new ProcessStartInfo(url){UseShellExecute=true});return new Display("Vue web ouverte. Le même bouton permet de l’arrêter.");}}
                    catch(System.Net.Http.HttpRequestException){}
                    catch(TaskCanceledException) when(!ct.IsCancellationRequested){}
                }
                throw new TimeoutException("Le serveur local ne répond pas.");
            }
            catch{StopWeb();throw;}
        });
    }
    private void StopWeb(){if(web!=null){try{if(!web.HasExited)web.Kill(entireProcessTree:true);}finally{web.Dispose();web=null;}}}
}
