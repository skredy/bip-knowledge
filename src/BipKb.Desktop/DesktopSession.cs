using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace BipKb.Desktop;

internal sealed record OperationEntry(string Action, DateTimeOffset Started, string State, DateTimeOffset? Ended=null);
internal static class DesktopSession
{
    private static string DirectoryFor(Workspace w)=>Path.Combine(w.Local,"desktop","activity");
    public static string Begin(Workspace w,OperationEntry entry)
    {
        var path=Path.Combine(DirectoryFor(w),$"{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}.json");
        Json.Write(path,entry);
        return path;
    }
    public static string History(Workspace w)
    {
        var dir=DirectoryFor(w);
        if(!Directory.Exists(dir))return "Aucune opération enregistrée.";
        var lines=new List<string>();
        foreach(var path in Directory.EnumerateFiles(dir,"*.json").OrderDescending(StringComparer.Ordinal).Take(60))
        {
            var item=Json.Read<OperationEntry>(File.ReadAllText(path));
            lines.Add($"{item.Started.LocalDateTime:g} · {item.Action} · {item.State}"+
                (item.Ended is {} end?$" · {(end-item.Started).TotalSeconds:N0} s":" · fin non enregistrée"));
        }
        return string.Join("\n",lines);
    }

    public static Display Dossiers(Workspace w,int offset,CancellationToken ct)
    {
        const int limit=30;
        try
        {
            using var index=new McpIndex(w,ct);
            using var cmd=index.Command("""
                SELECT id, json_extract(body,'$.goal'), json_extract(body,'$.created')
                FROM mcp_records WHERE kind='task'
                ORDER BY json_extract(body,'$.created') DESC,id LIMIT $limit OFFSET $offset
                """,("$limit",limit),("$offset",offset));
            using var reader=cmd.ExecuteReader();
            var rows=new List<Reference>();
            while(reader.Read())rows.Add(new($"{reader.GetString(1)} · {reader.GetString(2)}","task",reader.GetString(0)));
            return new Display($"{rows.Count} dossier(s) à partir de la position {offset+1}.",null,rows);
        }
        catch(McpIndexUnavailableException)
        {
            var tasks=w.Load().Tasks.OrderByDescending(t=>t.Created).Skip(offset).Take(limit);
            return new Display("Dossiers de la mémoire publiée. Actualisez l’index dans Activité pour accélérer cette liste.",null,
                tasks.Select(t=>new Reference($"{t.Created:g} · {t.Goal}","task",t.Id)).ToArray());
        }
    }
}
