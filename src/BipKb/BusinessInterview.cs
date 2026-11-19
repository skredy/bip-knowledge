namespace BipKb;

public sealed record BriefItem(string Topic,string Text,string Origin,string[] TurnIds);
public sealed record ProjectBrief(string Name,string Objective,string Scope,BriefItem[] Items,string[] OpenQuestions);
public sealed record BriefReply(string Understanding,string Question,string QuestionReason,bool Ready,ProjectBrief Brief);
public sealed record BriefRequest(int SchemaVersion,string SessionId,string BaseRevision,ProjectProfile? Current,TaskTurn[] Turns);
public sealed record ConfirmedBrief(ProjectBrief Content,TaskTurn[] Declarations,string Actor,DateTimeOffset At);
public sealed record BriefDraft(string SessionId,string BaseRevision,List<TaskTurn> Turns,BriefReply? Reply=null,bool Published=false);
public interface IBusinessAssistant
{
    Task<BriefReply> Frame(BriefRequest request,CancellationToken ct);
    Task<SourceQualification> Qualify(SourceQualificationRequest request,CancellationToken ct);
    Task<BusinessReply> Understand(BusinessRequest request,string? attachment,CancellationToken ct);
}

/// <summary>Human confirmation is a local event, never a field trusted from the model.</summary>
public sealed class BusinessInterview(Workspace workspace,IBusinessAssistant assistant)
{
    private string DraftPath=>Path.Combine(workspace.Local,"business","interview.json");
    public BriefDraft? Draft()=>File.Exists(DraftPath)?Json.Read<BriefDraft>(File.ReadAllText(DraftPath)):null;
    public static ProjectProfile? Profile(Knowledge k)
    {
        if(k.Projects.Count>1)throw new InvalidOperationException("Plusieurs projets : créez un workspace neuf pour le parcours métier.");
        return k.Projects.SingleOrDefault();
    }
    public async Task<BriefDraft> Respond(string? message,CancellationToken ct)
    {
        BriefDraft draft;ProjectProfile? profile;
        using(var gate=workspace.Lock())
        {
            workspace.EnsureWritable();profile=Profile(workspace.Load());
            draft=Draft() is {Published:false} existing?existing:new(Guid.NewGuid().ToString("N"),workspace.SemanticRevision,[]);
            if(draft.BaseRevision!=workspace.SemanticRevision)throw new RevisionConflictException("La base a changé depuis le cadrage. Reprenez avec project setup --restart ; le dialogue précédent sera archivé.");
            if(message!=null)
            {
                if(string.IsNullOrWhiteSpace(message)||message.Length>4000)throw new ArgumentException("Réponse attendue : 1 à 4000 caractères.");
                // Repeating a failed command must not duplicate the saved human answer.
                if(!(draft.Reply==null&&draft.Turns.LastOrDefault() is {Role:"user"} last&&last.Text==message))
                {
                    if(draft.Turns.Count(t=>t.Role=="user")>=40)throw new InvalidOperationException("Entretien limité à 40 réponses ; confirmez le cadrage disponible ou reprenez avec --restart.");
                    if(draft.Reply is {} previous)draft.Turns.Add(new("a"+Guid.NewGuid().ToString("N"),"assistant",previous.Understanding+"\n"+previous.Question,DateTimeOffset.UtcNow,[]));
                    draft.Turns.Add(new("t"+Guid.NewGuid().ToString("N"),"user",message,DateTimeOffset.UtcNow,[]));
                }
                draft=draft with{Reply=null};
            }
            if(draft.Reply!=null)return draft;
            Json.Write(DraftPath,draft); // Persist BEFORE model/network work.
        }
        var request=new BriefRequest(1,draft.SessionId,draft.BaseRevision,profile,draft.Turns.ToArray());
        var reply=await assistant.Frame(request,ct);Validate(reply,request);
        using(var gate=workspace.Lock())
        {
            var current=Draft();
            if(current==null||Json.Serialize(current)!=Json.Serialize(draft)||workspace.SemanticRevision!=draft.BaseRevision)throw new RevisionConflictException("Le cadrage a changé pendant l'appel. Les réponses humaines sont conservées ; relancez project setup.");
            draft=draft with{Reply=reply};Json.Write(DraftPath,draft);
        }
        return draft;
    }
    public ProjectProfile Confirm(string actor)
    {
        if(string.IsNullOrWhiteSpace(actor)||actor.Length>200)throw new ArgumentException("Un auteur est requis (maximum 200 caractères).");
        ProjectProfile published;
        using(var gate=workspace.Lock())
        {
            var draft=Draft()??throw new InvalidOperationException("Commencez avec project setup.");
            var k=workspace.Load();var old=Profile(k);
            if(draft.Published)return old??throw new InvalidDataException("Cadrage publié absent.");
            if(draft.BaseRevision!=workspace.SemanticRevision)throw new RevisionConflictException("La base a changé ; reprenez le cadrage avec --restart.");
            var reply=draft.Reply??throw new InvalidOperationException("Réponse Copilot absente : relancez project setup avant de confirmer.");
            Validate(reply,new(1,draft.SessionId,draft.BaseRevision,old,draft.Turns.ToArray()));
            if(!reply.Ready)throw new InvalidOperationException("L'objectif et le périmètre doivent être précisés avant confirmation.");
            var origins=(old?.Brief?.Declarations??[]).Concat(draft.Turns.Where(t=>t.Role=="user")).DistinctBy(t=>t.Id).ToDictionary(t=>t.Id);
            var used=reply.Brief.Items.SelectMany(i=>i.TurnIds).Distinct().Select(id=>origins[id]).ToArray();
            var brief=new ConfirmedBrief(reply.Brief,used,actor,DateTimeOffset.UtcNow);
            var systems=reply.Brief.Items.Where(i=>i.Topic=="system").Select(i=>i.Text).ToArray();
            published=new(old?.Id??Guid.NewGuid().ToString("N"),old?.Name??reply.Brief.Name,reply.Brief.Objective,"À documenter",systems,[],[],old?.Authorities??[],old?.Glossary??[],brief);
            k.Projects.Clear();k.Projects.Add(published);workspace.Save(k);
            Json.Write(DraftPath,draft with{Published=true});
        }
        new SearchEngine(workspace).Refresh();return published;
    }
    public void Restart()
    {
        using var gate=workspace.Lock();if(!File.Exists(DraftPath))return;
        var archive=Path.Combine(workspace.Local,"business","interview-"+Guid.NewGuid().ToString("N")+".json");
        File.Move(DraftPath,archive);
    }
    public async Task<object> Interactive(TextReader input,TextWriter output,CancellationToken ct)
    {
        output.WriteLine("Cadrons votre projet. /pause conserve l'entretien ; /confirm valide le récapitulatif. Vous pouvez répondre « je ne sais pas ».");
        var draft=Draft();
        if(draft==null||draft.Published)
        {
            output.WriteLine("Que cherchez-vous à comprendre ou à faire évoluer avec ce projet ?");output.Write("> ");output.Flush();
            var first=input.ReadLine();if(first==null||first=="/pause")return new{status="paused"};
            draft=await Respond(first,ct);
        }
        else if(draft.Reply==null)draft=await Respond(null,ct);
        while(true)
        {
            var reply=draft.Reply!;
            output.WriteLine(Render(reply));output.Write("> ");output.Flush();
            var answer=input.ReadLine();
            if(answer==null||answer is "/pause" or "/cancel")return new{status="paused",message="Réponses conservées. Reprise : bip-kb project setup."};
            if(answer=="/confirm")return Confirm(Environment.UserName);
            if(string.IsNullOrWhiteSpace(answer))continue;
            draft=await Respond(answer,ct);
        }
    }
    public static ProjectProfile Compact(ProjectProfile profile)=>profile.Brief is {} b?profile with{Brief=b with{Declarations=[]}}:profile;
    public static string Describe(ProjectProfile profile)
    {
        if(profile.Brief is not {} b)return "";
        return "\n## Cadrage métier confirmé (déclarations, pas preuves documentaires)\n"+b.Content.Scope+"\n"+
            string.Join("\n",b.Content.Items.Select(i=>$"- [{i.Origin}] {i.Text}"))+"\nÀ éclaircir : "+string.Join(" ; ",b.Content.OpenQuestions);
    }
    public static string Render(BriefReply reply)
    {
        var b=reply.Brief;
        return $"\n{reply.Understanding}\n\nProjet : {b.Name}\nObjectif : {b.Objective}\nPérimètre : {b.Scope}\n"+
            string.Join("\n",b.Items.Select(i=>$"- [{i.Origin}] {i.Text}"))+"\nQuestions ouvertes : "+string.Join(" ; ",b.OpenQuestions)+
            (reply.Ready?"\n/confirm pour retenir ce cadrage ; ou précisez ce qui doit changer.":"")+
            (reply.Question.Length>0?$"\n{reply.Question}\n{reply.QuestionReason}":"");
    }
    public static void Validate(BriefReply r,BriefRequest request)
    {
        static bool Text(string? s,int max,bool empty=false)=>s!=null&&s.Length<=max&&(empty||!string.IsNullOrWhiteSpace(s));
        var b=r.Brief;
        if(!Text(r.Understanding,2000)||!Text(r.Question,600,true)||!Text(r.QuestionReason,600,true)||b==null||!Text(b.Name,120)||!Text(b.Objective,1200)||!Text(b.Scope,1200)||b.Items==null||b.Items.Length>30||b.OpenQuestions==null||b.OpenQuestions.Length>12||b.OpenQuestions.Any(q=>!Text(q,500))||!r.Ready&&r.Question.Length==0)
            throw new InvalidDataException("Cadrage invalide : reformulation, objectif, périmètre et une question au maximum sont attendus.");
        if(request.Current!=null&&b.Name!=request.Current.Name)throw new InvalidDataException("Conservez le nom du projet existant.");
        var ids=request.Turns.Where(t=>t.Role=="user").Concat(request.Current?.Brief?.Declarations??[]).Select(t=>t.Id).ToHashSet();
        string[] topics=["objective","scope","actor","process","concept","current","target","system","source","constraint","decision","vocabulary"];
        foreach(var item in b.Items)
            if(item==null||!topics.Contains(item.Topic)||!Text(item.Text,1200)||item.Origin is not ("declared" or "hypothesis")||item.TurnIds==null||item.TurnIds.Length==0||item.TurnIds.Any(id=>!ids.Contains(id)))
                throw new InvalidDataException("Chaque élément de cadrage doit citer une déclaration humaine disponible ; le modèle ne peut déclarer une décision approuvée.");
        if(r.Ready&&(!b.Items.Any(i=>i.Topic=="objective"&&i.Origin=="declared")||!b.Items.Any(i=>i.Topic=="scope"&&i.Origin=="declared")))throw new InvalidDataException("Objectif et périmètre déclarés requis pour proposer la confirmation.");
        if(Json.Serialize(r).Length>16000)throw new InvalidDataException("Cadrage trop long ; synthétisez sans perdre les exceptions.");
    }
}
