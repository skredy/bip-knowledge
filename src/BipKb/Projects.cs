namespace BipKb;

public sealed class Projects(Workspace workspace)
{
    public ProjectProfile? Find(string? project)
    {
        var list=workspace.Load().Projects;
        if(project==null)return list.Count==1?list[0]:null;
        return list.SingleOrDefault(p=>p.Id==project||p.Name.Equals(project,StringComparison.OrdinalIgnoreCase));
    }
    public ProjectProfile Setup(string? file,TextReader input,TextWriter output,bool interactive)
    {
        ProjectProfile profile;
        if(file!=null)profile=Json.Read<ProjectProfile>(File.ReadAllText(file));
        else
        {
            if(!interactive)throw new ArgumentException("Use project setup --file project.json in non-interactive mode.");
            output.WriteLine("Décrivons votre projet pour aider bip-kb à comprendre les documents que vous lui donnerez.");
            output.WriteLine("Seuls le nom et l'objectif sont nécessaires. Les exemples ne sont pas des réponses présélectionnées.");
            output.WriteLine("Entrée conserve la valeur affichée ou laisse un champ facultatif vide. ? réaffiche l'aide ; /cancel annule sans enregistrer.");
            string Read(string label,string explanation,string? fallback=null,bool optional=false)
            {
                output.WriteLine("\n"+explanation);
                while(true)
                {
                    output.Write(label+(optional?" (facultatif)":"")+(!string.IsNullOrEmpty(fallback)?$" [{fallback}]":"")+" : ");output.Flush();
                    var value=input.ReadLine()?.Trim()??throw new OperationCanceledException();
                    if(value.Equals("/cancel",StringComparison.OrdinalIgnoreCase))throw new OperationCanceledException();
                    if(value=="?"){output.WriteLine(explanation);continue;}
                    if(value.Length>0)return value;
                    if(!string.IsNullOrEmpty(fallback))return fallback;
                    if(optional)return "";
                    output.WriteLine("Ce champ est nécessaire ; quelques mots suffisent. /cancel pour quitter.");
                }
            }
            string[] Items(string value)=>value.Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var name=Read("Nom du projet","Le nom commun aux sources que vous voulez relier. Exemple : Migration BIP.",Find(null)?.Name);var existing=Find(name);
            name=existing?.Name??name;
            var objective=Read("Ce que vous voulez réaliser","Pourquoi rassemblez-vous ces connaissances ? Exemple : recréer les endpoints de l'ancien BIP avec le nouveau modèle de données.",existing?.Objective);
            var architecture=Read("Technologies et organisation du code","Comment l'application est-elle construite ? Exemple : API .NET, SQL Server, DTO et handlers CQRS. Si vous ne savez pas encore, appuyez sur Entrée.",existing?.Architecture,true);
            if(architecture.Length==0)architecture="Non précisée";
            var systems=Items(Read("Applications ou bases de données concernées","Les logiciels ou bases entre lesquels vous travaillez, pas vos dossiers ni vos formats de fichiers. Exemple : ancien BIP ; nouvelle API BIP ; base SQL cible. Séparez plusieurs noms par ;",existing==null?null:string.Join(" ; ",existing.Systems),true));
            var domains=Items(Read("Sujets métier à organiser","Les sujets fonctionnels qui aideront à retrouver et relier les connaissances. Exemple : Clients ; Commandes ; Facturation. Ce ne sont pas les couches techniques comme DTO ou Repository. Une liste partielle suffit ; vous pourrez la compléter.",existing==null?null:string.Join(" ; ",existing.Domains.Select(d=>d.Name)),true))
                .Select(n=>existing?.Domains.SingleOrDefault(d=>d.Name.Equals(n,StringComparison.OrdinalIgnoreCase))??new DomainProfile(Guid.NewGuid().ToString("N"),n,"À préciser",[])).ToArray();
            var conventions=Items(Read("Règles de développement à respecter","Les habitudes explicites de votre équipe. Exemple : un handler par query ; reprendre les DTO existants ; tester les filtres métier. Laissez vide si les exemples de code doivent servir de référence. Séparez plusieurs règles par ;",existing==null?null:string.Join(" ; ",existing.Conventions),true));
            profile=new(existing?.Id??Guid.NewGuid().ToString("N"),name,objective,architecture,systems,conventions,domains,existing?.Authorities??[],existing?.Glossary??[]);
            output.WriteLine($"\nRécapitulatif\nProjet : {profile.Name}\nObjectif : {profile.Objective}\nTechnologies : {profile.Architecture}");
            output.WriteLine("Applications / bases : "+(systems.Length==0?"À préciser":string.Join(" ; ",systems)));
            output.WriteLine("Sujets métier : "+(domains.Length==0?"À préciser lors des imports":string.Join(" ; ",domains.Select(d=>d.Name))));
            output.WriteLine("Règles de développement : "+(conventions.Length==0?"À préciser":string.Join(" ; ",conventions)));
            output.WriteLine("Ces indications orientent les recherches. Les fichiers seront ajoutés ensuite avec bip-kb --ingest <chemin>.");
            while(true)
            {
                output.Write("Enregistrer ce profil ? oui/non [non] : ");output.Flush();
                var answer=input.ReadLine()?.Trim().ToLowerInvariant();
                if(answer is "oui" or "o" or "yes" or "y")break;
                if(answer is null or "" or "non" or "n" or "no" or "/cancel")throw new OperationCanceledException();
                output.WriteLine("Répondez oui pour enregistrer, ou non pour annuler.");
            }
        }
        Validate(profile);
        using var gate=workspace.Lock();workspace.EnsureWritable();var k=workspace.Load();
        if(k.Projects.Any(p=>p.Name.Equals(profile.Name,StringComparison.OrdinalIgnoreCase)&&p.Id!=profile.Id))throw new ArgumentException("Project name already exists; retain its stable id when updating.");
        k.Projects.RemoveAll(p=>p.Id==profile.Id);k.Projects.Add(profile);workspace.Save(k);return profile;
    }
    public static void Validate(ProjectProfile p)
    {
        if(string.IsNullOrWhiteSpace(p.Id)||string.IsNullOrWhiteSpace(p.Name)||string.IsNullOrWhiteSpace(p.Objective)||string.IsNullOrWhiteSpace(p.Architecture)||p.Domains==null||p.Glossary==null||p.Authorities==null||p.Conventions==null||p.Systems==null||Json.Serialize(p).Length>14000)throw new ArgumentException("Invalid project profile (maximum 14000 serialized characters).");
        if(p.Domains.Select(d=>d.Id).Distinct().Count()!=p.Domains.Length||p.Domains.Any(d=>d.RelatedDomains.Any(id=>!p.Domains.Any(x=>x.Id==id||x.Name==id))))throw new ArgumentException("Domain links must reference declared domains.");
    }
    public static HashSet<string> Related(ProjectProfile? profile,string domain,int depth=2)
    {
        var found=new HashSet<string>(StringComparer.OrdinalIgnoreCase){domain};
        for(var i=0;i<depth&&profile!=null;i++)
            foreach(var d in profile.Domains.Where(d=>found.Contains(d.Name)).ToArray())
                foreach(var link in d.RelatedDomains){var next=profile.Domains.Single(x=>x.Id==link||x.Name==link);found.Add(next.Name);}
        return found;
    }
    public object Summary(string project)
    {
        var k=workspace.Load();var p=Find(project)??throw new ArgumentException("Unknown project.");
        var claims=k.Claims.Where(c=>c.Context?.Project.Equals(p.Name,StringComparison.OrdinalIgnoreCase)==true).ToArray();
        return new{profile=p,revision=workspace.SemanticRevision,derived=true,domains=p.Domains.Select(d=>new{d.Name,d.Responsibility,d.RelatedDomains,claims=claims.Where(c=>c.Context!.Domain==d.Name).Select(c=>new{c.Id,c.Statement,c.Obsolete,c.NeedsReview,c.EvidenceIds}).Take(30)}),openQuestions=k.Questions.Where(q=>q.Project==p.Name&&q.Answer==null),warning="Derived view; consult cited evidence and decisions before use."};
    }
}
