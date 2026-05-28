namespace Clawbot.Agents.Core.Skills.Lead;

public sealed record DedupCandidate(Guid CandidateContactId, float Similarity);

public sealed record DedupQuery(string DisplayName, string? Phone, string? Email, IReadOnlyDictionary<string, string> ExternalIds);

public interface ILeadDeduplicator : ISkill
{
    Task<IReadOnlyList<DedupCandidate>> FindCandidatesAsync(Guid tenantId, DedupQuery query, int topK, CancellationToken ct);
}

// Source: Qdrant cosine similarity over contact embeddings (display_name + phone + email).
// Stitching cross-platform identity beyond exact (platform, external_id) match.
internal sealed class QdrantLeadDeduplicator : ILeadDeduplicator
{
    public string Name => "lead-deduplication";

    public Task<IReadOnlyList<DedupCandidate>> FindCandidatesAsync(Guid tenantId, DedupQuery query, int topK, CancellationToken ct) =>
        throw new NotImplementedException("TODO: embed query via SK + Qdrant search collection 'contacts'.");
}
