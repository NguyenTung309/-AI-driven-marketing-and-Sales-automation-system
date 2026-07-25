namespace Clawbot.Agents.Core.Content.Chain;

// Hợp đồng dữ liệu cho chuỗi sinh nội dung (prompt chaining — Chapter 1).
// P1 chỉ có 2 mắt xích: plan (L1) + write (L3). Mỗi mắt xích trả record bất biến;
// cổng kiểm tất định (ContentChainGates) nhận raw text của LLM và trả về record đã kiểm
// hoặc mã lỗi — KHÔNG throw.

// L1 — plan: brief thô đã được chưng cất thành kế hoạch có cấu trúc.
public sealed record ContentPlan(
    string Objective,
    string Audience,
    string KeyMessage,
    string? Offer,
    string Tone,
    ContentPlanCta Cta,
    IReadOnlyList<string> MustInclude,
    IReadOnlyList<string> MustAvoid,
    string Language);

public sealed record ContentPlanCta(string Type, string Text);

// Kết quả cổng G1 (parse + kiểm plan): pass thì mang Plan, fail thì có mã lỗi.
public sealed record ContentPlanGateResult(bool Succeeded, string ErrorCode, ContentPlan? Plan)
{
    public static ContentPlanGateResult Ok(ContentPlan plan) => new(true, string.Empty, plan);

    public static ContentPlanGateResult Fail(string errorCode) => new(false, errorCode, null);
}

// L2 — outline: dàn ý + 3 hook + bằng chứng ĐÃ đối chiếu citation. ProofPoints ở đây CHỈ gồm những điểm
// có citationId trỏ tới chunk KB thật (đã qua G2) — điểm trỏ citation lạ bị loại, không sửa, không bịa (§4.2).
public sealed record ContentOutline(
    IReadOnlyList<string> Hooks,
    int SelectedHookIndex,                          // hook được cổng chấm điểm chọn (§4.5); -1 khi chưa chọn
    IReadOnlyList<ContentOutlineSection> Sections,
    IReadOnlyList<ContentProofPoint> ProofPoints,   // đã qua G2 — citationId luôn hợp lệ
    IReadOnlyList<string> RiskFlags,
    int DroppedProofPoints);                         // số proofPoint bị loại vì citation lạ (evidence_missing)

public sealed record ContentOutlineSection(string Section, IReadOnlyList<string> Points);

public sealed record ContentProofPoint(string Claim, int CitationId);

// Kết quả cổng G2 (parse + đối chiếu citation dàn ý L2).
public sealed record ContentOutlineGateResult(bool Succeeded, string ErrorCode, ContentOutline? Outline)
{
    public static ContentOutlineGateResult Ok(ContentOutline outline) => new(true, string.Empty, outline);

    public static ContentOutlineGateResult Fail(string errorCode) => new(false, errorCode, null);
}

// Kết quả chọn hook tất định (§4.5): index thắng + điểm từng hook (số, PII-free) để ghi trace.
public sealed record HookSelection(int SelectedIndex, IReadOnlyList<int> Scores);

// Kết quả cổng G3 (kiểm thân bài L3).
public sealed record ContentBodyGateResult(bool Succeeded, string ErrorCode)
{
    public static ContentBodyGateResult Ok() => new(true, string.Empty);

    public static ContentBodyGateResult Fail(string errorCode) => new(false, errorCode);
}

// L4 — package: bài đã đóng gói theo nền tảng. Body cuối = Caption + Hashtags (merge theo §4.4).
// Hashtags ở đây đã CHUẨN HÓA qua G4 (bỏ rỗng/trùng/cấm/vượt ngưỡng — đếm vào DroppedHashtags).
// FirstComment/AltText P3 chỉ ghi PRESENCE vào trace (PII-free), chưa dùng thật (mở rộng phase sau, §4.4).
public sealed record ContentPackage(
    string Caption,
    IReadOnlyList<string> Hashtags,
    string? FirstComment,
    string? AltText,
    int DroppedHashtags);

// Kết quả cổng G4 (parse + kiểm bài đóng gói L4).
public sealed record ContentPackageGateResult(bool Succeeded, string ErrorCode, ContentPackage? Package)
{
    public static ContentPackageGateResult Ok(ContentPackage package) => new(true, string.Empty, package);

    public static ContentPackageGateResult Fail(string errorCode) => new(false, errorCode, null);
}

// Một dòng trace cho một lần chạy mắt xích. payload_json là ảnh chụp CẤU TRÚC (enum/độ dài/đếm),
// PII-free by construction — không chứa văn bản tự do của khách.
public sealed record ContentChainStepTrace(
    string StepId,
    string PromptVersion,
    string Model,
    int InputTokens,
    int OutputTokens,
    decimal UsdCost,
    long LatencyMs,
    string GateResult,
    string? PayloadJson);

// Kết quả cả chuỗi. Succeeded=false => ContentAgent chạy fallback single-shot (§7).
// Token/chi phí là phần chuỗi ĐÃ chi thực (đầy đủ khi thành công, một phần khi fallback).
// Plan/Outline chỉ có KHI chuỗi chạy đủ (Succeeded=true) — dùng để lưu L1/L2 cho repurpose tái dùng (P4, §4.5).
public sealed record ContentChainOutcome(
    bool Succeeded,
    string Body,
    string? FallbackReason,
    IReadOnlyList<ContentChainStepTrace> Traces,
    int InputTokens,
    int OutputTokens,
    decimal UsdCost,
    string Model,
    ContentPlan? Plan = null,
    ContentOutline? Outline = null);

// Mã lỗi cổng — ghi thẳng vào cột gate_result. Giữ trong tập ASCII an toàn (letter/digit + _-.:).
public static class ContentChainErrorCodes
{
    // G1 — plan
    public const string PlanEmptyOutput = "plan_empty_output";
    public const string PlanParseFailed = "plan_parse_failed";
    public const string PlanFieldMissing = "plan_field_missing";
    public const string PlanEnumInvalid = "plan_enum_invalid";
    public const string PlanFieldTooLong = "plan_field_too_long";
    public const string PlanContainsUrl = "plan_contains_url";
    public const string PlanTooManyItems = "plan_too_many_items";

    // G2 — outline
    public const string OutlineEmptyOutput = "outline_empty_output";
    public const string OutlineParseFailed = "outline_parse_failed";
    public const string OutlineNoHooks = "outline_no_hooks";
    public const string OutlineFieldTooLong = "outline_field_too_long";
    public const string OutlineTooManyItems = "outline_too_many_items";

    // Không phải lỗi cổng — ghi vào payload_json khi loại proofPoint có citation lạ (§4.2). Gate vẫn pass.
    public const string EvidenceMissing = "evidence_missing";

    // G3 — write
    public const string WriteEmptyOutput = "write_empty_output";
    public const string WriteTooShort = "write_too_short";
    public const string WriteTooLong = "write_too_long";
    public const string WriteContainsUrl = "write_contains_url";
    public const string WritePlaceholderLeft = "write_placeholder_left";
    public const string WriteCopiesBrief = "write_copies_brief";
    public const string WriteLanguageMismatch = "write_language_mismatch";

    // G4 — package
    public const string PackageEmptyOutput = "package_empty_output";
    public const string PackageParseFailed = "package_parse_failed";
    public const string PackageCaptionEmpty = "package_caption_empty";
    public const string PackageCaptionTooLong = "package_caption_too_long";
    public const string PackageFieldTooLong = "package_field_too_long";

    // Không phải lỗi cổng — ghi vào payload_json khi loại hashtag rác/trùng/vượt ngưỡng (§4.4). Gate vẫn pass.
    public const string HashtagDropped = "hashtag_dropped";

    // Vận hành
    public const string StepTimeout = "step_timeout";
    public const string StepError = "step_error";
    public const string GatePassed = "passed";
    public const string ChainFallback = "chain_fallback";
}
