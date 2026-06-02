using Clawbot.Domain.Leads;

namespace Clawbot.Agents.Core.Lead;

public sealed record ScoringDecision(int Delta, string Reason, IReadOnlyList<string> MatchedRules);

public static class LeadScoringEngine
{
    public static ScoringDecision Evaluate(
        string eventCode,
        string? platform,
        IReadOnlyList<LeadScoringRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (string.IsNullOrWhiteSpace(eventCode))
            return new ScoringDecision(0, "no event_code", Array.Empty<string>());

        var matched = rules
            .Where(r => r.IsActive
                && string.Equals(r.EventCode, eventCode, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(r.Platform) || string.Equals(r.Platform, platform, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matched.Count == 0)
            return new ScoringDecision(0, $"no rule for event={eventCode}", Array.Empty<string>());

        var delta = matched.Sum(r => r.Weight);
        var reasons = matched.Select(r => $"{r.EventCode}{(string.IsNullOrEmpty(r.Platform) ? "" : "/" + r.Platform)}:{r.Weight:+#;-#;0}").ToList();
        return new ScoringDecision(delta, string.Join(", ", reasons), reasons);
    }
}
