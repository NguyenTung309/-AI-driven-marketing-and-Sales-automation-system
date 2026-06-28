using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Multitenancy;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests.Channels;

public sealed class PancakeAdapterSendTests : IDisposable
{
    private readonly HttpClient _http = new(new PancakeSendTestHandler("test_page_token"));
    private readonly IPancakeConfigResolver _resolver;
    private readonly ITenantAccessor _tenants = Substitute.For<ITenantAccessor>();

    public PancakeAdapterSendTests()
    {
        var resolver = Substitute.For<IPancakeConfigResolver>();
        resolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new PancakeRuntimeConfig(
                BaseUrl: "https://pages.fm/api/public_api/v2",
                AccessToken: "test_page_token",
                WebhookSecret: "",
                SignatureHeader: "x-pancake-signature",
                SignatureAlgo: "hmac-sha256",
                SignatureEncoding: "hex",
                SendPathTemplate: "/pages/{page_id}/conversations/{thread_id}/messages",
                AuthMode: "query",
                PageId: "pzl_test_page_123"));
        _resolver = resolver;
    }

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task SendAsync_WithFlatThreadId_ShouldUseConfigPageId()
    {
        var adapter = new PancakeChannelAdapter(_http, _resolver, _tenants);
        var ex = await Record.ExceptionAsync(() =>
            adapter.SendAsync("conv_abc_456", "Hello from test", CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task SendAsync_WithUserAccessToken_ShouldPreferUserToken()
    {
        using var http = new HttpClient(new PancakeSendTestHandler("user_page_token"));
        var adapter = new PancakeChannelAdapter(http, _resolver, _tenants);
        var ex = await Record.ExceptionAsync(() =>
            adapter.SendAsync("conv_abc_456", "Hello from test", "user_page_token", CancellationToken.None));
        Assert.Null(ex);
    }

    [Fact]
    public async Task SendAsync_WithPageThreadId_ShouldResolvePageToken_WhenNoExplicitToken()
    {
        // EARS[WHEN the thread id carries a page_id and no explicit token is passed THE SYSTEM SHALL resolve the
        // stored page access token for that page and send with it (page ops require a page token, not the user token)]
        using var http = new HttpClient(new PancakeSendTestHandler("pgt_resolved", "pzl_page_999", "conv_123"));
        var pageTokenResolver = Substitute.For<IPancakePageTokenResolver>();
        pageTokenResolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PancakePageToken("pgt_resolved", "pzl_page_999", "My Page", "facebook"));
        var adapter = new PancakeChannelAdapter(http, _resolver, _tenants, pageTokenResolver);

        var ex = await Record.ExceptionAsync(() =>
            adapter.SendAsync("pzl_page_999:conv_123", "Hello from test", CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task SendAsync_WithPageThreadId_FallsBackToConfigToken_WhenPageTokenNotStored()
    {
        // EARS[WHEN a page token resolver returns null (page not yet minted) THE SYSTEM SHALL fall back to the
        // configured token so a single-page tenant still sends]
        using var http = new HttpClient(new PancakeSendTestHandler("test_page_token", "pzl_page_999", "conv_123"));
        var pageTokenResolver = Substitute.For<IPancakePageTokenResolver>();
        pageTokenResolver.ResolveAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((PancakePageToken?)null);
        var adapter = new PancakeChannelAdapter(http, _resolver, _tenants, pageTokenResolver);

        var ex = await Record.ExceptionAsync(() =>
            adapter.SendAsync("pzl_page_999:conv_123", "Hello from test", CancellationToken.None));

        Assert.Null(ex);
    }
}

public sealed class PancakeSendTestHandler(string expectedToken, string expectedPage = "pzl_test_page_123", string expectedThread = "conv_abc_456") : HttpClientHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Contains(expectedPage, request.RequestUri!.ToString());
        Assert.Contains(expectedThread, request.RequestUri!.ToString());
        Assert.Contains("page_access_token=" + expectedToken, request.RequestUri!.ToString());
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
