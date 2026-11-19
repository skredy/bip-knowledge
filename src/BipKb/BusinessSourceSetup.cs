namespace BipKb;

public sealed class BusinessSourceSetup(Workspace workspace,IBusinessAssistant assistant,TextReader input,TextWriter output,bool interactive)
{
    public static void Validate(SourceQualification q)
    {
        if(string.IsNullOrWhiteSpace(q.Purpose)||q.Purpose.Length>1000||q.Topics==null||q.Topics.Length>8||q.Topics.Any(t=>string.IsNullOrWhiteSpace(t)||t.Length>100)||!Enum.IsDefined(q.Role)||q.Status is not (SourceStatus.Proposal or SourceStatus.Observed or SourceStatus.Historical)||q.Question==null||q.Question.Length>500||q.Reason==null||q.Reason.Length>1000)
            throw new InvalidDataException("Qualification invalide ; une source ne peut pas être déclarée approuvée par le modèle.");
    }
    public async Task<string> Prepare(string path,string? sourceId,SourceRole? role,string? description,bool describe,bool analyze,CancellationToken ct)
    {
        path=Path.GetFullPath(path);
        if(!File.Exists(path)&&!Directory.Exists(path))throw new IOException("Source inaccessible ; aucune connaissance supprimée.");
        Extractor.EnsureNoLinks(path);
        var profile=BusinessInterview.Profile(workspace.Load())??throw new InvalidOperationException("Commencez par bip-kb project setup pour cadrer ce projet.");
        if(profile.Brief==null)throw new InvalidOperationException("Cadrage métier requis : bip-kb project setup.");
        var bindings=workspace.Bindings();var config=workspace.Config;
        var matches=bindings.Where(b=>SourceSetup.Under(path,b.Value)).OrderByDescending(b=>b.Value.Length).ToArray();
        if(sourceId==null&&matches.Length>0)sourceId=matches[0].Key;
        var root=sourceId!=null&&bindings.TryGetValue(sourceId,out var bound)?bound:Directory.Exists(path)?path:Path.GetDirectoryName(path)!;
        if(!SourceSetup.Under(path,root))throw new ArgumentException("Chemin hors de la source sélectionnée.");
        if(sourceId!=null)SourceIdentifier.Validate(sourceId);
        var relative=Path.GetRelativePath(root,path).Replace('\\','/');
        var existing=config.Sources.SingleOrDefault(s=>s.Id==sourceId)?.Resolve(relative);
        if(existing!=null&&!describe&&description==null&&(role==null||role==existing.Role))
        {output.WriteLine("Contexte de source réutilisé : "+existing.Context.Description);return sourceId!;}
        if(sourceId==null)
        {
            var basis=SourceIdentifier.Suggest(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));sourceId=basis;
            for(var n=2;config.Sources.Any(s=>s.Id==sourceId);n++)sourceId=basis+"-"+n;
        }
        var draftPath=Path.Combine(workspace.Local,"business","source-notes",Json.Hash(path)+".json");
        var notes=description??(File.Exists(draftPath)?Json.Read<string>(File.ReadAllText(draftPath)):existing?.Context.Description??"");
        if(notes.Length==0&&interactive)
        {
            output.WriteLine("À quoi sert cette source pour votre projet ? Entrée laisse le rôle à déterminer ; /pause pour arrêter.");output.Write("> ");output.Flush();
            notes=input.ReadLine()??throw new OperationCanceledException();if(notes=="/pause")throw new OperationCanceledException();
        }
        if(notes.Length>4000)throw new ArgumentException("Description limitée à 4000 caractères.");
        Json.Write(draftPath,notes);
        var files=await Extractor.Inventory(path,root,config,ct);
        SourceQualification qualification;
        if(analyze)
        {
            var inventory=files.Take(20).Select(f=>Path.GetRelativePath(root,f)).ToList();
            foreach(var file in files.Where(f=>!Extractor.IsImage(f)).Take(2))
            {
                try
                {
                    var extracted=Extractor.Read(file,config.MaxFileBytes);
                    inventory.AddRange(extracted.Units.Take(3).Select(u=>u.Locator+" : "+u.Text[..Math.Min(500,u.Text.Length)]));
                }
                catch(Exception e) when(e is InvalidDataException or System.Xml.XmlException or System.Text.Json.JsonException)
                {inventory.Add("Aperçu indisponible pour "+Path.GetFileName(file));}
            }
            output.WriteLine("Qualification de la source à partir du cadrage — attente de Copilot");
            qualification=await assistant.Qualify(new(BusinessInterview.Compact(profile),Path.GetFileName(path),inventory.ToArray(),notes),ct);Validate(qualification);
            if(qualification.Question.Length>0&&interactive)
            {
                output.WriteLine(qualification.Question+" (Entrée : laisser à éclaircir)");output.Write("> ");output.Flush();
                var answer=input.ReadLine()??throw new OperationCanceledException();if(answer=="/pause")throw new OperationCanceledException();
                if(answer.Length>1500)throw new ArgumentException("Précision limitée à 1500 caractères.");
                if(!string.IsNullOrWhiteSpace(answer))
                {
                    notes+="\nQuestion : "+qualification.Question+"\nRéponse : "+answer;Json.Write(draftPath,notes);
                    qualification=await assistant.Qualify(new(BusinessInterview.Compact(profile),Path.GetFileName(path),inventory.ToArray(),notes),ct);Validate(qualification);
                }
            }
        }
        else qualification=new(Path.GetFileName(path)+" — indexation locale, sens métier à analyser.",[],role??SourceRole.Unknown,SourceStatus.Proposal,"","Qualification métier différée. "+notes[..Math.Min(notes.Length,800)]);
        output.WriteLine("Source : "+sourceId+"\n"+qualification.Purpose);
        if(qualification.Question.Length>0)output.WriteLine("À éclaircir : "+qualification.Question);
        var detail="Sujets proposés : "+string.Join(", ",qualification.Topics)+". "+qualification.Reason+(qualification.Question.Length>0?" | Question ouverte : "+qualification.Question:"");
        var context=new SourceContext(profile.Name,"Transversal",qualification.Purpose,qualification.Status,detail[..Math.Min(1200,detail.Length)]);
        var boundRole=config.Sources.SingleOrDefault(s=>s.Id==sourceId)?.Role;
        var effectiveRole=role??(boundRole is not null and not SourceRole.Unknown?boundRole.Value:qualification.Role);
        workspace.ConfigureSource(sourceId,root,path,new(effectiveRole,context));
        return sourceId;
    }
}
