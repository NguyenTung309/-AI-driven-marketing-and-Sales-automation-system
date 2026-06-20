using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Api.Contracts.KnowledgeBase;
using Clawbot.Domain.KnowledgeBase;

namespace Clawbot.Api.Services;

internal sealed record KbClaudeEvaluation(bool Passed, string? Reason);

internal sealed class KbTestRunnerService(IRagRetriever rag, IClaudeChatClient claude, ILlmCallScope llmScope)
{
    private const string AgentCode = "chat-agent";

    private readonly IRagRetriever _rag = rag;
    private readonly IClaudeChatClient _claude = claude;
    private readonly ILlmCallScope _llmScope = llmScope;

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
}
