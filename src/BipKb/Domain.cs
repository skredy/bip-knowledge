using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BipKb;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) }
    };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string text) => JsonSerializer.Deserialize<T>(text, Options) ?? throw new InvalidDataException("Empty JSON.");
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, Serialize(value) + "\n"); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
public enum SourceRole { Unknown, Legacy, Target, Current }
public enum Contribution { New, Corroboration, Complement, Contradiction, Evolution, Hypothesis, None }
public sealed record SourceDefinition(string Id, SourceRole Role, string[]? ExcelSheets=null);
public sealed record Settings
{
    public int Version { get; init; } = 1;
    public List<SourceDefinition> Sources { get; init; } = [];
    public string[] Exclusions { get; init; } = ["**/bin/**", "**/obj/**", "**/node_modules/**", "**/.git/**", "**/.local/**"];
    public string AnalysisProfile { get; init; } = "evidence-v1";
    public string CopilotExecutable { get; init; } = "copilot";
    public int TimeoutSeconds { get; init; } = 180;
    public int MaxFileBytes { get; init; } = 10 * 1024 * 1024;
}
public sealed record Unit(string Id, string Locator, string Text);
public sealed record Extraction(List<Unit> Units, List<string> Warnings, bool Image = false);
public sealed record Evidence(string Id, string SourceId, string RelativePath, string Version, string Locator, string Excerpt);
public sealed record Entity(string Id, string Name, string Kind);
public sealed record Finding
{
    public required string Entity { get; init; }
    public required string Kind { get; init; }
    public required string Statement { get; init; }
    public required Contribution Contribution { get; init; }
    public required string[] EvidenceIds { get; init; }
    public string[] RelatedClaimIds { get; init; } = [];
    public bool Uncertain { get; init; }
}
public sealed record Analysis(List<Finding> Findings);
public sealed record Claim(string Id, string EntityId, string Statement, Contribution Contribution, string[] EvidenceIds,
    string[] RelatedClaimIds, SourceRole Role, bool NeedsReview, bool Obsolete = false);
public sealed record SourceVersion(string Id, string SourceId, string RelativePath, string Hash, string CacheKey,
    SourceRole Role, string[] EvidenceIds, string[] ClaimIds, bool Active, string[] Warnings);
public sealed record Decision(string Id, string ClaimId, string Action, string Reason, string Actor, string[] Supersedes, DateTimeOffset At, string EvidenceFingerprint = "");
public sealed record IngestionReport(string Id, DateTimeOffset At, string SourceId, int Files, int Unchanged,
    Dictionary<Contribution, int> Counts, string[] Warnings, string[] ClaimIds);
public sealed record Knowledge
{
    public int Version { get; init; } = 1;
    public List<SourceVersion> Sources { get; init; } = [];
    public List<Entity> Entities { get; init; } = [];
    public List<Evidence> Evidence { get; init; } = [];
    public List<Claim> Claims { get; init; } = [];
    public List<IngestionReport> Reports { get; init; } = [];
}
public sealed record AnalysisRequest(string Role, Unit[] Units, Claim[] Existing, Entity[] Entities, Decision[] Decisions, string Phase);
public sealed record Answer(string Text, string[] ClaimIds, string[] Uncertainties);
public interface IAnalyzer
{
    Task<Analysis> Analyze(AnalysisRequest request, string? image, CancellationToken ct);
    Task<Answer> Ask(string question, Claim[] claims, CancellationToken ct);
}
