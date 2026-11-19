using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ExcelDataReader;

namespace BipKb;

/// <summary>Lossless reader view before semantic decisions: no assumed header row or 2000-character slicing.</summary>
public static class BusinessExtraction
{
    public const string Version="business-reader-v1";
    public static Extraction Read(string path,int maxBytes,string[]? selectedSheets=null)
    {
        var extension=Path.GetExtension(path).ToLowerInvariant();
        if(extension is not (".xlsx" or ".xls"))return Extractor.Read(path,maxBytes,selectedSheets);
        if(new FileInfo(path).Length>maxBytes)throw new InvalidDataException("Classeur trop volumineux.");
        if(extension==".xlsx")
        {
            using var archive=ZipFile.OpenRead(path);
            if(archive.Entries.Sum(e=>e.Length)>60_000_000)throw new InvalidDataException("Classeur décompressé trop volumineux (60 Mo maximum pour ce lecteur).");
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream=File.OpenRead(path);using var reader=ExcelReaderFactory.CreateReader(stream);
        var units=new List<Unit>();var warnings=new List<string>();
        void Add(string sheet,string locator,string text)
        {
            if(text.Length==0)return;
            if(System.Text.RegularExpressions.Regex.IsMatch(text,@"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})"))throw new InvalidDataException("Credential-like content detected; file not sent to Copilot.");
            if(text.Length>80000)throw new InvalidDataException("Une ligne Excel dépasse 80 000 caractères ; répartissez cette ligne avant l'analyse.");
            units.Add(new(Json.Hash(locator+"\n"+text),locator,text,sheet));
        }
        do
        {
            if(selectedSheets!=null&&!selectedSheets.Contains(reader.Name))continue;
            var sheet=reader.Name;var row=0;
            while(reader.Read())
            {
                row++;var cells=new List<string>();
                for(var c=0;c<reader.FieldCount;c++)
                {
                    var value=reader.GetValue(c);if(value==null)continue;
                    var text=value is DateTime date?date.ToString("O",CultureInfo.InvariantCulture):Convert.ToString(value,CultureInfo.InvariantCulture)??"";
                    if(text.Length>0)cells.Add(Column(c+1)+row+"="+text);
                }
                Add(sheet,$"sheet:{sheet}; row:{row}",string.Join(" | ",cells));
            }
        }while(reader.NextResult());
        if(extension==".xlsx")
        {
            using var zip=ZipFile.OpenRead(path);long expanded=0;
            XDocument? Xml(string entry)
            {
                var item=zip.GetEntry(entry);if(item==null)return null;
                expanded+=item.Length;if(item.Length>30_000_000||expanded>60_000_000)throw new InvalidDataException("Métadonnées Excel décompressées trop volumineuses.");
                using var data=item.Open();using var xml=XmlReader.Create(data,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=30_000_000});return XDocument.Load(xml);
            }
            var workbook=Xml("xl/workbook.xml");var relations=Xml("xl/_rels/workbook.xml.rels");
            var targets=relations?.Descendants().Where(e=>e.Name.LocalName=="Relationship").ToDictionary(e=>(string)e.Attribute("Id")!,e=>(string)e.Attribute("Target")!)??[];
            foreach(var sheet in workbook?.Descendants().Where(e=>e.Name.LocalName=="sheet")??[])
            {
                var name=(string?)sheet.Attribute("name")??"";if(selectedSheets!=null&&!selectedSheets.Contains(name))continue;
                var rid=sheet.Attributes().FirstOrDefault(a=>a.Name.LocalName=="id")?.Value;
                if(rid==null||!targets.TryGetValue(rid,out var target))continue;
                var sheetUri=new Uri(new Uri("https://workbook.invalid/xl/workbook.xml"),target);
                if(sheetUri.Host!="workbook.invalid"){warnings.Add("Lien externe de feuille non consulté.");continue;}
                var entry=sheetUri.AbsolutePath.TrimStart('/');
                var xml=Xml(entry);if(xml==null)continue;
                Add(name,$"sheet:{name}; metadata",$"Feuille {name}; état : {(string?)sheet.Attribute("state")??"visible"}");
                foreach(var cell in xml.Descendants().Where(e=>e.Name.LocalName=="c"))
                {
                    var formula=cell.Elements().SingleOrDefault(e=>e.Name.LocalName=="f");
                    if(formula!=null)Add(name,$"sheet:{name}; cell:{(string?)cell.Attribute("r")}; formula",formula.ToString(SaveOptions.DisableFormatting));
                }
                foreach(var merge in xml.Descendants().Where(e=>e.Name.LocalName=="mergeCell"))Add(name,$"sheet:{name}; merge:{(string?)merge.Attribute("ref")}","Cellules fusionnées : "+(string?)merge.Attribute("ref"));
                var relPath=entry[..(entry.LastIndexOf('/')+1)]+"_rels/"+Path.GetFileName(entry)+".rels";
                foreach(var relation in Xml(relPath)?.Descendants().Where(e=>e.Name.LocalName=="Relationship"&&((string?)e.Attribute("Type"))?.EndsWith("/comments")==true)??[])
                {
                    var resolved=new Uri(new Uri("https://workbook.invalid/"+entry),(string?)relation.Attribute("Target")??"");
                    if(resolved.Host!="workbook.invalid")continue;
                    foreach(var comment in Xml(resolved.AbsolutePath.TrimStart('/'))?.Descendants().Where(e=>e.Name.LocalName=="comment")??[])
                        Add(name,$"sheet:{name}; cell:{(string?)comment.Attribute("ref")}; comment",string.Concat(comment.Descendants().Where(e=>e.Name.LocalName=="t").Select(e=>e.Value)));
                }
            }
            warnings.Add("Vue lecteur complète des valeurs, formules XML, cellules fusionnées et commentaires classiques disponibles ; styles/couleurs, graphiques, macros et commentaires modernes non interprétés. Valeurs calculées enregistrées, formules non exécutées.");
        }
        else warnings.Add("XLS : valeurs et coordonnées conservées ; formules, commentaires et mise en forme non disponibles dans ce lecteur.");
        return new(units.GroupBy(u=>u.Parent).SelectMany(g=>g).ToList(),warnings);
    }
    private static string Column(int n){var text="";while(n>0){n--;text=(char)('A'+n%26)+text;n/=26;}return text;}
}
