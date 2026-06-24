using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

public sealed class AgentSchedule : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string GoalTemplate { get; private set; } = string.Empty;
    public string Cadence { get; private set; } = string.Empty;
    public string? CronExpression { get; private set; }
    public string TimezoneId { get; private set; } = string.Empty;
    public DateTimeOffset NextRunAt { get; private set; }
    public DateTimeOffset? LastRunAt { get; private set; }
    public string OverlapPolicy { get; private set; } = "skip";
    public string MisfirePolicy { get; private set; } = "skip_missed";
    public bool RequiresApproval { get; private set; }
    public string? ApprovalPolicyJson { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private AgentSchedule() { }

    public static AgentSchedule Create(
        Guid tenantId,
        string name,
        string goalTemplate,
        string cadence,
        string? cronExpression,
        string timezoneId,
        DateTimeOffset nextRunAt,
        bool requiresApproval,
        DateTimeOffset createdAt,
        string overlapPolicy = "skip",
        string misfirePolicy = "skip_missed",
        string? approvalPolicyJson = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            GoalTemplate = goalTemplate.Trim(),
            Cadence = cadence.Trim().ToLowerInvariant(),
            CronExpression = string.IsNullOrWhiteSpace(cronExpression) ? null : cronExpression.Trim(),
            TimezoneId = timezoneId.Trim(),
            NextRunAt = nextRunAt,
            RequiresApproval = requiresApproval,
            OverlapPolicy = string.IsNullOrWhiteSpace(overlapPolicy) ? "skip" : overlapPolicy.Trim().ToLowerInvariant(),
            MisfirePolicy = string.IsNullOrWhiteSpace(misfirePolicy) ? "skip_missed" : misfirePolicy.Trim().ToLowerInvariant(),
            ApprovalPolicyJson = string.IsNullOrWhiteSpace(approvalPolicyJson) ? null : approvalPolicyJson,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void UpdateSchedule(
        string name,
        string goalTemplate,
        string cadence,
        string? cronExpression,
        string timezoneId,
        DateTimeOffset nextRunAt,
        bool requiresApproval,
        string overlapPolicy,
        string misfirePolicy,
        string? approvalPolicyJson,
        DateTimeOffset updatedAt)
    {
        Name = name.Trim();
        GoalTemplate = goalTemplate.Trim();
        Cadence = cadence.Trim().ToLowerInvariant();
        CronExpression = string.IsNullOrWhiteSpace(cronExpression) ? null : cronExpression.Trim();
        TimezoneId = timezoneId.Trim();
        NextRunAt = nextRunAt;
        RequiresApproval = requiresApproval;
        OverlapPolicy = string.IsNullOrWhiteSpace(overlapPolicy) ? "skip" : overlapPolicy.Trim().ToLowerInvariant();
        MisfirePolicy = string.IsNullOrWhiteSpace(misfirePolicy) ? "skip_missed" : misfirePolicy.Trim().ToLowerInvariant();
        ApprovalPolicyJson = string.IsNullOrWhiteSpace(approvalPolicyJson) ? null : approvalPolicyJson;
        UpdatedAt = updatedAt;
    }

    public void Activate(DateTimeOffset updatedAt)
    {
        IsActive = true;
        UpdatedAt = updatedAt;
    }

    public void Pause(DateTimeOffset updatedAt)
    {
        IsActive = false;
        UpdatedAt = updatedAt;
    }

    public void RecordRun(DateTimeOffset lastRunAt, DateTimeOffset nextRunAt, DateTimeOffset updatedAt)
    {
        LastRunAt = lastRunAt;
        NextRunAt = nextRunAt;
        UpdatedAt = updatedAt;
    }

    public void Archive(DateTimeOffset updatedAt)
    {
        DeletedAt = updatedAt;
        IsActive = false;
        UpdatedAt = updatedAt;
    }
}
