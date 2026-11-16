using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BipKb;

public static class SourceIdentifier
{
    public const string Help="Un identifiant court pour retrouver cette source, par exemple etude-sql ou code_actuel. Utilisez uniquement a-z, A-Z, 0-9, - et _ ; sans espace, accent, point ni chemin de fichier.";

    public static string? Error(string? value)=>string.IsNullOrEmpty(value)||value.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c is not '-' and not '_')
        ?"Identifiant de source invalide. "+Help:null;

    public static void Validate(string value)
    {
        if(Error(value) is {} error)throw new ArgumentException(error);
    }

    public static string Suggest(string value)
    {
        var plain=string.Concat(value.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark));
        var id=Regex.Replace(plain,"[^a-zA-Z0-9_-]+","-").Trim('-').ToLowerInvariant();
        return id.Length==0?"source":id;
    }
}
