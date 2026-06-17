using Clawbot.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Clawbot.Api.Tests;

public sealed class ReplicationHealthServiceTests
{
    [Fact]
    public async Task GetAsync_returns_disabled_report_when_replication_is_not_enabled()
    {
        var sut = new ReplicationHealthService(
            Options.Create(new ReplicationOptions { Enabled = false, CurrentRegion = "local", PrimaryRegion = "local" }),
            new StaticReplicationLagProbe(ReplicationProbeResult.NotConfigured("lag probe disabled")));

        var report = await sut.GetAsync(CancellationToken.None);

        report.Status.Should().Be("disabled");
        report.CurrentRegion.Should().Be("local");
        report.PrimaryRegion.Should().Be("local");
        report.WritesAllowed.Should().BeTrue();
        report.Checks.Should().Contain(c => c.Name == "replication_enabled" && c.Status == "disabled");
    }

    [Fact]
    public async Task GetAsync_reports_ok_for_primary_region_with_complete_topology()
    {
        var sut = new ReplicationHealthService(
            Options.Create(new ReplicationOptions
            {
                Enabled = true,
                CurrentRegion = "sea",
                PrimaryRegion = "sea",
                MaxReplicaLagSeconds = 30,
                Regions =
                {
                    new ReplicationRegionOptions { Name = "sea", Role = "primary", Priority = 1, AppBaseUrl = "https://sea.example.com" },
                    new ReplicationRegionOptions { Name = "hkg", Role = "secondary", Priority = 2, AppBaseUrl = "https://hkg.example.com" },
                },
            }),
            new StaticReplicationLagProbe(ReplicationProbeResult.Available(TimeSpan.Zero)));

        var report = await sut.GetAsync(CancellationToken.None);

        report.Status.Should().Be("ok");
        report.CurrentRole.Should().Be("primary");
        report.WritesAllowed.Should().BeTrue();
        report.ActiveRegions.Should().Be(2);
        report.Checks.Should().Contain(c => c.Name == "topology" && c.Status == "ok");
        report.Checks.Should().Contain(c => c.Name == "write_guard" && c.Status == "ok");
    }

    [Fact]
    public async Task GetAsync_reports_degraded_secondary_when_replica_lag_exceeds_threshold()
    {
        var sut = new ReplicationHealthService(
            Options.Create(new ReplicationOptions
            {
                Enabled = true,
                CurrentRegion = "hkg",
                PrimaryRegion = "sea",
                MaxReplicaLagSeconds = 30,
                Regions =
                {
                    new ReplicationRegionOptions { Name = "sea", Role = "primary", Priority = 1 },
                    new ReplicationRegionOptions { Name = "hkg", Role = "secondary", Priority = 2 },
                },
            }),
            new StaticReplicationLagProbe(ReplicationProbeResult.Available(TimeSpan.FromSeconds(91))));

        var report = await sut.GetAsync(CancellationToken.None);

        report.Status.Should().Be("degraded");
        report.CurrentRole.Should().Be("secondary");
        report.WritesAllowed.Should().BeFalse();
        report.ReplicaLagSeconds.Should().Be(91);
        report.Checks.Should().Contain(c => c.Name == "replica_lag" && c.Status == "degraded");
        report.Checks.Should().Contain(c => c.Name == "write_guard" && c.Status == "ok");
    }

    private sealed class StaticReplicationLagProbe(ReplicationProbeResult result) : IReplicationLagProbe
    {
        public Task<ReplicationProbeResult> ProbeAsync(CancellationToken ct = default)
        {
            _ = ct;
            return Task.FromResult(result);
        }
    }
}
