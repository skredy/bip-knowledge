namespace BipKb;

/// <summary>Collects declared context before any model call. Streams are injectable for terminal tests.</summary>
public sealed class SourceSetup(Workspace workspace,TextReader input,TextWriter output,bool interactive)
{
    public static bool Under(string path,string root)
    {
        var relative=Path.GetRelativePath(root,path);
        return relative!=".."&&!relative.StartsWith(".."+Path.DirectorySeparatorChar)&&!Path.IsPathRooted(relative);
    }
    public async Task<string> Prepare(string path,string? sourceId,SourceRole? role,string? contextFile,bool describe,CancellationToken ct,bool analyze=true)
    {
        path=Path.GetFullPath(path);
        if(!File.Exists(path)&&!Directory.Exists(path))throw new IOException("Source inaccessible ; aucun changement effectué.");
        workspace.EnsureWritable();
        Extractor.EnsureNoLinks(path);
        if(sourceId!=null&&SourceIdentifier.Error(sourceId) is {} sourceError)
        {
            if(!interactive)throw new ArgumentException(sourceError);
            output.WriteLine(sourceError);
            sourceId=Read("Identifiant court de la source",SourceIdentifier.Suggest(sourceId),ct,validate:SourceIdentifier.Error);
        }
        var config=workspace.Config;var bindings=workspace.Bindings();
        if(sourceId==null)
        {
            var matches=bindings.Where(b=>Under(path,b.Value)).Select(b=>b.Key).ToArray();
            if(matches.Length>1)
            {
                if(!interactive)throw new ArgumentException("Plusieurs sources correspondent. Précisez --source : "+string.Join(", ",matches));
                output.WriteLine("Sources possibles : "+string.Join(", ",matches));
                sourceId=Read("Source à utiliser",null,ct,validate:v=>matches.Contains(v)?null:"Choisissez une des sources proposées.");
            }
            else sourceId=matches.SingleOrDefault();
        }
        if(sourceId!=null)SourceIdentifier.Validate(sourceId);
        string? root=null;
        if(sourceId!=null&&bindings.TryGetValue(sourceId,out root)&&!Under(path,root))throw new ArgumentException("Chemin hors de la source sélectionnée.");
        if(root==null&&sourceId!=null&&config.Sources.Any(s=>s.Id==sourceId))throw new ArgumentException("Cette source partagée doit être remappée avec source bind avant ingestion.");
        root??=Directory.Exists(path)?path:Path.GetDirectoryName(path)!;
        var definition=config.Sources.SingleOrDefault(s=>s.Id==sourceId);
        var existing=definition?.Resolve(Path.GetRelativePath(root,path));
        SourceDescriptor? supplied=null;
        if(contextFile!=null)
        {
            if(new FileInfo(contextFile).Length>32000)throw new ArgumentException("Fichier de contexte trop volumineux.");
            supplied=Json.Read<SourceDescriptor>(File.ReadAllText(contextFile));supplied.Validate();
            if(role!=null&&role!=supplied.Role)throw new ArgumentException("--role contredit le rôle du fichier de contexte.");
        }
        if(existing!=null&&supplied==null&&!describe&&(role==null||role==existing.Role))
        {
            try
            {
                existing.Validate();
                output.WriteLine($"Contexte réutilisé : {existing.Context.Project} / {existing.Context.Domain} — {existing.Role}, {existing.Context.Status}. Utilisez --describe pour le modifier.");
                return sourceId!;
            }
            catch(ArgumentException e) when(interactive)
            {
                output.WriteLine(e.Message);output.WriteLine("Corrigeons ce contexte. Les réponses existantes restent proposées.");
            }
        }
        if(!interactive&&supplied==null)throw new ArgumentException("Contexte manquant ou modifié. Relancez dans un terminal interactif, ou fournissez --context-file contexte.json (role, context: project, domain, description, status, notes). Aucun appel Copilot effectué.");
        var files=await Extractor.Inventory(path,root,config,ct);
        output.WriteLine($"Inventaire : {files.Count} fichier(s) ; "+string.Join(", ",files.GroupBy(p=>Path.GetExtension(p).ToLowerInvariant()).OrderBy(g=>g.Key).Select(g=>$"{g.Count()} {g.Key}")));
        SourceDescriptor? suggestionContext=null;
        if(interactive&&existing==null&&supplied==null)
        {
            var preview=new System.Text.StringBuilder(Path.GetFileName(path));
            foreach(var file in files.Where(f=>!Extractor.IsImage(f)).Take(2))
            {
                try{foreach(var unit in Extractor.Read(file,config.MaxFileBytes).Units.Take(2))preview.Append(' ').Append(unit.Text[..Math.Min(500,unit.Text.Length)]);}
                catch(Exception e) when(e is InvalidDataException or System.Xml.XmlException or System.Text.Json.JsonException){output.WriteLine("Aperçu indisponible : "+Path.GetFileName(file));}
            }
            var terms=SearchEngine.Terms(preview.ToString());
            var candidates=config.Sources.SelectMany(s=>(s.Overrides?.Values??Enumerable.Empty<SourceDescriptor>()).Concat(s.Context==null?[]:[new SourceDescriptor(s.Role,s.Context)]))
                .Select(d=>new{Descriptor=d,Score=terms.Count(t=>SearchEngine.Normalize(d.Context.Description+" "+d.Context.Domain).Contains(t))}).Where(x=>x.Score>=2).OrderByDescending(x=>x.Score).ToArray();
            if(candidates.Length>0)
            {
                suggestionContext=candidates[0].Descriptor;
                output.WriteLine("Contexte proche trouvé (suggestion, pas une classification certaine) : "+Json.Serialize(suggestionContext));
                if(Confirm("Réutiliser ce contexte ?",ct))supplied=suggestionContext;
            }
        }
        if(sourceId==null)
        {
            if(!interactive)throw new ArgumentException("Première ingestion automatisée : fournissez --source <nom> et --context-file.");
            var basis=SourceIdentifier.Suggest(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));var suggestion=basis;
            for(var suffix=2;config.Sources.Any(s=>s.Id==suggestion);suffix++)suggestion=basis+"-"+suffix;
            output.WriteLine(SourceIdentifier.Help);
            sourceId=Read("Identifiant court de la source",suggestion,ct,validate:v=>SourceIdentifier.Error(v)??(config.Sources.Any(s=>s.Id==v)?"Identifiant déjà utilisé. Choisissez un autre identifiant pour ce nouvel emplacement.":null));
        }
        if(supplied==null)
        {
            output.WriteLine("Le format ne définit pas le sens métier. Ce contexte s'appliquera uniquement au chemin sélectionné et sera conservé.");
            var known=config.Sources.SelectMany(s=>(s.Overrides?.Values.Select(v=>v.Context)??Enumerable.Empty<SourceContext>()).Concat(s.Context==null?[]:[s.Context])).Select(c=>$"{c.Project} / {c.Domain}").Distinct().Take(20).ToArray();
            if(known.Length>0)output.WriteLine("Contextes existants (réutilisez les mêmes noms pour relier les sources) : "+string.Join(" ; ",known));
            var profiles=workspace.Load().Projects;
            if(profiles.Count>0)output.WriteLine("Projets configurés : "+string.Join(" ; ",profiles.Select(p=>p.Name)));
            var project=Read("Projet auquel rattacher ces fichiers",existing?.Context.Project??(profiles.Count==1?profiles[0].Name:null),ct,maxLength:120);
            var selectedProject=profiles.SingleOrDefault(p=>p.Name.Equals(project,StringComparison.OrdinalIgnoreCase));
            if(selectedProject!=null)output.WriteLine("Sujets métier configurés : "+string.Join(" ; ",selectedProject.Domains.Select(d=>d.Name)));
            output.WriteLine("Sujet métier = ce dont parlent ces fichiers, par exemple Clients, Commandes ou Facturation.");
            var domain=Read("Sujet métier principal",existing?.Context.Domain??(selectedProject?.Domains.Length==1?selectedProject.Domains[0].Name:null),ct,maxLength:120);
            output.WriteLine("legacy = ancien système ; target = cible prévue ; current = implémentation actuelle.");
            var roleText=Read("Rôle : legacy / target / current",(role??existing?.Role??definition?.Role)?.ToString().ToLowerInvariant(),ct,validate:v=>Enum.TryParse<SourceRole>(v,true,out var r)&&Enum.IsDefined(r)&&r!=SourceRole.Unknown?null:"Rôle invalide : choisissez legacy, target ou current.");
            if(!Enum.TryParse<SourceRole>(roleText,true,out var selectedRole)||!Enum.IsDefined(selectedRole)||selectedRole==SourceRole.Unknown)throw new ArgumentException("Rôle inconnu.");
            output.WriteLine("proposal = proposition à discuter ; validated = validé par votre équipe ; observed = constaté dans le code ou les données ; historical = référence ancienne.");
            var statusText=Read("Statut : proposal / validated / observed / historical",existing?.Context.Status.ToString().ToLowerInvariant(),ct,validate:v=>Enum.TryParse<SourceStatus>(v,true,out var status)&&Enum.IsDefined(status)&&status!=SourceStatus.Unknown?null:"Statut invalide : choisissez proposal, validated, observed ou historical.");
            if(!Enum.TryParse<SourceStatus>(statusText,true,out var status)||!Enum.IsDefined(status))throw new ArgumentException("Statut inconnu.");
            var description=Read("Que contient cet ensemble, et à quoi sert-il ?",existing?.Context.Description,ct,maxLength:1200);
            var notes=Read("Précisions, limites ou correspondances connues (facultatif)",existing?.Context.Notes??"",ct,true,maxLength:1200);
            while(true)
            {
                supplied=new(selectedRole,new(project,domain,description,status,notes));
                try{supplied.Validate();break;}
                catch(ArgumentException e)
                {
                    output.WriteLine(e.Message);
                    description=Read("Description abrégée",description,ct,maxLength:1200);
                    notes=Read("Précisions abrégées (facultatif)",notes,ct,true,maxLength:1200);
                }
            }
        }
        output.WriteLine("Source : "+sourceId);output.WriteLine(Json.Serialize(supplied));
        if(interactive)
        {
            if(!Confirm(analyze?"Enregistrer ce contexte et analyser ?":"Enregistrer ce contexte et indexer localement (sans Copilot) ?",ct))throw new OperationCanceledException("Ingestion annulée avant enregistrement.");
        }
        ct.ThrowIfCancellationRequested();workspace.ConfigureSource(sourceId,root,path,supplied);
        output.WriteLine("Contexte enregistré. Si l'ingestion échoue, relancez la même commande : ces réponses seront réutilisées.");
        if(new Projects(workspace).Find(supplied.Context.Project)==null)output.WriteLine("Fiche globale non configurée : lancez bip-kb project setup pour préparer les enquêtes et conversations.");
        return sourceId;
    }
    private bool Confirm(string label,CancellationToken ct)
    {
        var answer=Read(label+" oui/non","non",ct,validate:v=>v.ToLowerInvariant() is "oui" or "o" or "yes" or "y" or "non" or "n" or "no"?null:"Répondez oui ou non.");
        return answer.ToLowerInvariant() is "oui" or "o" or "yes" or "y";
    }
    private string Read(string label,string? fallback,CancellationToken ct,bool optional=false,int? maxLength=null,Func<string,string?>? validate=null)
    {
        while(true)
        {
            ct.ThrowIfCancellationRequested();output.Write(label+(maxLength!=null?$" ({maxLength} caractères max.)":"")+(fallback!=null?$" [{fallback}]":"")+" : ");output.Flush();
            var line=input.ReadLine()??throw new OperationCanceledException("Saisie interrompue.");
            if(line.Trim().Equals("/cancel",StringComparison.OrdinalIgnoreCase))throw new OperationCanceledException();
            var value=line.Trim().Length==0?fallback??"":line.Trim();
            var error=SourceContext.TextError(label,value,maxLength??int.MaxValue,!optional)??validate?.Invoke(value);
            if(error==null)return value;
            output.WriteLine(error+" Corrigez cette réponse, ou /cancel pour annuler.");
        }
    }
}
