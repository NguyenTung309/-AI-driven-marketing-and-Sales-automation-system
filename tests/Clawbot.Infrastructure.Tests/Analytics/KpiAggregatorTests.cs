using Clawbot.Domain.Ads;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Leads;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Analytics;
using Clawbot.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Clawbot.SharedKernel.Time;

namespace Clawbot.Infrastructure.Tests.Analytics;

public sealed class KpiAggregatorTests
{
    [Fact]
    public async Task Daily_aggregate_returns_platform_rows_and_all_totals()
    {
        using var fx = new TestAppDb();
        var day = new DateOnly(2026, 6, 7);
        var dayStart = new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.FromHours(7));

        var facebookLead = Lead.Create(fx.TenantId, Guid.NewGuid(), "facebook", dayStart.AddHours(1));
        var facebookCustomer = Lead.Create(fx.TenantId, Guid.NewGuid(), "facebook", dayStart.AddHours(2));
        SetStage(facebookCustomer, "customer");
        var youtubeLead = Lead.Create(fx.TenantId, Guid.NewGuid(), "youtube", dayStart.AddHours(3));
        var previousDayLead = Lead.Create(fx.TenantId, Guid.NewGuid(), "facebook", dayStart.AddDays(-1));

        var facebookConversation = Conversation.Open(fx.TenantId, "facebook", "fb-1", dayStart.AddHours(4));
        facebookConversation.AppendMessage("in", "customer", "hello", "text", dayStart.AddHours(4));
        facebookConversation.AppendMessage("out", "agent", "reply", "text", dayStart.AddHours(4).AddMinutes(5));
        facebookConversation.AppendMessage("in", "customer", "again", "text", dayStart.AddHours(4).AddMinutes(10));
        facebookConversation.AppendMessage("out", "agent", "reply again", "text", dayStart.AddHours(4).AddMinutes(20));

        var youtubeConversation = Conversation.Open(fx.TenantId, "youtube", "yt-1", dayStart.AddHours(5));
        youtubeConversation.AppendMessage("in", "customer", "question", "text", dayStart.AddHours(5));

        var campaign = AdsCampaign.Create(fx.TenantId, "facebook", "campaign-1", dayStart);
        var spend = AdsMetricsDaily.Create(fx.TenantId, campaign.Id, day, cpl: 42m, frequency: null, ctr: null, spend: 120.50m, dayStart);

        fx.Db.Leads.AddRange(facebookLead, facebookCustomer, youtubeLead, previousDayLead);
        fx.Db.Conversations.AddRange(facebookConversation, youtubeConversation);
        fx.Db.AdsCampaigns.Add(campaign);
        fx.Db.AdsMetricsDailies.Add(spend);
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var sut = new KpiAggregator(fx.Db);

        var rows = await sut.AggregateDailyAsync(fx.TenantId, day, CancellationToken.None);

        var facebook = rows.Single(r => r.Platform == "facebook");
        facebook.Leads.Should().Be(2);
        facebook.Dms.Should().Be(1);
        facebook.Replies.Should().Be(2);
        facebook.Conversions.Should().Be(1);
        facebook.AvgResponseTimeSec.Should().Be(450m);
        facebook.AdSpend.Should().Be(120.50m);

        var youtube = rows.Single(r => r.Platform == "youtube");
        youtube.Leads.Should().Be(1);
        youtube.Dms.Should().Be(1);
        youtube.Replies.Should().Be(0);
        youtube.Conversions.Should().Be(0);
        youtube.AvgResponseTimeSec.Should().BeNull();
        youtube.AdSpend.Should().BeNull();

        var all = rows.Single(r => r.Platform == "all");
        all.Leads.Should().Be(3);
        all.Dms.Should().Be(2);
        all.Replies.Should().Be(2);
        all.Conversions.Should().Be(1);
        all.AvgResponseTimeSec.Should().Be(450m);
        all.AdSpend.Should().Be(120.50m);

        (await fx.Db.Leads.IgnoreQueryFilters().CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task Daily_aggregate_sums_only_approved_revenue_by_decided_at_and_platform()
    {
        using var fx = new TestAppDb();
        var day = new DateOnly(2026, 6, 7);
        var dayStart = new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.FromHours(7));
        var userId = Guid.NewGuid();

        var facebookLead = Lead.Create(fx.TenantId, Guid.NewGuid(), "facebook", dayStart.AddHours(1));
        facebookLead.MarkCustomer("paid", dayStart.AddHours(2));
        var youtubeLead = Lead.Create(fx.TenantId, Guid.NewGuid(), "youtube", dayStart.AddHours(1));
        youtubeLead.MarkCustomer("paid", dayStart.AddHours(2));

        // Approved today facebook
        var approvedFb = LeadRevenue.CreateManual(fx.TenantId, facebookLead.Id, 5_000_000m, "VND", userId, dayStart.AddHours(3));
        // Pending AI — không vào KPI
        var pending = LeadRevenue.ProposeByAi(fx.TenantId, facebookLead.Id, 9_000_000m, "VND", "x", dayStart.AddHours(4));
        // Rejected
        var rejected = LeadRevenue.ProposeByAi(fx.TenantId, youtubeLead.Id, 1_000_000m, "VND", "y", dayStart.AddHours(4));
        rejected.Reject(userId, dayStart.AddHours(5));
        // Approved youtube hôm nay
        var approvedYt = LeadRevenue.CreateManual(fx.TenantId, youtubeLead.Id, 2_000_000m, "VND", userId, dayStart.AddHours(6));
        // Approved ngày khác — không vào
        var previous = LeadRevenue.CreateManual(fx.TenantId, facebookLead.Id, 7_000_000m, "VND", userId, dayStart.AddDays(-1));

        fx.Db.Leads.AddRange(facebookLead, youtubeLead);
        fx.Db.LeadRevenues.AddRange(approvedFb, pending, rejected, approvedYt, previous);
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var rows = await new KpiAggregator(fx.Db).AggregateDailyAsync(fx.TenantId, day, CancellationToken.None);

        rows.Single(r => r.Platform == "facebook").Revenue.Should().Be(5_000_000m);
        rows.Single(r => r.Platform == "youtube").Revenue.Should().Be(2_000_000m);
        rows.Single(r => r.Platform == "all").Revenue.Should().Be(7_000_000m);
    }

    [Fact]
    public async Task Rollup_job_writes_platform_rows_idempotently()
    {
        using var fx = new TestAppDb();
        var metricDate = new DateOnly(2026, 6, 7);
        var dayStart = new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.FromHours(7));
        var tenant = Tenant.Create("acme", "Acme", "pro", dayStart);
        SetId(tenant, fx.TenantId);

        var facebookLead = Lead.Create(fx.TenantId, Guid.NewGuid(), "facebook", dayStart.AddHours(1));
        var youtubeLead = Lead.Create(fx.TenantId, Guid.NewGuid(), "youtube", dayStart.AddHours(2));
        var facebookConversation = Conversation.Open(fx.TenantId, "facebook", "fb-rollup", dayStart.AddHours(3));
        facebookConversation.AppendMessage("in", "customer", "hello", "text", dayStart.AddHours(3));
        facebookConversation.AppendMessage("out", "agent", "reply", "text", dayStart.AddHours(3).AddMinutes(2));

        fx.Db.Tenants.Add(tenant);
        fx.Db.Leads.AddRange(facebookLead, youtubeLead);
        fx.Db.Conversations.Add(facebookConversation);
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 8, 0, 30, 0, TimeSpan.FromHours(7)).ToUniversalTime());
        var job = new DailyKpiRollupJob(
            fx.Db,
            new KpiAggregator(fx.Db),
            clock,
            NullLogger<DailyKpiRollupJob>.Instance);

        await job.RunAsync();
        await job.RunAsync();

        var rows = await fx.Db.KpiDailies.IgnoreQueryFilters()
            .Where(k => k.TenantId == fx.TenantId && k.Date == metricDate)
            .OrderBy(k => k.Platform)
            .ToListAsync();

        rows.Count.Should().Be(6);
        rows.Select(r => r.Platform).Should().Contain(["all", "facebook", "instagram", "tiktok", "youtube", "zalo"]);

        var facebook = rows.Single(r => r.Platform == "facebook");
        facebook.Leads.Should().Be(1);
        facebook.Dms.Should().Be(1);
        facebook.Replies.Should().Be(1);
        facebook.AvgResponseTimeSec.Should().Be(120m);

        var all = rows.Single(r => r.Platform == "all");
        all.Leads.Should().Be(2);
        all.Dms.Should().Be(1);
        all.Replies.Should().Be(1);
        all.AvgResponseTimeSec.Should().Be(120m);
    }

    private static void SetStage(Lead lead, string stage) =>
        typeof(Lead).GetProperty(nameof(Lead.Stage))!.GetSetMethod(nonPublic: true)!.Invoke(lead, [stage]);

    private static void SetId(Tenant tenant, Guid id) =>
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.GetSetMethod(nonPublic: true)!.Invoke(tenant, [id]);
}
