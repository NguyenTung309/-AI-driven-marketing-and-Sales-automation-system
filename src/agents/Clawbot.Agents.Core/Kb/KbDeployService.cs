using Clawbot.Agents.Core.Rag;
using Clawbot.Domain.KnowledgeBase;
using System.ClientModel;
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
        var collection = ConfiguredEmbeddingProvider.CollectionName(embeddingConfig);

        foreach (var (idx, chunk) in chunks.Select((c, i) => (i, c)))
        {
            ct.ThrowIfCancellationRequested();
            var embedding = await EmbedWithRetryAsync(embeddingConfig, tenantId, chunk, ct).ConfigureAwait(false);
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

        // Chỉ mutate entity SAU khi mọi lệnh gọi bên ngoài (embed + upsert) đã thành công.
        // Mutate sớm mà embed lỗi giữa chừng sẽ để lại entity dirty trong DbContext đang tracking —
        // một SaveChanges bất kỳ sau đó flush trạng thái dở dang xuống DB.
        // Embedding đại diện cả bản = centroid (trung bình cộng) embedding các chunk. KHÔNG embed
        // nguyên văn tài liệu trong 1 lượt gọi: văn bản dài vượt hạn mức input của model embedding
        // làm deploy fail ngay từ lượt đầu (đã dính với tài liệu ~43k ký tự), lại tốn thêm 1 lượt.
        version.SetEmbeddingJson(JsonSerializer.Serialize(MeanVector(records)));
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
            // Một đoạn văn đơn lẻ có thể tự nó dài hơn maxChunkChars (văn bản liền mạch không xuống
            // dòng kép). Nếu để nguyên, chunk vượt hạn mức token của model embedding → deploy fail
            // (đã dính với đoạn 15955 ký tự > 8192 token). Cắt cứng những đoạn quá dài thành nhiều mảnh.
            foreach (var piece in SplitOversized(para.Trim(), maxChunkChars))
            {
                if (current.Length + piece.Length > maxChunkChars && current.Length > 0)
                {
                    chunks.Add(current.ToString().Trim());
                    current.Clear();
                }
                current.AppendLine(piece);
            }
        }
        if (current.Length > 0)
            chunks.Add(current.ToString().Trim());

        return chunks;
    }

    // Cắt một đoạn dài hơn maxChunkChars thành nhiều mảnh, ưu tiên ranh giới khoảng trắng gần cuối
    // mảnh để không cắt giữa từ. Đoạn ngắn trả về nguyên vẹn (mảnh đơn).
    private static IEnumerable<string> SplitOversized(string text, int maxChunkChars)
    {
        if (text.Length <= maxChunkChars)
        {
            if (text.Length > 0) yield return text;
            yield break;
        }

        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(maxChunkChars, text.Length - start);
            // Nếu chưa tới cuối chuỗi, lùi về khoảng trắng cuối cùng trong mảnh để cắt gọn theo từ.
            if (start + length < text.Length)
            {
                var lastSpace = text.LastIndexOf(' ', start + length - 1, length);
                if (lastSpace > start) length = lastSpace - start;
            }

            var piece = text.Substring(start, length).Trim();
            if (piece.Length > 0) yield return piece;
            start += length;
        }
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

    // Provider free (OpenRouter :free) giới hạn ~20 req/phút — tài liệu dài cần 40+ lượt embed nên
    // 429 giữa chừng là chuyện thường. Backoff tuyến tính đủ dài để cửa sổ rate limit kịp hồi;
    // lỗi không transient (key sai, model không tồn tại, input quá dài) ném ngay khỏi chờ vô ích.
    private const int MaxEmbedAttempts = 4;
    private static readonly TimeSpan EmbedRetryBaseDelay = TimeSpan.FromSeconds(8);

    private async Task<ReadOnlyMemory<float>> EmbedWithRetryAsync(
        ResolvedEmbeddingConfig config, Guid tenantId, string text, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await EmbedAsync(config, tenantId, text, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                attempt < MaxEmbedAttempts && ex is not OperationCanceledException && IsTransientEmbedError(ex))
            {
                LogEmbedRetry(logger, attempt, MaxEmbedAttempts, ex.Message);
                await Task.Delay(EmbedRetryBaseDelay * attempt, ct).ConfigureAwait(false);
            }
        }
    }

    // 429/5xx là transient. Đường SDK ném ClientResultException có Status; đường multimodal fallback
    // ném InvalidOperationException với thông điệp chứa "HTTP <code>" — soi mã ngay sau marker đó.
    internal static bool IsTransientEmbedError(Exception ex)
    {
        if (ex is ClientResultException client) return client.Status == 429 || client.Status >= 500;
        if (ex is HttpRequestException) return true;

        var message = ex.Message;
        var marker = message.IndexOf("HTTP ", StringComparison.Ordinal);
        if (marker < 0) return false;
        var codeSpan = message.AsSpan(marker + 5);
        var digits = 0;
        while (digits < codeSpan.Length && char.IsAsciiDigit(codeSpan[digits])) digits++;
        return digits > 0 && int.TryParse(codeSpan[..digits], out var code) && (code == 429 || code >= 500);
    }

    // Centroid các chunk làm embedding đại diện bản — cùng số chiều với chunk, đủ tốt cho so khớp thô.
    internal static float[] MeanVector(IReadOnlyList<VectorRecord> records)
    {
        var dimension = records[0].Embedding.Length;
        var sum = new double[dimension];
        foreach (var record in records)
        {
            var span = record.Embedding.Span;
            var limit = Math.Min(dimension, span.Length);
            for (var i = 0; i < limit; i++) sum[i] += span[i];
        }

        var mean = new float[dimension];
        for (var i = 0; i < dimension; i++) mean[i] = (float)(sum[i] / records.Count);
        return mean;
    }

    [LoggerMessage(EventId = 8001, Level = LogLevel.Information,
        Message = "KB deploy: {ModuleCode} v{Version} → {ChunkCount} chunks into {Collection}")]
    private static partial void LogDeployed(ILogger logger, string moduleCode, int version, int chunkCount, string collection);

    [LoggerMessage(EventId = 8002, Level = LogLevel.Warning,
        Message = "KB embed transient error (attempt {Attempt}/{MaxAttempts}): {Reason} — retrying")]
    private static partial void LogEmbedRetry(ILogger logger, int attempt, int maxAttempts, string reason);
}
