using Clawbot.Domain.Ads;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Ads;

public sealed class AdsCampaignTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset CreatedAt = new(2026, 6, 7, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_sets_initial_state()
    {
        var campaign = AdsCampaign.Create(TenantId, "meta", "ext-123", CreatedAt);

        campaign.TenantId.Should().Be(TenantId);
        campaign.Platform.Should().Be("meta");
        campaign.ExternalCampaignId.Should().Be("ext-123");
        campaign.Status.Should().BeNull();
        campaign.TargetCpl.Should().BeNull();
        campaign.DaypartPaused.Should().BeFalse();
        campaign.CreatedAt.Should().Be(CreatedAt);
        campaign.UpdatedAt.Should().Be(CreatedAt);
    }

    [Fact]
    public void MarkSynced_updates_all_fields()
    {
        var campaign = AdsCampaign.Create(TenantId, "tiktok", "ext-456", CreatedAt);
        var syncedAt = CreatedAt.AddHours(1);

        campaign.MarkSynced("CONVERSIONS", 500m, "ACTIVE", 100m, syncedAt);

        campaign.Objective.Should().Be("CONVERSIONS");
        campaign.DailyBudget.Should().Be(500m);
        campaign.Status.Should().Be("ACTIVE");
        campaign.TargetCpl.Should().Be(100m);
        campaign.SyncedAt.Should().Be(syncedAt);
        campaign.UpdatedAt.Should().Be(syncedAt);
    }

    [Fact]
    public void Pause_sets_status_to_PAUSED()
    {
        var campaign = AdsCampaign.Create(TenantId, "meta", "ext-123", CreatedAt);
        campaign.MarkSynced(null, null, "ACTIVE", null, CreatedAt);
        var at = CreatedAt.AddHours(2);

        campaign.Pause(at);

        campaign.Status.Should().Be("PAUSED");
        campaign.UpdatedAt.Should().Be(at);
    }

    [Fact]
    public void Resume_sets_status_to_ACTIVE()
    {
        var campaign = AdsCampaign.Create(TenantId, "meta", "ext-123", CreatedAt);
        campaign.Pause(CreatedAt.AddHours(1));
        var at = CreatedAt.AddHours(2);

        campaign.Resume(at);

        campaign.Status.Should().Be("ACTIVE");
        campaign.UpdatedAt.Should().Be(at);
    }

    [Fact]
    public void ScaleBudget_updates_daily_budget()
    {
        var campaign = AdsCampaign.Create(TenantId, "meta", "ext-123", CreatedAt);
        campaign.MarkSynced(null, 500m, "ACTIVE", null, CreatedAt);
        var at = CreatedAt.AddHours(3);

        campaign.ScaleBudget(600m, at);

        campaign.DailyBudget.Should().Be(600m);
        campaign.UpdatedAt.Should().Be(at);
    }

    [Fact]
    public void MarkDaypartPaused_toggles_flag()
    {
        var campaign = AdsCampaign.Create(TenantId, "tiktok", "ext-456", CreatedAt);
        var at = CreatedAt.AddHours(1);

        campaign.MarkDaypartPaused(true, at);

        campaign.DaypartPaused.Should().BeTrue();
        campaign.UpdatedAt.Should().Be(at);

        var at2 = CreatedAt.AddHours(2);
        campaign.MarkDaypartPaused(false, at2);

        campaign.DaypartPaused.Should().BeFalse();
        campaign.UpdatedAt.Should().Be(at2);
    }
}
