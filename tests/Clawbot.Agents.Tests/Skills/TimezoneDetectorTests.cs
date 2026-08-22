using Clawbot.Agents.Core.Skills.Lead;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Heuristic timezone detector: ưu tiên phone prefix > country > locale > default VN.
public sealed class TimezoneDetectorTests
{
    private static NodaTimezoneDetector NewDetector() => new();

    [Fact]
    public void Name_IsTimezoneDetection()
    {
        NewDetector().Name.Should().Be("timezone-detection");
    }

    [Theory]
    [InlineData("+84987654321", "Asia/Ho_Chi_Minh")]
    [InlineData("0084987654321", "Asia/Ho_Chi_Minh")] // 00 prefix stripped
    [InlineData("+8613800138000", "Asia/Shanghai")]
    [InlineData("+886912345678", "Asia/Taipei")]
    [InlineData("+6591234567", "Asia/Singapore")]
    public void Detect_ByPhonePrefix_ReturnsHighConfidence(string phone, string expectedTz)
    {
        var guess = NewDetector().Detect(phone, null, null);

        guess.IanaTimezone.Should().Be(expectedTz);
        guess.Confidence.Should().Be(0.85f);
        guess.Source.Should().StartWith("phone_prefix");
    }

    [Fact]
    public void Detect_PrefersLongerPrefix_OverShorter()
    {
        // 886 (TW) must win over 8/86 for a Taiwan number.
        var guess = NewDetector().Detect("+886223456789", null, null);

        guess.IanaTimezone.Should().Be("Asia/Taipei");
    }

    [Fact]
    public void Detect_PhoneTooLongForPrefix_FallsThrough()
    {
        // 20 digits exceeds every MaxLen so no phone match; falls to default.
        var guess = NewDetector().Detect("84999999999999999999", null, null);

        guess.Source.Should().Be("default");
    }

    [Theory]
    [InlineData("VN", "Asia/Ho_Chi_Minh")]
    [InlineData("us", "America/New_York")]
    [InlineData("GB", "Europe/London")]
    public void Detect_ByCountryCode_ReturnsCountryConfidence(string country, string expectedTz)
    {
        var guess = NewDetector().Detect(null, null, country);

        guess.IanaTimezone.Should().Be(expectedTz);
        guess.Confidence.Should().Be(0.80f);
        guess.Source.Should().Be("country_code");
    }

    [Fact]
    public void Detect_ByCountryName_ReturnsNameConfidence()
    {
        var guess = NewDetector().Detect(null, null, "Vietnam");

        guess.IanaTimezone.Should().Be("Asia/Ho_Chi_Minh");
        guess.Confidence.Should().Be(0.75f);
        guess.Source.Should().Be("country_name");
    }

    [Theory]
    [InlineData("vi-vn", "Asia/Ho_Chi_Minh")]
    [InlineData("ja", "Asia/Tokyo")]
    [InlineData("en-gb", "Europe/London")]
    public void Detect_ByLocale_ReturnsLocaleConfidence(string locale, string expectedTz)
    {
        var guess = NewDetector().Detect(null, locale, null);

        guess.IanaTimezone.Should().Be(expectedTz);
        guess.Confidence.Should().Be(0.65f);
        guess.Source.Should().StartWith("locale");
    }

    [Fact]
    public void Detect_NothingMatches_DefaultsToVietnam()
    {
        var guess = NewDetector().Detect(null, null, null);

        guess.IanaTimezone.Should().Be("Asia/Ho_Chi_Minh");
        guess.Confidence.Should().Be(0.30f);
        guess.Source.Should().Be("default");
    }

    [Fact]
    public void Detect_UnknownCountryCode_FallsToDefault()
    {
        var guess = NewDetector().Detect(null, null, "ZZ");

        guess.Source.Should().Be("default");
    }

    [Fact]
    public void Detect_PhoneWins_OverCountryAndLocale()
    {
        // Phone (VN) beats an explicit US country + JP locale.
        var guess = NewDetector().Detect("+84987654321", "ja", "US");

        guess.IanaTimezone.Should().Be("Asia/Ho_Chi_Minh");
        guess.Source.Should().StartWith("phone_prefix");
    }
}
