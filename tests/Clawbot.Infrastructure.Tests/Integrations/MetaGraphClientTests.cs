using System.Net;
using System.Security.Cryptography;
using System.Text;
using Clawbot.Infrastructure.Integrations.Meta;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Integrations;

public sealed class MetaGraphClientTests
{
    private const string AppId = "app-123";
    private const string AppSecret = "server-secret";
    private const string RootToken = "bisu-root-token";
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task BuildAuthorizationUrl_uses_business_login_authorization_code_flow_v25()
    {
        var client = BuildClient(new SequenceHandler());

        var url = await client.BuildAuthorizationUrlAsync(TenantId, "state-123");

        url.Should().StartWith("https://www.facebook.com/v25.0/dialog/oauth?");
        url.Should().Contain("client_id=app-123");
        url.Should().Contain("config_id=config-123");
        url.Should().Contain("response_type=code");
        url.Should().Contain("override_default_response_type=true");
        url.Should().Contain("state=state-123");
        url.Should().Contain("redirect_uri=https%3A%2F%2Fapi.example%2Fapi%2Fadmin%2Fmeta%2Fcallback");
    }

    [Fact]
    public async Task ExchangeCodeAsync_posts_secrets_in_form_body_not_request_uri()
    {
        var handler = new SequenceHandler(_ => Json(HttpStatusCode.OK, """{"access_token":"root-token","token_type":"bearer","expires_in":3600}"""));
        var client = BuildClient(handler);

        var token = await client.ExchangeCodeAsync(TenantId, "oauth-code");

        token.AccessToken.Should().Be("root-token");
        token.ExpiresIn.Should().Be(3600);
        var request = handler.Requests.Should().ContainSingle().Which;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.AbsolutePath.Should().Be("/v25.0/oauth/access_token");
        request.RequestUri!.Query.Should().BeEmpty();
        request.Body.Should().Contain("client_id=app-123");
        request.Body.Should().Contain("client_secret=server-secret");
        request.Body.Should().Contain("code=oauth-code");
    }

    [Fact]
    public async Task DebugTokenAsync_keeps_app_secret_out_of_request_uri()
    {
        var handler = new SequenceHandler(_ => Json(HttpStatusCode.OK, """
            {"data":{"is_valid":true,"app_id":"app-123","type":"USER","user_id":"user-1","scopes":[]}}
            """));
        var client = BuildClient(handler);

        var debug = await client.DebugTokenAsync(TenantId, "root-token");

        debug.IsValid.Should().BeTrue();
        var request = handler.Requests.Should().ContainSingle().Which;
        request.RequestUri!.AbsolutePath.Should().Be("/v25.0/debug_token");
        request.RequestUri!.Query.Should().Contain("input_token=root-token");
        request.RequestUri!.Query.Should().NotContain("server-secret");
        request.RequestUri!.Query.Should().NotContain("access_token");
        request.Authorization.Should().Be("Bearer app-123|server-secret");
    }

    [Fact]
    public async Task GetIdentityAsync_requests_only_user_id_in_development_mode()
    {
        var handler = new SequenceHandler(_ => Json(HttpStatusCode.OK, """{"id":"user-1"}"""));
        var client = BuildClient(handler, MetaAuthorizationModes.DevelopmentUser);

        var identity = await client.GetIdentityAsync(TenantId, "user-token");

        identity.Id.Should().Be("user-1");
        identity.ClientBusinessId.Should().BeEmpty();
        var query = handler.Requests.Should().ContainSingle().Which.RequestUri!.Query;
        query.Should().Contain("fields=id");
        query.Should().NotContain("client_business_id");
    }

    [Fact]
    public async Task GetPagesAsync_follows_paging_and_adds_standard_appsecret_proof()
    {
        var handler = new SequenceHandler(
            _ => Json(HttpStatusCode.OK, """
                {
                  "data":[{"id":"page-1","name":"Page One","access_token":"page-token-1","tasks":["CREATE_CONTENT"]}],
                  "paging":{"cursors":{"after":"cursor-2"},"next":"https://graph.facebook.com/next"}
                }
                """),
            _ => Json(HttpStatusCode.OK, """
                {"data":[{"id":"page-2","name":"Page Two","access_token":"page-token-2","tasks":["ANALYZE"]}]}
                """));
        var client = BuildClient(handler);

        var pages = await client.GetPagesAsync(TenantId, RootToken);

        pages.Should().HaveCount(2);
        pages[0].Tasks.Should().Contain("CREATE_CONTENT");
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].RequestUri!.Query.Should().Contain("after=cursor-2");
        foreach (var request in handler.Requests)
        {
            request.RequestUri!.Query.Should().Contain($"appsecret_proof={Proof(RootToken)}");
            request.RequestUri!.Query.Should().NotContain("appsecret_time");
        }
    }

    [Fact]
    public async Task PublishPageAsync_uses_photo_post_id_for_canonical_permalink()
    {
        var handler = new SequenceHandler(_ => Json(HttpStatusCode.OK, """{"id":"photo-1","post_id":"page-1_99"}"""));
        var client = BuildClient(handler);

        var result = await client.PublishPageAsync(TenantId, "page-1", "page-token", "Caption", "https://cdn.example/photo.jpg");

        result.PostId.Should().Be("page-1_99");
        result.Permalink.Should().Be("https://www.facebook.com/page-1_99");
        var request = handler.Requests.Should().ContainSingle().Which;
        request.RequestUri!.AbsolutePath.Should().Be("/v25.0/page-1/photos");
        request.Body.Should().Contain("caption=Caption");
        request.Body.Should().Contain("url=https%3A%2F%2Fcdn.example%2Fphoto.jpg");
        request.Body.Should().Contain($"appsecret_proof={Proof("page-token")}");
        request.Body.Should().NotContain("appsecret_time");
    }

    [Fact]
    public async Task PublishPageAsync_text_post_uses_exact_feed_path()
    {
        var handler = new SequenceHandler(_ => Json(HttpStatusCode.OK, """{"id":"page-1_100"}"""));
        var client = BuildClient(handler);

        var published = await client.PublishPageAsync(
            TenantId,
            "page-1",
            "page-token",
            "Text only",
            imageUrl: null);

        published.PostId.Should().Be("page-1_100");
        var request = handler.Requests.Should().ContainSingle().Which;
        request.RequestUri!.AbsolutePath.Should().Be("/v25.0/page-1/feed");
        request.RequestUri.AbsolutePath.Should().NotContain("%0C");
        request.Body.Should().Contain("message=Text+only");
    }

    [Fact]
    public async Task ResolveInstagramAccountAsync_uses_configured_graph_endpoint_and_page_token()
    {
        var handler = new SequenceHandler(_ => Json(HttpStatusCode.OK, """
            {"instagram_business_account":{"id":"ig-user-123"}}
            """));
        var client = BuildClient(
            handler,
            baseUrl: "https://meta-proxy.example/graph",
            apiVersion: "v99.0");

        var accountId = await client.ResolveInstagramAccountAsync(
            TenantId,
            "page-123",
            "page-token",
            CancellationToken.None);

        accountId.Should().Be("ig-user-123");
        var request = handler.Requests.Should().ContainSingle().Which;
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri!.GetLeftPart(UriPartial.Path)
            .Should().Be("https://meta-proxy.example/graph/v99.0/page-123");
        request.RequestUri!.Query.Should().Contain("fields=instagram_business_account%7Bid%7D");
        request.RequestUri!.Query.Should().NotContain("access_token");
        request.Authorization.Should().Be("Bearer page-token");
        request.RequestUri!.Query.Should().Contain($"appsecret_proof={Proof("page-token")}");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"instagram_business_account\":null}")]
    [InlineData("{\"instagram_business_account\":{}}")]
    [InlineData("{\"instagram_business_account\":{\"id\":123}}")]
    public async Task ResolveInstagramAccountAsync_returns_null_when_page_is_not_linked(string responseBody)
    {
        var handler = new SequenceHandler(_ => Json(HttpStatusCode.OK, responseBody));
        var client = BuildClient(handler);

        var accountId = await client.ResolveInstagramAccountAsync(
            TenantId,
            "page-123",
            "page-token",
            CancellationToken.None);

        accountId.Should().BeNull();
    }

    [Fact]
    public async Task PublishInstagramAsync_creates_ready_container_publishes_and_resolves_provider_permalink()
    {
        var handler = new SequenceHandler(
            _ => Json(HttpStatusCode.OK, """{"id":"creation-123"}"""),
            _ => Json(HttpStatusCode.OK, """{"status_code":"FINISHED","status":"Finished"}"""),
            _ => Json(HttpStatusCode.OK, """{"id":"media-456"}"""),
            _ => Json(HttpStatusCode.OK, """{"permalink":"https://www.instagram.com/p/provider-slug/"}"""));
        var client = BuildClient(
            handler,
            baseUrl: "https://meta-proxy.example/graph/",
            apiVersion: "/v99.0/");

        var result = await client.PublishInstagramAsync(
            TenantId,
            "ig-user-123",
            "page-token",
            "Caption with spaces",
            "https://cdn.example/photo.jpg?signature=image-secret",
            CancellationToken.None);

        result.MediaId.Should().Be("media-456");
        result.Permalink.Should().Be("https://www.instagram.com/p/provider-slug/");
        handler.Requests.Should().HaveCount(4);
        handler.Requests.Should().OnlyContain(request =>
            request.RequestUri!.GetLeftPart(UriPartial.Authority) == "https://meta-proxy.example");
        handler.Requests.Should().OnlyContain(request =>
            request.RequestUri!.AbsolutePath.StartsWith("/graph/v99.0/", StringComparison.Ordinal));

        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/graph/v99.0/ig-user-123/media");
        handler.Requests[0].Body.Should().Contain("image_url=https%3A%2F%2Fcdn.example%2Fphoto.jpg%3Fsignature%3Dimage-secret");
        handler.Requests[0].Body.Should().Contain("caption=Caption+with+spaces");
        handler.Requests[0].Body.Should().Contain($"appsecret_proof={Proof("page-token")}");

        handler.Requests[1].Method.Should().Be(HttpMethod.Get);
        handler.Requests[1].RequestUri!.AbsolutePath.Should().Be("/graph/v99.0/creation-123");
        handler.Requests[1].RequestUri!.Query.Should().Contain("fields=status_code%2Cstatus");
        handler.Requests[1].Authorization.Should().Be("Bearer page-token");

        handler.Requests[2].Method.Should().Be(HttpMethod.Post);
        handler.Requests[2].RequestUri!.AbsolutePath.Should().Be("/graph/v99.0/ig-user-123/media_publish");
        handler.Requests[2].Body.Should().Contain("creation_id=creation-123");
        handler.Requests[2].Body.Should().Contain($"appsecret_proof={Proof("page-token")}");

        handler.Requests[3].Method.Should().Be(HttpMethod.Get);
        handler.Requests[3].RequestUri!.AbsolutePath.Should().Be("/graph/v99.0/media-456");
        handler.Requests[3].RequestUri!.Query.Should().Contain("fields=permalink");
        handler.Requests[3].RequestUri!.Query.Should().NotContain("access_token");
        handler.Requests[3].Authorization.Should().Be("Bearer page-token");
    }

    [Fact]
    public async Task PublishInstagramAsync_does_not_fabricate_permalink_from_opaque_media_id()
    {
        var handler = new SequenceHandler(
            _ => Json(HttpStatusCode.OK, """{"id":"creation-123"}"""),
            _ => Json(HttpStatusCode.OK, """{"status_code":"FINISHED"}"""),
            _ => Json(HttpStatusCode.OK, """{"id":"opaque-media-id"}"""),
            _ => Json(HttpStatusCode.OK, "{}"));
        var client = BuildClient(handler);

        var result = await client.PublishInstagramAsync(
            TenantId,
            "ig-user-123",
            "page-token",
            "Caption",
            "https://cdn.example/photo.jpeg",
            CancellationToken.None);

        result.MediaId.Should().Be("opaque-media-id");
        result.Permalink.Should().BeNull();
    }

    [Fact]
    public async Task PublishInstagramAsync_rejects_missing_creation_id_before_publish()
    {
        var handler = new SequenceHandler(_ => Json(HttpStatusCode.OK, """{"status":"accepted"}"""));
        var client = BuildClient(handler);

        var action = () => client.PublishInstagramAsync(
            TenantId,
            "ig-user-123",
            "page-token",
            "Caption",
            "https://cdn.example/photo.jpg",
            CancellationToken.None);

        var error = await action.Should().ThrowAsync<MetaGraphException>();
        error.Which.Message.Should().Be("meta_response_missing_id");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task PublishInstagramAsync_rejects_missing_published_media_id()
    {
        var handler = new SequenceHandler(
            _ => Json(HttpStatusCode.OK, """{"id":"creation-123"}"""),
            _ => Json(HttpStatusCode.OK, """{"status_code":"FINISHED"}"""),
            _ => Json(HttpStatusCode.OK, """{"status":"published"}"""));
        var client = BuildClient(handler);

        var action = () => client.PublishInstagramAsync(
            TenantId,
            "ig-user-123",
            "page-token",
            "Caption",
            "https://cdn.example/photo.jpg",
            CancellationToken.None);

        var error = await action.Should().ThrowAsync<MetaGraphException>();
        error.Which.Message.Should().Be("meta_response_missing_id");
        handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task PublishInstagramAsync_classifies_media_publish_token_errors()
    {
        var handler = new SequenceHandler(
            _ => Json(HttpStatusCode.OK, """{"id":"creation-123"}"""),
            _ => Json(HttpStatusCode.OK, """{"status_code":"FINISHED"}"""),
            _ => Json(HttpStatusCode.BadRequest, """
                {"error":{"message":"Session expired","type":"OAuthException","code":190,"error_subcode":463}}
                """));
        var client = BuildClient(handler);

        var action = () => client.PublishInstagramAsync(
            TenantId,
            "ig-user-123",
            "expired-page-token",
            "Caption",
            "https://cdn.example/photo.jpg",
            CancellationToken.None);

        var error = await action.Should().ThrowAsync<MetaGraphException>();
        error.Which.Code.Should().Be(190);
        error.Which.Subcode.Should().Be(463);
        error.Which.IsTokenError.Should().BeTrue();
    }

    [Fact]
    public async Task PublishInstagramAsync_retries_same_ready_container_after_not_ready_response()
    {
        var handler = new SequenceHandler(
            _ => Json(HttpStatusCode.OK, """{"id":"creation-123"}"""),
            _ => Json(HttpStatusCode.OK, """{"status_code":"FINISHED"}"""),
            _ => Json(HttpStatusCode.BadRequest, """
                {"error":{"message":"Media ID is not available","type":"OAuthException","code":9007,"error_subcode":2207027}}
                """),
            _ => Json(HttpStatusCode.OK, """{"status_code":"FINISHED"}"""),
            _ => Json(HttpStatusCode.OK, """{"id":"media-456"}"""),
            _ => Json(HttpStatusCode.OK, "{}"));
        var client = BuildClient(handler);

        var result = await client.PublishInstagramAsync(
            TenantId,
            "ig-user-123",
            "page-token",
            "Caption",
            "https://cdn.example/photo.jpg",
            CancellationToken.None);

        result.MediaId.Should().Be("media-456");
        handler.Requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/media", StringComparison.Ordinal)).Should().Be(1);
        handler.Requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/media_publish", StringComparison.Ordinal)).Should().Be(2);
        handler.Requests.Where(request => request.RequestUri!.AbsolutePath.EndsWith("/media_publish", StringComparison.Ordinal))
            .Should().OnlyContain(request => request.Body.Contains("creation_id=creation-123", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishInstagramAsync_keeps_success_when_permalink_graph_lookup_fails()
    {
        var handler = new SequenceHandler(
            _ => Json(HttpStatusCode.OK, """{"id":"creation-123"}"""),
            _ => Json(HttpStatusCode.OK, """{"status_code":"FINISHED"}"""),
            _ => Json(HttpStatusCode.OK, """{"id":"media-456"}"""),
            _ => Json(HttpStatusCode.BadRequest, """{"error":{"message":"lookup failed","code":4}}"""));
        var client = BuildClient(handler);

        var result = await client.PublishInstagramAsync(
            TenantId,
            "ig-user-123",
            "page-token",
            "Caption",
            "https://cdn.example/photo.jpg",
            CancellationToken.None);

        result.Should().Be(new MetaInstagramPublishedMedia("media-456", null));
        handler.Requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/media", StringComparison.Ordinal)).Should().Be(1);
        handler.Requests.Count(request => request.RequestUri!.AbsolutePath.EndsWith("/media_publish", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task PublishInstagramAsync_keeps_success_when_permalink_transport_is_canceled()
    {
        using var callerCts = new CancellationTokenSource();
        var handler = new SequenceHandler(
            _ => Json(HttpStatusCode.OK, """{"id":"creation-123"}"""),
            _ => Json(HttpStatusCode.OK, """{"status_code":"FINISHED"}"""),
            _ => Json(HttpStatusCode.OK, """{"id":"media-456"}"""),
            _ =>
            {
                callerCts.Cancel();
                throw new OperationCanceledException(callerCts.Token);
            });
        var client = BuildClient(handler);

        var result = await client.PublishInstagramAsync(
            TenantId,
            "ig-user-123",
            "page-token",
            "Caption",
            "https://cdn.example/photo.jpg",
            callerCts.Token);

        result.Should().Be(new MetaInstagramPublishedMedia("media-456", null));
        callerCts.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task PublishInstagramAsync_preserves_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var client = BuildClient(new SequenceHandler());

        var action = () => client.PublishInstagramAsync(
            TenantId,
            "ig-user-123",
            "page-token",
            "Caption",
            "https://cdn.example/photo.jpg",
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Graph_error_190_is_classified_as_token_error()
    {
        var handler = new SequenceHandler(_ => Json(HttpStatusCode.BadRequest, """
            {"error":{"message":"Session expired","type":"OAuthException","code":190,"error_subcode":463}}
            """));
        var client = BuildClient(handler);

        var action = () => client.GetIdentityAsync(TenantId, "expired-token");

        var error = await action.Should().ThrowAsync<MetaGraphException>();
        error.Which.Code.Should().Be(190);
        error.Which.Subcode.Should().Be(463);
        error.Which.IsTokenError.Should().BeTrue();
    }

    private static MetaGraphClient BuildClient(
        HttpMessageHandler handler,
        string authorizationMode = MetaAuthorizationModes.BusinessSystemUser,
        string baseUrl = "https://graph.facebook.com",
        string apiVersion = "v25.0")
    {
        var configurations = Substitute.For<IMetaGraphConfigurationResolver>();
        configurations.ResolveAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new MetaGraphOptions
            {
                AppId = AppId,
                AppSecret = AppSecret,
                ConfigurationId = "config-123",
                AuthorizationMode = authorizationMode,
                RedirectUri = "https://api.example/api/admin/meta/callback",
                FrontendReturnUrl = "https://app.example/system",
                BaseUrl = baseUrl,
                ApiVersion = apiVersion,
            });
        return new MetaGraphClient(
            new HttpClient(handler),
            configurations,
            NullLogger<MetaGraphClient>.Instance);
    }

    private static string Proof(string token)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _index;
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri,
                body,
                request.Headers.Authorization?.ToString()));
            if (_index >= responses.Length)
                throw new InvalidOperationException("No fake Meta response remains.");
            return responses[_index++](request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        string Body,
        string? Authorization);
}
