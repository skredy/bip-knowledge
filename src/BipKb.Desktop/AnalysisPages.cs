using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace BipKb.Desktop;

public partial class MainWindow
{
    private static string ShowBrief(ProjectBrief brief) =>
        $"{brief.Name}\n{brief.Objective}\n\nPérimètre\n{brief.Scope}\n\n" +
        string.Join("\n",brief.Items.Select(i=>$"• {i.Text}"))+"\n\n"+string.Join("\n",brief.OpenQuestions.Select(q=>$"À éclaircir · {q}"));

    private void ProjectPage()
    {
        var note=Input("Que cherchez-vous à comprendre ou à faire évoluer ?",multiline:true);
        var row=Row();
        BusinessInterview Interview()=>new(Space,new CopilotAnalyzer(Space));
        Button("Voir le projet",()=>Run("Projet",ct=>
        {
            var profile=new Projects(Space).Find(null);
            return new Display(profile?.Brief is {} brief ? ShowBrief(brief.Content) : profile?.Objective??"Le cadrage reste à définir.",profile);
        }),row);
        Button("Reprendre le cadrage",()=>Execute("Brouillon",async ct=>
        {
            var interview=Interview();
            var draft=interview.Draft();
            if(draft is {Reply:null,Published:false})draft=await interview.Respond(null,ct);
            return draft?.Reply is {} reply ? new Display(reply.Understanding+"\n\n"+reply.Question+"\n\n"+ShowBrief(reply.Brief),reply) :
                new Display(draft?.Turns.LastOrDefault()?.Text ?? "Décrivez votre projet pour commencer.");
        }),row);
        Button("Envoyer à Copilot",async ()=>
        {
            var message=Required(note);
            await Execute("Entretien avec Copilot",async ct=>
            {
                var reply=(await Interview().Respond(message,ct)).Reply!;
                Dispatcher.Invoke(note.Clear);
                return new Display(reply.Understanding+"\n\n"+reply.Question+"\n\n"+ShowBrief(reply.Brief)+(reply.Ready?"\n\nLe cadrage peut être confirmé.":""),reply);
            });
        },row);
        Button("Confirmer le cadrage",()=>Confirm("Valider la synthèse de l’entretien comme cadrage du projet ?") ?
            Run("Confirmation du cadrage",ct=>{var p=Interview().Confirm(Environment.UserName);return new Display(ShowBrief(p.Brief!.Content));}) : Task.CompletedTask,row);
        Button("Nouvel entretien",()=>Confirm("Archiver le brouillon actuel et recommencer l’entretien ?") ?
            Run("Nouvel entretien",ct=>{Interview().Restart();return new Display("Brouillon archivé. Saisissez votre premier message.");}) : Task.CompletedTask,row);
        Button("Profil historique…",()=>
        {
            if (Space.Config.BusinessMode) throw new InvalidOperationException("Utilisez l’entretien pour cadrer ce workspace.");
            var file=Dialog.Pick(this,"file");
            return file==null ? Task.CompletedTask : Run("Importer le profil",ct=>{
                var p=new Projects(Space).Setup(file,TextReader.Null,TextWriter.Null,false);return new Display(p.Objective,p);});
        },row);
    }

    private sealed record SourceRun(string Path,string Description,string Mode,string? Source,string? Role,bool Guided,int Budget,string? Context);
    private string LastSource => Path.Combine(Space.Local,"desktop","source.json");

    private void SourcesPage()
    {
        var path=Input("Fichier ou dossier source"); path.AllowDrop=true;
        path.PreviewDragOver+=(_,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        path.Drop+=(_,e)=>{if(e.Data.GetData(DataFormats.FileDrop) is string[] {Length:>0} files)path.Text=files[0];};
        var choose=Row();
        foreach(var (title,type) in new[]{("Fichier…","file"),("Dossier…","folder")})
            Button(title,()=>{if(Dialog.Pick(this,type) is {} value)path.Text=value;return Task.CompletedTask;},choose);
        Button("Sources connues",()=>Run("Sources",ct=>{
            var bindings=Space.Bindings();
            var refs=Space.Config.Sources.Select(s=>new Reference($"{s.Id} · {s.Role} · {bindings.GetValueOrDefault(s.Id,"à relier")}","source",s.Id)).ToArray();
            return new Display($"{refs.Length} source(s) configurée(s). Ouvrez une source pour consulter sa couverture.",null,refs);}),choose);
        Button("Inventorier",async ()=>{
            var input=Required(path);
            await Execute("Inventaire local",async ct=>{
                var full=Path.GetFullPath(input);
                var root=Space.Bindings().Values.Where(b=>SourceSetup.Under(full,b)).OrderByDescending(b=>b.Length).FirstOrDefault()
                    ??(Directory.Exists(full)?full:Path.GetDirectoryName(full)!);
                var files=await Extractor.Inventory(full,root,Space.Config,ct);
                return new Display($"{files.Count} fichier(s) pris en charge\n\n"+string.Join("\n",files.GroupBy(Path.GetExtension)
                    .OrderByDescending(g=>g.Count()).Select(g=>$"{g.Key} · {g.Count()}"))+"\n\n"+string.Join("\n",files.Take(80).Select(f=>Path.GetRelativePath(root,f))));
            });
        },choose);
        var notes=Input("À quoi sert cette source ? (facultatif)");
        var mode=new ComboBox {ItemsSource=new[]{"Automatique","Indexer localement","Analyser avec Copilot","Tout réanalyser"},SelectedIndex=0};
        Controls.Children.Add(mode);
        var row=Row();
        async Task Ingest(bool options,SourceRun? resume=null)
        {
            var input=Required(path);var description=notes.Text.Trim();var selected=mode.SelectedIndex;
            var doc=new[]{".xlsx",".xls",".drawio",".png",".jpg",".jpeg"}.Contains(Path.GetExtension(input),StringComparer.OrdinalIgnoreCase);
            var analyze=selected>=2 || selected==0 && Space.Config.BusinessMode && doc;
            var settings=options ? Dialog.Show(this,"Appliquer",
                new("id","Identifiant de source (facultatif)",Optional:true),new("role","Rôle",Choices:["Automatique","Legacy","Target","Current"]),
                new("analysis","Méthode",Choices:["Guidée","Par lots"]),new("budget","Budget d’appels guidés (1 à 100)",Space.Config.MaxExplorationCalls.ToString()),
                new("context","Ancien workspace · fichier de contexte facultatif",Optional:true,Picker:"file")) : null;
            if(options&&settings==null)return;
            var role=Enum.TryParse<SourceRole>(resume?.Role??settings?.GetValueOrDefault("role"),out var r)?r:(SourceRole?)null;
            var guided=resume?.Guided??(settings?.GetValueOrDefault("analysis")!="Par lots");
            var budget=resume?.Budget??(options?int.Parse(settings!["budget"]):Space.Config.MaxExplorationCalls);
            if(budget is <1 or >100)throw new ArgumentException("Budget attendu entre 1 et 100.");
            if(selected==3&&!Confirm("Recommencer l’analyse Copilot complète de cette source ?"))return;
            var source=resume?.Source??settings?.GetValueOrDefault("id");var context=resume?.Context??settings?.GetValueOrDefault("context");
            await Execute(analyze?"Analyse Copilot":"Indexation locale",async ct=>
            {
                Json.Write(LastSource,new SourceRun(input,description,analyze?"analysis":"index",source,role?.ToString(),guided,budget,context));
                var analyzer=new CopilotAnalyzer(Space){Progress=Progress};
                using var details=new StringWriter();
                string? id=string.IsNullOrEmpty(source)?null:source;
                id=Space.Config.BusinessMode
                    ?await new BusinessSourceSetup(Space,analyzer,TextReader.Null,details,false,Progress,
                        question=>Dispatcher.Invoke(()=>Dialog.Show(this,"Continuer",new Field("answer",question,Optional:true))?.GetValueOrDefault("answer")),
                        (sourceId,descriptor)=>Dispatcher.Invoke(()=>{
                            ct.ThrowIfCancellationRequested();
                            if(!Confirm($"{sourceId} · {descriptor.Role} · {descriptor.Context.Status}\n\n{descriptor.Context.Description}\n{descriptor.Context.Notes}\n\nEnregistrer cette qualification et lancer le traitement ?"))
                                throw new OperationCanceledException();
                        })).Prepare(input,id,role,description.Length==0?null:description,false,analyze,ct)
                    :await new SourceSetup(Space,TextReader.Null,details,false).Prepare(input,id,role,string.IsNullOrEmpty(context)?null:context,false,ct,analyze);
                Json.Write(LastSource,new SourceRun(input,"",analyze?"analysis":"index",id,role?.ToString(),guided,budget,context));
                var report=await new Ingestor(Space,analyzer,Progress).Run(input,id,null,selected==3,ct,analyze,guided,analyze&&guided?budget:null);
                return Report(report,details.ToString());
            });
        }
        Button("Lancer ou reprendre",()=>Ingest(false),row);Button("Options et lancement…",()=>Ingest(true),row);
        Button("Reprendre la dernière source",async ()=>{
            if(!File.Exists(LastSource))throw new IOException("Aucune ingestion lancée depuis ce client pour ce workspace.");
            var saved=Json.Read<SourceRun>(File.ReadAllText(LastSource));path.Text=saved.Path;notes.Text=saved.Description;
            mode.SelectedIndex=saved.Mode=="analysis"?2:1;await Ingest(false,saved);
        },row);
        Button("Relier un emplacement…",()=>
        {
            var f=Dialog.Show(this,"Relier",new("id","Identifiant de source"),new("path","Dossier local",Picker:"folder"),
                new("role","Rôle",Choices:["Conserver","Legacy","Target","Current"]));
            return f==null?Task.CompletedTask:Run("Liaison",ct=>{Space.Bind(f["id"],f["path"],Enum.TryParse<SourceRole>(f["role"],out var r)?r:null);return new Display("Emplacement enregistré.");});
        },row);
    }

    private static Display Report(IngestionReport r,string notes="") => new(
        $"{r.Files} fichier(s) · {r.Unchanged} inchangé(s) · {r.UnanalyzedFiles} sans analyse\n{r.SourceId} · {r.At:g}\n\n"+
        string.Join("\n",r.Counts.Select(c=>$"{c.Key} · {c.Value}"))+"\n"+string.Join("\n",r.Warnings)+"\n"+notes,
        new{r.Id,r.Mode,r.Files,r.Counts,r.Warnings});
    private Task SourceDetail(string id)=>Run("Couverture de la source",ct=>
    {
        var dir=Path.Combine(Space.Local,"exploration","status");
        var stages=Directory.Exists(dir)?Directory.EnumerateFiles(dir,"*.json").Select(p=>Json.Read<ExplorationStatus>(File.ReadAllText(p))).Where(s=>s.Source==id).ToArray():[];
        object? published=null;string? indexMessage=null;
        try {published=new McpQueries(Space).Call("analysis",id,null,20,0,ct);}
        catch(McpIndexUnavailableException e){indexMessage=e.Message;}
        var result=new{source=Space.Config.Sources.FirstOrDefault(s=>s.Id==id),published,local=stages,indexMessage};
        return new Display(Render(result),result);
    });

    private void KnowledgePage()
    {
        var query=Input("Termes, concept métier ou identifiant");
        var row=Row();
        Button("Rechercher",async ()=>
        {
            var text=Required(query);
            await Run("Recherche locale",ct=>{
                var found=new SearchEngine(Space).Search(new(text),readOnly:true,ct:ct);
                return new Display($"{found.Hits.Length} résultat(s) · {found.Milliseconds:N0} ms\n"+
                    string.Join("\n\n",found.Hits.Select(h=>$"{h.Title} · {h.Role}\n{h.Text}")),null,
                    found.Hits.Select(h=>new Reference($"{h.Title} · voir les preuves","proof",h.Id)));});
        },row);
        Button("Recherche filtrée…",async ()=>{
            var text=Required(query);
            var f=Dialog.Show(this,"Rechercher",new("domain","Domaine (facultatif)",Optional:true),
                new("role","Rôle",Choices:["Tous","Target","Legacy","Current"]),new("history","Versions",Choices:["Actives","Historique inclus"]));
            if(f==null)return;
            await Run("Recherche filtrée",ct=>{
                var r=new SearchEngine(Space).Search(new(text,Domain:string.IsNullOrWhiteSpace(f["domain"])?null:f["domain"],
                    Role:Enum.TryParse<SourceRole>(f["role"],out var role)?role:null,IncludeHistorical:f["history"]=="Historique inclus"),readOnly:true,ct:ct);
                return new Display(string.Join("\n\n",r.Hits.Select(h=>$"{h.Title} · {h.Role}\n{h.Text}")),null,
                    r.Hits.Select(h=>new Reference(h.Title,"proof",h.Id)));});
        },row);
        Button("Préparer un contexte",async ()=>{var text=Required(query);await Run("Contexte",ct=>
            new Display((string)new McpQueries(Space).Call("context",text,null,20,0,ct)));},row);
        Button("Explorer par identifiant…",()=>
        {
            var f=Dialog.Show(this,"Explorer",new("kind","Données",Choices:["evidence","entity","relations","analysis","questions","project","task"]),
                new("id","Identifiant (facultatif pour project et questions)",Optional:true),new("offset","Premier résultat (à partir de 0)","0"));
            if(f==null)return Task.CompletedTask;
            var offset=int.Parse(f["offset"]);if(offset<0)throw new ArgumentException("Position positive requise.");
            return Run("Lecture indexée",ct=>{
                var result=new McpQueries(Space).Call(f["kind"],f["id"],null,20,offset,ct);
                return new Display(Render(result),result);});
        },row);
        Button("Renommer une entité…",()=>
        {
            var f=Dialog.Show(this,"Renommer",new("id","Identifiant de l’entité"),new("name","Nouveau nom"));
            return f==null?Task.CompletedTask:Run("Renommer",ct=>{
                using var gate=Space.Lock();Space.EnsureWritable();var k=Space.Load();var entity=k.Entities.Single(x=>x.Id==f["id"]);
                k.Entities[k.Entities.IndexOf(entity)]=entity with{Name=f["name"],Aliases=(entity.Aliases??[]).Append(entity.Name).Distinct().ToArray()};
                Space.Save(k);return new Display("Nom enregistré. Actualisez l’index dans Activité.");});
        },row);
    }

    private void CasesPage()
    {
        var note=Input("Question métier ou précision pour le dossier ouvert",multiline:true);
        var row=Row();
        async Task Ask(bool follow)
        {
            var text=Required(note);
            await Execute("Recherche avec Copilot", async ct=>{
                var research=new Research(Space,new CopilotAnalyzer(Space){Progress=Progress});
                var id=follow?taskId??throw new ArgumentException("Ouvrez d’abord un dossier."):research.Start(text,null).Id;
                taskId=id;var task=await research.Run(id,follow?text:null,ct);
                return Case(task);
            });
        }
        Button("Poser une question",()=>Ask(false),row);Button("Poursuivre le dossier",()=>Ask(true),row);
        var offset=0;
        Task ListCases()=>Run("Dossiers",ct=>DesktopSession.Dossiers(Space,offset,ct));
        Button("Dossiers enregistrés",()=>{offset=0;return ListCases();},row);
        Button("30 suivants",()=>{offset+=30;return ListCases();},row);
        Button("Exporter en Markdown…",()=>
        {
            if(taskId==null)throw new InvalidOperationException("Ouvrez d’abord un dossier.");
            var file=Dialog.Pick(this,"save");return file==null?Task.CompletedTask:Run("Export du dossier",ct=>{
                File.WriteAllText(file,new Research(Space,new CopilotAnalyzer(Space)).Export(taskId));return new Display(file);});
        },row);
    }
    private Task OpenCase(string id)=>Run("Dossier",ct=>{var task=new Research(Space,new CopilotAnalyzer(Space)).Get(id);taskId=id;return Case(task);});
    private static Display Case(WorkTask t)
    {
        var last=t.Turns.LastOrDefault(turn=>turn.Role=="assistant");
        var trace=t.Searches.Select(s=>new{s.Cycle,s.Query,s.Milliseconds,found=s.ResultIds.Length,supplied=s.SuppliedIds,s.RequestHash});
        var turns=t.Turns.Select(turn=>new{turn.Role,turn.Text,turn.At});
        var coverage=t.Coverage.Select(c=>new{c.Name,c.Supported,c.Explanation});
        return new Display(last?.Text??t.Goal,new{t.Id,t.Goal,turns,searches=trace,coverage,citations=last?.Citations},(last?.Citations??[]).Select(id=>
        {
            var hit=t.Findings.FirstOrDefault(h=>h.Id==id||h.EvidenceIds.Contains(id));
            return new Reference(hit?.Title??id,"proof",id);
        }));
    }
    private void ReviewPage()
    {
        var row=Row();
        Button("À arbitrer",()=>Run("Arbitrages",ct=>{
            var claims=Space.Review(Space.Load()).ToArray();return new Display($"{claims.Length} assertion(s) à examiner.",null,
                claims.Select(c=>new Reference(c.Statement,"review",c.Id)));}),row);
        Button("Questions métier",()=>Run("Questions",ct=>{
            var questions=Space.Load().Questions;return new Display("Sélectionnez une question.",null,
                questions.Select(q=>new Reference((q.Answer==null?"Ouverte":"Répondue")+" · "+q.Text,"question",q.Id)));}),row);
        Button("Déclarer une connaissance…",()=>
        {
            var f=Dialog.Show(this,"Confirmer la déclaration",new("text","Déclaration humaine"),new("actor","Auteur",Environment.UserName));
            return f==null?Task.CompletedTask:Run("Déclaration",ct=>{
                var p=new Projects(Space).Find(null)??throw new InvalidOperationException("Cadrez le projet.");
                var q=new Research(Space,new CopilotAnalyzer(Space)).Remember(p.Name,f["text"],f["actor"],true);return new Display(q.Answer!,q);});
        },row);
        Button("Décider…",()=>
        {
            var id=(Links.SelectedItem as Reference) is {Kind:"review"} selected?selected.Id:reviewId;
            var f=Dialog.Show(this,"Enregistrer la décision",new("id","Assertion",id??""),new("action","Décision",Choices:["accept","reject","distinct","replace"]),
                new("reason","Justification"),new("actor","Auteur",Environment.UserName));
            return f==null?Task.CompletedTask:Run("Décision",ct=>{var d=Space.Decide(f["id"],f["action"],f["reason"],f["actor"]);return new Display("Décision enregistrée.",d);});
        },row);
    }
    private string? reviewId;
    private Task ReviewItem(string id)=>Run("Assertion et preuves",ct=>{
        reviewId=id;var k=Space.Load();var claim=k.Claims.Single(c=>c.Id==id);
        var related=k.Claims.Where(c=>claim.RelatedClaimIds.Contains(c.Id)).Prepend(claim).ToArray();
        var text=string.Join("\n\n",related.Select(c=>$"{c.Role} · {c.State} · {c.Contribution}\n{c.Statement}"+
            (c.Conditions is {Length:>0}?"\nConditions · "+string.Join(", ",c.Conditions):"")));
        var ids=related.SelectMany(c=>c.EvidenceIds).ToHashSet();
        return new Display(text,new{related,decisions=Space.Decisions().Where(d=>related.Any(c=>c.Id==d.ClaimId))},
            k.Evidence.Where(e=>ids.Contains(e.Id)).Select(e=>new Reference($"{e.RelativePath} · {e.Locator}","proof",e.Id)).ToArray());});
    private async Task AnswerQuestion(string id)
    {
        var selected=Space;
        var q=await Task.Run(()=>selected.Load().Questions.Single(q=>q.Id==id));
        if(workspace!=selected||job!=null)return;
        var f=Dialog.Show(this,"Confirmer la réponse",new("answer",q.Text,q.Answer??""),new("actor","Auteur",Environment.UserName));
        await (f==null?Task.CompletedTask:Run("Réponse métier",ct=>{new Research(Space,new CopilotAnalyzer(Space)).AnswerQuestion(id,f["answer"],f["actor"],true);
            return new Display("Réponse enregistrée.");}));
    }

    private static string Render(object result)
    {
        var lines=new List<string>();
        void Format(JsonElement value,string name)
        {
            if(lines.Count>=600)return;
            if(value.ValueKind==JsonValueKind.Object){foreach(var property in value.EnumerateObject())Format(property.Value,property.Name);}
            else if(value.ValueKind==JsonValueKind.Array){foreach(var item in value.EnumerateArray())Format(item,name);}
            else if(value.ValueKind!=JsonValueKind.Null)lines.Add($"{name}   {value}");
        }
        Format(JsonSerializer.SerializeToElement(result,Json.Options),"");
        return lines.Count==0?"Aucun résultat.":string.Join("\n",lines);
    }
}
