using BipKb;

if(args.Length!=2||!int.TryParse(args[1],out var count)||count is <200 or >50000)
    throw new ArgumentException("Usage: IndexBenchmark <empty-workspace> <200..50000 objects>");
var workspace=new Workspace(args[0]);workspace.Init();var k=new Knowledge();
const int width=200;
var context=new SourceContext("Benchmark","Commandes","Synthetic dense proof graph",SourceStatus.Observed);
for(var start=0;start<count;start+=width)
{
    var size=Math.Min(width,count-start);var file=$"model{start/width}.cs";
    var proofs=Enumerable.Range(start,size).Select(i=>Json.Hash("proof"+i)).ToArray();
    var owner=Json.Hash("object"+start);
    for(var offset=0;offset<size;offset++)
    {
        var i=start+offset;var id=Json.Hash("object"+i);
        k.Evidence.Add(new(proofs[offset],"bench",file,"v1","line:"+offset,$"Champ{i} est un entier ; preuve synthetique REF{i:D8}."));
        k.Objects.Add(new(id,"Modele.Champ"+i,"member",proofs,SourceId:"bench",Project:context.Project,Domain:context.Domain));
        if(offset>0)k.Relations.Add(new(Json.Hash("relation"+i),owner,id,RelationKind.References,proofs));
    }
    k.Sources.Add(new(Json.Hash("source"+start),"bench",file,"v1","key",SourceRole.Current,proofs,[],true,[],context));
}
workspace.Save(k);
Json.Write(Path.Combine(workspace.Local,"materialized.json"),new CachedMemory(workspace.Revision,k));
Console.WriteLine(Json.Serialize(new{objects=count,proofs=k.Evidence.Count,relations=k.Relations.Count,logicalObjectProofLinks=k.Objects.Sum(o=>(long)o.EvidenceIds.Length),materializedBytes=new FileInfo(Path.Combine(workspace.Local,"materialized.json")).Length}));
