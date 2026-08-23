using Clawbot.Domain.Analytics;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Analytics;

public sealed class ReportArtifactTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 15);

        var artifact = ReportArtifact.Create(TenantId, ReportArtifact.KindSnapshot, "Weekly Report",
            "facebook", "engagement", from, to, "{\"rows\":[]}", Now);

        artifact.TenantId.Should().Be(TenantId);
        artifact.Kind.Should().Be("snapshot");
        artifact.Title.Should().Be("Weekly Report");
        artifact.Platform.Should().Be("facebook");
        artifact.Metric.Should().Be("engagement");
        artifact.FromDate.Should().Be(from);
        artifact.ToDate.Should().Be(to);
        artifact.DataJson.Should().Be("{\"rows\":[]}");
        artifact.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_AllowsNullMetric()
    {
        var artifact = ReportArtifact.Create(TenantId, ReportArtifact.KindAnomaly, "Alert",
            "zalo", null, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 7), "{}", Now);

        artifact.Metric.Should().BeNull();
        artifact.Kind.Should().Be("anomaly");
    }

    [Fact]
    public void KindConstants_HaveExpectedValues()
    {
        ReportArtifact.KindSnapshot.Should().Be("snapshot");
        ReportArtifact.KindAnomaly.Should().Be("anomaly");
        ReportArtifact.KindForecast.Should().Be("forecast");
    }
}
