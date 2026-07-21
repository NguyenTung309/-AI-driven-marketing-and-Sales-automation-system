using Clawbot.Domain.Common;
using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Content;

public sealed class ContentWorkflowMetricsHourlyTests
{
    [Fact]
    public void Create_normalizes_instant_to_utc_hour_and_starts_at_zero()
    {
        var tenantId = Guid.NewGuid();
        var localTime = new DateTimeOffset(2026, 7, 20, 15, 37, 42, TimeSpan.FromHours(7));

        var metrics = ContentWorkflowMetricsHourly.Create(tenantId, localTime);

        metrics.TenantId.Should().Be(tenantId);
        metrics.HourUtc.Should().Be(new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero));
        metrics.ReviewPassedCount.Should().Be(0);
        metrics.ReviewRejectedCount.Should().Be(0);
        metrics.ReviewNeedsHumanCount.Should().Be(0);
        metrics.ReviewFailedCount.Should().Be(0);
        metrics.ImageReviewedCount.Should().Be(0);
        metrics.ImageNotApplicableCount.Should().Be(0);
        metrics.ImageSkippedUnsupportedCount.Should().Be(0);
        metrics.ImageFailedCount.Should().Be(0);
        metrics.HumanFallbackCount.Should().Be(0);
        metrics.HumanOverrideCount.Should().Be(0);
        metrics.HumanRejectCount.Should().Be(0);
        metrics.HeldScheduleCount.Should().Be(0);
        metrics.PublishSucceededCount.Should().Be(0);
        metrics.PublishFailedCount.Should().Be(0);
        metrics.PublishOutcomeUnknownCount.Should().Be(0);
        metrics.ReviewLatencyMsSum.Should().Be(0);
        metrics.ReviewLatencySampleCount.Should().Be(0);
        metrics.PublishLatencyMsSum.Should().Be(0);
        metrics.PublishLatencySampleCount.Should().Be(0);
        metrics.LlmInputTokens.Should().Be(0);
        metrics.LlmOutputTokens.Should().Be(0);
        metrics.LlmCostUsd.Should().Be(0m);
        metrics.Should().BeAssignableTo<ITenantOwned>();
        metrics.Should().BeAssignableTo<IAuditExempt>();
    }

    [Fact]
    public void Create_rejects_empty_tenant()
    {
        var act = () => ContentWorkflowMetricsHourly.Create(Guid.Empty, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
    }
}
