namespace BipKb;

/// <summary>Adapter-only references; persisted requests, evidence and replies retain canonical IDs.</summary>
public sealed class ExplorationCitations
{
    private readonly ExplorationRequest original;
    private readonly Dictionary<string,string> evidence;
    private readonly Dictionary<string,string> claims;
    public ExplorationRequest Request {get;}
    public ExplorationCitations(ExplorationRequest request)
    {
        original=request;
        evidence=request.Visible.ToDictionary(v=>"u"+v.Number,v=>v.Unit.Id,StringComparer.Ordinal);
        // Stable claim aliases avoid changing c1's meaning when retrieved context changes between turns.
        claims=request.Context.Existing.ToDictionary(c=>"c"+Json.Hash(c.Id)[..12],c=>c.Id,StringComparer.Ordinal);
        var claimAliases=claims.ToDictionary(p=>p.Value,p=>p.Key,StringComparer.Ordinal);
        Request=request with
        {
            Visible=request.Visible.Select(v=>v with{Unit=v.Unit with{Id="u"+v.Number}}).ToArray(),
            Context=request.Context with
            {
                Units=[],
                Existing=request.Context.Existing.Select(c=>c with{Id=claimAliases[c.Id],EvidenceIds=[],RelatedClaimIds=c.RelatedClaimIds.Where(claimAliases.ContainsKey).Select(id=>claimAliases[id]).ToArray()}).ToArray(),
                Decisions=request.Context.Decisions.Where(d=>claimAliases.ContainsKey(d.ClaimId)).Select(d=>d with{ClaimId=claimAliases[d.ClaimId],EvidenceFingerprint=""}).ToArray(),
                HumanDeclarations=request.Context.HumanDeclarations?.Select(q=>q with{EvidenceIds=[]}).ToArray()
            }
        };
    }
    public ExplorationReply Restore(ExplorationReply reply)
    {
        // No prefix matching, guessed citation or fallback to an existing claim's evidence.
        DocumentExplorer.Validate(reply,Request);
        var restored=reply with
        {
            Findings=reply.Findings.Select(f=>f with{EvidenceIds=f.EvidenceIds.Select(id=>evidence[id]).ToArray(),RelatedClaimIds=f.RelatedClaimIds.Select(id=>claims[id]).ToArray()}).ToList(),
            Reviews=reply.Reviews.Select(r=>r with{UnitId=evidence[r.UnitId]}).ToArray()
        };
        DocumentExplorer.Validate(restored,original);
        return restored;
    }
}
