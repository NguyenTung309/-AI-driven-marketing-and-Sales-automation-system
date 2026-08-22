using Clawbot.Agents.Core.Chat;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Mã lỗi + IsAllowed cho từng verdict base URL.
public sealed class BaseUrlVerdictExtensionsTests
{
    [Theory]
    [InlineData(BaseUrlVerdict.Allowed, true)]
    [InlineData(BaseUrlVerdict.AllowedDnsUnverified, true)]
    [InlineData(BaseUrlVerdict.Malformed, false)]
    [InlineData(BaseUrlVerdict.SchemeNotAllowed, false)]
    [InlineData(BaseUrlVerdict.PrivateHostNotGranted, false)]
    [InlineData(BaseUrlVerdict.MixedDnsAnswer, false)]
    public void IsAllowed_MatchesVerdict(BaseUrlVerdict verdict, bool expected)
    {
        verdict.IsAllowed().Should().Be(expected);
    }

    [Theory]
    [InlineData(BaseUrlVerdict.SchemeNotAllowed, "base_url_requires_https")]
    [InlineData(BaseUrlVerdict.PrivateHostNotGranted, "base_url_private_host")]
    [InlineData(BaseUrlVerdict.MixedDnsAnswer, "base_url_mixed_dns")]
    [InlineData(BaseUrlVerdict.Malformed, "invalid_base_url")]
    [InlineData(BaseUrlVerdict.Allowed, "invalid_base_url")]
    [InlineData(BaseUrlVerdict.AllowedDnsUnverified, "invalid_base_url")]
    public void ToErrorCode_MapsVerdict(BaseUrlVerdict verdict, string expected)
    {
        verdict.ToErrorCode().Should().Be(expected);
    }
}
