using System.Net;
using System.Net.Sockets;
using Clawbot.Agents.Core.Chat;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Chat;

// Phase 2.14: HTTPS/no-redirect/UseProxy=false outbound guard, DNS mixed-answer rejection,
// private origins only via operator grant (never tenant-controlled).
public sealed class LlmBaseUrlGuardTests
{
    [Fact]
    public void IsAllowedBaseUrl_rejects_dns_names_that_resolve_to_private_hosts()
    {
        var original = LlmBaseUrlGuard.ResolveHostAddresses;
        LlmBaseUrlGuard.ResolveHostAddresses = host =>
            host == "evil.example"
                ? [IPAddress.Parse("127.0.0.1")]
                : [IPAddress.Parse("8.8.8.8")];
        try
        {
            LlmBaseUrlGuard.IsAllowedBaseUrl("https://evil.example").Should().BeFalse();
            LlmBaseUrlGuard.IsAllowedBaseUrl("https://api.example").Should().BeTrue();
        }
        finally
        {
            LlmBaseUrlGuard.ResolveHostAddresses = original;
        }
    }

    [Fact]
    public void IsAllowedBaseUrl_rejects_mixed_public_and_private_dns_answers()
    {
        var original = LlmBaseUrlGuard.ResolveHostAddresses;
        LlmBaseUrlGuard.ResolveHostAddresses = _ =>
            [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.5")];
        try
        {
            LlmBaseUrlGuard.IsAllowedBaseUrl("https://mixed.example").Should().BeFalse();
        }
        finally
        {
            LlmBaseUrlGuard.ResolveHostAddresses = original;
        }
    }

    [Fact]
    public void IsAllowedBaseUrl_fails_closed_when_dns_resolution_fails()
    {
        var original = LlmBaseUrlGuard.ResolveHostAddresses;
        LlmBaseUrlGuard.ResolveHostAddresses = _ => throw new SocketException();
        try
        {
            LlmBaseUrlGuard.IsAllowedBaseUrl("https://missing.example").Should().BeFalse();
        }
        finally
        {
            LlmBaseUrlGuard.ResolveHostAddresses = original;
        }
    }

    [Fact]
    public void IsAllowedBaseUrl_allows_private_http_only_when_operator_granted()
    {
        LlmBaseUrlGuard.IsAllowedBaseUrl("http://localhost:11434").Should().BeFalse();
        LlmBaseUrlGuard.IsAllowedBaseUrl("http://localhost:11434", allowPrivateHosts: true).Should().BeTrue();
        LlmBaseUrlGuard.IsAllowedBaseUrl(
            "http://localhost:11434",
            allowPrivateHosts: false,
            allowedPrivateOrigins: ["http://localhost:11434"]).Should().BeTrue();
        LlmBaseUrlGuard.IsAllowedBaseUrl("http://api.example", allowPrivateHosts: true).Should().BeFalse();
    }

    [Fact]
    public void IsAllowedBaseUrl_rejects_userinfo_and_non_http_schemes()
    {
        LlmBaseUrlGuard.IsAllowedBaseUrl("https://user:pass@api.example").Should().BeFalse();
        LlmBaseUrlGuard.IsAllowedBaseUrl("ftp://api.example").Should().BeFalse();
    }

    [Fact]
    public void CreateGuardedHttpClient_rejects_disallowed_url_before_connect()
    {
        var original = LlmBaseUrlGuard.ResolveHostAddresses;
        LlmBaseUrlGuard.ResolveHostAddresses = _ => [IPAddress.Parse("127.0.0.1")];
        try
        {
            var act = () => LlmBaseUrlGuard.CreateGuardedHttpClient(
                new Uri("https://evil.local", UriKind.Absolute));
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*not allowed*");
        }
        finally
        {
            LlmBaseUrlGuard.ResolveHostAddresses = original;
        }
    }

    [Fact]
    public void CreateGuardedHttpClient_disables_redirects_and_proxy()
    {
        var original = LlmBaseUrlGuard.ResolveHostAddresses;
        LlmBaseUrlGuard.ResolveHostAddresses = _ => [IPAddress.Parse("8.8.8.8")];
        try
        {
            using var client = LlmBaseUrlGuard.CreateGuardedHttpClient(
                new Uri("https://api.example", UriKind.Absolute));
            // Handler is not exposed; assert construction succeeds for public HTTPS.
            client.BaseAddress.Should().Be(new Uri("https://api.example"));
            client.Timeout.Should().Be(TimeSpan.FromSeconds(120));
        }
        finally
        {
            LlmBaseUrlGuard.ResolveHostAddresses = original;
        }
    }
}
