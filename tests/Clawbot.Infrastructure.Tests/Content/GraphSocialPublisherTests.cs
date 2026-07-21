using System.Net;
using System.Text.Json;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Integrations.Meta;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Content;

public sealed class GraphSocialPublisherTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ContentItemId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset ScheduledAt = new(2026, 6, 28, 0, 0, 0, TimeSpan.Zero);

    private static GraphPublisherOptions FacebookOptions() => new()
    {
        Facebook = new GraphChannelOptions
        {
            Enabled = true,
            PageId = "123456",
            PageAccessToken = "pgt_fb",
        },
    };

    private static GraphPublisherOptions ZaloOptions() => new()
    {
        Zalo = new GraphChannelOptions
        {
            Enabled = true,
            Endpoint = "https://openapi.zalo.me/v2.0/oa",
            OaId = "oa_1",
            OaAccessToken = "oa_tok",
        },
    };

    private static GraphPublisherOptions InstagramOptions() => new()
    {
        InstagramPublishingEnabled = true,
    };

    [Fact]
    public async Task PublishAsync_Facebook_PostsFeedWithPageToken_AndReturnsPermalink()
    {
        // EARS[WHEN publishing to Facebook THE SYSTEM SHALL POST message + access_token to /{page_id}/feed and return the permalink]
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"123456_789"}"""),
        });
        var publisher = new GraphSocialPublisher(
            new HttpClient(handler),
            Options.Create(FacebookOptions()),
            credentialResolver: null, NullLogger<GraphSocialPublisher>.Instance);

        var result = await publisher.PublishAsync(FacebookRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PostUrl.Should().Be("https://www.facebook.com/123456_789");
        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.ToString().Should().EndWith("/123456/feed");
        // access_token + message travel in the form body (Graph requires form-encoded for /feed with a page token).
        handler.Body.Should().Contain("access_token=pgt_fb");
        handler.Body.Should().Contain("message=Learn+HSK+today");
    }

    [Fact]
    public async Task PublishAsync_Facebook_PostsPhotoWhenImageAssetExists()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"123456_790"}"""),
        });
        var publisher = new GraphSocialPublisher(
            new HttpClient(handler),
            Options.Create(FacebookOptions()),
            credentialResolver: null, NullLogger<GraphSocialPublisher>.Instance);

        var result = await publisher.PublishAsync(
            new PublishRequest(
                TenantId,
                ContentItemId,
                "facebook",
                "Learn HSK today",
                """[{"type":"image","url":"https://cdn.example/hsk.png"}]""",
                ScheduledAt),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        handler.RequestUri!.ToString().Should().EndWith("/123456/photos");
        handler.Body.Should().Contain("caption=Learn+HSK+today");
        handler.Body.Should().Contain("url=https%3A%2F%2Fcdn.example%2Fhsk.png");
    }

    [Fact]
    public async Task PublishAsync_Facebook_FailsWhenResponseMissingId()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"foo":"bar"}"""),
        });
        var publisher = new GraphSocialPublisher(
            new HttpClient(handler),
            Options.Create(FacebookOptions()),
            credentialResolver: null, NullLogger<GraphSocialPublisher>.Instance);

        var result = await publisher.PublishAsync(FacebookRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("missing_id");
    }

    [Fact]
    public async Task PublishAsync_Facebook_FailsWhenNotConfigured()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var publisher = new GraphSocialPublisher(
            new HttpClient(handler),
            Options.Create(new GraphPublisherOptions { Facebook = new GraphChannelOptions { Enabled = false } }),
            credentialResolver: null, NullLogger<GraphSocialPublisher>.Instance);

        var result = await publisher.PublishAsync(FacebookRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("facebook_not_configured");
    }

    [Fact]
    public async Task PublishAsync_Facebook_SurfacesNonSuccessStatusCode()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"message":"permission denied"}}"""),
        });
        var publisher = new GraphSocialPublisher(
            new HttpClient(handler),
            Options.Create(FacebookOptions()),
            credentialResolver: null, NullLogger<GraphSocialPublisher>.Instance);

        var result = await publisher.PublishAsync(FacebookRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().StartWith("facebook_http_400");
    }

    [Fact]
    public async Task PublishAsync_Zalo_PostsArticleWithOaToken_AndReturnsPostUrl()
    {
        // EARS[WHEN publishing to Zalo THE SYSTEM SHALL POST the article with the OA token and return the post URL]
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"error":0,"message":"ok","data":{"token":"posttok"}}"""),
        });
        var publisher = new GraphSocialPublisher(
            new HttpClient(handler),
            Options.Create(ZaloOptions()),
            credentialResolver: null, NullLogger<GraphSocialPublisher>.Instance);

        var result = await publisher.PublishAsync(ZaloRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PostUrl.Should().Be("https://zalo.me/p/posttok");
        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.ToString().Should().EndWith("/article/verify_only");
        using var payload = JsonDocument.Parse(handler.Body);
        payload.RootElement.GetProperty("access_token").GetString().Should().Be("oa_tok");
        payload.RootElement.GetProperty("body").GetString().Should().Be("Learn HSK today");
    }

    [Fact]
    public async Task PublishAsync_Zalo_FailsOnNonZeroError()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"error":-123,"message":"invalid token"}"""),
        });
        var publisher = new GraphSocialPublisher(
            new HttpClient(handler),
            Options.Create(ZaloOptions()),
            credentialResolver: null, NullLogger<GraphSocialPublisher>.Instance);

        var result = await publisher.PublishAsync(ZaloRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("zalo_error");
        result.Error.Should().Contain("invalid token");
    }

    [Fact]
    public async Task PublishAsync_UnsupportedPlatform_ReturnsError()
    {
        var publisher = new GraphSocialPublisher(
            new HttpClient(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK))),
            Options.Create(FacebookOptions()),
            credentialResolver: null, NullLogger<GraphSocialPublisher>.Instance);

        var result = await publisher.PublishAsync(
            new PublishRequest(TenantId, ContentItemId, "myspace", "body", "[]", ScheduledAt),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("unsupported_platform");
    }

    [Fact]
    public async Task PublishAsync_Facebook_PrefersDbCredentials_OverOptions()
    {
        // EARS[WHEN a DB credential exists for the tenant THE SYSTEM SHALL use it instead of options]
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"dbpage_1"}"""),
        });
        var dbCreds = new GraphChannelOptions
        {
            Enabled = true,
            PageId = "dbpage",
            PageAccessToken = "pgt_from_db",
        };
        var resolver = NSubstitute.Substitute.For<ISocialCredentialResolver>();
        resolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(dbCreds);
        // Options point at a DIFFERENT page/token — the DB creds must win.
        var publisher = new GraphSocialPublisher(
            new HttpClient(handler),
            Options.Create(FacebookOptions()),
            resolver,
            NullLogger<GraphSocialPublisher>.Instance);

        var result = await publisher.PublishAsync(FacebookRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        handler.RequestUri!.ToString().Should().Contain("/dbpage/feed");
        handler.Body.Should().Contain("access_token=pgt_from_db");
    }

    [Fact]
    public async Task PublishAsync_Facebook_uses_tenant_meta_page_connection()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var integrations = Substitute.For<IMetaIntegrationService>();
        integrations.ResolvePageAsync(TenantId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new MetaPageCredential(Guid.NewGuid(), "page-123", "Main Page", "page-token"));
        var graph = Substitute.For<IMetaGraphClient>();
        graph.PublishPageAsync(TenantId, "page-123", "page-token", "Learn HSK today", null, Arg.Any<CancellationToken>())
            .Returns(new MetaPublishedPost("page-123_9", "https://www.facebook.com/page-123_9"));
        var publisher = new GraphSocialPublisher(
            new HttpClient(handler),
            Options.Create(new GraphPublisherOptions()),
            credentialResolver: null,
            NullLogger<GraphSocialPublisher>.Instance,
            integrations,
            graph);

        var result = await publisher.PublishAsync(FacebookRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PostUrl.Should().Be("https://www.facebook.com/page-123_9");
        handler.RequestCount.Should().Be(0, "the legacy Page token path must not run when a tenant Meta connection exists");
    }

    [Fact]
    public async Task PublishAsync_Facebook_refreshes_page_token_once_after_graph_token_error()
    {
        var assetId = Guid.NewGuid();
        var integrations = Substitute.For<IMetaIntegrationService>();
        integrations.ResolvePageAsync(TenantId, assetId, Arg.Any<CancellationToken>())
            .Returns(new MetaPageCredential(assetId, "page-123", "Main Page", "expired-token"));
        integrations.RefreshPageAsync(TenantId, assetId, Arg.Any<CancellationToken>())
            .Returns(new MetaPageCredential(assetId, "page-123", "Main Page", "fresh-token"));
        var graph = Substitute.For<IMetaGraphClient>();
        graph.PublishPageAsync(TenantId, "page-123", "expired-token", "Learn HSK today", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MetaPublishedPost>(new MetaGraphException("expired", code: 190, subcode: 463)));
        graph.PublishPageAsync(TenantId, "page-123", "fresh-token", "Learn HSK today", null, Arg.Any<CancellationToken>())
            .Returns(new MetaPublishedPost("page-123_10", "https://www.facebook.com/page-123_10"));
        var publisher = new GraphSocialPublisher(
            new HttpClient(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK))),
            Options.Create(new GraphPublisherOptions()),
            credentialResolver: null,
            NullLogger<GraphSocialPublisher>.Instance,
            integrations,
            graph);

        var result = await publisher.PublishAsync(
            new PublishRequest(TenantId, ContentItemId, "facebook", "Learn HSK today", "[]", ScheduledAt, assetId),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PostUrl.Should().Be("https://www.facebook.com/page-123_10");
        await integrations.Received(1).RefreshPageAsync(TenantId, assetId, Arg.Any<CancellationToken>());
        await graph.Received(1).PublishPageAsync(TenantId, "page-123", "fresh-token", "Learn HSK today", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_Instagram_returns_not_configured_when_feature_is_disabled()
    {
        var integrations = Substitute.For<IMetaIntegrationService>();
        var graph = Substitute.For<IMetaGraphClient>();
        var publisher = new GraphSocialPublisher(
            new HttpClient(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK))),
            Options.Create(new GraphPublisherOptions()),
            credentialResolver: null,
            NullLogger<GraphSocialPublisher>.Instance,
            integrations,
            graph);

        var result = await publisher.PublishAsync(InstagramRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("instagram_not_configured");
        await integrations.DidNotReceive().ResolveInstagramAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
        await graph.DidNotReceive().PublishInstagramAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("[]", "instagram_media_required")]
    [InlineData("not-json", "instagram_media_invalid")]
    [InlineData("[{\"type\":\"image\",\"url\":\"http://cdn.example/photo.jpg\"}]", "instagram_media_invalid")]
    [InlineData("[{\"type\":\"image\",\"url\":\"https://cdn.example/photo.png\"}]", "instagram_media_invalid")]
    public async Task PublishAsync_Instagram_rejects_unusable_media_before_meta_calls(
        string assetsJson,
        string expectedError)
    {
        var integrations = Substitute.For<IMetaIntegrationService>();
        var graph = Substitute.For<IMetaGraphClient>();
        var publisher = new GraphSocialPublisher(
            new HttpClient(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK))),
            Options.Create(InstagramOptions()),
            credentialResolver: null,
            NullLogger<GraphSocialPublisher>.Instance,
            integrations,
            graph);

        var result = await publisher.PublishAsync(InstagramRequest(assetsJson), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(expectedError);
        await integrations.DidNotReceive().ResolveInstagramAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
        await graph.DidNotReceive().PublishInstagramAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_Instagram_resolves_linked_account_and_publishes_jpeg()
    {
        var assetId = Guid.NewGuid();
        var integrations = Substitute.For<IMetaIntegrationService>();
        integrations.ResolveInstagramAsync(TenantId, assetId, Arg.Any<CancellationToken>())
            .Returns(new MetaInstagramResolution(
                MetaInstagramResolutionStatus.Resolved,
                new MetaInstagramCredential(assetId, "ig-user-123", "page-token")));
        var graph = Substitute.For<IMetaGraphClient>();
        graph.PublishInstagramAsync(
                TenantId,
                "ig-user-123",
                "page-token",
                "Learn HSK today",
                "https://cdn.example/hsk.jpeg",
                Arg.Any<CancellationToken>())
            .Returns(new MetaInstagramPublishedMedia(
                "media-123",
                "https://www.instagram.com/p/provider-slug/"));
        var publisher = new GraphSocialPublisher(
            new HttpClient(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK))),
            Options.Create(InstagramOptions()),
            credentialResolver: null,
            NullLogger<GraphSocialPublisher>.Instance,
            integrations,
            graph);

        var result = await publisher.PublishAsync(
            InstagramRequest("[{\"type\":\"image\",\"url\":\"https://cdn.example/hsk.jpeg\"}]", assetId),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PostUrl.Should().Be("https://www.instagram.com/p/provider-slug/");
        await integrations.Received(1).ResolveInstagramAsync(TenantId, assetId, Arg.Any<CancellationToken>());
        await graph.Received(1).PublishInstagramAsync(
            TenantId,
            "ig-user-123",
            "page-token",
            "Learn HSK today",
            "https://cdn.example/hsk.jpeg",
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(MetaInstagramResolutionStatus.MissingScopes, "instagram_permissions_missing")]
    [InlineData(MetaInstagramResolutionStatus.NotLinked, "instagram_not_linked")]
    [InlineData(MetaInstagramResolutionStatus.MetaOrPageUnavailable, "instagram_not_configured")]
    public async Task PublishAsync_Instagram_maps_resolution_failures_to_stable_safe_errors(
        MetaInstagramResolutionStatus status,
        string expectedError)
    {
        var integrations = Substitute.For<IMetaIntegrationService>();
        integrations.ResolveInstagramAsync(TenantId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new MetaInstagramResolution(status, null));
        var graph = Substitute.For<IMetaGraphClient>();
        var publisher = new GraphSocialPublisher(
            new HttpClient(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK))),
            Options.Create(InstagramOptions()),
            credentialResolver: null,
            NullLogger<GraphSocialPublisher>.Instance,
            integrations,
            graph);

        var result = await publisher.PublishAsync(InstagramRequest(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(expectedError);
        await graph.DidNotReceive().PublishInstagramAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_Instagram_refreshes_selected_page_once_after_token_error()
    {
        var assetId = Guid.NewGuid();
        var integrations = Substitute.For<IMetaIntegrationService>();
        integrations.ResolveInstagramAsync(TenantId, assetId, Arg.Any<CancellationToken>())
            .Returns(new MetaInstagramResolution(
                MetaInstagramResolutionStatus.Resolved,
                new MetaInstagramCredential(assetId, "ig-user-123", "expired-page-token")));
        integrations.RefreshPageAsync(TenantId, assetId, Arg.Any<CancellationToken>())
            .Returns(new MetaPageCredential(assetId, "page-123", "Main Page", "fresh-page-token"));
        var graph = Substitute.For<IMetaGraphClient>();
        graph.PublishInstagramAsync(
                TenantId,
                "ig-user-123",
                "expired-page-token",
                "Learn HSK today",
                "https://cdn.example/hsk.jpg",
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MetaInstagramPublishedMedia>(
                new MetaGraphException("expired", code: 190, subcode: 463)));
        graph.ResolveInstagramAccountAsync(
                TenantId,
                "page-123",
                "fresh-page-token",
                Arg.Any<CancellationToken>())
            .Returns("ig-user-123");
        graph.PublishInstagramAsync(
                TenantId,
                "ig-user-123",
                "fresh-page-token",
                "Learn HSK today",
                "https://cdn.example/hsk.jpg",
                Arg.Any<CancellationToken>())
            .Returns(new MetaInstagramPublishedMedia(
                "media-124",
                "https://www.instagram.com/p/retried-slug/"));
        var publisher = new GraphSocialPublisher(
            new HttpClient(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK))),
            Options.Create(InstagramOptions()),
            credentialResolver: null,
            NullLogger<GraphSocialPublisher>.Instance,
            integrations,
            graph);

        var result = await publisher.PublishAsync(
            InstagramRequest("[{\"type\":\"image\",\"url\":\"https://cdn.example/hsk.jpg\"}]", assetId),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.PostUrl.Should().Be("https://www.instagram.com/p/retried-slug/");
        await integrations.Received(1).RefreshPageAsync(TenantId, assetId, Arg.Any<CancellationToken>());
        await graph.Received(1).ResolveInstagramAccountAsync(
            TenantId,
            "page-123",
            "fresh-page-token",
            Arg.Any<CancellationToken>());
        await graph.Received(1).PublishInstagramAsync(
            TenantId,
            "ig-user-123",
            "fresh-page-token",
            "Learn HSK today",
            "https://cdn.example/hsk.jpg",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_Instagram_marks_reconnect_required_when_retry_token_also_fails()
    {
        var assetId = Guid.NewGuid();
        var integrations = Substitute.For<IMetaIntegrationService>();
        integrations.ResolveInstagramAsync(TenantId, assetId, Arg.Any<CancellationToken>())
            .Returns(new MetaInstagramResolution(
                MetaInstagramResolutionStatus.Resolved,
                new MetaInstagramCredential(assetId, "ig-user-123", "expired-page-token")));
        integrations.RefreshPageAsync(TenantId, assetId, Arg.Any<CancellationToken>())
            .Returns(new MetaPageCredential(assetId, "page-123", "Main Page", "fresh-page-token"));
        var graph = Substitute.For<IMetaGraphClient>();
        graph.PublishInstagramAsync(
                TenantId,
                "ig-user-123",
                Arg.Any<string>(),
                "Learn HSK today",
                "https://cdn.example/hsk.jpg",
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MetaInstagramPublishedMedia>(
                new MetaGraphException("token secret must not escape", code: 190, subcode: 463)));
        graph.ResolveInstagramAccountAsync(
                TenantId,
                "page-123",
                "fresh-page-token",
                Arg.Any<CancellationToken>())
            .Returns("ig-user-123");
        var publisher = new GraphSocialPublisher(
            new HttpClient(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK))),
            Options.Create(InstagramOptions()),
            credentialResolver: null,
            NullLogger<GraphSocialPublisher>.Instance,
            integrations,
            graph);

        var result = await publisher.PublishAsync(
            InstagramRequest("[{\"type\":\"image\",\"url\":\"https://cdn.example/hsk.jpg\"}]", assetId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("instagram_reconnect_required");
        await integrations.Received(1).RefreshPageAsync(TenantId, assetId, Arg.Any<CancellationToken>());
        await integrations.Received(1).MarkReconnectRequiredAsync(
            TenantId,
            "meta_token_190_463",
            Arg.Any<CancellationToken>());
        await graph.Received(2).PublishInstagramAsync(
            TenantId,
            "ig-user-123",
            Arg.Any<string>(),
            "Learn HSK today",
            "https://cdn.example/hsk.jpg",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_Instagram_does_not_leak_tokens_or_image_query_credentials_in_errors_or_logs()
    {
        const string pageToken = "page-token-secret";
        const string imageSecret = "image-query-secret";
        var imageUrl = $"https://cdn.example/hsk.jpg?X-Amz-Signature={imageSecret}";
        var assetId = Guid.NewGuid();
        var integrations = Substitute.For<IMetaIntegrationService>();
        integrations.ResolveInstagramAsync(TenantId, assetId, Arg.Any<CancellationToken>())
            .Returns(new MetaInstagramResolution(
                MetaInstagramResolutionStatus.Resolved,
                new MetaInstagramCredential(assetId, "ig-user-123", pageToken)));
        var graph = Substitute.For<IMetaGraphClient>();
        graph.PublishInstagramAsync(
                TenantId,
                "ig-user-123",
                pageToken,
                "Learn HSK today",
                imageUrl,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<MetaInstagramPublishedMedia>(
                new MetaGraphException($"Rejected {imageUrl} with {pageToken}", code: 10)));
        var logger = new RecordingLogger<GraphSocialPublisher>();
        var publisher = new GraphSocialPublisher(
            new HttpClient(new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK))),
            Options.Create(InstagramOptions()),
            credentialResolver: null,
            logger,
            integrations,
            graph);

        var result = await publisher.PublishAsync(
            InstagramRequest($"[{{\"type\":\"image\",\"url\":\"{imageUrl}\"}}]", assetId),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("instagram_graph_10");
        result.Error.Should().NotContain(pageToken);
        result.Error.Should().NotContain(imageSecret);
        logger.Entries.Should().NotContain(entry => entry.Contains(pageToken, StringComparison.Ordinal));
        logger.Entries.Should().NotContain(entry => entry.Contains(imageSecret, StringComparison.Ordinal));
    }

    private static PublishRequest FacebookRequest() =>
        new(TenantId, ContentItemId, "facebook", "Learn HSK today", "[]", ScheduledAt);

    private static PublishRequest ZaloRequest() =>
        new(TenantId, ContentItemId, "zalo", "Learn HSK today", "[]", ScheduledAt);

    private static PublishRequest InstagramRequest(
        string assetsJson = "[{\"type\":\"image\",\"url\":\"https://cdn.example/hsk.jpg\"}]",
        Guid? metaAssetId = null) =>
        new(TenantId, ContentItemId, "instagram", "Learn HSK today", assetsJson, ScheduledAt, metaAssetId);

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpMethod Method { get; private set; } = HttpMethod.Get;
        public Uri? RequestUri { get; private set; }
        public string Body { get; private set; } = string.Empty;
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            if (request.Content is not null)
                Body = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _ = logLevel;
            _ = eventId;
            Entries.Add($"{formatter(state, exception)} {exception?.Message}".Trim());
        }
    }
}
