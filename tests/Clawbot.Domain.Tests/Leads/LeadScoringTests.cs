using Clawbot.Domain.Leads;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Leads;

public sealed class LeadScoringRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var rule = LeadScoringRule.Create(TenantId, "asked_price", 9, "facebook", Now);

        rule.TenantId.Should().Be(TenantId);
        rule.EventCode.Should().Be("asked_price");
        rule.Weight.Should().Be(9);
        rule.Platform.Should().Be("facebook");
        rule.IsActive.Should().BeTrue();
        rule.CreatedAt.Should().Be(Now);
        rule.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var rule = LeadScoringRule.Create(TenantId, "e", 5, null, Now);

        rule.Deactivate();

        rule.IsActive.Should().BeFalse();
    }
}

public sealed class LeadScoringDefaultsTests
{
    [Fact]
    public void Rules_ContainsExpectedEventCodes()
    {
        LeadScoringDefaults.Rules.Should().Contain(r => r.EventCode == "asked_price");
        LeadScoringDefaults.Rules.Should().Contain(r => r.EventCode == "purchase_intent");
        LeadScoringDefaults.Rules.Should().Contain(r => r.EventCode == "asked_commitment");
    }

    [Fact]
    public void Rules_AllHavePositiveWeights()
    {
        LeadScoringDefaults.Rules.Should().OnlyContain(r => r.Weight > 0);
    }

    [Fact]
    public void Rules_AllHaveDescriptions()
    {
        LeadScoringDefaults.Rules.Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.Description));
    }
}
