using System.Collections.Concurrent;
using System.Net;
using Clawbot.Infrastructure;
using Clawbot.Infrastructure.Channels.Pancake;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Tests;

public sealed class PancakeHttpClientRegistrationTests
{
    [Fact]
    public async Task MintAsync_SendsExactlyOneRequest_WhenServerReturns503()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await using var provider = BuildProvider(handler);
        var gateway = provider.GetRequiredService<IPageTokenMintGateway>();

        // Act
        var act = () => gateway.MintAsync("sensitive-user-token", "page-1");

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        handler.RequestCount.Should().Be(1);
    }

    [Theory]
    [InlineData("http://pages.fm/api/v1")]
    [InlineData("https://pages.fm.evil.test/api/v1")]
    [InlineData("https://pages.fm:444/api/v1")]
    [InlineData("https://user@pages.fm/api/v1")]
    [InlineData("https://pages.fm/unapproved")]
    public void NormalizeBaseUrl_RejectsUnapprovedEndpoint(string baseUrl)
    {
        // Act
        var act = () => PancakeEndpointPolicy.NormalizeBaseUrl(baseUrl);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("pancake_base_url_not_allowed*");
    }

    [Theory]
    [InlineData("https://pages.fm/api/v1", "https://pages.fm/api/v1")]
    [InlineData("https://pages.fm/api/public_api/v1/", "https://pages.fm/api/public_api/v1")]
    [InlineData("https://pages.fm/api/public_api/v2", "https://pages.fm/api/public_api/v2")]
    public void NormalizeBaseUrl_AcceptsApprovedEndpoint(string baseUrl, string expected)
    {
        // Act
        var result = PancakeEndpointPolicy.NormalizeBaseUrl(baseUrl);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public async Task MintAsync_RejectsUnapprovedHostBeforeSendingCredential()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        await using var provider = BuildProvider(
            handler,
            baseUrl: "https://attacker.test/api/v1");
        var gateway = provider.GetRequiredService<IPageTokenMintGateway>();

        // Act
        var act = () => gateway.MintAsync("sensitive-user-token", "page-1");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("pancake_base_url_not_allowed*");
        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task MintAsync_EmitsNoHttpClientFactoryLogs_ForCredentialBearingUri()
    {
        // Arrange
        var logs = new ConcurrentBag<LogEntry>();
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"page-token\"}"),
        });
        await using var provider = BuildProvider(handler, logs);
        var gateway = provider.GetRequiredService<IPageTokenMintGateway>();

        // Act
        await gateway.MintAsync("credential-that-must-not-appear", "page-1");

        // Assert
        logs.Should().NotContain(entry =>
            entry.Category.StartsWith("System.Net.Http.HttpClient.", StringComparison.Ordinal));
        logs.Should().NotContain(entry =>
            entry.Message.Contains("credential-that-must-not-appear", StringComparison.Ordinal));
    }

    private static ServiceProvider BuildProvider(
        HttpMessageHandler handler,
        ConcurrentBag<LogEntry>? logs = null,
        string baseUrl = "https://pages.fm/api/v1")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Channels:Pancake:UserApi:BaseUrl"] = baseUrl,
                ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=unused;",
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["ConnectionStrings:RabbitMq"] = "amqp://guest:guest@localhost:5672",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            if (logs is not null)
                builder.AddProvider(new CollectingLoggerProvider(logs));
        });
        services.AddInfrastructure(configuration);
        services.AddHttpClient<IPageTokenMintGateway, HttpPancakePageTokenMintGateway>()
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed record LogEntry(string Category, string Message);

    private sealed class CollectingLoggerProvider(ConcurrentBag<LogEntry> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, logs);
        public void Dispose() { }
    }

    private sealed class CollectingLogger(
        string category,
        ConcurrentBag<LogEntry> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            logs.Add(new LogEntry(category, formatter(state, exception)));
    }
}
