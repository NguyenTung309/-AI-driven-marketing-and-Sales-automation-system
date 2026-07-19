using Clawbot.Infrastructure.Observability;
using FluentAssertions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Observability;

public sealed class RequestStatsCounterTests
{
    [Fact]
    public void Increment_aggregates_by_hour_tenant_and_status_class()
    {
        var counter = new RequestStatsCounter();
        var tenant = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 19, 10, 15, 0, TimeSpan.Zero);

        counter.Increment(tenant, 200, now);
        counter.Increment(tenant, 201, now);
        counter.Increment(tenant, 404, now);
        counter.Increment(tenant, 500, now);
        counter.Increment(null, 200, now);

        var snap = counter.SnapshotAndReset();
        snap.Should().HaveCount(4);
        snap.Single(s => s.StatusClass == "2xx" && s.TenantId == tenant).Count.Should().Be(2);
        snap.Single(s => s.StatusClass == "4xx" && s.TenantId == tenant).Count.Should().Be(1);
        snap.Single(s => s.StatusClass == "5xx" && s.TenantId == tenant).Count.Should().Be(1);
        snap.Single(s => s.StatusClass == "2xx" && s.TenantId == Guid.Empty).Count.Should().Be(1);

        counter.SnapshotAndReset().Should().BeEmpty();
    }

    [Fact]
    public void StatusClassOf_maps_ranges()
    {
        RequestStatsCounter.StatusClassOf(200).Should().Be("2xx");
        RequestStatsCounter.StatusClassOf(404).Should().Be("4xx");
        RequestStatsCounter.StatusClassOf(503).Should().Be("5xx");
        RequestStatsCounter.StatusClassOf(101).Should().Be("other");
    }
}
