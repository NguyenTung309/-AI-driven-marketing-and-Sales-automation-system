using Clawbot.Domain.Observability;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Observability;

public sealed class RequestStatsHourlyTests
{
    [Fact]
    public void Create_TruncatesToHourAndSetsFields()
    {
        var at = new DateTimeOffset(2026, 8, 17, 14, 45, 30, TimeSpan.Zero);
        var tenantId = Guid.NewGuid();

        var stats = RequestStatsHourly.Create(at, tenantId, "2xx", 42);

        stats.BucketHour.Should().Be(new DateTimeOffset(2026, 8, 17, 14, 0, 0, TimeSpan.Zero));
        stats.TenantId.Should().Be(tenantId);
        stats.StatusClass.Should().Be("2xx");
        stats.Count.Should().Be(42);
    }

    [Fact]
    public void Add_IncrementsCount()
    {
        var stats = RequestStatsHourly.Create(DateTimeOffset.UtcNow, Guid.Empty, "5xx", 10);

        stats.Add(5);

        stats.Count.Should().Be(15);
    }

    [Fact]
    public void TruncateHour_StripsMinutesSeconds()
    {
        var input = new DateTimeOffset(2026, 1, 15, 9, 59, 59, TimeSpan.FromHours(-5));

        var result = RequestStatsHourly.TruncateHour(input);

        result.Should().Be(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero));
    }
}
