using System.Text;
using System.Text.Json;

namespace BipKb;

public static class CopilotOutput
{
    private static readonly JsonSerializerOptions ResponseOptions=new(Json.Options){RespectRequiredConstructorParameters=true};
    public static string Unfence(string value)
    {
        var text=value.Trim().TrimStart('\uFEFF').Trim();var newline=text.IndexOf('\n');
        if(newline>=0&&text.EndsWith("```",StringComparison.Ordinal))
        {
            var opening=text[..newline].TrimEnd('\r');
            if(opening=="```"||opening.Equals("```json",StringComparison.OrdinalIgnoreCase))return text[(newline+1)..^3].Trim();
        }
        return text;
    }

    public static T Read<T>(string response)
    {
        var text=Unfence(response);
        JsonDocument document;
        try{document=JsonDocument.Parse(text);}
        catch(JsonException)
        {
            // Preserve characters, identifiers and values. Only encode literal LF/CR/TAB inside strings.
            // Never invent missing braces, quotes, fields, citations, or reinterpret arbitrary prose.
            text=EscapeLiteralWhitespace(text);
            try{document=JsonDocument.Parse(text);}
            catch(JsonException error)
            {throw Failure("syntaxe JSON invalide",error);}
        }
        using(document)
            if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Réponse Copilot refusée : un objet JSON est requis à la racine.");
        try{return JsonSerializer.Deserialize<T>(text,ResponseOptions)??throw new InvalidDataException("Réponse Copilot vide.");}
        catch(JsonException error){throw Failure("JSON lisible mais structure incompatible (champ requis, inconnu ou valeur de type incorrect)",error);}
    }

    private static InvalidDataException Failure(string reason,JsonException error)
    {
        // Never display arbitrary field names/values: even exception paths can contain source text.
        var path=error.Path;
        var safe=path!=null&&System.Text.RegularExpressions.Regex.IsMatch(path,@"^\$(\.(findings|entity|kind|statement|contribution|evidenceIds|relatedClaimIds|uncertain|qualifiedName|conditions|text|claimIds|uncertainties|answer|citations|nextQueries|questions|action|plan|summary|reads|start|count|query|offset|reviews|unitId|status|reason|visualStatus|exclusions)|\[[0-9]{1,6}\])*$")?path:"champ non prévu";
        return new InvalidDataException($"Réponse Copilot refusée : {reason} (chemin {safe}, ligne {error.LineNumber}, position {error.BytePositionInLine}).",error);
    }

    private static string EscapeLiteralWhitespace(string text)
    {
        var result=new StringBuilder(text.Length);var inside=false;var escaped=false;
        foreach(var c in text)
        {
            if(!inside){result.Append(c);if(c=='"')inside=true;continue;}
            if(escaped)
            {
                // Backslash followed by a raw newline has ambiguous intent; do not repair it.
                if(c is '\n' or '\r' or '\t')return text;
                result.Append(c);escaped=false;continue;
            }
            if(c=='\\'){result.Append(c);escaped=true;}
            else if(c=='"'){result.Append(c);inside=false;}
            else if(c=='\n')result.Append("\\n");
            else if(c=='\r')result.Append("\\r");
            else if(c=='\t')result.Append("\\t");
            else result.Append(c);
        }
        return result.ToString();
    }

    public static string JsonLines(string output)
    {
        string? answer=null;var completed=false;
        try
        {
            using var reader=new StringReader(output.TrimStart('\uFEFF'));string? line;
            while((line=reader.ReadLine())!=null)
            {
                if(string.IsNullOrWhiteSpace(line))continue;
                using var document=JsonDocument.Parse(line);var item=document.RootElement;
                var type=item.GetProperty("type").GetString();
                if(type is "session.error" or "error")throw new InvalidDataException("Copilot a signalé une erreur dans son flux JSONL ; aucune réponse acceptée.");
                if(type=="result")
                {
                    if(completed||!item.TryGetProperty("exitCode",out var code)||code.GetInt32()!=0)throw new InvalidDataException("Copilot n'a pas confirmé une fin d'analyse réussie dans son flux JSONL.");
                    completed=true;
                }
                else if(type=="assistant.message")
                {
                    if(completed)throw new InvalidDataException("Réponse Copilot reçue après la fin du flux JSONL.");
                    // Copilot session events carry data.content; some CLI versions flatten event data.
                    var data=item.TryGetProperty("data",out var nested)?nested:item;
                    if(data.TryGetProperty("content",out var content)&&content.ValueKind==JsonValueKind.String&&content.GetString() is {Length:>0} message)answer=message;
                }
                // Deltas, reasoning, telemetry and tool output are never promoted to an answer.
            }
        }
        catch(Exception error) when(error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {throw new InvalidDataException("Flux JSONL Copilot invalide ou incompatible. Aucune réponse partielle acceptée.",error);}
        if(!completed||answer==null)throw new InvalidDataException("Flux JSONL Copilot incomplet : réponse finale ou confirmation de fin absente.");
        return answer;
    }
}
