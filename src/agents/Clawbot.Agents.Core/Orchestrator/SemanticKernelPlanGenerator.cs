using System.ClientModel;
using System.Net;
using System.Text.Json;
using Clawbot.Agents.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
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

    // Self-repair: LLM chập chờn (rỗng / JSON hỏng / sai schema) là bản chất — mỗi lần fail, đưa chính
    // lỗi đó lại cho model tự sửa thay vì đánh fail cả run ngay lần đầu.
    private const int MaxAttempts = 3;

    public async Task<OrchestrationPlanDocument> GenerateAsync(
        string goal,
        IReadOnlyList<AgentCatalogEntry> catalog,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var history = new ChatHistory(BuildSystemPrompt(catalog));
        history.AddUserMessage((goal ?? string.Empty).Trim());

        PlanGenerationException? lastFailure = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            string? json;
            try
            {
                var replies = await _chat.GetChatMessageContentsAsync(history, cancellationToken: ct).ConfigureAwait(false);
                json = replies.Count > 0 ? replies[0].Content : null;
            }
            catch (Exception ex) when (IsProviderAuthFailure(ex))
            {
                // Auth hỏng là lỗi cấu hình — retry vô nghĩa.
                LogProviderAuthFailed(_logger, ex);
                throw new PlanGenerationException("LLM của orchestrator bị từ chối (401/403). Kiểm tra API key, model, base URL, rồi bấm Test trong Cấu hình LLM trước khi bind agent.", ex);
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                LogEmptyResponse(_logger, catalog.Count);
                lastFailure = new PlanGenerationException("Mô hình lập kế hoạch không trả về nội dung. Kiểm tra cấu hình provider/model của agent orchestrator.");
                history.AddUserMessage("Bạn vừa trả về rỗng. Trả về DUY NHẤT JSON OrchestrationPlanDocument hợp lệ theo schema đã nêu, không thêm chữ nào khác.");
                continue;
            }

            OrchestrationPlanDocument? plan = null;
            try
            {
                plan = JsonSerializer.Deserialize<OrchestrationPlanDocument>(NormalizeJson(json), JsonOptions);
            }
            catch (JsonException ex)
            {
                LogParseFailed(_logger, ex, Truncate(json), ex.Message);
                var at = string.IsNullOrEmpty(ex.Path) ? "" : $" (vị trí {ex.Path})";
                lastFailure = new PlanGenerationException($"Mô hình trả về JSON không hợp lệ{at}.", ex);
                history.AddAssistantMessage(json);
                history.AddUserMessage($"JSON trên không hợp lệ{at}: {ex.Message}. Sửa lại và trả về DUY NHẤT JSON hợp lệ đúng schema (id/agent/description/status là chuỗi, dependsOn là mảng chuỗi).");
                continue;
            }

            if (plan is null)
            {
                LogParseFailed(_logger, null, Truncate(json), "deserialized to null");
                lastFailure = new PlanGenerationException("Mô hình trả về JSON rỗng.");
                history.AddUserMessage("Kết quả rỗng. Trả về DUY NHẤT JSON OrchestrationPlanDocument hợp lệ.");
                continue;
            }

            var validation = OrchestrationPlanValidator.Validate(plan, catalog);
            if (!validation.IsValid)
            {
                LogValidationFailed(_logger, validation.Error, Truncate(json));
                lastFailure = new PlanGenerationException($"Kế hoạch sai cấu trúc: {validation.Error}");
                history.AddAssistantMessage(json);
                history.AddUserMessage($"Kế hoạch trên sai cấu trúc: {validation.Error}. Sửa đúng lỗi này (chỉ dùng agent code trong danh sách cho phép) và trả về DUY NHẤT JSON hợp lệ.");
                continue;
            }

            return plan;
        }

        throw lastFailure ?? new PlanGenerationException("Mô hình lập kế hoạch không trả về nội dung.");
    }

    private static bool IsProviderAuthFailure(Exception ex) =>
        ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
        || ex is ClientResultException { Status: 401 or 403 }
        || ex.InnerException is not null && IsProviderAuthFailure(ex.InnerException);

    // Cap raw model output in logs so a runaway response can't flood the log sink.
    private static string Truncate(string value) =>
        value.Length <= 2000 ? value : value[..2000] + "…(truncated)";

    [LoggerMessage(EventId = 4201, Level = LogLevel.Warning, Message = "Planner returned empty response (catalog agents={AgentCount}). Check the orchestrator LLM provider/model binding.")]
    private static partial void LogEmptyResponse(ILogger logger, int agentCount);

    [LoggerMessage(EventId = 4202, Level = LogLevel.Warning, Message = "Planner JSON parse failed ({Reason}). Raw response: {RawResponse}")]
    private static partial void LogParseFailed(ILogger logger, Exception? ex, string rawResponse, string reason);

    [LoggerMessage(EventId = 4203, Level = LogLevel.Warning, Message = "Planner plan validation failed: {Reason}. Raw response: {RawResponse}")]
    private static partial void LogValidationFailed(ILogger logger, string? reason, string rawResponse);

    [LoggerMessage(EventId = 4204, Level = LogLevel.Warning, Message = "Planner LLM provider rejected request with 401/403. Check orchestrator provider credentials, endpoint/baseUrl, model/deployment, permissions, and Agent LLM binding.")]
    private static partial void LogProviderAuthFailed(ILogger logger, Exception ex);

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
               "Each task MAY also include roleInstruction: a short Vietnamese system prompt (1-3 câu) that " +
               "tailors the sub-agent's persona/rules for THIS specific task. Only add it when it sharpens the " +
               "sub-agent beyond its default role; otherwise omit it (null). " +
               "The \"agent\" field MUST be exactly one of these codes — do NOT invent new agent names: " +
               allowedCodes + ". " +
               "Available agents:\n" + agents;
    }
}
