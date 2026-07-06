using System.Text;
using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawbot.Agents.Core.Research;

// Cham diem xu huong bang kho tri thuc + LLM thay vi keyword hardcode:
//   1) Qdrant (qua IRagRetriever): embed topic, do similarity voi KB cua tenant -> tin hieu "lien quan he thong".
//   2) LLM: 1 call duy nhat cham relevant/score 0-10 + sinh y tuong noi dung tieng Viet cho tung trend.
//   3) Fallback: host/tenant chua cau hinh LLM/embedding hoac loi -> WeightedTrendScorer (heuristic cu),
//      de weekly scan khong bao gio chet vi scorer.
public sealed partial class SemanticLlmTrendScorer(
    IRagRetriever? rag = null,
    IClaudeChatClient? claude = null,
    ILogger<SemanticLlmTrendScorer>? logger = null) : ITrendRelevanceScorer
{
    private const int SemanticConcurrency = 4;
    private const int RagTopK = 3;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IRagRetriever? _rag = rag;
    private readonly IClaudeChatClient? _claude = claude;
    private readonly ILogger<SemanticLlmTrendScorer> _logger = logger ?? NullLogger<SemanticLlmTrendScorer>.Instance;
    private readonly WeightedTrendScorer _fallback = new();

    public async Task<IReadOnlyList<ScoredTrend>> ScoreAsync(
        Guid tenantId,
        IReadOnlyList<RawTrend> trends,
        IReadOnlyList<string> keywords,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trends);
        ArgumentNullException.ThrowIfNull(keywords);

        if (trends.Count == 0)
            return [];

        if (_claude is null)
            return await _fallback.ScoreAsync(tenantId, trends, keywords, ct).ConfigureAwait(false);

        var similarities = await ComputeKbSimilaritiesAsync(tenantId, trends, ct).ConfigureAwait(false);

        try
        {
            var prompt = BuildPrompt(trends, keywords, similarities);
            var reply = await _claude.CompleteAsync(SystemPrompt, [], prompt, ct).ConfigureAwait(false);
            var verdicts = ParseVerdicts(reply.Text);
            if (verdicts.Count == 0)
            {
                LogEmptyVerdicts(_logger, tenantId);
                return await _fallback.ScoreAsync(tenantId, trends, keywords, ct).ConfigureAwait(false);
            }

            var scored = new List<ScoredTrend>();
            foreach (var verdict in verdicts)
            {
                if (!verdict.Relevant || verdict.I < 0 || verdict.I >= trends.Count)
                    continue;

                var trend = trends[verdict.I];
                var sim = similarities[verdict.I];
                var ideas = verdict.Ideas is { Count: > 0 }
                    ? verdict.Ideas
                    : trend.ContentIdeas;
                // LLM la nguon diem chinh (0-10 -> 0-100); similarity KB cong them de xep hang trong cung muc
                var score = Math.Round(Math.Clamp(verdict.Score, 0d, 10d) * 10d + sim * 5d, 4);
                scored.Add(new ScoredTrend(trend.Topic, trend.Source, trend.Metric, score, ideas));
            }

            LogScored(_logger, tenantId, trends.Count, scored.Count);
            return scored;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Gom ca LlmConfigNotConfiguredException: tenant chua bind LLM -> heuristic van chay
            LogLlmFallback(_logger, ex, tenantId);
            return await _fallback.ScoreAsync(tenantId, trends, keywords, ct).ConfigureAwait(false);
        }
    }

    // Similarity topic <-> KB qua Qdrant; loi/thieu cau hinh -> 0 (LLM van cham duoc)
    private async Task<double[]> ComputeKbSimilaritiesAsync(
        Guid tenantId,
        IReadOnlyList<RawTrend> trends,
        CancellationToken ct)
    {
        var sims = new double[trends.Count];
        if (_rag is null)
            return sims;

        using var gate = new SemaphoreSlim(SemanticConcurrency);
        var tasks = trends.Select(async (trend, idx) =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var chunks = await _rag.RetrieveAsync(
                    new RagRequest(tenantId, KbModuleCode: null, trend.Topic, RagTopK), ct).ConfigureAwait(false);
                sims[idx] = chunks.Count == 0 ? 0d : chunks.Max(c => (double)c.Score);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRagFailed(_logger, ex, tenantId, trend.Topic);
            }
            finally
            {
                gate.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return sims;
    }

    private const string SystemPrompt =
        "Bạn là bộ lọc xu hướng nội dung cho hệ thống marketing của một trung tâm dạy tiếng Trung tại Việt Nam. " +
        "Chỉ trả về JSON hợp lệ, không kèm giải thích.";

    private static string BuildPrompt(
        IReadOnlyList<RawTrend> trends,
        IReadOnlyList<string> keywords,
        double[] similarities)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Chủ đề hệ thống đang quan tâm (từ kho tri thức): " +
            (keywords.Count == 0 ? "học tiếng Trung, HSK" : string.Join(", ", keywords)));
        sb.AppendLine();
        sb.AppendLine("Danh sách xu hướng đang hot (kèm độ tương đồng 0-1 với kho tri thức):");
        for (var i = 0; i < trends.Count; i++)
        {
            sb.AppendLine(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{i}. \"{trends[i].Topic}\" | traffic: {trends[i].Metric} | similarity: {similarities[i]:0.00}"));
        }
        sb.AppendLine();
        sb.AppendLine("Với TỪNG xu hướng, đánh giá mức liên quan tới việc làm nội dung marketing cho hệ thống. " +
            "Trả về JSON array, mỗi phần tử: {\"i\":<số thứ tự>,\"relevant\":true|false,\"score\":0-10,\"ideas\":[\"ý tưởng nội dung tiếng Việt\",\"...\"]}. " +
            "relevant=false cho xu hướng không thể khai thác; ideas chỉ cần cho xu hướng relevant, tối đa 2 ý tưởng.");
        return sb.ToString();
    }

    private static List<LlmTrendVerdict> ParseVerdicts(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var json = text.Trim();
        // LLM hay boc ```json ... ``` — cat fence truoc khi parse
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = json.IndexOf('\n', StringComparison.Ordinal);
            var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                json = json[(firstNewline + 1)..lastFence].Trim();
        }

        try
        {
            return JsonSerializer.Deserialize<List<LlmTrendVerdict>>(json, JsonOpts) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record LlmTrendVerdict(int I, bool Relevant, double Score, List<string>? Ideas);

    [LoggerMessage(EventId = 5401, Level = LogLevel.Information,
        Message = "Semantic trend scoring for tenant {TenantId}: {RawCount} raw -> {ScoredCount} relevant")]
    private static partial void LogScored(ILogger logger, Guid tenantId, int rawCount, int scoredCount);

    [LoggerMessage(EventId = 5402, Level = LogLevel.Warning,
        Message = "LLM trend scoring failed for tenant {TenantId} - falling back to keyword heuristic")]
    private static partial void LogLlmFallback(ILogger logger, Exception ex, Guid tenantId);

    [LoggerMessage(EventId = 5403, Level = LogLevel.Warning,
        Message = "KB similarity lookup failed for tenant {TenantId} topic {Topic}")]
    private static partial void LogRagFailed(ILogger logger, Exception ex, Guid tenantId, string topic);

    [LoggerMessage(EventId = 5404, Level = LogLevel.Warning,
        Message = "LLM returned no parseable trend verdicts for tenant {TenantId} - falling back to keyword heuristic")]
    private static partial void LogEmptyVerdicts(ILogger logger, Guid tenantId);
}
