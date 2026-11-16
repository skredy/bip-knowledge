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
    public static void Write<T>(string path, T value)=>AtomicFile.Write(path,Serialize(value)+"\n");
}
public enum SourceRole { Unknown, Legacy, Target, Current }
public enum Contribution { New, Corroboration, Complement, Contradiction, Evolution, Hypothesis, None }
public enum SourceStatus { Unknown, Proposal, Validated, Observed, Historical }
public sealed record SourceContext(string Project, string Domain, string Description, SourceStatus Status, string? Notes = "")
{
    [JsonIgnore]
    public string Scope => Json.Serialize(new[]{Project.Trim().ToLowerInvariant(),Domain.Trim().ToLowerInvariant()});
    public void Validate()
    {
        var errors=new[]{TextError("Projet (project)",Project,120),TextError("Sujet métier (domain)",Domain,120),TextError("Description (description)",Description,1200),TextError("Précisions (notes)",Notes,1200,false),
            Status==SourceStatus.Unknown||!Enum.IsDefined(Status)?"Statut (status) : choisissez proposal, validated, observed ou historical.":null}.Where(e=>e!=null).ToArray();
        if(errors.Length>0)throw new ArgumentException("Contexte invalide : "+string.Join(" ",errors));
        if(Json.Serialize(this).Length>4000)throw new ArgumentException("Contexte trop volumineux après encodage ; raccourcissez la description ou les notes.");
    }
    public static string? TextError(string label,string? value,int maxLength,bool required=true)
    {
        if(required&&string.IsNullOrWhiteSpace(value))return label+" : ce champ est vide ; renseignez-le.";
        if(value?.Length>maxLength)return $"{label} : {value.Length} caractères saisis, maximum {maxLength}. Raccourcissez ce champ.";
        return null;
    }
}
public sealed record SourceDescriptor(SourceRole Role, SourceContext Context)
{
    public void Validate(){if(Role==SourceRole.Unknown||!Enum.IsDefined(Role))throw new ArgumentException("Rôle requis : legacy|target|current."); if(Context==null)throw new ArgumentException("Contexte absent.");Context.Validate();}
}
public sealed record SourceDefinition(string Id, SourceRole Role, string[]? ExcelSheets=null, SourceContext? Context=null, SortedDictionary<string,SourceDescriptor>? Overrides=null)
{
    public SourceDescriptor? Resolve(string relative)
    {
        relative=relative.Replace('\\','/');
        var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        var match=Overrides?.Where(p=>relative.Equals(p.Key,comparison)||relative.StartsWith(p.Key+"/",comparison)).OrderByDescending(p=>p.Key.Length).FirstOrDefault();
        return match?.Value??(Context==null?null:new SourceDescriptor(Role,Context));
    }
}
public sealed record Settings
{
    public int Version { get; init; } = 1;
    public List<SourceDefinition> Sources { get; init; } = [];
    public string[] Exclusions { get; init; } = ["**/bin/**", "**/obj/**", "**/node_modules/**", "**/.git/**", "**/.local/**"];
    public string AnalysisProfile { get; init; } = "evidence-v1";
    public string CopilotExecutable { get; init; } = "copilot";
    public int TimeoutSeconds { get; init; } = 180;
    public int MaxResearchCycles { get; init; } = 3;
    public int GraphDepth { get; init; } = 2;
    public int ResearchBudget { get; init; } = 16000;
    public int MaxFileBytes { get; init; } = 10 * 1024 * 1024;
}
public sealed record Unit(string Id, string Locator, string Text, string? Parent = null);
// Extraction-only metadata: canonical evidence and its IDs are not rewritten for prompt compaction.
public sealed record UnitHeader(string Scope,string Prefix);
public sealed record Extraction(List<Unit> Units, List<string> Warnings, bool Image = false, List<TechnicalObject>? Objects = null, List<KnowledgeRelation>? Relations = null, Dictionary<string,UnitHeader>? RepeatedHeaders = null);
public sealed record Evidence(string Id, string SourceId, string RelativePath, string Version, string Locator, string Excerpt, string? Parent = null);
public sealed record Entity(string Id, string Name, string Kind, string? Project = null, string? Domain = null, string[]? Aliases = null, string? IdentityKey = null);
public sealed record Finding
{
    public required string Entity { get; init; }
    public required string Kind { get; init; }
    public required string Statement { get; init; }
    public required Contribution Contribution { get; init; }
    public required string[] EvidenceIds { get; init; }
    public string[] RelatedClaimIds { get; init; } = [];
    public bool Uncertain { get; init; }
    public string? QualifiedName { get; init; }
    public string[] Conditions { get; init; } = [];
}
public sealed record Analysis(List<Finding> Findings);
public sealed record Claim(string Id, string EntityId, string Statement, Contribution Contribution, string[] EvidenceIds,
    string[] RelatedClaimIds, SourceRole Role, bool NeedsReview, bool Obsolete = false, SourceContext? Context = null, KnowledgeOrigin Origin = KnowledgeOrigin.Extracted, KnowledgeState State = KnowledgeState.Proposed, string[]? Conditions = null);
public sealed record SourceVersion(string Id, string SourceId, string RelativePath, string Hash, string CacheKey,
    SourceRole Role, string[] EvidenceIds, string[] ClaimIds, bool Active, string[] Warnings, SourceContext? Context = null, bool Analyzed = true);
public sealed record Decision(string Id, string ClaimId, string Action, string Reason, string Actor, string[] Supersedes, DateTimeOffset At, string EvidenceFingerprint = "");
public sealed record IngestionReport(string Id, DateTimeOffset At, string SourceId, int Files, int Unchanged,
    Dictionary<Contribution, int> Counts, string[] Warnings, string[] ClaimIds, string Mode = "analysis", int UnanalyzedFiles = 0);
public sealed record Knowledge
{
    public int Version { get; init; } = 2;
    public List<SourceVersion> Sources { get; init; } = [];
    public List<Entity> Entities { get; init; } = [];
    public List<Evidence> Evidence { get; init; } = [];
    public List<Claim> Claims { get; init; } = [];
    public List<IngestionReport> Reports { get; init; } = [];
    public List<ProjectProfile> Projects { get; init; } = [];
    public List<TechnicalObject> Objects { get; init; } = [];
    public List<KnowledgeRelation> Relations { get; init; } = [];
    public List<WorkTask> Tasks { get; init; } = [];
    public List<OpenQuestion> Questions { get; init; } = [];
}
public sealed record AnalysisRequest(string Role, Unit[] Units, Claim[] Existing, Entity[] Entities, Decision[] Decisions, string Phase, SourceContext? Context = null, ProjectProfile? Project = null, OpenQuestion[]? HumanDeclarations = null);
public sealed record Answer(string Text, string[] ClaimIds, string[] Uncertainties);
public interface IAnalyzer
{
    Task<Analysis> Analyze(AnalysisRequest request, string? image, CancellationToken ct);
    Task<Answer> Ask(string question, Claim[] claims, CancellationToken ct);
}

public enum KnowledgeOrigin { Extracted, Inferred, Declared }
public enum KnowledgeState { Proposed, Accepted, Rejected, Superseded, Reexamine }
public enum RelationKind { Calls, Reads, Writes, Returns, References, Implements, MapsTo, Replaces, Contradicts, DependsOn }
public sealed record TechnicalObject(string Id, string Name, string Kind, string[] EvidenceIds, string? Parent = null, string? SourceId = null, string? Project = null, string? Domain = null, bool Active = true);
public sealed record KnowledgeRelation(string Id, string From, string To, RelationKind Kind, string[] EvidenceIds, KnowledgeOrigin Origin = KnowledgeOrigin.Extracted, KnowledgeState State = KnowledgeState.Proposed);
public sealed record DomainProfile(string Id, string Name, string Responsibility, string[] RelatedDomains);
public sealed record AuthorityRule(string SourceId, string Topic, string Reason);
public sealed record GlossaryEntry(string Term, string[] Aliases, string Definition, string? Domain = null);
public sealed record ProjectProfile(string Id, string Name, string Objective, string Architecture, string[] Systems, string[] Conventions, DomainProfile[] Domains, AuthorityRule[] Authorities, GlossaryEntry[] Glossary);
public sealed record SearchHit(string Id, string Kind, string Title, string Text, string? Project, string? Domain, SourceRole? Role, string[] EvidenceIds, double Score, string[] Reasons, KnowledgeState State, bool Historical = false, KnowledgeOrigin Origin = KnowledgeOrigin.Extracted, SourceStatus? SourceStatus = null, string[]? Conditions = null);
public sealed record SearchQuery(string Text, string? Project = null, string? Domain = null, SourceRole? Role = null, string Intent = "general", int Limit = 20, int Depth = 2, bool IncludeHistorical = false);
public sealed record SearchResponse(string Revision, SearchHit[] Hits, string[] Warnings, double Milliseconds);
public sealed record OpenQuestion(string Id, string Project, string Text, string[] EvidenceIds, string? Answer = null, string? Actor = null);
public sealed record TaskTurn(string Id, string Role, string Text, DateTimeOffset At, string[] Citations);
public sealed record ResearchStep(int Cycle, string Query, string[] ResultIds, double Milliseconds, string[]? SuppliedIds = null, string? RequestHash = null);
public sealed record CoverageItem(string Name, bool Supported, string[] EvidenceIds, string Explanation);
public sealed record WorkTask(string Id, string Project, string Goal, DateTimeOffset Created, List<TaskTurn> Turns, List<ResearchStep> Searches, CoverageItem[] Coverage, SearchHit[] Findings, string? Revision = null, string? ResearchFingerprint = null);
public sealed record ResearchRequest(string Goal, ProjectProfile? Project, SearchHit[] Findings, TaskTurn[] Conversation, CoverageItem[] Coverage, int Cycle, OpenQuestion[]? HumanDeclarations = null);
public sealed record ResearchAdvice(string Answer, string[] Citations, string[] NextQueries, string[] Questions);
public interface IResearchAssistant { Task<ResearchAdvice> Investigate(ResearchRequest request, CancellationToken ct); }
