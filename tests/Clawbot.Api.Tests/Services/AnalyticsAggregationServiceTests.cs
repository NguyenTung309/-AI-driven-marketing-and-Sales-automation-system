using Clawbot.Api.Contracts.Analytics;
using Clawbot.Api.Services;
using Clawbot.Domain.Analytics;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests.Services;

public sealed class AnalyticsOmniRowTests
{
    private static KpiDailyDto Kpi(
        string platform,
        int leads = 1,
        int dms = 2,
        int replies = 3,
        int repliedDms = 2,
        int conversions = 1,
        decimal? avgResponse = 10m,
        int day = 1) =>
        new(new DateOnly(2026, 8, day), platform, leads, dms, replies, repliedDms, conversions, avgResponse);

    [Fact]
    public void BuildOmniRows_EmptyInput_ReturnsEmptyList()
    {
        AnalyticsAggregationService.BuildOmniRows([]).Should().BeEmpty();
    }

    [Fact]
    public void BuildOmniRows_SumsMetricsPerPlatform()
    {
        var rows = AnalyticsAggregationService.BuildOmniRows(
        [
            Kpi("facebook", leads: 3, dms: 10, replies: 8, repliedDms: 7, conversions: 2, day: 1),
            Kpi("facebook", leads: 4, dms: 12, replies: 9, repliedDms: 8, conversions: 3, day: 2),
        ]);

        var facebook = rows.Should().ContainSingle().Subject;
        facebook.Leads.Should().Be(7);
        facebook.Dms.Should().Be(22);
        facebook.Replies.Should().Be(17);
        facebook.RepliedDms.Should().Be(15);
        facebook.Conversions.Should().Be(5);
    }

    [Fact]
    public void BuildOmniRows_GroupsPlatformCaseInsensitively()
    {
        var rows = AnalyticsAggregationService.BuildOmniRows(
            [Kpi("facebook", leads: 1), Kpi("FaceBook", leads: 2)]);

        rows.Should().ContainSingle();
        rows[0].Leads.Should().Be(3);
    }

    [Fact]
    public void BuildOmniRows_AveragesResponseTimeAndRoundsToTwoDecimals()
    {
        var rows = AnalyticsAggregationService.BuildOmniRows(
            [Kpi("zalo", avgResponse: 10m, day: 1), Kpi("zalo", avgResponse: 15.555m, day: 2)]);

        rows[0].AvgResponseTimeSec.Should().Be(12.78m);
    }

    [Fact]
    public void BuildOmniRows_IgnoresNullResponseTimesInAverage()
    {
        var rows = AnalyticsAggregationService.BuildOmniRows(
            [Kpi("zalo", avgResponse: 20m, day: 1), Kpi("zalo", avgResponse: null, day: 2)]);

        rows[0].AvgResponseTimeSec.Should().Be(20m);
    }

    [Fact]
    public void BuildOmniRows_AllResponseTimesNull_ReturnsNull()
    {
        var rows = AnalyticsAggregationService.BuildOmniRows([Kpi("zalo", avgResponse: null)]);

        rows[0].AvgResponseTimeSec.Should().BeNull();
    }

    [Fact]
    public void BuildOmniRows_SortsByCanonicalPlatformOrder()
    {
        var rows = AnalyticsAggregationService.BuildOmniRows(
        [
            Kpi("threads"),
            Kpi("youtube"),
            Kpi("tiktok"),
            Kpi("instagram"),
            Kpi("zalo"),
            Kpi("facebook"),
            Kpi("all"),
        ]);

        rows.Select(r => r.Platform).Should().Equal(
            "all", "facebook", "zalo", "instagram", "tiktok", "youtube", "threads");
    }

    [Fact]
    public void BuildOmniRows_UnknownPlatforms_SortAlphabeticallyAtEnd()
    {
        var rows = AnalyticsAggregationService.BuildOmniRows([Kpi("wechat"), Kpi("threads")]);

        rows.Select(r => r.Platform).Should().Equal("threads", "wechat");
    }

    [Fact]
    public void BuildOmniRows_NullInput_Throws()
    {
        var act = () => AnalyticsAggregationService.BuildOmniRows(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsFreshForecast_WithinLast24Hours_IsTrue()
    {
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        AnalyticsAggregationService.IsFreshForecast(now.AddHours(-23), now).Should().BeTrue();
        AnalyticsAggregationService.IsFreshForecast(now, now).Should().BeTrue();
    }

    [Fact]
    public void IsFreshForecast_OlderThan24Hours_IsFalse()
    {
        var now = new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

        AnalyticsAggregationService.IsFreshForecast(now.AddHours(-25), now).Should().BeFalse();
    }
}

public sealed class AnalyticsAggregationQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly From = new(2026, 8, 10);
    private static readonly DateOnly To = new(2026, 8, 16);

    private static AppDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options, new FixedTenant(tenantId));
    }

    private static KpiDaily Kpi(
        Guid tenantId,
        DateOnly date,
        string platform,
        int leads = 1,
        int dms = 2,
        int replies = 1,
        int conversions = 1,
        DateTimeOffset? createdAt = null)
    {
        var kpi = KpiDaily.Create(tenantId, date, platform, createdAt ?? Now);
        kpi.Record(leads, dms, replies, dms, conversions, 12m);
        return kpi;
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }

    [Fact]
    public async Task LoadKpiAsync_FiltersByTenantAndDateRange()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        db.KpiDailies.AddRange(
            Kpi(tenantId, new DateOnly(2026, 8, 12), "facebook"),
            Kpi(tenantId, new DateOnly(2026, 8, 1), "facebook"),
            Kpi(Guid.NewGuid(), new DateOnly(2026, 8, 12), "facebook"));
        await db.SaveChangesAsync();
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var rows = await service.LoadKpiAsync(tenantId, From, To, platform: null);

        rows.Should().ContainSingle();
        rows[0].Date.Should().Be(new DateOnly(2026, 8, 12));
    }

    [Fact]
    public async Task LoadKpiAsync_FiltersByPlatformCaseInsensitively()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        db.KpiDailies.AddRange(
            Kpi(tenantId, new DateOnly(2026, 8, 12), "facebook"),
            Kpi(tenantId, new DateOnly(2026, 8, 12), "zalo"));
        await db.SaveChangesAsync();
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var rows = await service.LoadKpiAsync(tenantId, From, To, " FACEBOOK ");

        rows.Should().ContainSingle();
        rows[0].Platform.Should().Be("facebook");
    }

    [Fact]
    public async Task LoadKpiAsync_OrdersByDateThenPlatform()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        db.KpiDailies.AddRange(
            Kpi(tenantId, new DateOnly(2026, 8, 13), "facebook"),
            Kpi(tenantId, new DateOnly(2026, 8, 12), "zalo"),
            Kpi(tenantId, new DateOnly(2026, 8, 12), "facebook"));
        await db.SaveChangesAsync();
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var rows = await service.LoadKpiAsync(tenantId, From, To, platform: null);

        rows.Select(r => (r.Date.Day, r.Platform))
            .Should().Equal((12, "facebook"), (12, "zalo"), (13, "facebook"));
    }

    [Fact]
    public async Task GetOmnichannelAsync_NoData_FlagsStale()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var response = await service.GetOmnichannelAsync(tenantId, From, To);

        response.Rows.Should().BeEmpty();
        response.Stale.Should().BeTrue();
    }

    [Fact]
    public async Task GetOmnichannelAsync_FreshData_IsNotStale()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        db.KpiDailies.Add(Kpi(
            tenantId, new DateOnly(2026, 8, 12), "facebook", createdAt: DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var response = await service.GetOmnichannelAsync(tenantId, From, To);

        response.From.Should().Be(From);
        response.To.Should().Be(To);
        response.Rows.Should().ContainSingle();
        response.Stale.Should().BeFalse();
    }

    [Fact]
    public async Task GetFunnelAsync_ComputesConversionRates()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        db.KpiDailies.Add(Kpi(
            tenantId, new DateOnly(2026, 8, 12), "facebook",
            leads: 100, dms: 50, replies: 25, conversions: 10));
        await db.SaveChangesAsync();
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var funnel = await service.GetFunnelAsync(tenantId, From, To, "facebook");

        funnel.Platform.Should().Be("facebook");
        funnel.Leads.Should().Be(100);
        funnel.DmRate.Should().Be(0.5m);
        funnel.ReplyRate.Should().Be(0.5m);
        funnel.ConversionRate.Should().Be(0.1m);
    }

    [Fact]
    public async Task GetFunnelAsync_NoPlatformFilter_LabelsAsAll()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var funnel = await service.GetFunnelAsync(tenantId, From, To, "  ");

        funnel.Platform.Should().Be("all");
    }

    [Fact]
    public async Task GetFunnelAsync_ZeroDenominators_ReturnZeroRatesInsteadOfDivideByZero()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var funnel = await service.GetFunnelAsync(tenantId, From, To, platform: null);

        funnel.DmRate.Should().Be(0m);
        funnel.ReplyRate.Should().Be(0m);
        funnel.ConversionRate.Should().Be(0m);
    }

    [Fact]
    public async Task GetOmnichannelDeltaAsync_Dod_ShiftsByRangeLength()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var response = await service.GetOmnichannelDeltaAsync(tenantId, From, To, "dod");

        response.Compare.Should().Be("dod");
        response.PrevFrom.Should().Be(From.AddDays(-7));
        response.PrevTo.Should().Be(To.AddDays(-7));
    }

    [Fact]
    public async Task GetOmnichannelDeltaAsync_Wow_ShiftsBySevenDays()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));
        var singleDay = new DateOnly(2026, 8, 16);

        var response = await service.GetOmnichannelDeltaAsync(tenantId, singleDay, singleDay, "WOW");

        response.Compare.Should().Be("wow");
        response.PrevFrom.Should().Be(singleDay.AddDays(-7));
    }

    [Fact]
    public async Task GetOmnichannelDeltaAsync_UnknownCompare_FallsBackToDod()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var response = await service.GetOmnichannelDeltaAsync(tenantId, From, To, "monthly");

        response.Compare.Should().Be("dod");
    }

    [Fact]
    public async Task GetOmnichannelDeltaAsync_ComputesPercentDeltaPerMetric()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var current = new DateOnly(2026, 8, 16);
        var previous = current.AddDays(-1);
        db.KpiDailies.AddRange(
            Kpi(tenantId, current, "facebook", leads: 20),
            Kpi(tenantId, previous, "facebook", leads: 10));
        await db.SaveChangesAsync();
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var response = await service.GetOmnichannelDeltaAsync(tenantId, current, current, "dod");

        var leads = response.Metrics.Single(m => m.Metric == "leads");
        leads.Current.Should().Be(20m);
        leads.Previous.Should().Be(10m);
        leads.DeltaPct.Should().Be(100m);
    }

    [Fact]
    public async Task GetOmnichannelDeltaAsync_ZeroPrevious_LeavesDeltaNull()
    {
        // Chia cho 0 phải trả null (không có cơ sở so sánh), không phải 0% hay vô cực.
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var current = new DateOnly(2026, 8, 16);
        db.KpiDailies.Add(Kpi(tenantId, current, "facebook", leads: 5));
        await db.SaveChangesAsync();
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var response = await service.GetOmnichannelDeltaAsync(tenantId, current, current, "dod");

        response.Metrics.Single(m => m.Metric == "leads").DeltaPct.Should().BeNull();
    }

    [Fact]
    public async Task GetOmnichannelDeltaAsync_ReturnsAllSixMetrics()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var response = await service.GetOmnichannelDeltaAsync(tenantId, From, To, "dod");

        response.Metrics.Select(m => m.Metric).Should().Equal(
            "leads", "dms", "replies", "repliedDms", "conversions", "avgResponseTimeSec");
    }

    [Fact]
    public async Task GetForecastAsync_ReturnsOnlyFreshRowsWithinHorizon()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        db.KpiForecasts.AddRange(
            KpiForecast.Create(tenantId, "facebook", "leads", new DateOnly(2026, 8, 18), 10m, 8m, 12m, Now.AddHours(-1)),
            KpiForecast.Create(tenantId, "facebook", "leads", new DateOnly(2026, 8, 19), 11m, 9m, 13m, Now.AddHours(-1)),
            KpiForecast.Create(tenantId, "facebook", "leads", new DateOnly(2026, 8, 20), 12m, 10m, 14m, Now.AddHours(-48)));
        await db.SaveChangesAsync();
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        var rows = await service.GetForecastAsync(tenantId, "FaceBook", "LEADS", horizon: 5);

        rows.Should().HaveCount(2);
        rows[0].Date.Should().Be("2026-08-18");
        rows[0].Value.Should().Be(10d);
    }

    [Fact]
    public async Task GetForecastAsync_HorizonCapsResultCount()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        db.KpiForecasts.AddRange(
            KpiForecast.Create(tenantId, "all", "leads", new DateOnly(2026, 8, 18), 1m, 0m, 2m, Now),
            KpiForecast.Create(tenantId, "all", "leads", new DateOnly(2026, 8, 19), 1m, 0m, 2m, Now));
        await db.SaveChangesAsync();
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        (await service.GetForecastAsync(tenantId, "", "leads", horizon: 1)).Should().ContainSingle();
        // horizon <= 0 vẫn phải trả ít nhất 1 dòng thay vì rỗng.
        (await service.GetForecastAsync(tenantId, "", "leads", horizon: 0)).Should().ContainSingle();
    }

    [Fact]
    public async Task GetAgentPerformanceAsync_NoSessions_ReturnsEmpty()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var service = new AnalyticsAggregationService(db, new FixedClock(Now));

        (await service.GetAgentPerformanceAsync(tenantId, From, To)).Should().BeEmpty();
    }
}
