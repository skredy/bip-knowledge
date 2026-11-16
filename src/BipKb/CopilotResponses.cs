namespace BipKb;

/// <summary>One bounded format/navigation retry; provenance validation remains mandatory.</summary>
public static class CopilotResponses
{
    public static async Task<T> Run<T>(string prompt,string phase,string diagnosticDirectory,
        Func<string,CancellationToken,Task<string>> complete,Action<T> validate,CancellationToken ct,Action<string>? progress=null)
    {
        var rejected=new List<object>();
        string? navigationCorrection=null;
        for(var attempt=0;attempt<2;attempt++)
        {
            ct.ThrowIfCancellationRequested();
            // Repeat the original request, not the rejected answer or its potentially malicious instructions.
            var instruction=attempt==0?prompt:navigationCorrection!=null?"NAVIGATION RETRY: "+navigationCorrection+"\n"+prompt:
                "FORMAT RETRY: Your previous response was rejected. Follow the requested JSON contract exactly. Return one complete JSON object, no Markdown, commentary, extra fields or truncated content. Use double-quoted property names and strings; escape control characters. Use only supplied evidence IDs. Prefer fewer fully supported findings over a long incomplete response.\n"+prompt;
            var response=await complete(instruction,ct);
            T result;
            try{result=CopilotOutput.Read<T>(response);}
            catch(InvalidDataException error)
            {
                rejected.Add(new{attempt=attempt+1,reason=error.Message,response});
                if(attempt==0){progress?.Invoke("Réponse Copilot non conforme — nouvelle tentative de format (2/2)");continue;}
                throw Rejected(error,rejected,phase,prompt,diagnosticDirectory);
            }
            try{validate(result);}
            catch(CopilotNavigationException error)
            {
                rejected.Add(new{attempt=attempt+1,reason=error.Message,response});
                if(attempt==0)
                {
                    navigationCorrection=error.Correction;
                    progress?.Invoke(error.Message+" Nouvelle tentative de navigation (2/2).");continue;
                }
                throw Rejected(new InvalidDataException(error.Message),rejected,phase,prompt,diagnosticDirectory);
            }
            catch(InvalidDataException error)
            {
                rejected.Add(new{attempt=attempt+1,reason=error.Message,response});
                // Unknown evidence and contradictory references must not trigger a cosmetic repair.
                throw Rejected(error,rejected,phase,prompt,diagnosticDirectory);
            }
            return result;
        }
        throw new InvalidOperationException("Unreachable response attempt.");
    }

    private static InvalidDataException Rejected(InvalidDataException error,List<object> responses,string phase,string prompt,string directory)
    {
        var path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".json");
        string diagnostic;
        try
        {
            Json.Write(path,new{phase,requestHash=Json.Hash(prompt),responses});
            diagnostic=$"Diagnostic local : {path} (peut contenir des extraits privés ; ne pas partager tel quel).";
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException)
        {diagnostic="Le diagnostic local n'a pas pu être enregistré (erreur d'accès ou E/S).";}
        return new InvalidDataException($"{error.Message} Aucun résultat refusé n'a été accepté. {diagnostic} Les lots déjà validés sont conservés ; relancez sans --reanalyze pour les réutiliser.",error);
    }
}
