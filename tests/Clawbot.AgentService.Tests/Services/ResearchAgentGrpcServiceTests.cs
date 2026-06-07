using Clawbot.AgentService.Services;
using Clawbot.Agents.Contracts.Research;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using CoreResearch = Clawbot.Agents.Core.Research;

namespace Clawbot.AgentService.Tests.Services;

public sealed class ResearchAgentGrpcServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WeeklyTrends_extracts_kb_keywords_and_upserts_trend_briefs_idempotently()
    {
        var tenantId = Guid.NewGuid();
        using var fx = new AgentServiceTestAppDb(tenantId);
        var active = KbModule.Create(tenantId, "HSK-3", "Mandarin Speaking", Now);
        var archived = KbModule.Create(tenantId, "DROP", "Ignore Me", Now);
        fx.Db.KbModules.AddRange(active, archived);
        fx.Db.Entry(active).Property(nameof(KbModule.Description)).CurrentValue = "Chinese conversation";
        fx.Db.Entry(archived).Property(nameof(KbModule.Status)).CurrentValue = "archived";
        await fx.Db.SaveChangesAsync();

        var requests = new List<CoreResearch.ResearchScanRequest>();
        var scanCount = 0;
        var agent = Substitute.For<CoreResearch.IResearchAgent>();
        agent.ScanAsync(Arg.Any<CoreResearch.ResearchScanRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                requests.Add(call.ArgAt<CoreResearch.ResearchScanRequest>(0));
                scanCount++;
                IReadOnlyList<CoreResearch.ScoredTrend> trends =
                [
                    new(
                        "HSK speaking challenge",
                        "youtube",
                        scanCount == 1 ? "1K views" : "2K views",
                        42.5d,
                        ["Turn the trend into a speaking drill"]),
                ];
                return Task.FromResult(trends);
            });
        var clock = new FixedClock(Now);
        var service = new ResearchAgentGrpcService(agent, fx.Db, clock);
        var request = new TrendRequest { TenantId = tenantId.ToString(), WeekOf = "2026-W23" };

        var first = await service.WeeklyTrends(request, TestServerCallContext.Create());
        clock.UtcNow = Now.AddMinutes(5);
        var second = await service.WeeklyTrends(request, TestServerCallContext.Create());

        first.Trends.Should().ContainSingle().Which.Metric.Should().Be("1K views");
        second.Trends.Should().ContainSingle().Which.Metric.Should().Be("2K views");
        requests.Should().HaveCount(2);
        requests[0].TenantId.Should().Be(tenantId);
        requests[0].Geo.Should().Be("VN");
        requests[0].Keywords.Should().Contain(
            ["HSK-3", "HSK", "Mandarin Speaking", "Mandarin", "Speaking", "Chinese conversation", "Chinese", "conversation"]);
        requests[0].Keywords.Should().NotContain("DROP");

        var saved = await fx.Db.ContentBriefs.IgnoreQueryFilters().SingleAsync();
        saved.TenantId.Should().Be(tenantId);
        saved.Platform.Should().Be("youtube");
        saved.Status.Should().Be("pending");
        saved.CreatedAt.Should().Be(Now);
        saved.UpdatedAt.Should().Be(Now.AddMinutes(5));
        saved.Brief.Should().StartWith("[trend:2026-W23] HSK speaking challenge");
        saved.Brief.Should().Contain("Metric: 2K views");
        saved.Brief.Should().Contain("- Turn the trend into a speaking drill");
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
