using Clawbot.Domain.Common;

namespace Clawbot.Domain.Content;

public sealed class ContentWorkflowMetricsHourly : Entity<long>, ITenantOwned, IAuditExempt
{
    public Guid TenantId { get; private set; }
    public DateTimeOffset HourUtc { get; private set; }
    public long ReviewPassedCount { get; private set; }
    public long ReviewRejectedCount { get; private set; }
    public long ReviewNeedsHumanCount { get; private set; }
    public long ReviewFailedCount { get; private set; }
    public long ImageReviewedCount { get; private set; }
    public long ImageNotApplicableCount { get; private set; }
    public long ImageSkippedUnsupportedCount { get; private set; }
    public long ImageFailedCount { get; private set; }
    public long HumanFallbackCount { get; private set; }
    public long HumanOverrideCount { get; private set; }
    public long HumanRejectCount { get; private set; }
    public long HeldScheduleCount { get; private set; }
    public long PublishSucceededCount { get; private set; }
    public long PublishFailedCount { get; private set; }
    public long PublishOutcomeUnknownCount { get; private set; }
    public long ReviewLatencyMsSum { get; private set; }
    public long ReviewLatencySampleCount { get; private set; }
    public long PublishLatencyMsSum { get; private set; }
    public long PublishLatencySampleCount { get; private set; }
    public long LlmInputTokens { get; private set; }
    public long LlmOutputTokens { get; private set; }
    public decimal LlmCostUsd { get; private set; }

    private ContentWorkflowMetricsHourly() { }

    public static ContentWorkflowMetricsHourly Create(Guid tenantId, DateTimeOffset occurredAt)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("content_workflow_metrics_tenant_required", nameof(tenantId));

        var utc = occurredAt.ToUniversalTime();
        return new ContentWorkflowMetricsHourly
        {
            TenantId = tenantId,
            HourUtc = new DateTimeOffset(
                utc.Year,
                utc.Month,
                utc.Day,
                utc.Hour,
                0,
                0,
                TimeSpan.Zero),
        };
    }
}
