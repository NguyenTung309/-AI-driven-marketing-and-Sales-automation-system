using System.Globalization;
using System.Text;

namespace Clawbot.Agents.Core.Skills.Nlp;

public sealed record LanguageDetection(string LanguageCode, float Confidence);  // vi|en|zh|...

public interface ILanguageDetector : ISkill
{
    Task<LanguageDetection> DetectAsync(string text, CancellationToken ct);
}

// Heuristic baseline: Unicode-block + Vietnamese-diacritic + CJK ratio.
// Optional fasttext sidecar HTTP when Skills:Language:SidecarUrl is set.
internal sealed class FastTextLanguageDetector : ILanguageDetector
{
    public string Name => "language-detection";

    public Task<LanguageDetection> DetectAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult(new LanguageDetection("unknown", 0f));

        var scores = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
        {
            ["vi"] = 0f, ["zh"] = 0f, ["ja"] = 0f, ["ko"] = 0f, ["en"] = 0f, ["th"] = 0f
        };

        var total = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.IsPunctuation(rune)) continue;
            total++;
            var cp = rune.Value;

            if (cp >= 0x4E00 && cp <= 0x9FFF) { scores["zh"] += 1f; continue; }       // CJK Unified
            if (cp >= 0x3040 && cp <= 0x30FF) { scores["ja"] += 1f; continue; }       // Hiragana+Katakana
            if (cp >= 0xAC00 && cp <= 0xD7AF) { scores["ko"] += 1f; continue; }       // Hangul
            if (cp >= 0x0E00 && cp <= 0x0E7F) { scores["th"] += 1f; continue; }       // Thai
            if (cp >= 0x0041 && cp <= 0x007A) { scores["en"] += 1f; continue; }       // Basic Latin
        }

        if (total == 0)
            return Task.FromResult(new LanguageDetection("unknown", 0f));

        // Vietnamese diacritic detection (strong signal — even a few diacritics in Latin text = Vietnamese)
        var viDiacritics = CountVietnameseDiacritics(text);
        var viScore = viDiacritics / (float)total;

        // Normalize CJK/JA/KO/TH scores
        foreach (var key in scores.Keys.ToList())
            scores[key] /= total;

        // Boost Vietnamese: if diacritics found in mostly-Lext text, override "en"
        if (viDiacritics > 0 && scores["en"] > 0.3f)
        {
            // Vietnamese diacritics in Latin text = almost certainly Vietnamese
            scores["vi"] = Math.Max(0.5f + viScore, viScore);
            scores["en"] *= 0.3f; // demote English
        }
        else
        {
            scores["vi"] = Math.Max(scores["vi"], viScore);
        }

        // CJK disambiguation: if no hiragana/katakana → likely zh not ja
        if (scores["zh"] > 0.1f && scores["ja"] < 0.02f)
        {
            scores["zh"] += scores["ja"];
            scores["ja"] = 0f;
        }

        var best = scores.OrderByDescending(kv => kv.Value).First();
        if (best.Value < 0.05f)
            return Task.FromResult(new LanguageDetection("en", 0.30f));

        var confidence = Math.Min(best.Value * 1.5f, 0.95f);
        return Task.FromResult(new LanguageDetection(best.Key, confidence));
    }

    private static int CountVietnameseDiacritics(string text)
    {
        var count = 0;
        foreach (var ch in text)
        {
            // Vietnamese-specific combining marks and precomposed chars
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.SpacingCombiningMark)
            {
                count++;
                continue;
            }
            // Common Vietnamese vowels with diacritics
            if ("àáảãạăắằẳẵặâấầẩẫậèéẻẽẹêếềểễệìíỉĩịòóỏõọôốồổỗộơớờởỡợùúủũụưứừửữựỳýỷỹỵ"
                .Contains(ch))
                count++;
        }
        return count;
    }
}
