namespace Clawbot.Agents.Core.Skills.Lead;

public sealed record TimezoneGuess(string IanaTimezone, float Confidence, string Source);

public interface ITimezoneDetector : ISkill
{
    TimezoneGuess Detect(string? phone, string? locale, string? country);
}

// Source: https://nodatime.org + ITU phone country-code lookup.
// Default VN → Asia/Ho_Chi_Minh when nothing matches.
internal sealed class NodaTimezoneDetector : ITimezoneDetector
{
    public string Name => "timezone-detection";

    public TimezoneGuess Detect(string? phone, string? locale, string? country) =>
        throw new NotImplementedException("TODO: parse phone country code via libphonenumber + map to NodaTime DateTimeZone.");
}
