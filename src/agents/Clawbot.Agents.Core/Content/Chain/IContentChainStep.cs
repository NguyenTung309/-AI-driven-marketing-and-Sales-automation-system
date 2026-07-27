namespace Clawbot.Agents.Core.Content.Chain;

// Ngữ cảnh bất biến chảy qua chuỗi. Mỗi step trả về context MỚI (record with) — không mutate (§4.6).
public sealed record ContentChainContext(
    Guid TenantId,
    string Platform,
    string Brief,
    string Knowledge,          // khối KB đã dựng (giữ nguyên format BuildKnowledgeContext); L2 outline dùng
    string PlatformTemplate,   // Content:PromptTemplates:{platform} — mô tả giọng/độ dài nền tảng
    ContentChainLimits Limits,
    int ChunkCount,            // số chunk KB đánh số [1..k] — tập citationId hợp lệ để G2 đối chiếu (§4.2)
    ContentPlan? Plan = null,
    ContentOutline? Outline = null,
    string? Body = null,
    // Refine (P6, §4.7): góp ý reviewer cho lần viết lại. Chỉ set khi coordinator chạy refine sau reject;
    // WriteStep bơm vào L3 làm dữ liệu (không phải chỉ dẫn hệ thống). null ở đường sinh/repurpose/đổi hook.
    string? RefineFeedback = null);

// Cặp (system tin cậy, user chứa dữ liệu không tin cậy) cho một lần gọi LLM.
public sealed record ChainStepPrompt(string System, string User);

// Kết quả cổng của một step. Advance => context mới; Fail => mã lỗi. PayloadJson là ảnh chụp cấu trúc để ghi trace.
public sealed record ChainStepGateResult(
    bool Succeeded,
    string ErrorCode,
    ContentChainContext Context,
    string? PayloadJson)
{
    public static ChainStepGateResult Advance(ContentChainContext context, string? payloadJson) =>
        new(true, string.Empty, context, payloadJson);

    public static ChainStepGateResult Fail(string errorCode, ContentChainContext context, string? payloadJson) =>
        new(false, errorCode, context, payloadJson);
}

// Một mắt xích của chuỗi. Sách gán vai riêng từng bước — vai nằm ở persona trong prompt (BuildPrompt),
// binding LLM vẫn dùng chung agent code content-agent (§5.B).
public interface IContentChainStep
{
    int Order { get; }

    string StepId { get; }

    ChainStepPrompt BuildPrompt(ContentChainContext context);

    // Gọi lại đúng step kèm mã lỗi cổng — repair đúng 1 vòng (§7).
    ChainStepPrompt BuildRepairPrompt(ContentChainContext context, string errorCode);

    // Cổng kiểm tất định trên raw text của LLM.
    ChainStepGateResult ApplyGate(ContentChainContext context, string rawText);
}
