using System.Globalization;
using System.Text;
using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Kb;
using Clawbot.Agents.Core.Skills.Ops;
using Microsoft.Extensions.Logging;

namespace Clawbot.Agents.Core.Rag;

// LLM-mode retrieval (mặc định khi tenant KHÔNG cấu hình embedding): đọc ContentMd KB deployed từ SQL,
// chunk in-memory, prefilter từ khóa, rồi 1 lời gọi LLM của chính tenant chọn các đoạn liên quan.
// Thay thế hash-fallback cũ (vector giả, tìm sai âm thầm). Fail-safe: LLM lỗi -> top-K theo từ khóa;
// retrieval KHÔNG bao giờ throw — ném lỗi ở đây là chết cả auto-reply.
public sealed partial class LlmRagRetriever(
    IKbContentReader contentReader,
    IClaudeChatClient claude,
    ILlmCallScope llmScope,
    ILlmCostTracker cost,
    ILogger<LlmRagRetriever> logger) : IRagRetriever
{
    // Cap ứng viên đưa vào prompt: đủ rộng cho KB trung tâm nhỏ, đủ hẹp để 1 call rẻ + nhanh.
    private const int MaxCandidateChunks = 40;
    private const int MaxPromptChars = 12_000;
    private const int MaxChunkChars = 1000;
    // Score tổng hợp: ChatAgent escalate khi max score < 0.35 — đoạn được LLM chọn phải vượt ngưỡng đó.
    private const float TopScore = 0.9f;
    private const float ScoreStep = 0.05f;
    private const float MinScore = 0.5f;
    private const string FallbackAgentCode = "kb-retriever";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IKbContentReader _contentReader = contentReader;
    private readonly IClaudeChatClient _claude = claude;
    private readonly ILlmCallScope _llmScope = llmScope;
    private readonly ILlmCostTracker _cost = cost;
    private readonly ILogger<LlmRagRetriever> _logger = logger;

    private sealed record Candidate(string KbVersionId, string ModuleCode, string Snippet, int KeywordScore);

    public async Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var contents = await _contentReader.GetActiveContentAsync(request.TenantId, request.EffectiveModuleCodes, ct).ConfigureAwait(false);
            if (contents.Count == 0)
                return Array.Empty<RagChunk>();

            var candidates = BuildCandidates(contents, request.Query);
            if (candidates.Count == 0)
                return Array.Empty<RagChunk>();

            try
            {
                var selected = await SelectWithLlmAsync(request, candidates, ct).ConfigureAwait(false);
                LogRetrieved(_logger, request.TenantId, selected.Count, candidates.Count);
                return selected;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // LLM hỏng không được chặn auto-reply: rơi về top-K từ khóa (chỉ đoạn có khớp thật).
                LogLlmSelectFailed(_logger, ex, request.TenantId);
                return KeywordFallback(candidates, request.TopK);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRetrieveFailed(_logger, ex, request.TenantId);
            return Array.Empty<RagChunk>();
        }
    }

    // Chunk toàn bộ ContentMd active rồi prefilter từ khóa về MaxCandidateChunks + cap tổng ký tự.
    private static List<Candidate> BuildCandidates(IReadOnlyList<KbActiveContent> contents, string query)
    {
        var queryTokens = Tokenize(query);
        var all = new List<Candidate>();
        foreach (var content in contents)
        {
            foreach (var chunk in KbDeployService.ChunkContent(content.ContentMd, MaxChunkChars))
            {
                var score = queryTokens.Count == 0 ? 0 : KeywordScore(chunk, queryTokens);
                all.Add(new Candidate(content.KbVersionId, content.ModuleCode, chunk, score));
            }
        }

        // Ưu tiên đoạn khớp từ khóa; giữ nguyên thứ tự tài liệu trong nhóm cùng điểm (đọc tự nhiên hơn).
        var ordered = all.Count <= MaxCandidateChunks
            ? all
            : all.OrderByDescending(c => c.KeywordScore).Take(MaxCandidateChunks).ToList();

        var capped = new List<Candidate>(ordered.Count);
        var totalChars = 0;
        foreach (var candidate in ordered)
        {
            if (totalChars + candidate.Snippet.Length > MaxPromptChars && capped.Count > 0)
                break;
            capped.Add(candidate);
            totalChars += candidate.Snippet.Length;
        }
        return capped;
    }

    private async Task<IReadOnlyList<RagChunk>> SelectWithLlmAsync(RagRequest request, List<Candidate> candidates, CancellationToken ct)
    {
        var system =
            $"Bạn là bộ lọc ngữ cảnh cho trợ lý tư vấn. Cho câu hỏi của khách và danh sách đoạn tài liệu đánh số, " +
            $"chọn tối đa {request.TopK} đoạn chứa thông tin trả lời được câu hỏi. " +
            "Chỉ trả về JSON dạng {\"indexes\":[1,2]} — mảng rỗng nếu không đoạn nào liên quan. Không giải thích gì thêm.";

        var sb = new StringBuilder(MaxPromptChars + 256);
        sb.AppendLine(CultureInfo.InvariantCulture, $"Câu hỏi của khách: {request.Query}");
        sb.AppendLine();
        sb.AppendLine("Các đoạn tài liệu:");
        for (var i = 0; i < candidates.Count; i++)
            sb.AppendLine(CultureInfo.InvariantCulture, $"[{i + 1}] {candidates[i].Snippet}");

        // Callers (ChatAgent/SaleAssist/KbTestRunner...) đã Begin scope; không có thì tự mở với code riêng.
        var ambient = _llmScope.Current;
        using var _ = ambient is null ? _llmScope.Begin(request.TenantId, FallbackAgentCode) : null;

        var reply = await _claude.CompleteAsync(system, Array.Empty<ChatTurn>(), sb.ToString(), ct).ConfigureAwait(false);

        var scope = _llmScope.Current;
        await _cost.RecordAsync(new CostEntry(
            request.TenantId, scope?.AgentCode ?? FallbackAgentCode, reply.Model,
            reply.InputTokens, reply.OutputTokens, reply.UsdCost,
            scope?.CostAt ?? DateTimeOffset.UtcNow,
            scope?.ReservationId, scope?.SessionId, reply.IsEstimated), ct).ConfigureAwait(false);

        var indexes = ParseIndexes(reply.Text);
        var results = new List<RagChunk>(Math.Min(indexes.Count, request.TopK));
        foreach (var index in indexes)
        {
            if (index < 1 || index > candidates.Count) continue;
            var candidate = candidates[index - 1];
            var score = Math.Max(MinScore, TopScore - ScoreStep * results.Count);
            results.Add(new RagChunk(candidate.KbVersionId, candidate.ModuleCode, candidate.Snippet, score));
            if (results.Count >= request.TopK) break;
        }
        return results;
    }

    // Chấp nhận {"indexes":[...]} hoặc mảng trần [...]; model hay bọc thêm text/code-fence nên cắt
    // từ ký tự JSON đầu tiên. Parse hỏng -> throw để caller rơi về keyword fallback.
    internal static List<int> ParseIndexes(string replyText)
    {
        var text = replyText.Trim();
        var objStart = text.IndexOf('{', StringComparison.Ordinal);
        var arrStart = text.IndexOf('[', StringComparison.Ordinal);

        if (objStart >= 0 && (arrStart < 0 || objStart < arrStart))
        {
            var objEnd = text.LastIndexOf('}');
            var parsed = JsonSerializer.Deserialize<IndexEnvelope>(text[objStart..(objEnd + 1)], JsonOpts);
            return parsed?.Indexes?.ToList() ?? new List<int>();
        }

        if (arrStart >= 0)
        {
            var arrEnd = text.LastIndexOf(']');
            return JsonSerializer.Deserialize<List<int>>(text[arrStart..(arrEnd + 1)], JsonOpts) ?? new List<int>();
        }

        throw new JsonException("LLM reply contained no JSON indexes.");
    }

    private sealed record IndexEnvelope(List<int>? Indexes);

    private static List<RagChunk> KeywordFallback(List<Candidate> candidates, int topK) =>
        candidates
            .Where(c => c.KeywordScore > 0)
            .OrderByDescending(c => c.KeywordScore)
            .Take(topK)
            .Select((c, i) => new RagChunk(c.KbVersionId, c.ModuleCode, c.Snippet, Math.Max(MinScore, TopScore - ScoreStep * i)))
            .ToList();

    private static int KeywordScore(string chunk, IReadOnlyList<string> queryTokens)
    {
        var normalized = Normalize(chunk);
        var score = 0;
        foreach (var token in queryTokens)
        {
            var idx = 0;
            while ((idx = normalized.IndexOf(token, idx, StringComparison.Ordinal)) >= 0)
            {
                score++;
                idx += token.Length;
            }
        }
        return score;
    }

    internal static IReadOnlyList<string> Tokenize(string query) =>
        Normalize(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    // Lowercase + bỏ dấu tiếng Việt (kể cả đ→d) + gom mọi ký tự khác chữ/số thành khoảng trắng.
    internal static string Normalize(string text)
    {
        var formD = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            if (ch is 'đ') { sb.Append('d'); continue; }
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    [LoggerMessage(EventId = 7010, Level = LogLevel.Information,
        Message = "LLM retrieval for tenant {TenantId}: selected {Selected} of {Candidates} KB chunks")]
    private static partial void LogRetrieved(ILogger logger, Guid tenantId, int selected, int candidates);

    [LoggerMessage(EventId = 7011, Level = LogLevel.Warning,
        Message = "LLM retrieval selection failed for tenant {TenantId}; falling back to keyword ranking")]
    private static partial void LogLlmSelectFailed(ILogger logger, Exception ex, Guid tenantId);

    [LoggerMessage(EventId = 7012, Level = LogLevel.Warning,
        Message = "LLM retrieval failed for tenant {TenantId}; returning no chunks")]
    private static partial void LogRetrieveFailed(ILogger logger, Exception ex, Guid tenantId);
}
