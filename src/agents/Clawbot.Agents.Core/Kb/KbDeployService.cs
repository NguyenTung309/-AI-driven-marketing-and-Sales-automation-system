using Clawbot.Agents.Core.Rag;
using Clawbot.Domain.KnowledgeBase;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clawbot.SharedKernel.Vectors;
using Microsoft.Extensions.Logging;

namespace Clawbot.Agents.Core.Kb;

public sealed partial class KbDeployService(
    IEmbeddingProvider embedder,
    IVectorStore store,
    ILogger<KbDeployService> logger)
{
    public async Task<int> EmbedAndUpsertAsync(KbVersion version, string moduleCode, Guid tenantId, CancellationToken ct)
    {
        var chunks = ChunkContent(version.ContentMd);
        if (chunks.Count == 0) return 0;

        var records = new List<VectorRecord>(chunks.Count);

        var embeddingConfig = await ResolveConfigAsync(tenantId, ct).ConfigureAwait(false);
        var versionEmbedding = await EmbedAsync(embeddingConfig, tenantId, version.ContentMd, ct).ConfigureAwait(false);
        var collection = ConfiguredEmbeddingProvider.CollectionName(embeddingConfig);
        version.SetEmbeddingJson(JsonSerializer.Serialize(versionEmbedding.ToArray()));

        foreach (var (idx, chunk) in chunks.Select((c, i) => (i, c)))
        {
            ct.ThrowIfCancellationRequested();
            var embedding = await EmbedAsync(embeddingConfig, tenantId, chunk, ct).ConfigureAwait(false);
            records.Add(new VectorRecord(
                Id: ChunkPointId(version.Id, idx),
                Embedding: embedding,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["tenant_id"] = tenantId.ToString(),
                    ["module_code"] = moduleCode,
                    ["kb_version_id"] = version.Id.ToString(),
                    ["chunk_index"] = idx.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    // Store the full chunk as the RAG context snippet. Truncating below the chunk size
                    // dropped later-in-chunk facts (payment schedule, bank details) from the answer context.
                    ["snippet"] = chunk,
                }));
        }

        await store.UpsertAsync(collection, records, ct).ConfigureAwait(false);
        LogDeployed(logger, moduleCode, version.Version, records.Count, collection);
        return records.Count;
    }

    internal static List<string> ChunkContent(string contentMd, int maxChunkChars = 1000)
    {
        if (string.IsNullOrWhiteSpace(contentMd)) return [];

        var chunks = new List<string>();
        var paragraphs = contentMd.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);

        var current = new System.Text.StringBuilder();
        foreach (var para in paragraphs)
        {
            if (current.Length + para.Length > maxChunkChars && current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }
            current.AppendLine(para.Trim());
        }
        if (current.Length > 0)
            chunks.Add(current.ToString().Trim());

        return chunks;
    }

    internal static string ChunkPointId(Guid versionId, int chunkIndex)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{versionId:N}:{chunkIndex}"));
        return new Guid(bytes.AsSpan(0, 16)).ToString();
    }

    private async Task<ResolvedEmbeddingConfig> ResolveConfigAsync(Guid tenantId, CancellationToken ct) =>
        embedder is ConfiguredEmbeddingProvider configured
            ? await configured.ResolveConfigAsync(tenantId, ct).ConfigureAwait(false)
            : new ResolvedEmbeddingConfig("runtime", $"dim-{embedder.Dimension}", null, null, embedder.Dimension, "runtime");

    private Task<ReadOnlyMemory<float>> EmbedAsync(ResolvedEmbeddingConfig config, Guid tenantId, string text, CancellationToken ct) =>
        embedder is ConfiguredEmbeddingProvider configured
            ? configured.EmbedAsync(config, text, ct)
            : embedder.EmbedAsync(text, ct);

    [LoggerMessage(EventId = 8001, Level = LogLevel.Information,
        Message = "KB deploy: {ModuleCode} v{Version} → {ChunkCount} chunks into {Collection}")]
    private static partial void LogDeployed(ILogger logger, string moduleCode, int version, int chunkCount, string collection);
}
