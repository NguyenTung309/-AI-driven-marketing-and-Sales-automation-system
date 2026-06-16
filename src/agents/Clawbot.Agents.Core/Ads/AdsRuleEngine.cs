using Clawbot.Domain.Ads;

namespace Clawbot.Agents.Core.Ads;

public static class AdsRuleEngine
{
    private static readonly TimeOnly QuietStart = new(2, 0);
    private static readonly TimeOnly QuietEnd = new(5, 0);

    // Ads-1: proactive budget-spend alert threshold (90% of daily budget).
    private const decimal BudgetAlertRatio = 0.9m;

    public static IReadOnlyList<AdsDecision> Evaluate(
        AdsMetricSnapshot current,
        decimal? targetCpl,
        IReadOnlyList<AdsMetricSnapshot> last3Days,
        DateTimeOffset? lastScaledAt,
        DateTimeOffset nowGmt7,
        IReadOnlyList<AdsRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(last3Days);

        var decisions = new List<AdsDecision>();

        // Ads-1: proactive budget alert — fires regardless of quiet hours / rules so spend
        // hitting 90% is surfaced even overnight. Notification fan-out handled by the caller.
        if (current.DailyBudget > 0 && current.Spend / current.DailyBudget >= BudgetAlertRatio)
        {
            decisions.Add(new AdsDecision(null, "budget", "alert",
                $"spend {current.Spend:F2} / budget {current.DailyBudget:F2} >= 90%"));
        }

        if (IsQuietHour(nowGmt7))
        {
            decisions.Add(new AdsDecision(null, "dayparting", "pause", "quiet hour 02:00–05:00 GMT+7"));
            return decisions;
        }

        foreach (var rule in rules.Where(r => r.IsActive))
        {
            var effectiveThreshold = rule.Threshold;
            if (string.Equals(rule.Metric, "cpl", StringComparison.OrdinalIgnoreCase) && targetCpl.HasValue)
                effectiveThreshold = targetCpl.Value * rule.Threshold;

            var currentValue = GetMetric(current, rule.Metric);
            if (currentValue is null)
                continue;

            var matched = MatchesComparator(currentValue.Value, rule.Comparator, effectiveThreshold);
            if (!matched)
                continue;

            if (string.Equals(rule.Action, "scale_up", StringComparison.OrdinalIgnoreCase))
            {
                if (!CplGoodAcrossDays(last3Days, targetCpl))
                    continue;
                if (lastScaledAt.HasValue && (nowGmt7 - lastScaledAt.Value).TotalHours < 24)
                    continue;
            }

            var note = $"{rule.Metric}={currentValue.Value:F2} {rule.Comparator} {effectiveThreshold:F2}";
            decisions.Add(new AdsDecision(rule.Id, rule.Metric, rule.Action, note));
        }

        return ClampScaleDecisions(decisions);
    }

    public static bool IsQuietHour(DateTimeOffset gmt7)
    {
        var localTime = TimeOnly.FromDateTime(gmt7.ToOffset(TimeSpan.FromHours(7)).DateTime);
        return localTime >= QuietStart && localTime < QuietEnd;
    }

    private static bool CplGoodAcrossDays(IReadOnlyList<AdsMetricSnapshot> days, decimal? targetCpl)
    {
        if (!targetCpl.HasValue || days.Count < 3)
            return false;

        var goodThreshold = targetCpl.Value * 0.7m;
        return days.Take(3).All(d => d.Cpl > 0 && d.Cpl <= goodThreshold);
    }

    private static decimal? GetMetric(AdsMetricSnapshot snapshot, string metric) =>
        metric.ToLowerInvariant() switch
        {
            "cpl" => snapshot.Cpl,
            "frequency" => snapshot.Frequency,
            "ctr" => snapshot.Ctr,
            "spend" => snapshot.Spend,
            _ => null,
        };

    private static bool MatchesComparator(decimal value, string comparator, decimal threshold) =>
        comparator.ToLowerInvariant() switch
        {
            "gt" => value > threshold,
            "lt" => value < threshold,
            "gte" => value >= threshold,
            "lte" => value <= threshold,
            "eq" => value == threshold,
            _ => false,
        };

    private static IReadOnlyList<AdsDecision> ClampScaleDecisions(IReadOnlyList<AdsDecision> decisions)
    {
        var scaleUps = decisions.Where(d => d.Action == "scale_up").ToList();
        if (scaleUps.Count <= 1)
            return decisions;

        return decisions.Where(d => d.Action != "scale_up")
            .Concat(scaleUps.Take(1))
            .ToList();
    }
}
