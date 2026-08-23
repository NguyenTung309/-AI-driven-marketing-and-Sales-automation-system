using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Content;

public sealed class ContentWorkflowMetricsHourlyTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsTenantAndTruncatesToHour()
    {
        var occurredAt = new DateTimeOffset(2026, 8, 17, 14, 35, 22, TimeSpan.FromHours(7));

        var metrics = ContentWorkflowMetricsHourly.Create(TenantId, occurredAt);

        metrics.TenantId.Should().Be(TenantId);
        metrics.HourUtc.Should().Be(new DateTimeOffset(2026, 8, 17, 7, 0, 0, TimeSpan.Zero));
        metrics.ReviewPassedCount.Should().Be(0);
        metrics.LlmCostUsd.Should().Be(0m);
    }

    [Fact]
    public void Create_AllCountersStartAtZero()
    {
        var metrics = ContentWorkflowMetricsHourly.Create(
            TenantId,
            new DateTimeOffset(2026, 8, 17, 14, 0, 0, TimeSpan.Zero));

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
    }

    [Fact]
    public void Create_NonUtcInput_NormalizesToUtcHour()
    {
        // Đầu vào +07:00 phải quy về UTC trước khi cắt giờ, không được cắt theo giờ địa phương.
        var metrics = ContentWorkflowMetricsHourly.Create(
            TenantId,
            new DateTimeOffset(2026, 8, 17, 0, 45, 0, TimeSpan.FromHours(7)));

        metrics.HourUtc.Should().Be(new DateTimeOffset(2026, 8, 16, 17, 0, 0, TimeSpan.Zero));
        metrics.HourUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Create_EmptyTenant_Throws()
    {
        var act = () => ContentWorkflowMetricsHourly.Create(Guid.Empty, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }
}
