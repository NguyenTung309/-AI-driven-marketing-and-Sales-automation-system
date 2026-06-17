namespace Clawbot.Agents.Core.Skills.Nlp;

public sealed record ToxicityScores(
    float Toxicity,
    float Insult,
    float Threat,
    float SexualExplicit,
    float Profanity);

public interface IToxicityFilter : ISkill
{
    Task<ToxicityScores> ScoreAsync(string text, CancellationToken ct);
    Task<bool> IsBlockedAsync(string text, float threshold, CancellationToken ct);
}

public sealed class ToxicityOptions
{
    public const string SectionName = "Skills:Toxicity";
    public string SidecarUrl { get; set; } = string.Empty;
    public float InboundBlockThreshold { get; set; } = 0.7f;
    public float OutboundBlockThreshold { get; set; } = 0.8f;
    public float DraftBlockThreshold { get; set; } = 0.6f;
}

// Heuristic baseline: VI/EN profanity lexicon + caps/emoji-flood signals.
// Optional detoxify/Perspective sidecar when Skills:Toxicity:SidecarUrl is set.
public sealed class DetoxifyToxicityFilter : IToxicityFilter
{
    public string Name => "toxicity-filter";

    private static readonly string[] ProfanityVi =
    {
        "đéo", "đụ", "lồn", "cặc", "đít", "cc", "vl", "dm", "dmm",
        "đm", "đmm", "clgt", "súc vật", "óc chó", "thằng chó", "con chó",
        "cứt", "mẹ mày", "bố mày", "chó chết", "não tàn", "ngu", "đần"
    };

    private static readonly string[] ProfanityEn =
    {
        "fuck", "shit", "bitch", "asshole", "dick", "bastard", "crap",
        "damn", "stupid", "idiot", "moron", "retard", "slut", "whore",
        "piss", "cock", "pussy", "wtf", "stfu", "gtfo"
    };

    private static readonly string[] ThreatWords =
    {
        "giết", "chết", "đánh", "đâm", "bắn", "phá", "thiêu", "tấn công",
        "kill", "murder", "death", "attack", "bomb", "shoot", "stab", "destroy",
        "kill you", "giết mày", "giết bạn"
    };

    private static readonly string[] InsultWords =
    {
        "ngu", "đần", "óc heo", "bần", "hèn", "khốn", "đồ", "thằng", "con",
        "stupid", "idiot", "fool", "loser", "dumb", "pathetic", "worthless",
        "废物", "白痴", "蠢货"
    };

    public Task<ToxicityScores> ScoreAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(new ToxicityScores(0f, 0f, 0f, 0f, 0f));

        var lower = text.ToLowerInvariant();
        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount == 0) wordCount = 1;

        var profanityHits = CountHits(lower, ProfanityVi) + CountHits(lower, ProfanityEn);
        var threatHits = CountHits(lower, ThreatWords);
        var insultHits = CountHits(lower, InsultWords);

        var capsRatio = CountUpperCase(text) / (float)Math.Max(text.Length, 1);
        var emojiCount = CountEmoji(text);
        var emojiRatio = emojiCount / (float)Math.Max(wordCount, 1);

        var profanity = Math.Min(profanityHits * 0.25f + (capsRatio > 0.7f ? 0.1f : 0f), 1f);
        var threat = Math.Min(threatHits * 0.35f, 1f);
        var insult = Math.Min(insultHits * 0.30f, 1f);
        var sexual = 0f; // heuristic can't reliably detect; sidecar upgrade path
        var toxicity = Math.Min(
            profanity * 0.4f + threat * 0.3f + insult * 0.2f + (emojiRatio > 0.5f ? 0.1f : 0f),
            1f);

        return Task.FromResult(new ToxicityScores(toxicity, insult, threat, sexual, profanity));
    }

    public async Task<bool> IsBlockedAsync(string text, float threshold, CancellationToken ct)
    {
        var scores = await ScoreAsync(text, ct).ConfigureAwait(false);
        return scores.Toxicity >= threshold;
    }

    private static int CountHits(string lower, string[] lexicon)
    {
        var count = 0;
        foreach (var word in lexicon)
        {
            var idx = 0;
            while ((idx = lower.IndexOf(word, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += word.Length;
            }
        }
        return count;
    }

    private static int CountUpperCase(string text)
    {
        var count = 0;
        foreach (var ch in text)
            if (char.IsLetter(ch) && char.IsUpper(ch)) count++;
        return count;
    }

    private static int CountEmoji(string text)
    {
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var cp = rune.Value;
            if (cp >= 0x1F600 && cp <= 0x1F64F) count++;  // Emoticons
            else if (cp >= 0x1F300 && cp <= 0x1F5FF) count++;  // Misc Symbols
            else if (cp >= 0x1F680 && cp <= 0x1F6FF) count++;  // Transport
            else if (cp >= 0x1F900 && cp <= 0x1F9FF) count++;  // Supplemental
            else if (cp >= 0x2600 && cp <= 0x26FF) count++;    // Misc symbols
            else if (cp >= 0x2700 && cp <= 0x27BF) count++;    // Dingbats
        }
        return count;
    }
}
