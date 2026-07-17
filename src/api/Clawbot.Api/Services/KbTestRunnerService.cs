using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Api.Contracts.KnowledgeBase;
using Clawbot.Domain.KnowledgeBase;

namespace Clawbot.Api.Services;

internal sealed record KbClaudeEvaluation(bool Passed, string? Reason);

internal sealed record KbGeneratedCase(string Question, string ExpectedAnswer);

internal sealed class KbTestRunnerService(IRagRetriever rag, IClaudeChatClient claude, ILlmCallScope llmScope)
{
    private const string AgentCode = "chat-agent";

    // Mã máy-đọc-được khi RAG không trả về chunk nào: kho vector không có dữ liệu cho module/bản
    // đang chấm (thường do bước embed lúc deploy đã lỗi). Runner dùng mã này để phân biệt
    // "AI trả lời sai" với "hạ tầng thiếu dữ liệu" — toàn bộ case dính mã này thì báo lỗi thay vì ghi 0%.
    public const string NoContextReason = "no_context_retrieved";

    // ponytail: trần nội dung nạp cho bộ sinh Q&A. Nâng từ 8k -> 24k để tài liệu dài không mất phần đuôi
    // (bộ test phải phủ được cả tài liệu). Vẫn kẹp để không nổ token trên tài liệu cực lớn.
    private const int MaxGeneratePromptChars = 24000;

    // Sinh theo lô nhỏ: mỗi lượt chỉ xin ~8 câu để output không quá dài — xin nhiều câu trong 1 lượt
    // làm output vượt giới hạn và provider báo "stream reported failure". Nhiều lô gộp lại vẫn phủ tốt.
    private const int GenerateBatchSize = 8;

    // Số câu đã tạo liệt kê ngược cho lô sau tránh trùng — kẹp để prompt không phình.
    private const int MaxAvoidListed = 24;

    private const string GenerateSystemPrompt =
        "You are a QA engineer building test cases for a knowledge base. " +
        "Only use facts present in the provided content.";

    private readonly IRagRetriever _rag = rag;
    private readonly IClaudeChatClient _claude = claude;
    private readonly ILlmCallScope _llmScope = llmScope;

    // Ask the LLM to author grounded Q&A pairs straight from the KB content. Used to seed the
    // "Kiểm thử Q&A" set so operators don't have to hand-write every case.
    public async Task<IReadOnlyList<KbGeneratedCase>> GenerateCasesAsync(
        Guid tenantId,
        string contentMd,
        int count,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contentMd) || count <= 0) return [];

        using var _llm = _llmScope.Begin(tenantId, AgentCode);
        var content = contentMd.Length > MaxGeneratePromptChars
            ? contentMd[..MaxGeneratePromptChars]
            : contentMd;

        var collected = new List<KbGeneratedCase>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxBatches = (count + GenerateBatchSize - 1) / GenerateBatchSize + 3;

        // Gộp nhiều lô nhỏ + bỏ trùng, hướng lô sau tránh câu đã có. Dừng khi đủ số lượng,
        // 2 lô liền không ra câu mới, hoặc chạm trần số lô (chặn lặp vô hạn).
        for (int attempt = 0, emptyStreak = 0; collected.Count < count && emptyStreak < 2 && attempt < maxBatches; attempt++)
        {
            var batch = Math.Min(GenerateBatchSize, count - collected.Count);
            IReadOnlyList<KbGeneratedCase> parsed;
            try
            {
                var reply = await _claude.CompleteAsync(
                    GenerateSystemPrompt, Array.Empty<ChatTurn>(),
                    BuildGeneratePrompt(content, batch, collected), ct).ConfigureAwait(false);
                parsed = ParseGeneratedCases(reply.Text);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Một lô hỏng (vd lỗi stream) không nên xoá sạch thành quả — dừng, trả phần đã sinh được.
                break;
            }

            var fresh = 0;
            foreach (var c in parsed)
            {
                if (!seen.Add(c.Question)) continue;
                collected.Add(c);
                fresh++;
                if (collected.Count >= count) break;
            }
            emptyStreak = fresh == 0 ? emptyStreak + 1 : 0;
        }

        return collected;
    }

    private static string BuildGeneratePrompt(string content, int count, List<KbGeneratedCase> already)
    {
        var avoid = string.Empty;
        if (already.Count > 0)
        {
            var recent = already.Skip(Math.Max(0, already.Count - MaxAvoidListed)).Select(a => $"- {a.Question}");
            avoid = "\n\nCác câu hỏi ĐÃ tạo (KHÔNG lặp lại — hãy hỏi về dữ kiện/khía cạnh KHÁC):\n" + string.Join('\n', recent);
        }

        return $"Knowledge base content:\n{content}\n\n" +
            $"Generate {count} realistic customer questions a sales/support agent would receive, " +
            "each with the correct answer grounded ONLY in the content above. " +
            "COVERAGE IS THE GOAL: spread the questions across the ENTIRE document so they touch as many " +
            "DISTINCT facts, sections and details as possible — do NOT cluster them on the opening section, " +
            "and do NOT ask two questions about the same fact. Include specifics (numbers, prices, dates, " +
            "conditions, exceptions) when the content has them. Use the same language as the content." +
            avoid +
            "\nReply with ONLY a JSON array: [{\"question\":\"...\",\"expectedAnswer\":\"...\"}]";
    }

    public static IReadOnlyList<KbGeneratedCase> ParseGeneratedCases(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return [];

        try
        {
            using var doc = JsonDocument.Parse(ExtractJsonArray(responseText));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var cases = new List<KbGeneratedCase>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var question = item.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.String
                    ? q.GetString() : null;
                var answer = item.TryGetProperty("expectedAnswer", out var a) && a.ValueKind == JsonValueKind.String
                    ? a.GetString() : null;
                if (!string.IsNullOrWhiteSpace(question) && !string.IsNullOrWhiteSpace(answer))
                    cases.Add(new KbGeneratedCase(question.Trim(), answer.Trim()));
            }

            return cases;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task<KbTestCaseResult> EvaluateAsync(
        Guid tenantId,
        string moduleCode,
        KbTestCase testCase,
        CancellationToken ct)
    {
        using var _llm = _llmScope.Begin(tenantId, AgentCode);
        var chunks = await _rag.RetrieveAsync(
            new RagRequest(tenantId, moduleCode, testCase.Question, 3), ct).ConfigureAwait(false);

        // Không có chunk nào thì fail thẳng — hỏi LLM trên context rỗng vừa tốn 1 lượt gọi
        // vừa che mất nguyên nhân thật (kho vector trống, không phải nội dung sai).
        if (chunks.Count == 0)
            return new KbTestCaseResult(testCase.Id, testCase.Question, false, NoContextReason);

        var context = string.Join("\n---\n", chunks.Select(c => c.Snippet));
        var evalPrompt = $"Context:\n{context}\n\nQuestion: {testCase.Question}\n" +
            $"Expected answer: {testCase.ExpectedAnswer}\n\n" +
            "Does the context contain information to answer the question correctly? " +
            "Reply with only JSON: {\"passed\":true/false,\"reason\":\"...\"}";

        var reply = await _claude.CompleteAsync(
            "You are a KB accuracy evaluator. Check if the retrieved context supports the expected answer.",
            Array.Empty<ChatTurn>(), evalPrompt, ct).ConfigureAwait(false);

        var evaluation = ParseClaudeEvaluation(reply.Text);
        return new KbTestCaseResult(testCase.Id, testCase.Question, evaluation.Passed, evaluation.Reason);
    }

    public static KbClaudeEvaluation ParseClaudeEvaluation(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return new KbClaudeEvaluation(false, "empty_evaluator_response");

        try
        {
            using var doc = JsonDocument.Parse(ExtractJsonObject(responseText));
            var root = doc.RootElement;
            var passed = root.TryGetProperty("passed", out var passedElement)
                && passedElement.ValueKind == JsonValueKind.True;
            var reason = root.TryGetProperty("reason", out var reasonElement)
                && reasonElement.ValueKind == JsonValueKind.String
                    ? reasonElement.GetString()
                    : null;

            return new KbClaudeEvaluation(passed, string.IsNullOrWhiteSpace(reason) ? null : reason);
        }
        catch (JsonException)
        {
            return new KbClaudeEvaluation(false, "invalid_evaluator_response");
        }
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        return start >= 0 && end >= start ? text[start..(end + 1)] : text;
    }

    private static string ExtractJsonArray(string text)
    {
        var start = text.IndexOf('[', StringComparison.Ordinal);
        var end = text.LastIndexOf(']');
        return start >= 0 && end >= start ? text[start..(end + 1)] : text;
    }
}
