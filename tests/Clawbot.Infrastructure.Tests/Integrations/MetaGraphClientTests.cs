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
    public async Task ExchangeCodeAsync_calls_v25_oauth_endpoint_server_to_server()
    {
        var handler = new SequenceHandler(_ => Json(HttpStatusCode.OK, """{"access_token":"root-token","token_type":"bearer","expires_in":3600}"""));
        var client = BuildClient(handler);

        var token = await client.ExchangeCodeAsync(TenantId, "oauth-code");

        token.AccessToken.Should().Be("root-token");
        token.ExpiresIn.Should().Be(3600);
        handler.Requests.Should().ContainSingle();
        var uri = handler.Requests[0].RequestUri!.ToString();
        uri.Should().Contain("/v25.0/oauth/access_token?");
        uri.Should().Contain("client_id=app-123");
        uri.Should().Contain("client_secret=server-secret");
        uri.Should().Contain("code=oauth-code");
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
            request.RequestUri.Query.Should().NotContain("appsecret_time");
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
        string authorizationMode = MetaAuthorizationModes.BusinessSystemUser)
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
                ApiVersion = "v25.0",
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
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri, body));
            if (_index >= responses.Length)
                throw new InvalidOperationException("No fake Meta response remains.");
            return responses[_index++](request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri? RequestUri, string Body);
}
