using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;

namespace Clawbot.Agents.Core.Learning;

public sealed record KbAccuracyCase(string Question, string ExpectedAnswer);

public sealed record KbAccuracyPair(decimal? Before, decimal? After);

// Đo accuracy trước/sau cho 1 đề xuất KB mà KHÔNG deploy tạm: "trước" = context RAG hiện tại;
// "sau" = cùng context + contentMd đề xuất nối thêm. Không có test case => (null, null) — rail
// auto-approve coi đó là "chưa đo được" và rơi về người duyệt.
public sealed class KbSuggestionAccuracyEvaluator(IRagRetriever rag, IClaudeChatClient claude, ILlmCallScope llmScope)
{
    private const string AgentCode = "chat-agent";
    // ponytail: trần 2 LLM call/case; 20 case đủ tin cậy thống kê cho 1 module, tránh đốt ngân sách đêm.
    private const int MaxCases = 20;

    private readonly IRagRetriever _rag = rag;
    private readonly IClaudeChatClient _claude = claude;
    private readonly ILlmCallScope _llmScope = llmScope;

    public async Task<KbAccuracyPair> EvaluateAsync(
        Guid tenantId,
        string moduleCode,
        IReadOnlyList<KbAccuracyCase> cases,
        string proposedContentMd,
        CancellationToken ct = default)
    {
        if (cases.Count == 0)
            return new KbAccuracyPair(null, null);

        using var _ = _llmScope.Begin(tenantId, AgentCode);
        var sample = cases.Count > MaxCases ? cases.Take(MaxCases).ToList() : cases;
        var passedBefore = 0;
        var passedAfter = 0;

        foreach (var testCase in sample)
        {
            var chunks = await _rag.RetrieveAsync(
                new RagRequest(tenantId, moduleCode, testCase.Question, 3), ct).ConfigureAwait(false);
            var contextBefore = string.Join("\n---\n", chunks.Select(c => c.Snippet));
            var contextAfter = string.IsNullOrEmpty(contextBefore)
                ? proposedContentMd
                : contextBefore + "\n---\n" + proposedContentMd;

            if (await JudgeAsync(contextBefore, testCase, ct).ConfigureAwait(false)) passedBefore++;
            if (await JudgeAsync(contextAfter, testCase, ct).ConfigureAwait(false)) passedAfter++;
        }

        return new KbAccuracyPair(ToPercent(passedBefore, sample.Count), ToPercent(passedAfter, sample.Count));
    }

    private static decimal ToPercent(int passed, int total) =>
        Math.Round(passed * 100m / total, 2);

    private async Task<bool> JudgeAsync(string context, KbAccuracyCase testCase, CancellationToken ct)
    {
        var prompt = $"Context:\n{context}\n\nQuestion: {testCase.Question}\n" +
            $"Expected answer: {testCase.ExpectedAnswer}\n\n" +
            "Does the context contain information to answer the question correctly? " +
            "Reply with only JSON: {\"passed\":true/false}";

        try
        {
            var reply = await _claude.CompleteAsync(
                "You are a KB accuracy evaluator. Check if the retrieved context supports the expected answer.",
                Array.Empty<ChatTurn>(), prompt, ct).ConfigureAwait(false);
            return ParsePassed(reply.Text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false; // đo không được = fail case đó (fail-closed, không đoán)
        }
    }

    internal static bool ParsePassed(string text)
    {
        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return false;
            var doc = JsonSerializer.Deserialize<JsonElement>(text[start..(end + 1)]);
            return doc.TryGetProperty("passed", out var p) && p.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
