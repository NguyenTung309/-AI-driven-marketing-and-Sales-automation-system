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
    public Guid? ApprovedBy { get; private set; }
    // SPEC-16 P2-6: when a reviewer (lead-type) agent approves, attribution is the agent_definition id, not a human userId.
    public Guid? ApprovedByAgentId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
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
        Guid? briefId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BriefId = briefId,
            Platform = platform,
            Body = body,
            CreatedBy = createdBy,
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

    public void Reject(DateTimeOffset at)
    {
        Status = "rejected";
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

    public void MarkPublished(DateTimeOffset at)
    {
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
}
