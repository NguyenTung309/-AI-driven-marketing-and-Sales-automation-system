using System.Net;
using System.Net.Sockets;
using Clawbot.Agents.Core.Chat;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Chat;

/// <summary>
/// Seam DNS là biến static nên các test phải chạy tuần tự và luôn trả seam về mặc định.
/// </summary>
[Collection(nameof(LlmBaseUrlGuardTests))]
[CollectionDefinition(nameof(LlmBaseUrlGuardTests), DisableParallelization = true)]
public sealed class LlmBaseUrlGuardTests : IDisposable
{
    private readonly Func<string, IPAddress[]> _originalResolver = LlmBaseUrlGuard.ResolveHostAddresses;

    private static void ResolveTo(params string[] addresses) =>
        LlmBaseUrlGuard.ResolveHostAddresses = _ => addresses.Select(IPAddress.Parse).ToArray();

    private static void ResolveFails() =>
        LlmBaseUrlGuard.ResolveHostAddresses = _ => throw new SocketException((int)SocketError.HostNotFound);

    public void Dispose() => LlmBaseUrlGuard.ResolveHostAddresses = _originalResolver;

    [Fact]
    public void PublicHttpsHost_IsAllowed()
    {
        ResolveTo("15.235.208.173");

        LlmBaseUrlGuard.CheckBaseUrl("https://api.ai-box.vn").Should().Be(BaseUrlVerdict.Allowed);
        LlmBaseUrlGuard.IsAllowedBaseUrl("https://api.ai-box.vn").Should().BeTrue();
    }

    [Fact]
    public void UnresolvableHttpsHost_IsAllowedButFlaggedUnverified()
    {
        // Máy chủ hỏng DNS không được biến một URL public hợp lệ thành "URL sai".
        // Chặn thật nằm ở ConnectCallback, phân giải lại mỗi lần mở kết nối.
        ResolveFails();

        LlmBaseUrlGuard.CheckBaseUrl("https://api.ai-box.vn")
            .Should().Be(BaseUrlVerdict.AllowedDnsUnverified);
        LlmBaseUrlGuard.IsAllowedBaseUrl("https://api.ai-box.vn").Should().BeTrue();
    }

    [Fact]
    public void EmptyDnsAnswer_IsTreatedAsUnverifiedNotInvalid()
    {
        LlmBaseUrlGuard.ResolveHostAddresses = _ => [];

        LlmBaseUrlGuard.CheckBaseUrl("https://api.ai-box.vn")
            .Should().Be(BaseUrlVerdict.AllowedDnsUnverified);
    }

    [Theory]
    [InlineData("https://internal.example.com", "10.0.0.5")]
    [InlineData("https://internal.example.com", "127.0.0.1")]
    [InlineData("https://internal.example.com", "169.254.169.254")]
    [InlineData("https://internal.example.com", "100.64.0.1")]
    public void PrivateHost_IsRejectedWithItsOwnReason(string url, string address)
    {
        ResolveTo(address);

        LlmBaseUrlGuard.CheckBaseUrl(url).Should().Be(BaseUrlVerdict.PrivateHostNotGranted);
        LlmBaseUrlGuard.IsAllowedBaseUrl(url).Should().BeFalse();
    }

    [Fact]
    public void MixedPublicAndPrivateAnswer_IsRejectedAsRebinding()
    {
        ResolveTo("15.235.208.173", "10.0.0.5");

        LlmBaseUrlGuard.CheckBaseUrl("https://rebind.example.com")
            .Should().Be(BaseUrlVerdict.MixedDnsAnswer);
        LlmBaseUrlGuard.IsAllowedBaseUrl("https://rebind.example.com").Should().BeFalse();
    }

    [Fact]
    public void PrivateHost_IsAllowedWithOperatorGrant()
    {
        ResolveTo("10.0.0.5");

        LlmBaseUrlGuard.CheckBaseUrl("https://internal.example.com", allowPrivateHosts: true)
            .Should().Be(BaseUrlVerdict.Allowed);
    }

    [Fact]
    public void Localhost_IsPrivateWithoutTouchingDns()
    {
        LlmBaseUrlGuard.ResolveHostAddresses = _ =>
            throw new InvalidOperationException("DNS must not be queried for localhost.");

        LlmBaseUrlGuard.CheckBaseUrl("https://localhost:1234")
            .Should().Be(BaseUrlVerdict.PrivateHostNotGranted);
    }

    [Theory]
    [InlineData("http://api.ai-box.vn")]
    [InlineData("ftp://api.ai-box.vn")]
    public void NonHttpsPublicUrl_IsRejectedAsScheme(string url)
    {
        ResolveTo("15.235.208.173");

        LlmBaseUrlGuard.CheckBaseUrl(url).Should().Be(BaseUrlVerdict.SchemeNotAllowed);
    }

    [Theory]
    [InlineData("api.ai-box.vn")]
    [InlineData("https://user:pass@api.ai-box.vn")]
    [InlineData("not a url")]
    public void MalformedUrl_IsRejectedAsMalformed(string url)
    {
        ResolveTo("15.235.208.173");

        LlmBaseUrlGuard.CheckBaseUrl(url).Should().Be(BaseUrlVerdict.Malformed);
    }

    [Theory]
    [InlineData(BaseUrlVerdict.SchemeNotAllowed, "base_url_requires_https")]
    [InlineData(BaseUrlVerdict.PrivateHostNotGranted, "base_url_private_host")]
    [InlineData(BaseUrlVerdict.MixedDnsAnswer, "base_url_mixed_dns")]
    [InlineData(BaseUrlVerdict.Malformed, "invalid_base_url")]
    public void ErrorCode_DistinguishesRejectionReason(BaseUrlVerdict verdict, string expected) =>
        verdict.ToErrorCode().Should().Be(expected);
}
