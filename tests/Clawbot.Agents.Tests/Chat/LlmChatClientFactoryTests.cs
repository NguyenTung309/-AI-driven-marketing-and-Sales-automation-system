using Clawbot.Agents.Core.Chat;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Chat;

public sealed class LlmChatClientFactoryTests
{
    [Theory]
    [InlineData("anthropic", typeof(AnthropicChatClient))]
    [InlineData("openai", typeof(OpenAiChatClient))]
    public void Create_selects_provider_specific_client(string provider, Type expected)
    {
        var sut = new LlmChatClientFactory(new StubHttpClientFactory());

        var client = sut.Create(Config(provider));

        client.Should().BeOfType(expected);
    }

    [Fact]
    public void Create_throws_for_unsupported_provider()
    {
        var sut = new LlmChatClientFactory(new StubHttpClientFactory());

        var act = () => sut.Create(Config("gemini"));

        act.Should().Throw<NotSupportedException>().WithMessage("*gemini*");
    }

    [Fact]
    public void Create_revalidates_configured_base_url_before_outbound_client_creation()
    {
        var sut = new LlmChatClientFactory(new StubHttpClientFactory());
        var config = Config("anthropic") with { BaseUrl = "https://127.0.0.1" };

        var act = () => sut.Create(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*base URL*");
    }

    private static ResolvedLlmConfig Config(string provider) =>
        new(provider, "model-x", "key", BaseUrl: null, MaxTokens: 256, Temperature: null,
            InputUsdPer1M: 1m, OutputUsdPer1M: 2m);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
