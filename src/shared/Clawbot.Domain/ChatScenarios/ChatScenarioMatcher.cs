using System.Text.RegularExpressions;

namespace Clawbot.Domain.ChatScenarios;

/// <summary>
/// Pure scenario matcher (M05). Given an inbound text + optional platform, picks the
/// best <see cref="ChatScenario"/> from a candidate set. No DB / no I/O — fully unit-testable.
///
/// Match strategy per candidate, highest score wins:
///   1. <see cref="ChatScenario.TriggerText"/> treated as a regex (case-insensitive). A regex hit
///      scores highest; the longer the matched span the higher the score (more specific).
///   2. If the trigger is not a valid regex, fall back to whole-trigger substring containment.
///   3. Ties broken by higher <see cref="ChatScenario.SuccessRate"/>, then by <c>Code</c> for determinism.
/// Platform filter: a scenario matches a platform when its CSV <see cref="ChatScenario.Platforms"/>
/// is empty, contains "all", or contains the requested platform (case-insensitive).
/// </summary>
public static class ChatScenarioMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static ChatScenario? Match(
        string text,
        string? platform,
        IEnumerable<ChatScenario> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        ChatScenario? best = null;
        var bestScore = 0;
        decimal bestRate = -1m;

        foreach (var scenario in candidates)
        {
            if (!PlatformAllowed(scenario.Platforms, platform))
                continue;

            var score = Score(scenario.TriggerText, text);
            if (score <= 0)
                continue;

            var rate = scenario.SuccessRate ?? 0m;
            var wins = score > bestScore
                || (score == bestScore && rate > bestRate)
                || (score == bestScore && rate == bestRate
                    && (best is null || string.CompareOrdinal(scenario.Code, best.Code) < 0));

            if (!wins)
                continue;

            best = scenario;
            bestScore = score;
            bestRate = rate;
        }

        return best;
    }

    private static bool PlatformAllowed(string platforms, string? requested)
    {
        if (string.IsNullOrWhiteSpace(platforms) || string.IsNullOrWhiteSpace(requested))
            return true;

        foreach (var part in platforms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals("all", StringComparison.OrdinalIgnoreCase)
                || part.Equals(requested, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static int Score(string trigger, string text)
    {
        if (string.IsNullOrWhiteSpace(trigger))
            return 0;

        try
        {
            var match = Regex.Match(
                text,
                trigger,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexTimeout);

            // Regex hit: specificity = matched span length (longer pattern hit = more specific).
            if (match.Success && match.Length > 0)
                return 1000 + match.Length;
        }
        catch (RegexParseException)
        {
            // Trigger is not a valid regex — fall through to substring containment.
        }
        catch (RegexMatchTimeoutException)
        {
            return 0;
        }

        return text.Contains(trigger, StringComparison.OrdinalIgnoreCase)
            ? trigger.Length
            : 0;
    }
}
