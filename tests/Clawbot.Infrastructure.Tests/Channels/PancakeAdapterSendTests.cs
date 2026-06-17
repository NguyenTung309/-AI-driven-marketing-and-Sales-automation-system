using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Multitenancy;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests.Channels;

public sealed class PancakeAdapterSendTests : IDisposable
{
    private readonly HttpClient _http = new(new PancakeSendTestHandler());
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
}

public sealed class PancakeSendTestHandler : HttpClientHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Contains("pzl_test_page_123", request.RequestUri!.ToString());
        Assert.Contains("conv_abc_456", request.RequestUri!.ToString());
        Assert.Contains("page_access_token=test_page_token", request.RequestUri!.ToString());
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
