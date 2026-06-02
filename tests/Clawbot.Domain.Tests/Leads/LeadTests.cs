using Clawbot.Domain.Leads;
using FluentAssertions;
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
}
