using Clawbot.Domain.Common;

namespace Clawbot.Domain.Content;

// Trace một mắt xích của chuỗi sinh nội dung (prompt chaining). Append-only telemetry, retention 30 ngày.
// payload_json là ảnh chụp CẤU TRÚC đã PII-redact (enum/độ dài/đếm) — không chứa văn bản khách.
// content_item_id để NULL ở P1 (item bền được tạo sau, ngoài luồng generate).
public sealed class ContentGenerationTrace : Entity<long>, ITenantOwned, IAuditExempt
{
    public const int StepIdMaxLength = 32;
    public const int PromptVersionMaxLength = 64;
    public const int ModelMaxLength = 128;
    public const int GateResultMaxLength = 128;
    public const int PayloadJsonMaxLength = 2000;

    public Guid TenantId { get; private set; }
    public Guid? ContentItemId { get; private set; }
    public Guid? BriefId { get; private set; }
    public Guid ChainRunId { get; private set; }
    public string StepId { get; private set; } = string.Empty;
    public string PromptVersion { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal UsdCost { get; private set; }
    public long LatencyMs { get; private set; }
    public string GateResult { get; private set; } = string.Empty;
    public string? PayloadJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ContentGenerationTrace() { }

    public static ContentGenerationTrace Create(
        Guid tenantId,
        Guid chainRunId,
        string stepId,
        string promptVersion,
        string model,
        int inputTokens,
        int outputTokens,
        decimal usdCost,
        long latencyMs,
        string gateResult,
        string? payloadJson,
        DateTimeOffset createdAt,
        Guid? contentItemId = null,
        Guid? briefId = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("content_generation_trace_tenant_required", nameof(tenantId));

        return new ContentGenerationTrace
        {
            TenantId = tenantId,
            ChainRunId = chainRunId,
            ContentItemId = contentItemId,
            BriefId = briefId,
            StepId = Clamp(stepId, StepIdMaxLength),
            PromptVersion = Clamp(promptVersion, PromptVersionMaxLength),
            Model = Clamp(model, ModelMaxLength),
            InputTokens = Math.Max(0, inputTokens),
            OutputTokens = Math.Max(0, outputTokens),
            UsdCost = usdCost < 0m ? 0m : usdCost,
            LatencyMs = Math.Max(0L, latencyMs),
            GateResult = Clamp(gateResult, GateResultMaxLength),
            PayloadJson = payloadJson is null ? null : Clamp(payloadJson, PayloadJsonMaxLength),
            CreatedAt = createdAt.ToUniversalTime(),
        };
    }

    private static string Clamp(string? value, int max)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= max ? text : text[..max];
    }
}
