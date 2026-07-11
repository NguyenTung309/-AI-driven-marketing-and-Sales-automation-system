using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Skills.Ops;

namespace Clawbot.Agents.Core.Content;

public sealed record ContentReviewResult(string Verdict, string Reason)
{
    public const string Approve = "approve";
    public const string RejectVerdict = "reject";
    public const string NeedsHuman = "needs_human";
}

// Review-gate P1: LLM reviewer cho content output. Verdict 3 giá trị (QĐ2/QĐ3 đã chốt):
// approve | reject | needs_human. Mọi lỗi (LLM down, timeout, JSON hỏng) => needs_human — FAIL-CLOSED,
// không bao giờ trả approve khi không chấm được.
public sealed class ContentReviewer(
    IClaudeChatClient claude,
    ILlmCallScope llmScope,
    ILlmCostTracker? costTracker = null)
{
    private const string AgentCode = "reviewer-agent";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly IClaudeChatClient _claude = claude;
    private readonly ILlmCallScope _llmScope = llmScope;
    private readonly ILlmCostTracker? _costTracker = costTracker;

    public async Task<ContentReviewResult> ReviewAsync(Guid tenantId, string platform, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new ContentReviewResult(ContentReviewResult.RejectVerdict, "empty_content");

        var system = AgentPromptDefaults.Compose(AgentPromptDefaults.DefaultFor(AgentCode))
            + "\n\n# Định dạng trả lời (bắt buộc)\n"
            + "Chỉ trả về đúng một JSON object, không thêm chữ nào khác: "
            + """{"verdict":"approve|reject|needs_human","reason":"ngắn gọn, tiếng Việt"}""";
        var user = $"Nền tảng: {platform}\n\nNội dung cần duyệt:\n{body}";

        try
        {
            // Resolve LLM binding của reviewer-agent theo tenant (cùng đường với chat/content agent).
            using var _ = _llmScope.Begin(tenantId, AgentCode);
            var reply = await _claude.CompleteAsync(system, Array.Empty<ChatTurn>(), user, ct).ConfigureAwait(false);
            await RecordCostAsync(tenantId, reply, ct).ConfigureAwait(false);
            return Parse(reply.Text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ContentReviewResult(ContentReviewResult.NeedsHuman, "review_timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ContentReviewResult(ContentReviewResult.NeedsHuman, "reviewer_unavailable: " + ex.Message);
        }
    }

    // JSON hỏng / verdict lạ => needs_human (fail-closed).
    internal static ContentReviewResult Parse(string text)
    {
        try
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
                return new ContentReviewResult(ContentReviewResult.NeedsHuman, "review_parse_failed");

            var doc = JsonSerializer.Deserialize<JsonElement>(text[start..(end + 1)], JsonOpts);
            var verdict = doc.TryGetProperty("verdict", out var v) ? v.GetString()?.Trim().ToLowerInvariant() : null;
            var reason = doc.TryGetProperty("reason", out var r) ? r.GetString() ?? string.Empty : string.Empty;

            return verdict switch
            {
                ContentReviewResult.Approve => new ContentReviewResult(ContentReviewResult.Approve, reason),
                ContentReviewResult.RejectVerdict => new ContentReviewResult(ContentReviewResult.RejectVerdict, reason),
                ContentReviewResult.NeedsHuman => new ContentReviewResult(ContentReviewResult.NeedsHuman, reason),
                _ => new ContentReviewResult(ContentReviewResult.NeedsHuman, "review_unknown_verdict"),
            };
        }
        catch (JsonException)
        {
            return new ContentReviewResult(ContentReviewResult.NeedsHuman, "review_parse_failed");
        }
    }

    private async Task RecordCostAsync(Guid tenantId, ClaudeReply reply, CancellationToken ct)
    {
        if (_costTracker is null || reply.UsdCost <= 0m)
            return;

        await _costTracker.RecordAsync(new CostEntry(
            tenantId,
            AgentCode,
            reply.Model,
            reply.InputTokens,
            reply.OutputTokens,
            reply.UsdCost,
            _llmScope.Current?.CostAt ?? DateTimeOffset.UtcNow,
            _llmScope.Current?.ReservationId), ct).ConfigureAwait(false);
    }
}
