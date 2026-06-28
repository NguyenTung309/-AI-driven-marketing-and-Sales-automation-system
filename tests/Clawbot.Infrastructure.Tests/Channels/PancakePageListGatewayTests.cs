using System.Net;
using System.Text.Json;
using Clawbot.Infrastructure.Channels.Pancake;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Channels;

public sealed class PancakePageListGatewayTests
{
    [Fact]
    public async Task ListAsync_ReturnsPageSummaries_FromDataArray()
    {
        // EARS[WHEN listing pages THE SYSTEM SHALL parse the Pancake response and return page summaries]
        var handler = new FixedHandler(HttpStatusCode.OK, """{"data":[{"id":"pzl_1","name":"Page One","platform":"facebook"},{"id":"pzl_2","name":"Page Two","platform":"zalo"}]}""");
        var gateway = new HttpPancakePageTokenMintGateway(
            new HttpClient(handler),
            new PancakeUserApiOptions { BaseUrl = "https://pages.fm/api/v1" },
            NullLogger<HttpPancakePageTokenMintGateway>.Instance);

        var pages = await gateway.ListAsync("user_tok", CancellationToken.None);

        pages.Should().HaveCount(2);
        pages[0].PageId.Should().Be("pzl_1");
        pages[0].Name.Should().Be("Page One");
        pages[1].Platform.Should().Be("zalo");
        handler.RequestUri!.ToString().Should().Contain("/pages?access_token=user_tok");
    }

    [Fact]
    public async Task ListAsync_AcceptsTopLevelArray()
    {
        var handler = new FixedHandler(HttpStatusCode.OK, """[{"id":"pzl_1","name":"Solo"}]""");
        var gateway = new HttpPancakePageTokenMintGateway(
            new HttpClient(handler),
            new PancakeUserApiOptions { BaseUrl = "https://pages.fm/api/v1" },
            NullLogger<HttpPancakePageTokenMintGateway>.Instance);

        var pages = await gateway.ListAsync("user_tok", CancellationToken.None);

        pages.Should().ContainSingle();
        pages[0].PageId.Should().Be("pzl_1");
    }

    [Fact]
    public async Task ListAsync_SkipsEntriesWithoutId()
    {
        var handler = new FixedHandler(HttpStatusCode.OK, """{"data":[{"name":"NoId"},{"id":"pzl_1","name":"Real"}]}""");
        var gateway = new HttpPancakePageTokenMintGateway(
            new HttpClient(handler),
            new PancakeUserApiOptions { BaseUrl = "https://pages.fm/api/v1" },
            NullLogger<HttpPancakePageTokenMintGateway>.Instance);

        var pages = await gateway.ListAsync("user_tok", CancellationToken.None);

        pages.Should().ContainSingle().Which.PageId.Should().Be("pzl_1");
    }

    private sealed class FixedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
