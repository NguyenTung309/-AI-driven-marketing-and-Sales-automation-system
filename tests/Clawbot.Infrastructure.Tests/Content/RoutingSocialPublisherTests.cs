using System.Net;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Integrations.Meta;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests.Content;

public sealed class RoutingSocialPublisherTests
{
    [Fact]
    public async Task PublishAsync_instagram_routes_to_native_publisher_without_generic_fallback()
    {
        var tenantId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var integrations = Substitute.For<IMetaIntegrationService>();
        integrations.ResolveInstagramAsync(tenantId, assetId, Arg.Any<CancellationToken>())
            .Returns(new MetaInstagramResolution(
                MetaInstagramResolutionStatus.Resolved,
                new MetaInstagramCredential(assetId, "ig-user-123", "page-token")));
        var graph = Substitute.For<IMetaGraphClient>();
        graph.PublishInstagramAsync(
                tenantId,
                "ig-user-123",
                "page-token",
                "Caption",
                "https://cdn.example/photo.jpg",
                Arg.Any<CancellationToken>())
            .Returns(new MetaInstagramPublishedMedia(
                "media-123",
                "https://www.instagram.com/p/provider-slug/"));
        var (sut, fallbackHandler) = BuildPublisher(integrations, graph);

        var result = await sut.PublishAsync(
            InstagramRequest(tenantId, assetId, "https://cdn.example/photo.jpg"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PostUrl.Should().Be("https://www.instagram.com/p/provider-slug/");
        fallbackHandler.RequestCount.Should().Be(0);
        await graph.Received(1).PublishInstagramAsync(
            tenantId,
            "ig-user-123",
            "page-token",
            "Caption",
            "https://cdn.example/photo.jpg",
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(MetaInstagramResolutionStatus.MissingScopes, "instagram_permissions_missing")]
    [InlineData(MetaInstagramResolutionStatus.NotLinked, "instagram_not_linked")]
    public async Task PublishAsync_instagram_resolution_errors_never_use_generic_fallback(
        MetaInstagramResolutionStatus status,
        string expectedError)
    {
        var tenantId = Guid.NewGuid();
        var integrations = Substitute.For<IMetaIntegrationService>();
        integrations.ResolveInstagramAsync(tenantId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new MetaInstagramResolution(status, null));
        var graph = Substitute.For<IMetaGraphClient>();
        var (sut, fallbackHandler) = BuildPublisher(integrations, graph);

        var result = await sut.PublishAsync(
            InstagramRequest(tenantId, null, "https://cdn.example/photo.jpg"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(expectedError);
        fallbackHandler.RequestCount.Should().Be(0);
    }

    [Theory]
    [InlineData("[]", "instagram_media_required")]
    [InlineData("[{\"type\":\"image\",\"url\":\"http://cdn.example/photo.jpg\"}]", "instagram_media_invalid")]
    public async Task PublishAsync_instagram_media_errors_never_use_generic_fallback(
        string assetsJson,
        string expectedError)
    {
        var integrations = Substitute.For<IMetaIntegrationService>();
        var graph = Substitute.For<IMetaGraphClient>();
        var (sut, fallbackHandler) = BuildPublisher(integrations, graph);
        var request = new PublishRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "instagram",
            "Caption",
            assetsJson,
            DateTimeOffset.UtcNow);

        var result = await sut.PublishAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(expectedError);
        fallbackHandler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_instagram_token_error_never_uses_generic_fallback()
    {
        var tenantId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var integrations = Substitute.For<IMetaIntegrationService>();
        integrations.ResolveInstagramAsync(tenantId, assetId, Arg.Any<CancellationToken>())
            .Returns(new MetaInstagramResolution(
                MetaInstagramResolutionStatus.Resolved,
                new MetaInstagramCredential(assetId, "ig-user-123", "expired-page-token")));
        integrations.RefreshPageAsync(tenantId, assetId, Arg.Any<CancellationToken>()).Returns((MetaPageCredential?)null);
        var graph = Substitute.For<IMetaGraphClient>();
        graph.PublishInstagramAsync(
                tenantId,
                "ig-user-123",
                "expired-page-token",
                "Caption",
                "https://cdn.example/photo.jpg",
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MetaInstagramPublishedMedia>(
                new MetaGraphException("expired token", code: 190, subcode: 463)));
        var (sut, fallbackHandler) = BuildPublisher(integrations, graph);

        var result = await sut.PublishAsync(
            InstagramRequest(tenantId, assetId, "https://cdn.example/photo.jpg"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("instagram_reconnect_required");
        fallbackHandler.RequestCount.Should().Be(0);
        await integrations.Received(1).MarkReconnectRequiredAsync(
            tenantId,
            "meta_page_token_refresh_failed",
            Arg.Any<CancellationToken>());
    }

    private static (RoutingSocialPublisher Publisher, CountingHandler FallbackHandler) BuildPublisher(
        IMetaIntegrationService integrations,
        IMetaGraphClient graph)
    {
        var fallbackHandler = new CountingHandler();
        var nativePublisher = new GraphSocialPublisher(
            new HttpClient(),
            Options.Create(new GraphPublisherOptions
            {
                InstagramPublishingEnabled = true,
            }),
            credentialResolver: null,
            NullLogger<GraphSocialPublisher>.Instance,
            integrations,
            graph);
        var fallbackPublisher = new HttpSocialPublisher(
            new HttpClient(fallbackHandler),
            Options.Create(new PublisherOptions
            {
                Endpoint = "https://publisher.example.test/publish",
                Token = "test-token",
            }));
        return (new RoutingSocialPublisher(nativePublisher, fallbackPublisher), fallbackHandler);
    }

    private static PublishRequest InstagramRequest(Guid tenantId, Guid? assetId, string imageUrl) =>
        new(
            tenantId,
            Guid.NewGuid(),
            "instagram",
            "Caption",
            $"[{{\"type\":\"image\",\"url\":\"{imageUrl}\"}}]",
            DateTimeOffset.UtcNow,
            assetId);

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
