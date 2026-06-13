using Clawbot.Agents.Core.Ads;
using Clawbot.Domain.Ads;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Ads;

public sealed class AdsRuleEngineTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = new(2026, 6, 7, 14, 0, 0, TimeSpan.FromHours(7));

    [Fact]
    public void Evaluate_relative_cpl_above_target_multiplier_returns_pause()
    {
        var snapshot = new AdsMetricSnapshot(Cpl: 200, Frequency: 1, Ctr: 1.5m, Spend: 500, DailyBudget: 1000);
        var rules = new[] { AdsRule.Create(TenantId, "meta", "cpl", "gt", 1.5m, "pause", Now) };
        var result = AdsRuleEngine.Evaluate(snapshot, targetCpl: 100m, [], null, Now, rules);

        result.Should().ContainSingle().Which.Action.Should().Be("pause");
    }

    [Fact]
    public void Evaluate_relative_cpl_below_target_multiplier_returns_no_match()
    {
        var snapshot = new AdsMetricSnapshot(Cpl: 120, Frequency: 1, Ctr: 1.5m, Spend: 500, DailyBudget: 1000);
        var rules = new[] { AdsRule.Create(TenantId, "meta", "cpl", "gt", 1.5m, "pause", Now) };
        var result = AdsRuleEngine.Evaluate(snapshot, targetCpl: 100m, [], null, Now, rules);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_absolute_frequency_threshold_returns_rotate()
    {
        var snapshot = new AdsMetricSnapshot(Cpl: 50, Frequency: 3, Ctr: 1.0m, Spend: 500, DailyBudget: 1000);
        var rules = new[] { AdsRule.Create(TenantId, "meta", "frequency", "gt", 2m, "rotate", Now) };
        var result = AdsRuleEngine.Evaluate(snapshot, targetCpl: null, [], null, Now, rules);

        result.Should().ContainSingle().Which.Action.Should().Be("rotate");
    }

    [Fact]
    public void Evaluate_no_matching_rules_returns_empty()
    {
        var snapshot = new AdsMetricSnapshot(Cpl: 50, Frequency: 1, Ctr: 2.0m, Spend: 0.5m, DailyBudget: 1000);
        var rules = new[] { AdsRule.Create(TenantId, "meta", "spend", "gt", 0.9m, "alert", Now) };
        var result = AdsRuleEngine.Evaluate(snapshot, targetCpl: null, [], null, Now, rules);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_multiple_rules_returns_multiple_decisions()
    {
        // Spend kept below 90% so the proactive budget alert doesn't add a 4th decision here.
        var snapshot = new AdsMetricSnapshot(Cpl: 200, Frequency: 3, Ctr: 0.5m, Spend: 500, DailyBudget: 1000);
        var rules = new[]
        {
            AdsRule.Create(TenantId, "meta", "cpl", "gt", 1.5m, "pause", Now),
            AdsRule.Create(TenantId, "meta", "frequency", "gt", 2m, "rotate", Now),
            AdsRule.Create(TenantId, "meta", "ctr", "lt", 0.8m, "pause", Now),
        };
        var result = AdsRuleEngine.Evaluate(snapshot, targetCpl: 100m, [], null, Now, rules);

        result.Should().HaveCount(3);
    }

    [Fact]
    public void Evaluate_scale_up_blocked_without_3_day_good_cpl_streak()
    {
        var snapshot = new AdsMetricSnapshot(Cpl: 50, Frequency: 1, Ctr: 2.0m, Spend: 500, DailyBudget: 1000);
        var rules = new[] { AdsRule.Create(TenantId, "meta", "cpl", "lt", 0.7m, "scale_up", Now) };

        var last3Days = new List<AdsMetricSnapshot>
        {
            new(Cpl: 80, Frequency: 1, Ctr: 2.0m, Spend: 500, DailyBudget: 1000),
            new(Cpl: 50, Frequency: 1, Ctr: 2.0m, Spend: 500, DailyBudget: 1000),
            new(Cpl: 50, Frequency: 1, Ctr: 2.0m, Spend: 500, DailyBudget: 1000),
        };

        var result = AdsRuleEngine.Evaluate(snapshot, targetCpl: 100m, last3Days, null, Now, rules);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_scale_up_allowed_with_3_day_good_cpl_streak()
    {
        var snapshot = new AdsMetricSnapshot(Cpl: 50, Frequency: 1, Ctr: 2.0m, Spend: 500, DailyBudget: 1000);
        var rules = new[] { AdsRule.Create(TenantId, "meta", "cpl", "lt", 0.7m, "scale_up", Now) };

        var last3Days = new List<AdsMetricSnapshot>
        {
            new(Cpl: 60, Frequency: 1, Ctr: 2.0m, Spend: 500, DailyBudget: 1000),
            new(Cpl: 50, Frequency: 1, Ctr: 2.0m, Spend: 500, DailyBudget: 1000),
            new(Cpl: 50, Frequency: 1, Ctr: 2.0m, Spend: 500, DailyBudget: 1000),
        };

        var result = AdsRuleEngine.Evaluate(snapshot, targetCpl: 100m, last3Days, null, Now, rules);

        result.Should().ContainSingle().Which.Action.Should().Be("scale_up");
    }

    [Fact]
    public void Evaluate_scale_up_blocked_within_24h_cooldown()
    {
        var snapshot = new AdsMetricSnapshot(Cpl: 50, Frequency: 1, Ctr: 2.0m, Spend: 500, DailyBudget: 1000);
        var rules = new[] { AdsRule.Create(TenantId, "meta", "cpl", "lt", 0.7m, "scale_up", Now) };

        var last3Days = new List<AdsMetricSnapshot>
        {
            new(Cpl: 60, Frequency: 1, Ctr: 2.0m, Spend: 500, DailyBudget: 1000),
            new(Cpl: 50, Frequency: 1, Ctr: 2.0m, Spend: 500, DailyBudget: 1000),
            new(Cpl: 50, Frequency: 1, Ctr: 2.0m, Spend: 500, DailyBudget: 1000),
        };

        var lastScaledAt = Now.AddHours(-12);
        var result = AdsRuleEngine.Evaluate(snapshot, targetCpl: 100m, last3Days, lastScaledAt, Now, rules);

        result.Should().BeEmpty();
    }

    [Fact]
    public void IsQuietHour_returns_true_between_02_and_05_gmt7()
    {
        var quietTime = new DateTimeOffset(2026, 6, 7, 3, 0, 0, TimeSpan.FromHours(7));
        AdsRuleEngine.IsQuietHour(quietTime).Should().BeTrue();

        var activeTime = new DateTimeOffset(2026, 6, 7, 10, 0, 0, TimeSpan.FromHours(7));
        AdsRuleEngine.IsQuietHour(activeTime).Should().BeFalse();
    }

    [Fact]
    public void Evaluate_during_quiet_hour_returns_dayparting_pause_only()
    {
        var quietTime = new DateTimeOffset(2026, 6, 7, 3, 0, 0, TimeSpan.FromHours(7));
        // Spend below 90% so only the dayparting pause is returned (no budget alert).
        var snapshot = new AdsMetricSnapshot(Cpl: 200, Frequency: 3, Ctr: 0.5m, Spend: 500, DailyBudget: 1000);
        var rules = new[]
        {
            AdsRule.Create(TenantId, "meta", "cpl", "gt", 1.5m, "pause", Now),
            AdsRule.Create(TenantId, "meta", "frequency", "gt", 2m, "rotate", Now),
        };

        var result = AdsRuleEngine.Evaluate(snapshot, targetCpl: 100m, [], null, quietTime, rules);

        result.Should().ContainSingle();
        result[0].Action.Should().Be("pause");
        result[0].Note.Should().Contain("quiet hour");
    }

    [Fact]
    public void Evaluate_budget_at_90pct_returns_budget_alert()
    {
        // Ads-1: spend >= 90% of daily budget fires a proactive alert, even during quiet hours.
        var quietTime = new DateTimeOffset(2026, 6, 7, 3, 0, 0, TimeSpan.FromHours(7));
        var snapshot = new AdsMetricSnapshot(Cpl: 50, Frequency: 1, Ctr: 2.0m, Spend: 950, DailyBudget: 1000);

        var result = AdsRuleEngine.Evaluate(snapshot, targetCpl: 100m, [], null, quietTime, []);

        result.Should().Contain(d => d.Action == "alert" && d.Metric == "budget");
    }
}
