using Clawbot.Agents.Core.Chat;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
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

    [Fact]
    public void AddClawbotChat_ignores_private_base_url_flag_outside_development()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LlmBaseUrl:AllowPrivate"] = "true" })
            .Build();
        services.AddClawbotChat(config, new TestHostEnvironment(Environments.Production));
        using var provider = services.BuildServiceProvider();
        var sut = provider.GetRequiredService<ILlmChatClientFactory>();

        var act = () => sut.Create(Config("anthropic") with { BaseUrl = "http://localhost:11434" });

        act.Should().Throw<InvalidOperationException>().WithMessage("*base URL*");
    }

    private static ResolvedLlmConfig Config(string provider) =>
        new(provider, "model-x", "key", BaseUrl: null, InputUsdPer1M: 1m, OutputUsdPer1M: 2m);

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Clawbot.Agents.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
