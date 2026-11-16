namespace BipKb;

/// <summary>Bounded prompts with exact Excel header sharing; never alter the stored evidence.</summary>
public static class AnalysisBatches
{
    public static IEnumerable<Unit[]> Create(IEnumerable<Unit> units,IReadOnlyDictionary<string,UnitHeader>? headers=null,int max=9000)
    {
        var batch=new List<Unit>();var seen=new HashSet<UnitHeader>();var size=0;
        foreach(var unit in units)
        {
            var header=headers?.GetValueOrDefault(unit.Id);
            if(header!=null&&!unit.Text.StartsWith(header.Prefix,StringComparison.Ordinal))header=null;
            // The old extractor emits a header-only unit for a formatted, empty spreadsheet row.
            if(header!=null&&unit.Text.Length==header.Prefix.Length)continue;
            Unit Project()=>header!=null&&seen.Contains(header)?unit with{Text=unit.Text[header.Prefix.Length..]}:unit;
            var projected=Project();var length=Json.Serialize(projected).Length;
            if(size+length>max&&batch.Count>0)
            {
                yield return batch.ToArray();batch.Clear();seen.Clear();size=0;
                projected=Project();length=Json.Serialize(projected).Length;
            }
            // As before, a single heavily JSON-escaped unit may exceed the batch target.
            // Request() and the CLI adapter still enforce the full request/argument limits.
            batch.Add(projected);size+=length;if(header!=null)seen.Add(header);
        }
        if(batch.Count>0)yield return batch.ToArray();
    }
}
