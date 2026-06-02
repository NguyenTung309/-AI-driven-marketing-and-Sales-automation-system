using Clawbot.Agents.Core.Lead;
using Clawbot.Domain.Leads;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Clawbot.Agents.Tests.Leads;

// M15 — LeadScoringEngine weighted-sum rule evaluation.
public sealed class LeadScoringEngineTests
{
    private static LeadScoringRule Rule(string eventCode, int weight, string? platform = null, bool active = true)
    {
        var rule = LeadScoringRule.Create(Guid.NewGuid(), eventCode, weight, platform, DateTimeOffset.UtcNow);
        if (!active)
        {
            rule.Deactivate();
        }

        return rule;
    }

    [Fact]
    public void Empty_event_code_returns_zero()
    {
        var result = LeadScoringEngine.Evaluate("", null, Array.Empty<LeadScoringRule>());

        result.Delta.Should().Be(0);
        result.Reason.Should().Be("no event_code");
        result.MatchedRules.Should().BeEmpty();
    }

    [Fact]
    public void No_matching_rule_returns_zero_with_reason()
    {
        var rules = new[] { Rule("asks_price", 10) };

        var result = LeadScoringEngine.Evaluate("shares_phone", null, rules);

        result.Delta.Should().Be(0);
        result.Reason.Should().Contain("no rule for event=shares_phone");
    }

    [Fact]
    public void Sums_weights_of_all_matching_rules()
    {
        var rules = new[] { Rule("asks_price", 10), Rule("asks_price", 5) };

        var result = LeadScoringEngine.Evaluate("asks_price", null, rules);

        result.Delta.Should().Be(15);
        result.MatchedRules.Should().HaveCount(2);
    }

    [Fact]
    public void Matching_is_case_insensitive_for_event_and_platform()
    {
        var rules = new[] { Rule("AsksPrice", 10, "Facebook") };

        var result = LeadScoringEngine.Evaluate("asksprice", "facebook", rules);

        result.Delta.Should().Be(10);
    }

    [Fact]
    public void Rule_without_platform_matches_any_platform()
    {
        var rules = new[] { Rule("asks_price", 10, platform: null) };

        var result = LeadScoringEngine.Evaluate("asks_price", "tiktok", rules);

        result.Delta.Should().Be(10);
    }

    [Fact]
    public void Rule_with_platform_requires_exact_platform()
    {
        var rules = new[] { Rule("asks_price", 10, "facebook") };

        var result = LeadScoringEngine.Evaluate("asks_price", "tiktok", rules);

        result.Delta.Should().Be(0);
    }

    [Fact]
    public void Inactive_rules_are_ignored()
    {
        var rules = new[] { Rule("asks_price", 10, active: false) };

        var result = LeadScoringEngine.Evaluate("asks_price", null, rules);

        result.Delta.Should().Be(0);
    }

    [Fact]
    public void Null_rules_throws()
    {
        var act = () => LeadScoringEngine.Evaluate("asks_price", null, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

// M15 — RoundRobinLeadAssignmentService owner rotation.
public sealed class RoundRobinLeadAssignmentServiceTests
{
    private static RoundRobinLeadAssignmentService Build(params Guid[] users)
    {
        var source = Substitute.For<IAssignmentPoolSource>();
        source.LoadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .Returns(new AssignmentPool(users));
        return new RoundRobinLeadAssignmentService(source);
    }

    [Fact]
    public async Task Empty_pool_returns_null()
    {
        var sut = Build();

        var owner = await sut.PickOwnerAsync(Guid.NewGuid(), CancellationToken.None);

        owner.Should().BeNull();
    }

    [Fact]
    public async Task Single_user_always_returned()
    {
        var user = Guid.NewGuid();
        var sut = Build(user);

        (await sut.PickOwnerAsync(Guid.NewGuid(), CancellationToken.None)).Should().Be(user);
        (await sut.PickOwnerAsync(Guid.NewGuid(), CancellationToken.None)).Should().Be(user);
    }

    [Fact]
    public async Task Rotates_across_pool()
    {
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var sut = Build(users);

        var picks = new List<Guid?>();
        for (var i = 0; i < users.Length; i++)
        {
            picks.Add(await sut.PickOwnerAsync(Guid.NewGuid(), CancellationToken.None));
        }

        // Cursor advances by one each call, so N consecutive picks cover all N owners.
        picks.Should().OnlyHaveUniqueItems();
        picks.Should().BeEquivalentTo(users.Select(u => (Guid?)u));
    }
}
