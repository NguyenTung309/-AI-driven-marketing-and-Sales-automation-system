using System.Text.RegularExpressions;

namespace Clawbot.Agents.Core.Skills.Lead;

public sealed record TimezoneGuess(string IanaTimezone, float Confidence, string Source);

public interface ITimezoneDetector : ISkill
{
    TimezoneGuess Detect(string? phone, string? locale, string? country);
}

// Heuristic E.164 country-code → IANA timezone map. No NodaTime/libphonenumber dependency.
// Default VN → Asia/Ho_Chi_Minh when nothing matches.
internal sealed class NodaTimezoneDetector : ITimezoneDetector
{
    public string Name => "timezone-detection";

    private static readonly Dictionary<string, (string Tz, string Name)> CountryTimezoneMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VN"] = ("Asia/Ho_Chi_Minh", "Vietnam"),
        ["CN"] = ("Asia/Shanghai", "China"),
        ["TW"] = ("Asia/Taipei", "Taiwan"),
        ["HK"] = ("Asia/Hong_Kong", "Hong Kong"),
        ["MO"] = ("Asia/Macau", "Macau"),
        ["JP"] = ("Asia/Tokyo", "Japan"),
        ["KR"] = ("Asia/Seoul", "South Korea"),
        ["TH"] = ("Asia/Bangkok", "Thailand"),
        ["ID"] = ("Asia/Jakarta", "Indonesia"),
        ["MY"] = ("Asia/Kuala_Lumpur", "Malaysia"),
        ["SG"] = ("Asia/Singapore", "Singapore"),
        ["PH"] = ("Asia/Manila", "Philippines"),
        ["IN"] = ("Asia/Kolkata", "India"),
        ["AU"] = ("Australia/Sydney", "Australia"),
        ["US"] = ("America/New_York", "United States"),
        ["GB"] = ("Europe/London", "United Kingdom"),
        ["DE"] = ("Europe/Berlin", "Germany"),
        ["FR"] = ("Europe/Paris", "France"),
        ["RU"] = ("Europe/Moscow", "Russia"),
        ["BR"] = ("America/Sao_Paulo", "Brazil"),
    };

    // E.164 prefix → country code (most common)
    private static readonly (string Prefix, string Country, int MaxLen)[] PhonePrefixes =
    {
        ("84", "VN", 11), ("86", "CN", 13), ("886", "TW", 12), ("852", "HK", 11),
        ("853", "MO", 11), ("81", "JP", 12), ("82", "KR", 12), ("66", "TH", 11),
        ("62", "ID", 13), ("60", "MY", 12), ("65", "SG", 10), ("63", "PH", 12),
        ("91", "IN", 12), ("61", "AU", 11), ("1", "US", 11), ("44", "GB", 12),
        ("49", "DE", 13), ("33", "FR", 11), ("7", "RU", 12), ("55", "BR", 13),
    };

    // locale → country code
    private static readonly Dictionary<string, string> LocaleCountryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vi"] = "VN", ["vi-vn"] = "VN", ["zh"] = "CN", ["zh-cn"] = "CN",
        ["zh-tw"] = "TW", ["ja"] = "JP", ["ko"] = "KR", ["th"] = "TH",
        ["en"] = "US", ["en-us"] = "US", ["en-gb"] = "GB",
    };

    public TimezoneGuess Detect(string? phone, string? locale, string? country)
    {
        // 1. Try phone prefix (highest confidence)
        if (!string.IsNullOrWhiteSpace(phone))
        {
            var digits = new string(phone.Where(char.IsDigit).ToArray());
            var normalized = digits.StartsWith("00", StringComparison.Ordinal) ? digits[2..] : digits;
            if (normalized.StartsWith('+'))
                normalized = normalized[1..];

            // Try longest prefix first (to avoid "1" matching before "86")
            foreach (var (prefix, cc, maxLen) in PhonePrefixes.OrderByDescending(p => p.Prefix.Length))
            {
                if (normalized.StartsWith(prefix, StringComparison.Ordinal) &&
                    normalized.Length <= maxLen &&
                    CountryTimezoneMap.TryGetValue(cc, out var tz))
                {
                    return new TimezoneGuess(tz.Tz, 0.85f, $"phone_prefix({prefix})");
                }
            }
        }

        // 2. Try explicit country code
        if (!string.IsNullOrWhiteSpace(country))
        {
            var cc = country.Trim().ToUpperInvariant();
            if (cc.Length == 2 && CountryTimezoneMap.TryGetValue(cc, out var tz))
                return new TimezoneGuess(tz.Tz, 0.80f, "country_code");

            // Try full country name match
            foreach (var (code, entry) in CountryTimezoneMap)
            {
                if (string.Equals(entry.Name, cc, StringComparison.OrdinalIgnoreCase))
                    return new TimezoneGuess(entry.Tz, 0.75f, "country_name");
            }
        }

        // 3. Try locale
        if (!string.IsNullOrWhiteSpace(locale))
        {
            var loc = locale.Trim().ToLowerInvariant();
            if (LocaleCountryMap.TryGetValue(loc, out var cc) &&
                CountryTimezoneMap.TryGetValue(cc, out var tz))
            {
                return new TimezoneGuess(tz.Tz, 0.65f, $"locale({loc})");
            }
        }

        // 4. Default: VN
        return new TimezoneGuess("Asia/Ho_Chi_Minh", 0.30f, "default");
    }
}
