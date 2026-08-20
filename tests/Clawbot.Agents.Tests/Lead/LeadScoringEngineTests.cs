using Clawbot.Agents.Core.Lead;
using Clawbot.Domain.Leads;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Lead;

// Bộ chấm điểm lead: khớp rule theo event_code + platform, ưu tiên rule gắn platform, hỗ trợ alias.
public sealed class LeadScoringEngineTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    private static LeadScoringRule Rule(string eventCode, int weight, string? platform = null, bool active = true)
    {
        var rule = LeadScoringRule.Create(Tenant, eventCode, weight, platform, Now);
        if (!active) rule.Deactivate();
        return rule;
    }

    [Fact]
    public void Evaluate_BlankEventCode_ReturnsZero()
    {
        var decision = LeadScoringEngine.Evaluate("  ", "facebook", []);

        decision.Delta.Should().Be(0);
        decision.Reason.Should().Be("no event_code");
        decision.MatchedRules.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_NoMatchingRule_ReturnsZeroWithReason()
    {
        var decision = LeadScoringEngine.Evaluate("asked_price", "facebook", [Rule("asked_teacher", 5)]);

        decision.Delta.Should().Be(0);
        decision.Reason.Should().Contain("no rule for event=asked_price");
    }

    [Fact]
    public void Evaluate_ExactMatch_SumsWeight()
    {
        var decision = LeadScoringEngine.Evaluate("asked_price", null, [Rule("asked_price", 10)]);

        decision.Delta.Should().Be(10);
        decision.MatchedRules.Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_InactiveRule_Ignored()
    {
        var decision = LeadScoringEngine.Evaluate("asked_price", null, [Rule("asked_price", 10, active: false)]);

        decision.Delta.Should().Be(0);
    }

    [Fact]
    public void Evaluate_PrefersPlatformSpecificOverNull()
    {
        var rules = new[]
        {
            Rule("asked_price", 3, platform: null),
            Rule("asked_price", 7, platform: "facebook"),
        };

        var decision = LeadScoringEngine.Evaluate("asked_price", "facebook", rules);

        // Chỉ lấy rule gắn platform facebook (7), bỏ rule null (3).
        decision.Delta.Should().Be(7);
        decision.MatchedRules.Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_NullPlatform_KeepsAllMatches()
    {
        var rules = new[]
        {
            Rule("asked_price", 3, platform: null),
            Rule("asked_price", 5, platform: null),
        };

        var decision = LeadScoringEngine.Evaluate("asked_price", null, rules);

        decision.Delta.Should().Be(8);
        decision.MatchedRules.Should().HaveCount(2);
    }

    [Fact]
    public void Evaluate_FallsBackToAlias_WhenExactMissing()
    {
        // event asked_price không có rule; alias asks_price có.
        var decision = LeadScoringEngine.Evaluate("asked_price", null, [Rule("asks_price", 4)]);

        decision.Delta.Should().Be(4);
    }

    [Fact]
    public void Evaluate_PurchaseIntentAlias_ResolvesToConfirmsEnroll()
    {
        var decision = LeadScoringEngine.Evaluate("purchase_intent", null, [Rule("confirms_enroll", 20)]);

        decision.Delta.Should().Be(20);
    }

    [Fact]
    public void Evaluate_TrimsEventCode()
    {
        var decision = LeadScoringEngine.Evaluate("  asked_price  ", null, [Rule("asked_price", 6)]);

        decision.Delta.Should().Be(6);
    }

    [Fact]
    public void Evaluate_NullRules_Throws()
    {
        var act = () => LeadScoringEngine.Evaluate("asked_price", null, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
