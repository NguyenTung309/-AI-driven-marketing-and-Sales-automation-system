using System.Net;
using System.Text.Json;
using Clawbot.Infrastructure.Content.Publishing;
using FluentAssertions;
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
            Endpoint = "https://graph.facebook.com/v21.0",
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
        result.PostUrl.Should().Be("https://www.facebook.com/123456/posts/789");
        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri!.ToString().Should().EndWith("/123456/feed");
        // access_token + message travel in the form body (Graph requires form-encoded for /feed with a page token).
        handler.Body.Should().Contain("access_token=pgt_fb");
        handler.Body.Should().Contain("message=Learn+HSK+today");
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
            Endpoint = "https://graph.facebook.com/v21.0",
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

    private static PublishRequest FacebookRequest() =>
        new(TenantId, ContentItemId, "facebook", "Learn HSK today", "[]", ScheduledAt);

    private static PublishRequest ZaloRequest() =>
        new(TenantId, ContentItemId, "zalo", "Learn HSK today", "[]", ScheduledAt);

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpMethod Method { get; private set; } = HttpMethod.Get;
        public Uri? RequestUri { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            if (request.Content is not null)
                Body = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(response);
        }
    }
}
