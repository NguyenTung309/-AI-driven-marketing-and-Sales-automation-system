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

    // ponytail: cap content fed to the generator; KB docs can be large and we only need enough
    // grounding to author realistic Q&A. Raise if long docs miss their tail topics.
    private const int MaxGeneratePromptChars = 8000;

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
        if (string.IsNullOrWhiteSpace(contentMd)) return [];

        using var _llm = _llmScope.Begin(tenantId, AgentCode);
        var content = contentMd.Length > MaxGeneratePromptChars
            ? contentMd[..MaxGeneratePromptChars]
            : contentMd;

        var prompt = $"Knowledge base content:\n{content}\n\n" +
            $"Generate {count} realistic customer questions a sales/support agent would receive, " +
            "each with the correct answer grounded ONLY in the content above. " +
            "Vary the topics. Use the same language as the content. " +
            "Reply with ONLY a JSON array: " +
            "[{\"question\":\"...\",\"expectedAnswer\":\"...\"}]";

        var reply = await _claude.CompleteAsync(
            "You are a QA engineer building test cases for a knowledge base. " +
            "Only use facts present in the provided content.",
            Array.Empty<ChatTurn>(), prompt, ct).ConfigureAwait(false);

        return ParseGeneratedCases(reply.Text);
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
