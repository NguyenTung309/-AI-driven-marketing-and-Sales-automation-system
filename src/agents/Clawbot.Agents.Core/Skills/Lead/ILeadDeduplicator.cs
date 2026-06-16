using System.Globalization;
using Clawbot.Agents.Core.Rag;
using Clawbot.SharedKernel.Vectors;

namespace Clawbot.Agents.Core.Skills.Lead;

public sealed record DedupCandidate(Guid CandidateContactId, float Similarity);

public sealed record DedupQuery(string DisplayName, string? Phone, string? Email, IReadOnlyDictionary<string, string> ExternalIds);

public interface ILeadDeduplicator : ISkill
{
    Task<IReadOnlyList<DedupCandidate>> FindCandidatesAsync(Guid tenantId, DedupQuery query, int topK, CancellationToken ct);
}

// Qdrant cosine similarity over contact embeddings (display_name + phone-tail + email).
// Layers on top of EfLeadDedupService (exact phone/email match) — this handles fuzzy.
internal sealed class QdrantLeadDeduplicator : ILeadDeduplicator
{
    // Versioned by embedder dimension — must match ContactEmbeddingSync's collection name.
    private readonly string _collection;
    private readonly IEmbeddingProvider _embedding;
    private readonly IVectorStore _store;

    public QdrantLeadDeduplicator(IEmbeddingProvider embedding, IVectorStore store)
    {
        _embedding = embedding;
        _store = store;
        _collection = $"contacts_v{embedding.Dimension}";
    }

    public string Name => "lead-deduplication";

    public async Task<IReadOnlyList<DedupCandidate>> FindCandidatesAsync(
        Guid tenantId, DedupQuery query, int topK, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var key = BuildKeyString(query);
        if (string.IsNullOrWhiteSpace(key))
            return Array.Empty<DedupCandidate>();

        var embedding = await _embedding.EmbedAsync(key, ct).ConfigureAwait(false);
        var matches = await _store.SearchAsync(_collection, embedding, topK * 4, ct).ConfigureAwait(false);

        var candidates = new List<DedupCandidate>();
        foreach (var m in matches)
        {
            if (!m.Metadata.TryGetValue("tenant_id", out var tid) ||
                !string.Equals(tid, tenantId.ToString("D", CultureInfo.InvariantCulture), StringComparison.Ordinal))
                continue;

            if (m.Score < 0.70f) continue;

            if (m.Metadata.TryGetValue("contact_id", out var cid) &&
                Guid.TryParse(cid, out var contactId))
            {
                candidates.Add(new DedupCandidate(contactId, m.Score));
            }

            if (candidates.Count >= topK) break;
        }

        return candidates;
    }

    private static string BuildKeyString(DedupQuery q)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(q.DisplayName)) parts.Add(q.DisplayName);
        if (!string.IsNullOrWhiteSpace(q.Phone))
        {
            // Normalize: last 7 digits for matching
            var digits = new string(q.Phone.Where(char.IsDigit).ToArray());
            parts.Add(digits.Length >= 7 ? digits[^7..] : digits);
        }
        if (!string.IsNullOrWhiteSpace(q.Email)) parts.Add(q.Email);
        return string.Join(" | ", parts);
    }
}
