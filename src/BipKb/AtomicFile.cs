namespace BipKb;

public static class AtomicFile
{
    // Rename within the same directory. Never delete/truncate the destination before replacement.
    public static void Write(string path,string content,Action<string,string>? replace=null,Action<TimeSpan>? delay=null)
    {
        path=Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        replace??=(from,to)=>File.Move(from,to,true);
        delay??=Thread.Sleep;
        try
        {
            File.WriteAllText(temp,content);
            for(var attempt=0;;attempt++)
            {
                try{replace(temp,path);break;}
                catch(Exception e) when(IsAccessConflict(e)&&attempt<4)
                {delay(TimeSpan.FromMilliseconds(100*(1<<attempt)));}
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Impossible d'enregistrer '{path}' par remplacement atomique (code système {e.HResult&0xffff}). Vérifiez les droits et les programmes qui verrouillent ce fichier. Le fichier de destination n'a pas été supprimé pour contourner l'erreur.",e);
        }
        finally
        {
            // Cleanup must not hide the original write error. The unique temporary file may still be locked.
            try{File.Delete(temp);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){ }
        }
    }
    private static bool IsAccessConflict(Exception e)=>e is UnauthorizedAccessException||e is IOException&&(e.HResult&0xffff) is 5 or 32 or 33;
}
