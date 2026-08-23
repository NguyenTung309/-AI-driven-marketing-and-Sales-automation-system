using Clawbot.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Clawbot.Api.Tests.Services;

public sealed class ReplicationHealthServiceTests
{
    private static ReplicationOptions TwoRegionOptions(
        string current = "hcm",
        string primary = "hcm",
        bool allowWrites = true) =>
        new()
        {
            Enabled = true,
            CurrentRegion = current,
            PrimaryRegion = primary,
            AllowWrites = allowWrites,
            MaxReplicaLagSeconds = 30,
            Regions =
            [
                new ReplicationRegionOptions
                {
                    Name = "hcm", Role = "primary", Priority = 0, AppBaseUrl = "https://hcm.test",
                },
                new ReplicationRegionOptions { Name = "hn", Role = "secondary", Priority = 1 },
            ],
        };

    private static ReplicationHealthService Create(
        ReplicationOptions options,
        ReplicationProbeResult? probe = null) =>
        new(Options.Create(options),
            new StubProbe(probe ?? ReplicationProbeResult.Available(TimeSpan.FromSeconds(3))));

    [Fact]
    public async Task GetAsync_Disabled_ReportsDisabledAndAllowsWrites()
    {
        var report = await Create(new ReplicationOptions { Enabled = false }).GetAsync();

        report.Status.Should().Be("disabled");
        report.CurrentRole.Should().Be("primary");
        report.WritesAllowed.Should().BeTrue();
        report.ReplicaLagSeconds.Should().BeNull();
        report.Checks.Should().ContainSingle()
            .Which.Name.Should().Be("replication_enabled");
    }

    [Fact]
    public async Task GetAsync_Disabled_StillListsConfiguredRegions()
    {
        var options = TwoRegionOptions();
        options.Enabled = false;

        var report = await Create(options).GetAsync();

        report.ActiveRegions.Should().Be(2);
        report.Regions.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAsync_PrimaryRegion_IsHealthyAndWritable()
    {
        var report = await Create(TwoRegionOptions()).GetAsync();

        report.Status.Should().Be("ok");
        report.CurrentRegion.Should().Be("hcm");
        report.CurrentRole.Should().Be("primary");
        report.WritesAllowed.Should().BeTrue();
        report.ReplicaLagSeconds.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_PrimaryRegion_SkipsLagProbe()
    {
        var probe = new StubProbe(ReplicationProbeResult.Available(TimeSpan.FromSeconds(1)));
        var service = new ReplicationHealthService(Options.Create(TwoRegionOptions()), probe);

        await service.GetAsync();

        probe.Calls.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_SecondaryRegion_ProbesLagAndBlocksWrites()
    {
        var report = await Create(
                TwoRegionOptions(current: "hn"),
                ReplicationProbeResult.Available(TimeSpan.FromSeconds(4.2)))
            .GetAsync();

        report.CurrentRole.Should().Be("secondary");
        report.WritesAllowed.Should().BeFalse();
        // Lag làm tròn LÊN để không báo thấp hơn thực tế.
        report.ReplicaLagSeconds.Should().Be(5);
        report.Status.Should().Be("ok");
    }

    [Fact]
    public async Task GetAsync_LagOverThreshold_IsDegraded()
    {
        var options = TwoRegionOptions(current: "hn");
        options.MaxReplicaLagSeconds = 10;

        var report = await Create(options, ReplicationProbeResult.Available(TimeSpan.FromSeconds(45)))
            .GetAsync();

        report.Status.Should().Be("degraded");
        report.Checks.Single(c => c.Name == "replica_lag").Status.Should().Be("degraded");
    }

    [Fact]
    public async Task GetAsync_ProbeUnavailable_IsDegradedWithProbeDetail()
    {
        var report = await Create(
                TwoRegionOptions(current: "hn"),
                ReplicationProbeResult.Unavailable("Lag probe failed: timeout"))
            .GetAsync();

        report.Status.Should().Be("degraded");
        report.ReplicaLagSeconds.Should().BeNull();
        report.Checks.Single(c => c.Name == "replica_lag").Detail.Should().Contain("timeout");
    }

    [Fact]
    public async Task GetAsync_UnknownCurrentRegion_IsDegraded()
    {
        var report = await Create(TwoRegionOptions(current: "danang")).GetAsync();

        report.Status.Should().Be("degraded");
        report.CurrentRole.Should().Be("unknown");
        report.Checks.Single(c => c.Name == "current_region").Status.Should().Be("degraded");
    }

    [Fact]
    public async Task GetAsync_UnknownPrimaryRegion_IsDegraded()
    {
        var report = await Create(TwoRegionOptions(primary: "danang")).GetAsync();

        report.Checks.Single(c => c.Name == "primary_region").Status.Should().Be("degraded");
    }

    [Fact]
    public async Task GetAsync_SinglePrimaryRequirement_FlagsBadTopology()
    {
        var options = TwoRegionOptions();
        options.Regions[1].Role = "primary";

        var report = await Create(options).GetAsync();

        report.Checks.Single(c => c.Name == "topology").Status.Should().Be("degraded");
    }

    [Fact]
    public async Task GetAsync_AllowWritesFalseOnPrimary_BlocksWrites()
    {
        var report = await Create(TwoRegionOptions(allowWrites: false)).GetAsync();

        report.WritesAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_NormalizesRegionNamesAndRoles()
    {
        var options = TwoRegionOptions(current: "  HCM  ", primary: "HCM");
        options.Regions[1].Role = "khong-hop-le";

        var report = await Create(options).GetAsync();

        report.CurrentRegion.Should().Be("hcm");
        report.PrimaryRegion.Should().Be("hcm");
        // Role lạ phải quy về "secondary" chứ không giữ nguyên.
        report.Regions.Single(r => r.Name == "hn").Role.Should().Be("secondary");
    }

    [Fact]
    public async Task GetAsync_BlankRegionNames_AreDropped()
    {
        var options = TwoRegionOptions();
        options.Regions.Add(new ReplicationRegionOptions { Name = "   " });

        var report = await Create(options).GetAsync();

        report.ActiveRegions.Should().Be(2);
    }

    [Fact]
    public async Task GetAsync_SortsRegionsByPriorityThenName()
    {
        var options = TwoRegionOptions();
        options.Regions.Add(new ReplicationRegionOptions { Name = "aaa", Priority = 1 });

        var report = await Create(options).GetAsync();

        report.Regions.Select(r => r.Name).Should().Equal("hcm", "aaa", "hn");
    }

    [Fact]
    public async Task GetAsync_NegativePriority_IsClampedToZero()
    {
        var options = TwoRegionOptions();
        options.Regions[0].Priority = -10;

        var report = await Create(options).GetAsync();

        report.Regions.Single(r => r.Name == "hcm").Priority.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_BlankAppBaseUrl_BecomesNull()
    {
        var options = TwoRegionOptions();
        options.Regions[1].AppBaseUrl = "   ";

        var report = await Create(options).GetAsync();

        report.Regions.Single(r => r.Name == "hn").AppBaseUrl.Should().BeNull();
    }

    [Fact]
    public void NoRegionConfigured_DefaultsToLocal()
    {
        var options = new ReplicationOptions();

        options.CurrentRegion.Should().Be("local");
        options.PrimaryRegion.Should().Be("local");
        options.MaxReplicaLagSeconds.Should().Be(30);
        options.AllowWrites.Should().BeTrue();
    }

    private sealed class StubProbe(ReplicationProbeResult result) : IReplicationLagProbe
    {
        public int Calls { get; private set; }

        public Task<ReplicationProbeResult> ProbeAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}

public sealed class ReplicationProbeResultTests
{
    [Fact]
    public void Available_IsOkAndRoundsLagUpInDetail()
    {
        var result = ReplicationProbeResult.Available(TimeSpan.FromSeconds(2.1));

        result.Status.Should().Be("ok");
        result.Lag.Should().Be(TimeSpan.FromSeconds(2.1));
        result.Detail.Should().Contain("3");
    }

    [Fact]
    public void NotConfigured_IsDegradedWithoutLag()
    {
        var result = ReplicationProbeResult.NotConfigured("chưa cấu hình");

        result.Status.Should().Be("degraded");
        result.Lag.Should().BeNull();
        result.Detail.Should().Be("chưa cấu hình");
    }

    [Fact]
    public void Unavailable_IsDegradedWithoutLag()
    {
        var result = ReplicationProbeResult.Unavailable("probe lỗi");

        result.Status.Should().Be("degraded");
        result.Lag.Should().BeNull();
    }
}
