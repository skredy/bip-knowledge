namespace BipKb;

/// <summary>An owned, private runtime directory. Cleanup must never replace a provider result or error.</summary>
public sealed class CopilotScratch:IAsyncDisposable
{
    private readonly Action<string> delete;
    private readonly TextWriter warnings;
    private bool disposed;
    public string DirectoryPath {get;}

    private CopilotScratch(string path,Action<string>? delete,TextWriter? warnings)
    {DirectoryPath=path;this.delete=delete??DeleteOwnedDirectory;this.warnings=warnings??Console.Error;}

    public static CopilotScratch Create(Action<string>? delete=null,TextWriter? warnings=null)
    {
        try{return new(Directory.CreateTempSubdirectory("bip-kb-copilot-").FullName,delete,warnings);}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {throw new IOException($"Impossible de créer le dossier de travail Copilot dans le répertoire temporaire système '{Path.GetTempPath()}'. Vérifiez l'accès à ce répertoire. Le contexte enregistré est conservé.",e);}
    }

    public async ValueTask DisposeAsync()
    {
        if(disposed)return;disposed=true;
        for(var attempt=0;attempt<3;attempt++)
        {
            try{delete(DirectoryPath);return;}
            catch(DirectoryNotFoundException){return;}
            catch(FileNotFoundException){return;}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException)
            {
                if(attempt<2){await Task.Delay(attempt==0?100:300);continue;}
                // Only the generated directory is reported, never provider output or document contents.
                try{warnings.WriteLine($"Avertissement : dossier temporaire Copilot non supprimé ({(e is UnauthorizedAccessException?"accès refusé":"verrouillage ou erreur E/S")}) : {DirectoryPath}. Il peut contenir des pièces jointes. Le résultat ou l'erreur de l'analyse reste inchangé.");}
                catch(Exception writeError) when(writeError is IOException or ObjectDisposedException){ }
            }
        }
    }

    private static void DeleteOwnedDirectory(string path)
    {
        var attributes=File.GetAttributes(path);
        if((attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Runtime directory replaced by a link.");
        var root=new DirectoryInfo(path);
        ClearReadOnly(root);
        root.Delete(true);
    }

    private static void ClearReadOnly(FileSystemInfo entry)
    {
        // Do not traverse junctions/symlinks or change attributes on their targets.
        if((entry.Attributes&FileAttributes.ReparsePoint)!=0)return;
        if((entry.Attributes&FileAttributes.ReadOnly)!=0)entry.Attributes&=~FileAttributes.ReadOnly;
        if(entry is DirectoryInfo directory)
            foreach(var child in directory.EnumerateFileSystemInfos())ClearReadOnly(child);
    }
}
