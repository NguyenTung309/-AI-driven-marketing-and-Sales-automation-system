namespace Clawbot.SharedKernel.Inbox;

// Review-gate P3 manual-mode: tenant flag RequireChatReplyApproval — khi bật, mọi AI reply hold
// thành pending_approval chờ người duyệt thay vì gửi tự động.
public interface IChatApprovalPolicyResolver
{
    Task<bool> IsRequiredAsync(Guid tenantId, CancellationToken ct = default);

    // Bypass review-gate P2: tenant flag SkipChatReplyReview — khi bật, reply gửi thẳng không qua
    // critic chấm giá/cam kết (safety cứng vẫn giữ). Default false = fail-closed như cũ.
    Task<bool> IsReviewGateBypassedAsync(Guid tenantId, CancellationToken ct = default);
}
