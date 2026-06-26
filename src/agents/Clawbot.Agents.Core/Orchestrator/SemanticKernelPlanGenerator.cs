using System.Text.Json;
using Clawbot.Agents.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Clawbot.Agents.Core.Orchestrator;

// Carries a user-safe Vietnamese reason (no raw model output / stack / PII) for why planning failed, so the
// orchestrator can surface it directly in the FE trace instead of a generic message. Derives from
// InvalidOperationException so existing planner-failure catch sites keep working unchanged.
public sealed class PlanGenerationException : InvalidOperationException
{
    public PlanGenerationException(string message) : base(message) { }
    public PlanGenerationException(string message, Exception inner) : base(message, inner) { }
}

public sealed partial class SemanticKernelPlanGenerator(IChatCompletionService chat, ILogger<SemanticKernelPlanGenerator>? logger = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IChatCompletionService _chat = chat;
    private readonly ILogger<SemanticKernelPlanGenerator> _logger = logger ?? NullLogger<SemanticKernelPlanGenerator>.Instance;

    public async Task<OrchestrationPlanDocument> GenerateAsync(
        string goal,
        IReadOnlyList<AgentCatalogEntry> catalog,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var history = new ChatHistory(BuildSystemPrompt(catalog));
        history.AddUserMessage((goal ?? string.Empty).Trim());

        var replies = await _chat.GetChatMessageContentsAsync(history, cancellationToken: ct).ConfigureAwait(false);
        var json = replies.Count > 0 ? replies[0].Content : null;
        if (string.IsNullOrWhiteSpace(json))
        {
            // Empty model output — usually a misconfigured provider/model or a refused completion.
            LogEmptyResponse(_logger, catalog.Count);
            throw new PlanGenerationException("Mô hình lập kế hoạch không trả về nội dung. Kiểm tra cấu hình provider/model của agent orchestrator.");
        }

        OrchestrationPlanDocument? plan;
        try
        {
            plan = JsonSerializer.Deserialize<OrchestrationPlanDocument>(NormalizeJson(json), JsonOptions);
        }
        catch (JsonException ex)
        {
            // Model returned non-JSON / malformed JSON. Log the raw response (truncated) server-side so the
            // root cause is diagnosable without leaking it to the user-facing trace. The safe reason carries
            // the JSON path (e.g. $.version) — diagnosable on the FE without exposing raw output.
            LogParseFailed(_logger, ex, Truncate(json), ex.Message);
            var at = string.IsNullOrEmpty(ex.Path) ? "" : $" (vị trí {ex.Path})";
            throw new PlanGenerationException($"Mô hình trả về JSON không hợp lệ{at}.", ex);
        }

        if (plan is null)
        {
            LogParseFailed(_logger, null, Truncate(json), "deserialized to null");
            throw new PlanGenerationException("Mô hình trả về JSON rỗng.");
        }

        var validation = OrchestrationPlanValidator.Validate(plan, catalog);
        if (!validation.IsValid)
        {
            // JSON parsed but is schema-invalid (e.g. unknown agent, missing field). Distinct reason so the
            // user/trace sees what was wrong instead of the generic JSON error.
            LogValidationFailed(_logger, validation.Error, Truncate(json));
            throw new PlanGenerationException($"Kế hoạch sai cấu trúc: {validation.Error}");
        }

        return plan;
    }

    // Cap raw model output in logs so a runaway response can't flood the log sink.
    private static string Truncate(string value) =>
        value.Length <= 2000 ? value : value[..2000] + "…(truncated)";

    [LoggerMessage(EventId = 4201, Level = LogLevel.Warning, Message = "Planner returned empty response (catalog agents={AgentCount}). Check the orchestrator LLM provider/model binding.")]
    private static partial void LogEmptyResponse(ILogger logger, int agentCount);

    [LoggerMessage(EventId = 4202, Level = LogLevel.Warning, Message = "Planner JSON parse failed ({Reason}). Raw response: {RawResponse}")]
    private static partial void LogParseFailed(ILogger logger, Exception? ex, string rawResponse, string reason);

    [LoggerMessage(EventId = 4203, Level = LogLevel.Warning, Message = "Planner plan validation failed: {Reason}. Raw response: {RawResponse}")]
    private static partial void LogValidationFailed(ILogger logger, string? reason, string rawResponse);

    private static string NormalizeJson(string json)
    {
        var trimmed = json.Trim();

        // Strip leading prose before opening fence (e.g. "Here is the plan:\n```json ... ```")
        var fenceStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart > 0)
            trimmed = trimmed[fenceStart..];

        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0)
            return trimmed;

        var body = trimmed[(firstLineEnd + 1)..].Trim();
        if (body.EndsWith("```", StringComparison.Ordinal))
            body = body[..^3].Trim();

        return body;
    }

    private static string BuildSystemPrompt(IReadOnlyList<AgentCatalogEntry> catalog)
    {
        var agents = string.Join("\n", catalog.Select(agent =>
            $"- {agent.Code} ({agent.ShortName}) type={agent.AgentType}: {agent.Description}; inputSchema={agent.InputSchemaJson}"));
        // The planner LLM tends to invent agent codes (e.g. "planner", "designer") that aren't in the
        // catalog, which then fails validation. List the exact allowed codes and forbid anything else.
        var allowedCodes = string.Join(", ", catalog.Select(agent => agent.Code));
        return "Return only JSON for an OrchestrationPlanDocument with version (integer) and tasks. " +
               "Each task must have id, agent, description, input, dependsOn, status, output, error. " +
               "Use status pending for new tasks. " +
               "The \"agent\" field MUST be exactly one of these codes — do NOT invent new agent names: " +
               allowedCodes + ". " +
               "Available agents:\n" + agents;
    }
}
