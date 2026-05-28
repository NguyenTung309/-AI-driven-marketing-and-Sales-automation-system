using Clawbot.SharedKernel.Vectors;

namespace Clawbot.Agents.Core.Rag;

/// <summary>
/// Pulls top-K snippets from the `kb_versions` Qdrant collection. Filtering by
/// tenant + module code is done client-side over payload until we add Qdrant
/// payload filters in M10. Source-of-truth content remains in `kb_versions.content_md`.
/// </summary>
public sealed class QdrantRagRetriever(IVectorStore store, IEmbeddingProvider embedder) : IRagRetriever
{
    public const string Collection = "kb_versions";

    public async Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) return Array.Empty<RagChunk>();

        var queryVec = await embedder.EmbedAsync(request.Query, ct).ConfigureAwait(false);
        var hits = await store.SearchAsync(Collection, queryVec, request.TopK * 4, ct).ConfigureAwait(false);

        var tenantTag = request.TenantId.ToString();
        var filtered = hits
            .Where(h => h.Metadata.TryGetValue("tenant_id", out var t) && string.Equals(t, tenantTag, StringComparison.Ordinal))
            .Where(h => string.IsNullOrEmpty(request.KbModuleCode)
                || (h.Metadata.TryGetValue("module_code", out var m) && string.Equals(m, request.KbModuleCode, StringComparison.Ordinal)))
            .Take(request.TopK)
            .Select(h => new RagChunk(
                KbVersionId: h.Id,
                KbModuleCode: h.Metadata.TryGetValue("module_code", out var mc) ? mc : string.Empty,
                Snippet: h.Metadata.TryGetValue("snippet", out var s) ? s : string.Empty,
                Score: h.Score))
            .ToList();

        return filtered;
    }
}
