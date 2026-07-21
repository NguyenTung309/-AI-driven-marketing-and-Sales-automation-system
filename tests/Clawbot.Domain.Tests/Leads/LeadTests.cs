using Clawbot.Domain.Leads;
using Clawbot.Domain.Leads.Events;
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace Clawbot.Domain.Tests.Leads;

// M15 — Lead.AdjustScore stage classifier + score clamp.
public sealed class LeadTests
{
    private static Lead NewLead() =>
        Lead.Create(Guid.NewGuid(), Guid.NewGuid(), "facebook", DateTimeOffset.UtcNow);

    [Fact]
    public void New_lead_starts_cold_with_zero_score()
    {
        var lead = NewLead();

        lead.Score.Should().Be(0);
        lead.Stage.Should().Be("cold");
    }

    [Theory]
    [InlineData(29, "cold")]
    [InlineData(30, "warm")]
    [InlineData(69, "warm")]
    [InlineData(70, "hot")]
    public void AdjustScore_classifies_stage_by_threshold(int delta, string expectedStage)
    {
        var lead = NewLead();

        lead.AdjustScore(delta, "evt", DateTimeOffset.UtcNow);

        lead.Score.Should().Be(delta);
        lead.Stage.Should().Be(expectedStage);
    }

    [Fact]
    public void AdjustScore_clamps_negative_to_zero()
    {
        var lead = NewLead();

        lead.AdjustScore(10, "up", DateTimeOffset.UtcNow);
        lead.AdjustScore(-50, "down", DateTimeOffset.UtcNow);

        lead.Score.Should().Be(0);
        lead.Stage.Should().Be("cold");
    }

    [Fact]
    public void AdjustScore_raises_LeadBecameHot_on_crossing_into_hot()
    {
        var lead = NewLead();

        lead.AdjustScore(75, "big jump", DateTimeOffset.UtcNow);

        lead.DomainEvents.OfType<LeadBecameHot>().Should().ContainSingle()
            .Which.Score.Should().Be(75);
    }

    [Fact]
    public void AdjustScore_raises_LeadBecameWarm_on_cold_to_warm_only()
    {
        var lead = NewLead();

        lead.AdjustScore(40, "warm up", DateTimeOffset.UtcNow);
        lead.DomainEvents.OfType<LeadBecameWarm>().Should().ContainSingle();

        // Already warm → another in-stage adjust must not re-raise.
        lead.ClearDomainEvents();
        lead.AdjustScore(5, "still warm", DateTimeOffset.UtcNow);
        lead.DomainEvents.OfType<LeadBecameWarm>().Should().BeEmpty();
    }

    [Fact]
    public void AdjustScore_does_not_raise_warm_when_jumping_straight_to_hot()
    {
        var lead = NewLead();

        lead.AdjustScore(80, "straight to hot", DateTimeOffset.UtcNow);

        lead.DomainEvents.OfType<LeadBecameWarm>().Should().BeEmpty();
        lead.DomainEvents.OfType<LeadBecameHot>().Should().ContainSingle();
    }

    [Fact]
    public void AdjustScore_accumulates_and_records_activity()
    {
        var lead = NewLead();
        var firstAt = new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero);
        var secondAt = firstAt.AddHours(1);

        lead.AdjustScore(20, "a", firstAt);
        lead.AdjustScore(15, "b", secondAt);

        lead.Score.Should().Be(35);
        lead.Stage.Should().Be("warm");
        lead.Activities.Should().HaveCount(2);
        lead.Activities.Should().OnlyContain(a => a.ActivityType == "score_adjust");
        lead.LastActivityAt.Should().Be(secondAt);
    }

    [Fact]
    public void AdjustScore_stale_message_does_not_move_LastActivityAt_backward()
    {
        var lead = NewLead();
        var newer = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var older = newer.AddHours(-2);

        lead.AdjustScore(40, "newer", newer);
        lead.AdjustScore(5, "older", older);

        lead.Score.Should().Be(45);
        lead.LastActivityAt.Should().Be(newer);
    }

    [Fact]
    public void AdjustScore_stale_message_does_not_reactivate_lost()
    {
        var lead = NewLead();
        var newer = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        lead.AdjustScore(40, "warm", newer);
        lead.MarkLost("im lặng", newer.AddMinutes(1));
        lead.ClearDomainEvents();

        lead.AdjustScore(10, "replayed old message", newer.AddHours(-3));

        lead.Stage.Should().Be("lost");
        lead.DomainEvents.OfType<LeadReactivated>().Should().BeEmpty();
        lead.LastActivityAt.Should().Be(newer);
    }

    [Fact]
    public void AdjustScore_records_score_change_reason_metadata()
    {
        var lead = NewLead();
        var at = new DateTimeOffset(2026, 6, 16, 9, 0, 0, TimeSpan.Zero);

        lead.AdjustScore(35, "asked price and shared phone", at);

        var activity = lead.Activities.Should().ContainSingle().Subject;
        activity.Notes.Should().Be("asked price and shared phone");

        using var doc = JsonDocument.Parse(activity.MetaJson);
        var root = doc.RootElement;
        root.GetProperty("previousScore").GetInt32().Should().Be(0);
        root.GetProperty("newScore").GetInt32().Should().Be(35);
        root.GetProperty("delta").GetInt32().Should().Be(35);
        root.GetProperty("requestedDelta").GetInt32().Should().Be(35);
        root.GetProperty("previousStage").GetString().Should().Be("cold");
        root.GetProperty("newStage").GetString().Should().Be("warm");
        root.GetProperty("reason").GetString().Should().Be("asked price and shared phone");
    }

    [Fact]
    public void MarkCustomer_SetsStage_RaisesEvent_WritesActivity()
    {
        var lead = NewLead();
        var userId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);
        lead.ClearDomainEvents();

        lead.MarkCustomer("Đã nhận thanh toán", at, userId);

        lead.Stage.Should().Be("customer");
        lead.LastActivityAt.Should().Be(at);
        lead.DomainEvents.OfType<LeadBecameCustomer>().Should().ContainSingle()
            .Which.OwnerUserId.Should().Be(lead.OwnerUserId);
        var activity = lead.Activities.Should().ContainSingle().Subject;
        activity.ActivityType.Should().Be("stage_change");
        activity.Notes.Should().Be("Đã nhận thanh toán");
        using var doc = JsonDocument.Parse(activity.MetaJson);
        doc.RootElement.GetProperty("previousStage").GetString().Should().Be("cold");
        doc.RootElement.GetProperty("newStage").GetString().Should().Be("customer");
        doc.RootElement.GetProperty("byUserId").GetGuid().Should().Be(userId);
        doc.RootElement.GetProperty("trigger").GetString().Should().Be("manual");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(80)]
    public void MarkCustomer_FromAnyStage_Works(int score)
    {
        var lead = NewLead();
        if (score > 0)
            lead.AdjustScore(score, "score", DateTimeOffset.UtcNow);

        lead.MarkCustomer("paid", DateTimeOffset.UtcNow);

        lead.Stage.Should().Be("customer");
    }

    [Fact]
    public void MarkCustomer_WhenAlreadyCustomer_IsNoOp()
    {
        var lead = NewLead();
        lead.MarkCustomer("paid", DateTimeOffset.UtcNow);
        lead.ClearDomainEvents();
        var activityCount = lead.Activities.Count;

        lead.MarkCustomer("duplicate", DateTimeOffset.UtcNow.AddMinutes(1));

        lead.Activities.Should().HaveCount(activityCount);
        lead.DomainEvents.OfType<LeadBecameCustomer>().Should().BeEmpty();
    }

    [Fact]
    public void AdjustScore_WhenCustomer_KeepsStage_StillAdjustsScore()
    {
        var lead = NewLead();
        lead.AdjustScore(40, "warm", DateTimeOffset.UtcNow);
        lead.MarkCustomer("paid", DateTimeOffset.UtcNow);

        lead.AdjustScore(-25, "signal", DateTimeOffset.UtcNow.AddMinutes(1));

        lead.Score.Should().Be(15);
        lead.Stage.Should().Be("customer");
    }

    [Fact]
    public void AdjustScore_WhenLost_PositiveDelta_ReactivatesByScore_RaisesLeadReactivated()
    {
        var lead = NewLead();
        lead.AdjustScore(35, "warm", DateTimeOffset.UtcNow);
        lead.MarkLost("silent", DateTimeOffset.UtcNow.AddDays(60));
        lead.ClearDomainEvents();

        lead.AdjustScore(10, "customer replied", DateTimeOffset.UtcNow.AddDays(61));

        lead.Score.Should().Be(45);
        lead.Stage.Should().Be("warm");
        lead.DomainEvents.OfType<LeadReactivated>().Should().ContainSingle()
            .Which.Score.Should().Be(45);
        lead.Activities.Should().ContainSingle(activity =>
            activity.ActivityType == "stage_change"
            && activity.Notes == "customer replied");
    }

    [Fact]
    public void AdjustScore_WhenLost_NegativeDelta_StaysLost()
    {
        var lead = NewLead();
        lead.AdjustScore(35, "warm", DateTimeOffset.UtcNow);
        lead.MarkLost("silent", DateTimeOffset.UtcNow.AddDays(60));
        lead.ClearDomainEvents();

        lead.AdjustScore(-10, "negative", DateTimeOffset.UtcNow.AddDays(61));

        lead.Score.Should().Be(25);
        lead.Stage.Should().Be("lost");
        lead.DomainEvents.OfType<LeadReactivated>().Should().BeEmpty();
    }

    [Fact]
    public void MarkLost_DoesNotBumpLastActivityAt()
    {
        var lead = NewLead();
        var lastSignalAt = new DateTimeOffset(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);
        lead.AdjustScore(30, "signal", lastSignalAt);

        lead.MarkLost("silent", lastSignalAt.AddDays(60));

        lead.Stage.Should().Be("lost");
        lead.LastActivityAt.Should().Be(lastSignalAt);
    }

    [Fact]
    public void ReopenStage_RecomputesFromScore_DoesNotChangeScore()
    {
        var lead = NewLead();
        lead.AdjustScore(75, "hot", DateTimeOffset.UtcNow);
        lead.MarkCustomer("paid", DateTimeOffset.UtcNow);
        var score = lead.Score;

        lead.ReopenStage("reopened", DateTimeOffset.UtcNow.AddMinutes(1), Guid.NewGuid());

        lead.Score.Should().Be(score);
        lead.Stage.Should().Be("hot");
        lead.Activities.Should().ContainSingle(activity =>
            activity.ActivityType == "stage_change"
            && activity.Notes == "reopened");
    }

    [Fact]
    public void AdjustScore_WhenLostReactivatesToHot_DoesNotRaiseDuplicateHotEvent()
    {
        var lead = NewLead();
        lead.AdjustScore(70, "hot", DateTimeOffset.UtcNow);
        lead.MarkLost("silent", DateTimeOffset.UtcNow.AddDays(60));
        lead.ClearDomainEvents();

        lead.AdjustScore(1, "customer replied", DateTimeOffset.UtcNow.AddDays(61));

        lead.Stage.Should().Be("hot");
        lead.DomainEvents.OfType<LeadReactivated>().Should().ContainSingle();
        lead.DomainEvents.OfType<LeadBecameHot>().Should().BeEmpty();
    }

    [Fact]
    public void TouchInboundActivity_OnPipeline_BumpsLastActivityAt_WithoutStageChange()
    {
        var lead = NewLead();
        lead.AdjustScore(30, "warm", DateTimeOffset.UtcNow.AddDays(-40));
        var inboundAt = DateTimeOffset.UtcNow;

        var changed = lead.TouchInboundActivity(inboundAt);

        changed.Should().BeTrue();
        lead.Stage.Should().Be("warm");
        lead.LastActivityAt.Should().Be(inboundAt);
        lead.DomainEvents.OfType<LeadReactivated>().Should().BeEmpty();
    }

    [Fact]
    public void TouchInboundActivity_OnLost_Reactivates()
    {
        var lead = NewLead();
        lead.AdjustScore(70, "hot", DateTimeOffset.UtcNow);
        lead.MarkLost("silent", DateTimeOffset.UtcNow.AddDays(60));
        lead.ClearDomainEvents();
        var at = DateTimeOffset.UtcNow.AddDays(61);

        var changed = lead.TouchInboundActivity(at);

        changed.Should().BeTrue();
        lead.Stage.Should().Be("hot");
        lead.LastActivityAt.Should().Be(at);
        lead.DomainEvents.OfType<LeadReactivated>().Should().ContainSingle();
    }
}
