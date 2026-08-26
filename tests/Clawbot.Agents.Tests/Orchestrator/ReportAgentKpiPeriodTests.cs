using System.Text.Json;
using Clawbot.AgentService.Services;
using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.Analytics;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class ReportAgentKpiPeriodTests
{
    private static readonly DateOnly Today = ReportAgentRunner.Today();

    [Fact]
    public void ResolveKpiRange_ParsesTodayAndYesterday()
    {
        var (todayFrom, todayTo) = ReportAgentRunner.ResolveKpiRange("hôm nay");
        todayFrom.Should().Be(Today);
        todayTo.Should().Be(Today);

        var (yesterdayFrom, yesterdayTo) = ReportAgentRunner.ResolveKpiRange("yesterday");
        yesterdayFrom.Should().Be(Today.AddDays(-1));
        yesterdayTo.Should().Be(Today.AddDays(-1));
    }

    [Fact]
    public void ResolveKpiRange_ParsesThisWeekAndLastWeek()
    {
        var (thisWeekFrom, thisWeekTo) = ReportAgentRunner.ResolveKpiRange("tuần này");
        thisWeekTo.Should().Be(Today);
        thisWeekFrom.DayOfWeek.Should().Be(DayOfWeek.Monday);
        (thisWeekFrom <= thisWeekTo).Should().BeTrue();

        var (lastWeekFrom, lastWeekTo) = ReportAgentRunner.ResolveKpiRange("last_week");
        lastWeekFrom.DayOfWeek.Should().Be(DayOfWeek.Monday);
        lastWeekTo.DayOfWeek.Should().Be(DayOfWeek.Sunday);
        lastWeekFrom.AddDays(6).Should().Be(lastWeekTo);
    }

    [Fact]
    public void ResolveKpiRange_ParsesThisMonthAndLastMonth()
    {
        var (thisMonthFrom, thisMonthTo) = ReportAgentRunner.ResolveKpiRange("tháng này");
        thisMonthFrom.Should().Be(new DateOnly(Today.Year, Today.Month, 1));
        thisMonthTo.Should().Be(Today);

        var (lastMonthFrom, lastMonthTo) = ReportAgentRunner.ResolveKpiRange("last_month");
        var endOfLastMonth = new DateOnly(Today.Year, Today.Month, 1).AddDays(-1);
        lastMonthTo.Should().Be(endOfLastMonth);
        lastMonthFrom.Should().Be(new DateOnly(endOfLastMonth.Year, endOfLastMonth.Month, 1));
    }

    [Fact]
    public void ResolveKpiRange_ParsesFromAndToDates()
    {
        var (from, to) = ReportAgentRunner.ResolveKpiRange(null, "2026-08-01", "2026-08-15");
        from.Should().Be(new DateOnly(2026, 8, 1));
        to.Should().Be(new DateOnly(2026, 8, 15));
    }

    [Fact]
    public void ResolveKpiRange_ParsesLookbackDays()
    {
        var (from, to) = ReportAgentRunner.ResolveKpiRange("2026-08-20", lookbackDays: 7);
        from.Should().Be(new DateOnly(2026, 8, 14));
        to.Should().Be(new DateOnly(2026, 8, 20));
    }

    [Fact]
    public async Task KpiSnapshotAsync_AggregatesKpiAcrossDateRangeAndProvidesDailyTrends()
    {
        await using var fixture = await KpiFixture.CreateAsync();
        var fromDate = new DateOnly(2026, 8, 10);
        var toDate = new DateOnly(2026, 8, 12);

        fixture.AddKpiDaily(new DateOnly(2026, 8, 10), "facebook", leads: 5, dms: 10, replies: 8, conversions: 2, avgRespSec: 30m);
        fixture.AddKpiDaily(new DateOnly(2026, 8, 10), "zalo", leads: 3, dms: 6, replies: 5, conversions: 1, avgRespSec: 20m);
        fixture.AddKpiDaily(new DateOnly(2026, 8, 11), "facebook", leads: 8, dms: 12, replies: 10, conversions: 3, avgRespSec: 25m);
        fixture.AddKpiDaily(new DateOnly(2026, 8, 12), "facebook", leads: 6, dms: 8, replies: 7, conversions: 2, avgRespSec: 35m);
        await fixture.Db.SaveChangesAsync();

        var report = await fixture.Runner.KpiSnapshotAsync(
            fixture.TenantId, fromDate, toDate, platform: "all", CancellationToken.None);

        report.FromDate.Should().Be(fromDate);
        report.ToDate.Should().Be(toDate);
        report.TotalLeads.Should().Be(22);
        report.TotalDms.Should().Be(36);
        report.TotalReplies.Should().Be(30);
        report.TotalConversions.Should().Be(8);

        // Platform breakdown
        report.PlatformRows.Should().HaveCount(2);
        var fb = report.PlatformRows.Single(r => r.Platform == "facebook");
        fb.Leads.Should().Be(19);
        fb.Dms.Should().Be(30);
        fb.Conversions.Should().Be(7);

        var zalo = report.PlatformRows.Single(r => r.Platform == "zalo");
        zalo.Leads.Should().Be(3);
        zalo.Dms.Should().Be(6);
        zalo.Conversions.Should().Be(1);

        // Daily trends
        report.DailyTrends.Should().HaveCount(3);
        report.DailyTrends[0].Date.Should().Be("2026-08-10");
        report.DailyTrends[0].Leads.Should().Be(8);
        report.DailyTrends[1].Date.Should().Be("2026-08-11");
        report.DailyTrends[1].Leads.Should().Be(8);
        report.DailyTrends[2].Date.Should().Be("2026-08-12");
        report.DailyTrends[2].Leads.Should().Be(6);
    }

    [Fact]
    public async Task OrchestrationAdapter_Snapshot_WithThisWeek_ReturnsFullReportAndArtifact()
    {
        await using var fixture = await KpiFixture.CreateAsync();
        var today = ReportAgentRunner.Today();
        fixture.AddKpiDaily(today, "facebook", leads: 10, dms: 20, replies: 15, conversions: 4, avgRespSec: 25m);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Adapter.ExecuteAsync(fixture.Task(new()
        {
            ["operation"] = "snapshot",
            ["date"] = "this_week",
        }), CancellationToken.None);

        result.Success.Should().BeTrue();
        using var payload = JsonDocument.Parse(result.Output);
        var root = payload.RootElement;
        root.GetProperty("operation").GetString().Should().Be("snapshot");
        root.GetProperty("totalLeads").GetInt32().Should().Be(10);
        root.GetProperty("totalDms").GetInt32().Should().Be(20);
        root.GetProperty("totalConversions").GetInt32().Should().Be(4);
        root.GetProperty("reportUrl").GetString().Should().StartWith("/reports/");
        root.GetProperty("dailyTrends").GetArrayLength().Should().BeGreaterThan(0);

        var artifact = await fixture.Db.ReportArtifacts.IgnoreQueryFilters().SingleAsync();
        artifact.Kind.Should().Be(ReportArtifact.KindSnapshot);
        artifact.DataJson.Should().Contain("leads").And.Contain("conversions");
    }

    private sealed class KpiFixture(
        SqliteConnection connection,
        AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public Guid TenantId { get; } = Guid.NewGuid();
        public ReportAgentRunner Runner { get; } = new(
            db,
            Substitute.For<IAnomalyDetector>(),
            Substitute.For<IForecaster>());

        public ReportOrchestrationAdapter Adapter => new(Runner);

        public void AddKpiDaily(
            DateOnly date,
            string platform,
            int leads,
            int dms,
            int replies,
            int conversions,
            decimal? avgRespSec)
        {
            var kpi = KpiDaily.Create(TenantId, date, platform, DateTimeOffset.UtcNow);
            kpi.Record(leads, dms, replies, dms, conversions, avgRespSec);
            Db.KpiDailies.Add(kpi);
        }

        public AgentTask Task(Dictionary<string, string> input, string description = "Báo cáo KPI") =>
            new(Guid.NewGuid().ToString("D"), "report-agent", description,
                new Dictionary<string, string>(input, StringComparer.OrdinalIgnoreCase)
                {
                    ["tenant_id"] = TenantId.ToString("D"),
                });

        public static async Task<KpiFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            var createScript = db.Database.GenerateCreateScript()
                .Replace("nvarchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
                .Replace("varchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
                .Replace("varbinary(max)", "BLOB", StringComparison.OrdinalIgnoreCase)
                .Replace("N'", "'", StringComparison.Ordinal);
            await db.Database.ExecuteSqlRawAsync(createScript);
            return new KpiFixture(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;

        public TenantContext Require() =>
            throw new InvalidOperationException("No tenant in unit test scope.");
    }
}
