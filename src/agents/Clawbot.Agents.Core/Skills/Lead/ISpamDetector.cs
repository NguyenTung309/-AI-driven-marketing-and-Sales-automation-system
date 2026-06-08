using System.Text.RegularExpressions;

namespace Clawbot.Agents.Core.Skills.Lead;

public sealed record SpamSignal(bool IsSpam, float Confidence, string? Reason);

public interface ISpamDetector : ISkill
{
    Task<SpamSignal> EvaluateAsync(string text, string? senderHandle, string? sourcePlatform, CancellationToken ct);
}

// Heuristic baseline: URL flood, repeated emoji, scam-keyword lexicon, link ratio.
// Optional Akismet HTTP when Skills:Spam:AkismetKey is set.
internal sealed partial class AkismetSpamDetector : ISpamDetector
{
    public string Name => "spam-detection";

    private static readonly string[] ScamKeywords =
    {
        "kiếm tiền", "thu nhập thụ động", "đầu tư", "lãi suất", "chia sẻ cơ hội",
        "nhân đôi", "trúng thưởng", "miễn phí 100%", "inbox ngay", "zalo",
        "earn money", "passive income", "investment", "double your",
        "winner", "congratulations", "claim now", "act fast", "limited time",
        "赚", "免费", "投资", "中奖", "兼职"
    };

    private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EmojiRegex = new(
        @"[\u2600-\u26FF\u2700-\u27BF]|[\uD83C-\uD83E][\uDC00-\uDFFF]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Task<SpamSignal> EvaluateAsync(string text, string? senderHandle, string? sourcePlatform, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(new SpamSignal(false, 0f, null));

        var lower = text.ToLowerInvariant();
        var reasons = new List<string>();
        var score = 0f;

        // URL flood: >3 URLs or URLs > 40% of text length
        var urls = UrlRegex.Matches(text);
        if (urls.Count > 3)
        {
            score += 0.4f;
            reasons.Add($"url_flood({urls.Count})");
        }
        else if (urls.Count > 0)
        {
            var urlChars = urls.Sum(m => m.Length);
            if (urlChars > text.Length * 0.4)
            {
                score += 0.3f;
                reasons.Add($"url_heavy({urlChars}/{text.Length})");
            }
        }

        // Emoji flood: >5 consecutive or >10 total
        var emojis = EmojiRegex.Matches(text);
        if (emojis.Count > 10)
        {
            score += 0.3f;
            reasons.Add($"emoji_flood({emojis.Count})");
        }
        else if (HasConsecutiveEmoji(text, 5))
        {
            score += 0.2f;
            reasons.Add("consecutive_emoji");
        }

        // Scam keywords
        var scamHits = 0;
        foreach (var kw in ScamKeywords)
        {
            if (lower.Contains(kw, StringComparison.Ordinal))
                scamHits++;
        }
        if (scamHits >= 3)
        {
            score += 0.4f;
            reasons.Add($"scam_keywords({scamHits})");
        }
        else if (scamHits >= 1)
        {
            score += 0.15f;
            reasons.Add($"scam_keyword({scamHits})");
        }

        // Repeated characters (e.g., "aaaaaaa" or "!!!!!!!!")
        if (RepeatedCharRegex().IsMatch(text))
        {
            score += 0.1f;
            reasons.Add("repeated_chars");
        }

        // ALL CAPS message > 20 chars
        if (text.Length > 20 && IsAllCaps(text))
        {
            score += 0.15f;
            reasons.Add("all_caps");
        }

        var confidence = Math.Min(score, 1f);
        var isSpam = confidence >= 0.5f;
        var reason = reasons.Count > 0 ? string.Join("; ", reasons) : null;

        return Task.FromResult(new SpamSignal(isSpam, confidence, reason));
    }

    private static bool HasConsecutiveEmoji(string text, int threshold)
    {
        var consecutive = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var cp = rune.Value;
            if ((cp >= 0x1F300 && cp <= 0x1F9FF) || (cp >= 0x2600 && cp <= 0x27BF))
            {
                if (++consecutive >= threshold) return true;
            }
            else
            {
                consecutive = 0;
            }
        }
        return false;
    }

    private static bool IsAllCaps(string text)
    {
        var letters = 0;
        var upper = 0;
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                letters++;
                if (char.IsUpper(ch)) upper++;
            }
        }
        return letters > 0 && (float)upper / letters > 0.85f;
    }

    [GeneratedRegex(@"(.)\1{4,}", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedCharRegex();
}
