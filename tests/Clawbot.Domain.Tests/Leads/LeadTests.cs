using Clawbot.Domain.Leads;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Leads;

// Vòng đời lead: điểm -> stage (cold/warm/hot), customer/lost, reactivate từ inbound, chống tin cũ out-of-order.
public sealed class LeadTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Contact = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    private static Lead NewLead() => Lead.Create(Tenant, Contact, "facebook", Now);

    [Fact]
    public void Create_StartsColdWithZeroScore()
    {
        var lead = NewLead();

        lead.Stage.Should().Be("cold");
        lead.Score.Should().Be(0);
        lead.SourcePlatform.Should().Be("facebook");
    }

    [Fact]
    public void AdjustScore_CrossesWarmThreshold()
    {
        var lead = NewLead();

        lead.AdjustScore(35, "asked_price", Now.AddMinutes(1));

        lead.Score.Should().Be(35);
        lead.Stage.Should().Be("warm");
    }

    [Fact]
    public void AdjustScore_CrossesHotThreshold()
    {
        var lead = NewLead();

        lead.AdjustScore(75, "purchase_intent", Now.AddMinutes(1));

        lead.Stage.Should().Be("hot");
    }

    [Fact]
    public void AdjustScore_NeverGoesNegative()
    {
        var lead = NewLead();

        lead.AdjustScore(-50, "spam", Now.AddMinutes(1));

        lead.Score.Should().Be(0);
        lead.Stage.Should().Be("cold");
    }

    [Fact]
    public void AdjustScore_StaleTimestamp_DoesNotRewindLastActivity()
    {
        var lead = NewLead();
        lead.AdjustScore(40, "recent", Now.AddMinutes(10));

        // Tin cũ hơn -> LastActivityAt không lùi.
        lead.AdjustScore(5, "old_message", Now.AddMinutes(1));

        lead.LastActivityAt.Should().Be(Now.AddMinutes(10));
    }

    [Fact]
    public void AdjustScore_RecordsActivity()
    {
        var lead = NewLead();

        lead.AdjustScore(10, "some_reason", Now.AddMinutes(1));

        lead.Activities.Should().Contain(a => a.ActivityType == "score_adjust");
    }

    [Fact]
    public void MarkCustomer_TransitionsAndIsIdempotent()
    {
        var lead = NewLead();

        lead.MarkCustomer("closed_deal", Now.AddMinutes(1));
        lead.Stage.Should().Be("customer");

        var activityCount = lead.Activities.Count;
        lead.MarkCustomer("again", Now.AddMinutes(2));
        // Idempotent: gọi lại không thêm activity.
        lead.Activities.Count.Should().Be(activityCount);
    }

    [Fact]
    public void MarkCustomer_IsTerminal_ScoreChangeDoesNotChangeStage()
    {
        var lead = NewLead();
        lead.MarkCustomer("won", Now.AddMinutes(1));

        lead.AdjustScore(-100, "noise", Now.AddMinutes(2));

        lead.Stage.Should().Be("customer");
    }

    [Fact]
    public void MarkLost_ThenReactivateFromInbound_ReturnsToPipeline()
    {
        var lead = NewLead();
        lead.AdjustScore(40, "warmup", Now.AddMinutes(1)); // warm, score 40
        lead.MarkLost("no_response", Now.AddMinutes(2));
        lead.Stage.Should().Be("lost");

        lead.ReactivateFromInbound(Now.AddMinutes(3)).Should().BeTrue();
        lead.Stage.Should().Be("warm"); // score 40 -> warm
    }

    [Fact]
    public void ReactivateFromInbound_OldMessage_DoesNotReactivate()
    {
        var lead = NewLead();
        lead.AdjustScore(40, "warmup", Now.AddMinutes(5));
        lead.MarkLost("lost", Now.AddMinutes(6));

        // Tin cũ hơn LastActivityAt -> không hồi sinh.
        lead.ReactivateFromInbound(Now.AddMinutes(1)).Should().BeFalse();
        lead.Stage.Should().Be("lost");
    }

    [Fact]
    public void AdjustScore_ReactivatesLostWhenPositiveDelta()
    {
        var lead = NewLead();
        lead.AdjustScore(40, "warmup", Now.AddMinutes(1));
        lead.MarkLost("lost", Now.AddMinutes(2));

        lead.AdjustScore(35, "asked_again", Now.AddMinutes(3));

        // lost + delta dương + không stale -> reactivated về pipeline.
        lead.Stage.Should().NotBe("lost");
        lead.Activities.Should().Contain(a => a.ActivityType == "stage_change");
    }

    [Fact]
    public void ReopenStage_FromCustomer_ReturnsToPipelineByScore()
    {
        var lead = NewLead();
        lead.AdjustScore(75, "hot", Now.AddMinutes(1)); // hot
        lead.MarkCustomer("won", Now.AddMinutes(2));

        lead.ReopenStage("reopened", Now.AddMinutes(3), Guid.NewGuid());

        lead.Stage.Should().Be("hot"); // score 75 -> hot
    }

    [Fact]
    public void ReopenStage_WhenNotTerminal_NoOp()
    {
        var lead = NewLead();
        lead.AdjustScore(40, "warm", Now.AddMinutes(1));

        lead.ReopenStage("x", Now.AddMinutes(2), null);

        lead.Stage.Should().Be("warm");
    }

    [Fact]
    public void TouchInboundActivity_PipelineStage_BumpsLastActivity()
    {
        var lead = NewLead();
        lead.AdjustScore(40, "warm", Now.AddMinutes(1));

        lead.TouchInboundActivity(Now.AddMinutes(5)).Should().BeTrue();
        lead.LastActivityAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void TouchInboundActivity_OldTimestamp_ReturnsFalse()
    {
        var lead = NewLead();
        lead.AdjustScore(40, "warm", Now.AddMinutes(10));

        lead.TouchInboundActivity(Now.AddMinutes(1)).Should().BeFalse();
    }

    [Fact]
    public void Assign_SetsOwner()
    {
        var lead = NewLead();
        var owner = Guid.NewGuid();

        lead.Assign(owner);

        lead.OwnerUserId.Should().Be(owner);
    }
}
