using Clawbot.Infrastructure.Channels.Pancake;
using FluentAssertions;

namespace Clawbot.Infrastructure.Tests.Channels.Pancake;

// Chốt chặn base URL Pancake: chỉ https://pages.fm + path trong allowlist, đá mọi thứ khác (SSRF guard).
public sealed class PancakeEndpointPolicyTests
{
    [Theory]
    [InlineData("https://pages.fm/api/v1", "https://pages.fm/api/v1")]
    [InlineData("https://pages.fm/api/public_api/v1", "https://pages.fm/api/public_api/v1")]
    [InlineData("https://pages.fm/api/public_api/v2", "https://pages.fm/api/public_api/v2")]
    [InlineData("  https://PAGES.fm/api/v1/  ", "https://pages.fm/api/v1")]
    public void NormalizeBaseUrl_AllowedHostAndPath_ReturnsCanonical(string input, string expected)
    {
        PancakeEndpointPolicy.NormalizeBaseUrl(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("http://pages.fm/api/v1")]            // không https
    [InlineData("https://pancake.vn/api/v1")]         // sai host
    [InlineData("https://evil.pages.fm/api/v1")]      // host con giả mạo
    [InlineData("https://pages.fm:8443/api/v1")]      // cổng khác
    [InlineData("https://user:pass@pages.fm/api/v1")] // có userinfo
    [InlineData("https://pages.fm/api/v1?x=1")]        // có query
    [InlineData("https://pages.fm/api/v1#frag")]       // có fragment
    [InlineData("https://pages.fm/api/v9")]            // path ngoài allowlist
    [InlineData("https://pages.fm/")]                   // path rỗng
    [InlineData("not-a-url")]
    public void NormalizeBaseUrl_Disallowed_Throws(string input)
    {
        var act = () => PancakeEndpointPolicy.NormalizeBaseUrl(input);

        act.Should().Throw<ArgumentException>().WithMessage("pancake_base_url_not_allowed*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeBaseUrl_BlankInput_Throws(string input)
    {
        var act = () => PancakeEndpointPolicy.NormalizeBaseUrl(input);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryNormalizeBaseUrl_Valid_ReturnsTrue()
    {
        PancakeEndpointPolicy.TryNormalizeBaseUrl("https://pages.fm/api/v1", out var normalized).Should().BeTrue();
        normalized.Should().Be("https://pages.fm/api/v1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://pancake.vn/api/v1")]
    public void TryNormalizeBaseUrl_Invalid_ReturnsFalseAndEmpty(string? input)
    {
        PancakeEndpointPolicy.TryNormalizeBaseUrl(input, out var normalized).Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [Fact]
    public void CreateNoRedirectHandler_DisablesAutoRedirect()
    {
        using var handler = PancakeEndpointPolicy.CreateNoRedirectHandler();

        handler.Should().BeOfType<HttpClientHandler>()
            .Which.AllowAutoRedirect.Should().BeFalse();
    }
}
