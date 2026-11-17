using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BipKb;

public sealed record CompactMemory([property:JsonPropertyOrder(-2)]int CacheFormat,[property:JsonPropertyOrder(-1)]string Revision,Knowledge Knowledge);
public sealed record CacheCompaction(long PreviousBytes,long CurrentBytes,long SavedBytes,double ReductionPercent,int ReferenceLists,bool Rebuilt,bool Verified,string Revision);

/// <summary>Local, lossless encoding. Equal string arrays share one stored representation, never mutable arrays in memory.</summary>
public static class MaterializedCache
{
    public const int Format=2;
    public static string PathName(Workspace workspace)=>Path.Combine(workspace.Local,"materialized.json");
    public static CachedMemory Read(string path)
    {
        using var stream=File.OpenRead(path);
        Span<byte> header=stackalloc byte[256];var length=stream.ReadAtLeast(header,256,throwOnEndOfStream:false);stream.Position=0;
        var reader=new Utf8JsonReader(header[..length],isFinalBlock:false,state:default);
        var compact=reader.Read()&&reader.TokenType==JsonTokenType.StartObject&&reader.Read()&&reader.TokenType==JsonTokenType.PropertyName&&reader.ValueTextEquals("cacheFormat");
        if(!compact)return JsonSerializer.Deserialize<CachedMemory>(stream,Json.Options)??throw new JsonException("Empty materialized cache.");
        if(!reader.Read()||reader.TokenType!=JsonTokenType.Number||!reader.TryGetInt32(out var format)||format!=Format)throw new JsonException("Unsupported materialized cache format.");
        var cached=JsonSerializer.Deserialize<CompactMemory>(stream,Options(new ReferenceLists()))??throw new JsonException("Empty materialized cache.");
        if(cached.CacheFormat!=Format||cached.Knowledge==null)throw new JsonException("Invalid materialized cache.");
        return new(cached.Revision,cached.Knowledge);
    }
    private static JsonSerializerOptions Options(ReferenceLists lists)
    {
        var options=new JsonSerializerOptions(Json.Options){WriteIndented=false};options.Converters.Insert(0,lists);return options;
    }
    public static int Write(string path,string revision,Knowledge knowledge,Action<string>? verify=null,CancellationToken ct=default)
    {
        var lists=new ReferenceLists(ct);
        AtomicFile.WriteStream(path,stream=>JsonSerializer.Serialize(stream,new CompactMemory(Format,revision,knowledge),Options(lists)),validate:temp=>{ct.ThrowIfCancellationRequested();verify?.Invoke(temp);});
        return lists.Count;
    }
    public static CacheCompaction Compact(Workspace workspace,bool rebuild=false,Action<string>? progress=null,CancellationToken ct=default)
    {
        using var gate=workspace.Lock();workspace.EnsureWritable();ct.ThrowIfCancellationRequested();
        var path=PathName(workspace);var before=File.Exists(path)?new FileInfo(path).Length:0;var revision=workspace.Revision;
        progress?.Invoke(rebuild?"Reconstruction depuis la mémoire publiée":"Chargement des connaissances pour compactage");
        // Do not overwrite an old cache before the explicit operation has verified its replacement.
        var knowledge=new MemoryStore(workspace).Load(ignoreCache:rebuild,writeCache:false);Workspace.Validate(knowledge);
        ct.ThrowIfCancellationRequested();
        if(workspace.Revision!=revision)throw new RevisionConflictException("La mémoire a changé ; relancez le compactage.");
        progress?.Invoke("Calcul de l'empreinte complète des connaissances");var expected=Fingerprint(knowledge,ct);
        progress?.Invoke("Écriture du cache compact : listes identiques stockées une seule fois");
        var lists=Write(path,revision,knowledge,temp=>
        {
            progress?.Invoke("Vérification intégrale avant remplacement du cache");
            var restored=Read(temp);Workspace.Validate(restored.Knowledge);
            if(restored.Revision!=revision||Fingerprint(restored.Knowledge,ct)!=expected)throw new InvalidDataException("Le cache compact ne restitue pas exactement les connaissances ; remplacement annulé.");
            ct.ThrowIfCancellationRequested();
            if(workspace.Revision!=revision)throw new RevisionConflictException("La mémoire a changé ; remplacement annulé.");
        },ct);
        var after=new FileInfo(path).Length;
        progress?.Invoke($"Cache compact vérifié : {before:N0} → {after:N0} octets ; aucune connaissance supprimée");
        return new(before,after,before-after,before==0?0:Math.Round(100.0*(before-after)/before,2),lists,rebuild,true,revision);
    }
    public static string Fingerprint(Knowledge knowledge,CancellationToken ct=default)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sink=new HashStream(hash,ct);
        JsonSerializer.Serialize(sink,knowledge,new JsonSerializerOptions(Json.Options){WriteIndented=false});
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private sealed class HashStream(IncrementalHash hash,CancellationToken ct):Stream
    {
        public override bool CanRead=>false;public override bool CanWrite=>true;public override bool CanSeek=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Write(byte[] buffer,int offset,int count)=>Write(buffer.AsSpan(offset,count));
        public override void Write(ReadOnlySpan<byte> buffer){ct.ThrowIfCancellationRequested();hash.AppendData(buffer);}
        public override void Flush(){}public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();
        public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();
    }
    private sealed class ReferenceLists(CancellationToken ct=default):JsonConverter<string[]>
    {
        private readonly Dictionary<string[],int> written=new(new ListComparer());
        private readonly List<string[]> read=[];
        public int Count=>written.Count;
        public override void Write(Utf8JsonWriter writer,string[] value,JsonSerializerOptions options)
        {
            ct.ThrowIfCancellationRequested();
            if(value.Length>=2)
            {
                if(written.TryGetValue(value,out var index))
                {writer.WriteStartObject();writer.WriteNumber("$ref",index);writer.WriteEndObject();return;}
                written.Add(value,written.Count);
            }
            writer.WriteStartArray();foreach(var item in value)writer.WriteStringValue(item);writer.WriteEndArray();
        }
        public override string[] Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
        {
            if(reader.TokenType==JsonTokenType.StartObject)
            {
                if(!reader.Read()||reader.TokenType!=JsonTokenType.PropertyName||!reader.ValueTextEquals("$ref")||!reader.Read()||reader.TokenType!=JsonTokenType.Number||!reader.TryGetInt32(out var index)||index<0||index>=read.Count||!reader.Read()||reader.TokenType!=JsonTokenType.EndObject)throw new JsonException("Invalid cached list reference.");
                return (string[])read[index].Clone();
            }
            if(reader.TokenType!=JsonTokenType.StartArray)throw new JsonException("Expected cached list.");
            var values=new List<string>();
            while(reader.Read()&&reader.TokenType!=JsonTokenType.EndArray)
            {
                if(reader.TokenType is not (JsonTokenType.String or JsonTokenType.Null))throw new JsonException("Invalid cached list item.");
                values.Add(reader.GetString()!);
            }
            if(reader.TokenType!=JsonTokenType.EndArray)throw new JsonException("Incomplete cached list.");
            var array=values.ToArray();if(array.Length>=2)read.Add((string[])array.Clone());return array;
        }
        private sealed class ListComparer:IEqualityComparer<string[]>
        {
            public bool Equals(string[]? x,string[]? y)=>ReferenceEquals(x,y)||x!=null&&y!=null&&x.AsSpan().SequenceEqual(y);
            public int GetHashCode(string[] values){var hash=new HashCode();foreach(var value in values)hash.Add(value,StringComparer.Ordinal);return hash.ToHashCode();}
        }
    }
}
