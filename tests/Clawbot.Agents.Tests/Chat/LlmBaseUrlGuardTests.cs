using System.Net;
using System.Net.Sockets;
using Clawbot.Agents.Core.Chat;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Chat;

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
}
