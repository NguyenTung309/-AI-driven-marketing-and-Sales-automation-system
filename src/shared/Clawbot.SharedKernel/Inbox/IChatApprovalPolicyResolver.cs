namespace Clawbot.SharedKernel.Inbox;

// Review-gate P3 manual-mode: tenant flag RequireChatReplyApproval — khi bật, mọi AI reply hold
// thành pending_approval chờ người duyệt thay vì gửi tự động.
public interface IChatApprovalPolicyResolver
{
    Task<bool> IsRequiredAsync(Guid tenantId, CancellationToken ct = default);
}
