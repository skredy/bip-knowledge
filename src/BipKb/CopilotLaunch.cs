using System.Diagnostics;
using System.Text.Json;

namespace BipKb;

/// <summary>Resolves an installed CLI, without executing shell wrappers or installation bootstrappers.</summary>
public sealed record CopilotLaunch(string Executable,string[] PrefixArguments,string Kind)
{
    public ProcessStartInfo Start(string? workingDirectory=null)
    {
        var start=new ProcessStartInfo(Executable){StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};
        if(workingDirectory!=null)start.WorkingDirectory=workingDirectory;
        foreach(var argument in PrefixArguments)start.ArgumentList.Add(argument);
        return start;
    }

    public static CopilotLaunch Resolve(string configured,string baseDirectory,bool? windows=null,string[]? searchDirectories=null)
    {
        var isWindows=windows??OperatingSystem.IsWindows();
        var dirs=searchDirectories??(Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator,StringSplitOptions.RemoveEmptyEntries)
            .Select(p=>p.Trim().Trim('"')).Where(Path.IsPathRooted).ToArray();
        if(string.IsNullOrWhiteSpace(configured))throw new ArgumentException("copilotExecutable est vide.");
        var explicitPath=Path.IsPathRooted(configured)||configured.Contains(Path.DirectorySeparatorChar)||configured.Contains(Path.AltDirectorySeparatorChar);
        var wrappers=new List<string>();
        var problems=new List<string>();
        IEnumerable<string> Candidates(string name)
        {
            if(!isWindows)return [name];
            if(Path.HasExtension(name))return [name];
            return [name+".exe",name+".cmd",name+".ps1",name+".bat"];
        }
        var paths=explicitPath?Candidates(Path.GetFullPath(configured,baseDirectory)):dirs.SelectMany(dir=>Candidates(Path.Combine(dir,configured)));
        foreach(var candidate in paths.Distinct(isWindows?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal))
        {
            if(!File.Exists(candidate))continue;
            var extension=Path.GetExtension(candidate).ToLowerInvariant();
            if(!isWindows||extension==".exe")return new(Path.GetFullPath(candidate),[],isWindows?"native":"direct");
            if(extension is ".cmd" or ".ps1" or ".bat")
            {
                wrappers.Add(candidate);
                var parent=Path.GetDirectoryName(candidate)!;
                // npm places the scoped package beside the command shims. Read its declared bin; never parse shell code.
                var package=Path.Combine(parent,"node_modules","@github","copilot");
                var manifest=Path.Combine(package,"package.json");
                if(!File.Exists(manifest))continue;
                try
                {
                    using var doc=JsonDocument.Parse(File.ReadAllText(manifest));var root=doc.RootElement;
                    if(root.GetProperty("name").GetString()!="@github/copilot")throw new InvalidDataException("Nom du package npm inattendu.");
                    var bin=root.GetProperty("bin");
                    var relative=(bin.ValueKind==JsonValueKind.String?bin.GetString():bin.GetProperty("copilot").GetString())??throw new InvalidDataException("Entrée npm absente.");
                    var entry=Path.GetFullPath(Path.Combine(package,relative));
                    if(!SourceSetup.Under(entry,package)||!File.Exists(entry))throw new InvalidDataException("Entrée npm absente ou hors du package.");
                    var entryExtension=Path.GetExtension(entry).ToLowerInvariant();
                    if(entryExtension==".exe")return new(entry,[],"npm-native");
                    if(entryExtension is not ".js" and not ".mjs" and not ".cjs")throw new InvalidDataException("Format d'entrée npm non pris en charge.");
                    var node=new[]{Path.Combine(parent,"node.exe")}.Concat(dirs.Select(dir=>Path.Combine(dir,"node.exe"))).FirstOrDefault(File.Exists);
                    if(node==null){problems.Add("Package npm trouvé, mais node.exe est introuvable dans PATH.");continue;}
                    return new(Path.GetFullPath(node),[entry],"npm-node");
                }
                catch(Exception e) when(e is JsonException or KeyNotFoundException or InvalidOperationException or InvalidDataException or ArgumentException)
                {problems.Add("Installation npm @github/copilot incomplète ou incompatible : vérifier son package.json et son point d'entrée.");}
            }
        }
        var details=wrappers.Count>0?" Un lanceur PowerShell/batch a été trouvé, mais aucune CLI exécutable correspondante. Le lanceur de l'extension VS Code ne constitue pas une installation autonome de Copilot CLI.":"";
        throw new FileNotFoundException("Copilot CLI introuvable pour copilotExecutable='"+configured+"'."+details+" "+string.Join(" ",problems.Distinct())+
            " Installez la CLI autonome (winget install --id GitHub.Copilot --exact, ou npm install -g @github/copilot), puis rouvrez le terminal et lancez bip-kb doctor. Si elle est déjà installée, indiquez son chemin dans copilotExecutable de bip.json. Aucun contexte enregistré n'est perdu.");
    }
}
