using Clawbot.Agents.Core.Rag;
using Clawbot.Domain.KnowledgeBase;
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

        var collection = $"kb_v{embedder.Dimension}";
        var records = new List<VectorRecord>(chunks.Count);

        foreach (var (idx, chunk) in chunks.Select((c, i) => (i, c)))
        {
            ct.ThrowIfCancellationRequested();
            var embedding = await embedder.EmbedAsync(chunk, ct).ConfigureAwait(false);
            records.Add(new VectorRecord(
                Id: version.Id.ToString(),
                Embedding: embedding,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["tenant_id"] = tenantId.ToString(),
                    ["module_code"] = moduleCode,
                    ["kb_version_id"] = version.Id.ToString(),
                    ["chunk_index"] = idx.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["snippet"] = Truncate(chunk, 500),
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

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "...";

    [LoggerMessage(EventId = 8001, Level = LogLevel.Information,
        Message = "KB deploy: {ModuleCode} v{Version} → {ChunkCount} chunks into {Collection}")]
    private static partial void LogDeployed(ILogger logger, string moduleCode, int version, int chunkCount, string collection);
}
