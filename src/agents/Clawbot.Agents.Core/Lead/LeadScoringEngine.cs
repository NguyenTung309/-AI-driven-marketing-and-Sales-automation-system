using Clawbot.Domain.Leads;

namespace Clawbot.Agents.Core.Lead;

public sealed record ScoringDecision(int Delta, string Reason, IReadOnlyList<string> MatchedRules);

public static class LeadScoringEngine
{
    // Classifier emits asked_*; older tenant seeds used asks_*. Prefer exact, then alias.
    private static readonly Dictionary<string, string[]> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["asked_price"] = ["asks_price"],
            ["asks_price"] = ["asked_price"],
            ["asked_schedule"] = ["asks_schedule"],
            ["asks_schedule"] = ["asked_schedule"],
            ["asked_class_size"] = ["asks_class_size", "asks_curriculum"],
            ["asks_class_size"] = ["asked_class_size"],
            ["asks_curriculum"] = ["asked_class_size"],
            ["asked_teacher"] = ["asks_teacher"],
            ["asks_teacher"] = ["asked_teacher"],
            ["asked_commitment"] = ["asks_commitment"],
            ["asks_commitment"] = ["asked_commitment"],
            ["asked_substantive_question"] = ["asks_question", "substantive_question"],
            ["purchase_intent"] = ["confirms_enroll", "sends_payment", "purchase"],
            ["confirms_enroll"] = ["purchase_intent"],
            ["sends_payment"] = ["purchase_intent"],
            ["fast_reply"] = ["quick_reply"],
        };

    public static ScoringDecision Evaluate(
        string eventCode,
        string? platform,
        IReadOnlyList<LeadScoringRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (string.IsNullOrWhiteSpace(eventCode))
            return new ScoringDecision(0, "no event_code", Array.Empty<string>());

        var code = eventCode.Trim();
        var matched = MatchRules(code, platform, rules, exactOnly: true);
        if (matched.Count == 0 && Aliases.TryGetValue(code, out var aliases))
        {
            foreach (var alias in aliases)
            {
                matched = MatchRules(alias, platform, rules, exactOnly: true);
                if (matched.Count > 0) break;
            }
        }

        if (matched.Count == 0)
            return new ScoringDecision(0, $"no rule for event={eventCode}", Array.Empty<string>());

        // Prefer platform-specific rule over platform-null when both match.
        var preferred = PreferPlatformSpecific(matched, platform);
        var delta = preferred.Sum(r => r.Weight);
        var reasons = preferred.Select(r => $"{r.EventCode}{(string.IsNullOrEmpty(r.Platform) ? "" : "/" + r.Platform)}:{r.Weight:+#;-#;0}").ToList();
        return new ScoringDecision(delta, string.Join(", ", reasons), reasons);
    }

    private static List<LeadScoringRule> MatchRules(
        string eventCode,
        string? platform,
        IReadOnlyList<LeadScoringRule> rules,
        bool exactOnly)
    {
        _ = exactOnly;
        return rules
            .Where(r => r.IsActive
                && string.Equals(r.EventCode, eventCode, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(r.Platform) || string.Equals(r.Platform, platform, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static List<LeadScoringRule> PreferPlatformSpecific(List<LeadScoringRule> matched, string? platform)
    {
        if (string.IsNullOrEmpty(platform))
            return matched;

        var specific = matched
            .Where(r => string.Equals(r.Platform, platform, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return specific.Count > 0 ? specific : matched;
    }
}
