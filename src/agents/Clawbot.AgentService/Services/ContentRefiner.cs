using CoreContent = Clawbot.Agents.Core.Content;

namespace Clawbot.AgentService.Services;

// Refine (P6, §4.7): tham số cho một lần sửa bám reviewer. L1/L2 (chain JSON) đã lưu trên item; RejectionReason là
// lý do reviewer trả về (văn bản, không phải mã) — bơm vào L3 làm góp ý cần khắc phục.
public sealed record ContentRefineRequest(
    Guid TenantId,
    Guid? BriefId,
    string Platform,
    string? ChainPlanJson,
    string? ChainOutlineJson,
    string RejectionReason);

// Kết quả sửa: thân bài mới đã chạy lại L3+L4. Coordinator chỉ cần Body để ghi tại chỗ.
public sealed record ContentDraftRefineResult(string Body);

// Refine (P6, §4.7): ranh giới giữa coordinator (AgentService) và ContentAgent (Core). Coordinator KHÔNG phụ thuộc
// trực tiếp vào chi tiết ContentAgent — chỉ cần "cho lý do reject + L1/L2, trả thân bài đã sửa hoặc null".
public interface IContentRefiner
{
    // Trả thân bài mới (đã chạy lại L3+L4 kèm góp ý reviewer) hoặc null khi không refine được
    // (chuỗi tắt, L1/L2 hỏng/thiếu, hoặc resume fallback) => caller giữ nguyên bài, về hàng chờ người.
    Task<ContentDraftRefineResult?> RefineAsync(ContentRefineRequest request, CancellationToken ct);
}

public sealed class ContentRefiner(CoreContent.ContentAgent agent) : IContentRefiner
{
    public async Task<ContentDraftRefineResult?> RefineAsync(ContentRefineRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ChainPlanJson)
            || string.IsNullOrWhiteSpace(request.ChainOutlineJson)
            || string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            return null;
        }

        var draft = await agent.RefineFromChainAsync(
            new CoreContent.ContentRefineFromChainRequest(
                request.TenantId,
                request.BriefId,
                request.Platform,
                request.ChainPlanJson,
                request.ChainOutlineJson,
                request.RejectionReason),
            ct).ConfigureAwait(false);

        return draft is null || string.IsNullOrWhiteSpace(draft.Body)
            ? null
            : new ContentDraftRefineResult(draft.Body.Trim());
    }
}
