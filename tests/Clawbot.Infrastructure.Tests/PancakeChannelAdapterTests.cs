using System.Net;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests;

public sealed class PancakeChannelAdapterTests
{
    [Fact]
    public async Task SendAsync_ForwardsPlatformToPageTokenResolver()
    {
        // Arrange
        var fixture = CreateFixture();

        // Act
        await fixture.Adapter.SendAsync(
            fixture.TenantId,
            " Instagram ",
            "page-1:thread-1",
            "reply");

        // Assert
        await AssertResolvedForInstagramAsync(fixture);
    }

    [Fact]
    public async Task SendCommentReplyAsync_ForwardsPlatformToPageTokenResolver()
    {
        // Arrange
        var fixture = CreateFixture();

        // Act
        await fixture.Adapter.SendCommentReplyAsync(
            fixture.TenantId,
            " Instagram ",
            "page-1:thread-1",
            "comment-1",
            "reply");

        // Assert
        await AssertResolvedForInstagramAsync(fixture);
    }

    [Fact]
    public async Task SendPrivateReplyAsync_ForwardsPlatformToPageTokenResolver()
    {
        // Arrange
        var fixture = CreateFixture();

        // Act
        await fixture.Adapter.SendPrivateReplyAsync(
            fixture.TenantId,
            " Instagram ",
            "page-1:thread-1",
            "post-1",
            "comment-1",
            "sender-1",
            "reply");

        // Assert
        await AssertResolvedForInstagramAsync(fixture);
    }

    private static async Task AssertResolvedForInstagramAsync(AdapterFixture fixture)
    {
        await fixture.TokenResolver.Received(1).ResolveAsync(
            fixture.TenantId,
            "instagram",
            "page-1",
            Arg.Any<CancellationToken>());
        fixture.Handler.RequestCount.Should().Be(1);
        fixture.Handler.LastRequestUri.Should().Contain(
            "page_access_token=instagram-page-token");
    }

    private static AdapterFixture CreateFixture()
    {
        var tenantId = Guid.NewGuid();
        var configResolver = Substitute.For<IPancakeConfigResolver>();
        configResolver.ResolveAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new PancakeRuntimeConfig(
                PancakeEndpointPolicy.DefaultPublicApiBaseUrl,
                string.Empty,
                string.Empty,
                "x-pancake-signature",
                "hmac-sha256",
                "hex",
                "/pages/{page_id}/conversations/{thread_id}/messages",
                "query",
                "page-1"));
        var tokenResolver = Substitute.For<IPancakePageTokenResolver>();
        tokenResolver.ResolveAsync(
                tenantId,
                "instagram",
                "page-1",
                Arg.Any<CancellationToken>())
            .Returns(new PancakePageToken(
                "instagram-page-token",
                "page-1",
                "Instagram page",
                "instagram"));
        var handler = new RecordingHandler();
        var adapter = new PancakeChannelAdapter(
            new HttpClient(handler),
            configResolver,
            new NullTenantAccessor(),
            tokenResolver);
        return new AdapterFixture(
            tenantId,
            adapter,
            tokenResolver,
            handler);
    }

    private sealed record AdapterFixture(
        Guid TenantId,
        PancakeChannelAdapter Adapter,
        IPancakePageTokenResolver TokenResolver,
        RecordingHandler Handler);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true,\"id\":\"message-1\"}"),
            });
        }
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;
        public TenantContext Require() =>
            throw new InvalidOperationException("No tenant in unit test scope.");
    }
}
