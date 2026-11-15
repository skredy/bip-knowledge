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
            string Read(string q){output.Write(q+" : ");var value=input.ReadLine()?.Trim();if(string.IsNullOrEmpty(value))throw new OperationCanceledException();return value;}
            var name=Read("Nom du projet");var existing=Find(name);
            var objective=Read("Objectif et périmètre");var architecture=Read("Architecture cible");var systems=Read("Systèmes concernés (séparés par ;)").Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
            var domains=Read("Domaines (séparés par ;)").Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries).Select(n=>existing?.Domains.SingleOrDefault(d=>d.Name==n)??new DomainProfile(Guid.NewGuid().ToString("N"),n,"À préciser",[])).ToArray();
            var conventions=Read("Conventions principales (séparées par ;)").Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
            profile=new(existing?.Id??Guid.NewGuid().ToString("N"),name,objective,architecture,systems,conventions,domains,existing?.Authorities??[],existing?.Glossary??[]);
            output.WriteLine(Json.Serialize(profile));output.Write("Enregistrer ? oui/non : ");if(input.ReadLine()?.Trim()!="oui")throw new OperationCanceledException();
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
