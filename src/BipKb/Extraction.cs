using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ExcelDataReader;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace BipKb;

public static class Extractor
{
    public const string Version="extractors-2";
    public static readonly string[] Extensions=[".xls",".xlsx",".json",".cs",".csproj",".sql",".md",".txt",".drawio",".png",".jpg",".jpeg"];
    public static bool Supported(string path)=>Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static bool IsImage(string path)=>new[]{".png",".jpg",".jpeg"}.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static bool SecretName(string path)
    {
        var n=Path.GetFileName(path).ToLowerInvariant();
        return n.StartsWith(".env")||n.Contains("secret")||n.Contains("credential")||n is "id_rsa" or "id_ed25519"||new[]{".pfx",".p12",".pem",".key"}.Contains(Path.GetExtension(n));
    }
    public static void EnsureNoLinks(string path)
    {
        for(var p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))
            if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new InvalidDataException("Symbolic links are not ingested.");
    }
    private static bool Excluded(string path, string[] patterns)
    {
        var segments=path.Replace('\\','/').Split('/');
        if(segments.Any(s=>s is ".git" or ".local" or "bin" or "obj" or "node_modules" or ".vs")) return true;
        return patterns.Any(p=>Regex.IsMatch(path.Replace('\\','/'),"^"+Regex.Escape(p).Replace(@"\*\*/","(?:.*/)?").Replace(@"\*\*",".*").Replace(@"\*","[^/]*").Replace(@"\?",".")+"$"));
    }
    public static async Task<List<string>> Inventory(string path,string root,Settings config,CancellationToken ct)
    {
        EnsureNoLinks(path);
        var single=File.Exists(path);
        if(single)
        {
            if(!Supported(path)||SecretName(path)||Excluded(Path.GetRelativePath(root,path),config.Exclusions)) throw new InvalidDataException("Unsupported or excluded file.");

        }
        if(!single&&!Directory.Exists(path)) throw new DirectoryNotFoundException("Source is inaccessible; no deletion inferred.");
        var found=new List<string>(); var hasIgnore=File.Exists(Path.Combine(root,".gitignore"));
        void Walk(string dir)
        {
            foreach(var entry in Directory.EnumerateFileSystemEntries(dir).Order())
            {
                ct.ThrowIfCancellationRequested();
                if(Path.GetFileName(entry)==".gitignore")hasIgnore=true;
                if((File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0) continue;
                if(Excluded(Path.GetRelativePath(root,entry),config.Exclusions)||SecretName(entry)) continue;
                if(Directory.Exists(entry)) Walk(entry); else if(Supported(entry)) found.Add(entry);
            }
        }
        if(single) found.Add(path); else Walk(path);
        var probe=new ProcessStartInfo("git"){WorkingDirectory=root};
        probe.ArgumentList.Add("rev-parse");probe.ArgumentList.Add("--is-inside-work-tree");
        ProcessResult repository;
        try { repository=await Processes.Run(probe,TimeSpan.FromSeconds(10),ct); }
        catch(System.ComponentModel.Win32Exception) { throw new InvalidOperationException("Git is required to honor source exclusions."); }
        if(repository.ExitCode!=0)
        {
            if(hasIgnore)throw new InvalidOperationException("Initialize the source Git repository to apply its .gitignore rules.");
            return found;
        }
        // Git itself implements nested ignores, negation and directory semantics.
        var check=new ProcessStartInfo("git"){WorkingDirectory=root,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        foreach(var a in new[]{"check-ignore","--no-index","-z","--stdin"}) check.ArgumentList.Add(a);
        try
        {
            using var p=Process.Start(check)!; var output=p.StandardOutput.ReadToEndAsync(ct); var errors=p.StandardError.ReadToEndAsync(ct);
            foreach(var f in found) await p.StandardInput.WriteAsync(Path.GetRelativePath(root,f)+"\0");
            p.StandardInput.Close(); await p.WaitForExitAsync(ct);
            var ignored=(await output).Split('\0',StringSplitOptions.RemoveEmptyEntries).ToHashSet(); var error=await errors;
            if(p.ExitCode>1 && !error.Contains("not a git repository")) throw new IOException("Git ignore evaluation failed.");
            if(p.ExitCode>1 && hasIgnore) throw new InvalidOperationException("Initialize the source Git repository to apply its .gitignore rules.");
            return found.Where(f=>!ignored.Contains(Path.GetRelativePath(root,f))).ToList();
        }
        catch(System.ComponentModel.Win32Exception) { throw new InvalidOperationException("Git is required to honor source exclusions."); }
    }
    public static Extraction Read(string file,int maxBytes,string[]? excelSheets=null)
    {
        if(new FileInfo(file).Length>maxBytes) throw new InvalidDataException("File exceeds configured maxFileBytes: "+Path.GetFileName(file));
        var units=new List<Unit>(); var warnings=new List<string>();
        void Add(string locator,string content)
        {
            if(string.IsNullOrWhiteSpace(content)) return;
            if(Regex.IsMatch(content,@"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})")) throw new InvalidDataException("Credential-like content detected; file not sent to Copilot.");
            for(var offset=0;offset<content.Length;offset+=2000)
            {
                var text=content.Substring(offset,Math.Min(2000,content.Length-offset));
                var loc=locator+(content.Length>2000?$"; chars {offset}-{offset+text.Length}":"");
                units.Add(new(Json.Hash(loc+"\n"+text),loc,text,locator));
            }
        }
        var ext=Path.GetExtension(file).ToLowerInvariant();
        if(IsImage(file)) return new([new("image","image:whole","Image attached; cite visible elements and mark uncertain text.")],[],true);
        if(ext is ".xls" or ".xlsx")
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            using var stream=File.OpenRead(file); using var reader=ExcelReaderFactory.CreateReader(stream);
            do
            {
                if(excelSheets is {Length:>0} && !excelSheets.Contains(reader.Name))continue;
                var row=0;string? headers=null;
                while(reader.Read())
                {
                    row++; var cells=new List<string>();
                    for(var col=0;col<reader.FieldCount;col++)
                        if(reader.GetValue(col) is {} value) cells.Add($"{Column(col)}{row}: {Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture)}");
                    if(row==1)headers=string.Join(" | ",cells);
                    Add($"sheet:{reader.Name}; row:{row}",(row>1?"Headers: "+headers+"\n":"")+string.Join("\n",cells));
                }
            }while(reader.NextResult());
        }
        else if(ext==".drawio")
        {
            var doc=Xml(File.ReadAllText(file));
            if(doc.Root?.Name.LocalName=="mxGraphModel") Graph(doc,"page:1",Add);
            else foreach(var page in doc.Descendants("diagram"))
            {
                var model=page.Element("mxGraphModel");
                if(model==null)
                {
                    if(page.Value.TrimStart().StartsWith('<')) model=Xml(page.Value).Root;
                    else
                    {
                        using var compressed=new MemoryStream(Convert.FromBase64String(page.Value.Trim())); using var deflate=new DeflateStream(compressed,CompressionMode.Decompress); using var decoded=new MemoryStream();
                        var buffer=new byte[8192]; int count;
                        while((count=deflate.Read(buffer))>0) { decoded.Write(buffer,0,count); if(decoded.Length>maxBytes) throw new InvalidDataException("Expanded diagram exceeds limit."); }
                        model=Xml(Uri.UnescapeDataString(Encoding.UTF8.GetString(decoded.ToArray()))).Root;
                    }
                }
                Graph(new XDocument(model!),"page:"+(string?)page.Attribute("name"),Add);
            }
        }
        else
        {
            var text=File.ReadAllText(file);
            switch(ext)
            {
                case ".json":
                    using(var document=JsonDocument.Parse(text))
                    {
                        void Visit(JsonElement element,string path)
                        {
                            if(element.ValueKind==JsonValueKind.Object) foreach(var p in element.EnumerateObject()) Visit(p.Value,path+"/"+p.Name.Replace("~","~0").Replace("/","~1"));
                            else if(element.ValueKind==JsonValueKind.Array) { var i=0; foreach(var v in element.EnumerateArray()) Visit(v,path+"/"+i++); }
                            else Add("json-pointer:"+path,element.GetRawText());
                        }
                        Visit(document.RootElement,"");
                    }
                    break;
                case ".cs":
                    var tree=CSharpSyntaxTree.ParseText(text); var node=tree.GetRoot();
                    foreach(var import in node.DescendantNodes().OfType<UsingDirectiveSyntax>())Add("using",import.ToString());
                    foreach(var ns in node.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())Add("namespace",ns.Name.ToString());
                    warnings.AddRange(tree.GetDiagnostics().Where(d=>d.Severity==Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d=>d.ToString()));
                    foreach(var member in node.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(m=>m is not NamespaceDeclarationSyntax and not FileScopedNamespaceDeclarationSyntax))
                    {
                        var line=member.GetLocation().GetLineSpan().StartLinePosition.Line+1;
                        if(member is TypeDeclarationSyntax type) Add($"line:{line}; type:{type.Identifier}",type.WithMembers(default).ToString());
                        else Add($"line:{line}; syntax:{member.Kind()}",member.ToString());
                    }
                    if(units.Count==0) Add("file",text);
                    break;
                case ".sql":
                    var parser=new TSql160Parser(true); var fragment=parser.Parse(new StringReader(text),out var errors);
                    warnings.AddRange(errors.Select(e=>$"SQL line {e.Line}: {e.Message}"));
                    if(Regex.IsMatch(text,@"\b(sp_executesql|EXEC(?:UTE)?\s*\()",RegexOptions.IgnoreCase)) warnings.Add("Dynamic SQL: dependencies may be incomplete.");
                    if(fragment is TSqlScript script) foreach(var batch in script.Batches) foreach(var stmt in batch.Statements) Add($"line:{stmt.StartLine}; {stmt.GetType().Name}",text.Substring(stmt.StartOffset,stmt.FragmentLength));
                    if(units.Count==0) Add("sql:unparsed",text);
                    break;
                case ".csproj":
                    var project=Xml(text);
                    foreach(var item in project.Descendants().Where(e=>e.Name.LocalName is "PackageReference" or "ProjectReference" or "TargetFramework" or "TargetFrameworks")) Add("project:"+item.Name.LocalName,item.ToString());
                    break;
                default:
                    var lines=text.Split('\n'); var chunk=new StringBuilder(); var start=1; var heading="";
                    for(var i=0;i<lines.Length;i++)
                    {
                        if((lines[i].StartsWith('#')||chunk.Length>8000)&&chunk.Length>0) { Add($"line:{start}; {heading}",chunk.ToString()); chunk.Clear(); start=i+1; }
                        if(lines[i].StartsWith('#')) heading=lines[i]; chunk.AppendLine(lines[i]);
                    }
                    Add($"line:{start}; {heading}",chunk.ToString()); break;
            }
        }
        return new(units,warnings);
    }
    private static string Column(int n) { var s=""; for(n++;n>0;n=(n-1)/26)s=(char)('A'+(n-1)%26)+s; return s; }
    private static XDocument Xml(string text) { using var reader=XmlReader.Create(new StringReader(text),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20_000_000}); return XDocument.Load(reader); }
    private static void Graph(XDocument doc,string page,Action<string,string> add)
    {
        foreach(var cell in doc.Descendants("mxCell"))
        {
            var label=(string?)cell.Attribute("value")??(string?)cell.Parent?.Attribute("label")??"";
            var source=(string?)cell.Attribute("source"); var target=(string?)cell.Attribute("target");
            if(label.Length>0||source!=null) add(page+"; cell:"+(string?)cell.Attribute("id"),$"label: {System.Net.WebUtility.HtmlDecode(label)}; source: {source}; target: {target}; parent: {(string?)cell.Attribute("parent")}");
        }
    }
}
