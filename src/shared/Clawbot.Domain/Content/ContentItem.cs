using Clawbot.Domain.Common;

namespace Clawbot.Domain.Content;

public sealed class ContentItem : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid? BriefId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string Status { get; private set; } = "draft";  // draft|approved|scheduled|published|rejected
    public string Body { get; private set; } = string.Empty;
    public string AssetsJson { get; private set; } = "[]";
    public Guid? CreatedBy { get; private set; }
    // Review-gate P1: agent that generated this item — the reviewer must be a DIFFERENT definition
    // (separation of duties), enforced in ContentApproveTool and the Review RPC.
    public Guid? CreatedByAgentId { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    // SPEC-16 P2-6: when a reviewer (lead-type) agent approves, attribution is the agent_definition id, not a human userId.
    public Guid? ApprovedByAgentId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public string? RejectedReason { get; private set; }
    // Review-gate P4 (SLA): giờ đăng mong muốn — set NGAY khi người/agent lên lịch, kể cả khi review-gate
    // chặn không tạo được schedule row, để ContentReviewSlaJob còn deadline mà nhắc người duyệt.
    public DateTimeOffset? DesiredPublishAt { get; private set; }
    // Chống spam alert: mỗi tier (trước hạn / quá hạn) chỉ notify 1 lần.
    public DateTimeOffset? LastReviewAlertAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private ContentItem() { }

    public static ContentItem Create(
        Guid tenantId,
        string platform,
        string body,
        Guid? createdBy,
        DateTimeOffset createdAt,
        Guid? briefId = null,
        Guid? createdByAgentId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BriefId = briefId,
            Platform = platform,
            Body = body,
            CreatedBy = createdBy,
            CreatedByAgentId = createdByAgentId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void Approve(Guid approverUserId, DateTimeOffset at)
    {
        Status = "approved";
        ApprovedBy = approverUserId;
        ApprovedAt = at;
        UpdatedAt = at;
    }

    // EARS[WHEN a reviewer agent approves a draft THE SYSTEM SHALL record the agent_definition id as the approver
    // (not a human userId) so audit attribution distinguishes autonomous approval from human approval]
    public void ApproveByAgent(Guid agentDefinitionId, DateTimeOffset at)
    {
        if (agentDefinitionId == Guid.Empty) throw new ArgumentException("agent definition id required", nameof(agentDefinitionId));
        Status = "approved";
        ApprovedByAgentId = agentDefinitionId;
        ApprovedAt = at;
        UpdatedAt = at;
    }

    // Review-gate: đóng dấu chữ ký reviewer-agent lên item ĐÃ 'scheduled' mà KHÔNG đổi status — gỡ deadlock
    // "scheduled nhưng chưa ký" (publish job giữ, Review từng từ chối status scheduled). Gọi ApproveByAgent ở
    // đây sẽ revert về 'approved' và hủy lịch, nên tách riêng: chỉ gắn chữ ký, giữ nguyên 'scheduled'.
    public void AttachAgentSignoff(Guid agentDefinitionId, DateTimeOffset at)
    {
        if (agentDefinitionId == Guid.Empty) throw new ArgumentException("agent definition id required", nameof(agentDefinitionId));
        ApprovedByAgentId = agentDefinitionId;
        ApprovedAt = at;
        UpdatedAt = at;
    }

    public void Reject(DateTimeOffset at, string? reason = null)
    {
        Status = "rejected";
        RejectedReason = string.IsNullOrWhiteSpace(reason) ? RejectedReason : reason.Trim();
        UpdatedAt = at;
    }

    public void UpdateBody(string body, DateTimeOffset at)
    {
        Body = body;
        UpdatedAt = at;
    }

    public void MarkScheduled(DateTimeOffset at)
    {
        Status = "scheduled";
        UpdatedAt = at;
    }

    // requireAgentReview: resolved tenant flag (RequireContentReview). Domain-level backstop — every
    // publish path (Hangfire job, content.publish tool) hits this regardless of caller-side gates.
    public void MarkPublished(DateTimeOffset at, bool requireAgentReview = false)
    {
        if (requireAgentReview && ApprovedByAgentId is null)
            throw new InvalidOperationException("content_review_required: item lacks reviewer-agent signoff (ApprovedByAgentId).");
        Status = "published";
        UpdatedAt = at;
    }

    public void SoftDelete(DateTimeOffset at)
    {
        DeletedAt = at;
        UpdatedAt = at;
    }

    public void SetAssets(string json, DateTimeOffset at)
    {
        AssetsJson = json;
        UpdatedAt = at;
    }

    public void RevertToApproved(DateTimeOffset at)
    {
        Status = "approved";
        UpdatedAt = at;
    }

    public void SetDesiredPublishAt(DateTimeOffset desiredAt, DateTimeOffset at)
    {
        DesiredPublishAt = desiredAt;
        UpdatedAt = at;
    }

    public void MarkReviewAlerted(DateTimeOffset at) => LastReviewAlertAt = at;
}
