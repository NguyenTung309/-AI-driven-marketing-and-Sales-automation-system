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
        var at = new DateTimeOffset(2026, 6, 2, 8, 0, 0, TimeSpan.Zero);

        lead.AdjustScore(20, "a", DateTimeOffset.UtcNow);
        lead.AdjustScore(15, "b", at);

        lead.Score.Should().Be(35);
        lead.Stage.Should().Be("warm");
        lead.Activities.Should().HaveCount(2);
        lead.Activities.Should().OnlyContain(a => a.ActivityType == "score_adjust");
        lead.LastActivityAt.Should().Be(at);
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
}
