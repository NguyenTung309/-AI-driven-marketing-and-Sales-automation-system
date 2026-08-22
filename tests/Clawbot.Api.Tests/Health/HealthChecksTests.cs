using System.Net;
using Clawbot.Api.Health;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;

namespace Clawbot.Api.Tests.Health;

/// <summary>
/// AgentServiceHealthCheck.CheckHealthAsync: thiếu/URL hỏng "AgentService:HealthUrl" -> Unhealthy;
/// HTTP 200 -> Healthy; HTTP lỗi hoặc throw HttpRequestException -> Unhealthy (không throw ra ngoài).
/// </summary>
public sealed class AgentServiceHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_MissingHealthUrlConfig_ReturnsUnhealthy()
    {
        var configuration = BuildConfiguration([]);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var check = new AgentServiceHealthCheck(httpClientFactory, configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_InvalidHealthUrlConfig_ReturnsUnhealthy()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AgentService:HealthUrl"] = "not-a-valid-absolute-url",
        });
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var check = new AgentServiceHealthCheck(httpClientFactory, configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_HandlerReturns200_ReturnsHealthy()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AgentService:HealthUrl"] = "https://agent-service.test/health",
        });
        var httpClientFactory = FakeHttpClientFactory(
            new StubHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var check = new AgentServiceHealthCheck(httpClientFactory, configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_HandlerReturns503_ReturnsUnhealthy()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AgentService:HealthUrl"] = "https://agent-service.test/health",
        });
        var httpClientFactory = FakeHttpClientFactory(
            new StubHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var check = new AgentServiceHealthCheck(httpClientFactory, configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_HandlerThrowsHttpRequestException_ReturnsUnhealthyWithException()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AgentService:HealthUrl"] = "https://agent-service.test/health",
        });
        var httpClientFactory = FakeHttpClientFactory(
            new StubHttpMessageHandler((_, _) =>
                Task.FromException<HttpResponseMessage>(new HttpRequestException("fixture failure"))));
        var check = new AgentServiceHealthCheck(httpClientFactory, configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<HttpRequestException>();
    }

    internal static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    internal static IHttpClientFactory FakeHttpClientFactory(HttpMessageHandler handler)
    {
        // httpClientFactory.CreateClient() trong code sản phẩm thực ra là extension method
        // gọi CreateClient(string) với tên mặc định; phải stub đúng overload có tham số này
        // thì NSubstitute mới chặn được cuộc gọi.
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));
        return factory;
    }

    internal sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}

/// <summary>
/// HttpEndpointHealthCheck.CheckHealthAsync: tương tự AgentServiceHealthCheck nhưng
/// configurationKey được truyền qua constructor thay vì hardcode.
/// </summary>
public sealed class HttpEndpointHealthCheckTests
{
    private const string ConfigurationKey = "Some:Url";

    [Fact]
    public async Task CheckHealthAsync_MissingConfiguredKey_ReturnsUnhealthy()
    {
        var configuration = AgentServiceHealthCheckTests.BuildConfiguration([]);
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var check = new HttpEndpointHealthCheck(httpClientFactory, configuration, ConfigurationKey);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_InvalidConfiguredUrl_ReturnsUnhealthy()
    {
        var configuration = AgentServiceHealthCheckTests.BuildConfiguration(new Dictionary<string, string?>
        {
            [ConfigurationKey] = "not-a-valid-absolute-url",
        });
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var check = new HttpEndpointHealthCheck(httpClientFactory, configuration, ConfigurationKey);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_HandlerReturns200_ReturnsHealthy()
    {
        var configuration = AgentServiceHealthCheckTests.BuildConfiguration(new Dictionary<string, string?>
        {
            [ConfigurationKey] = "https://endpoint.test/health",
        });
        var httpClientFactory = AgentServiceHealthCheckTests.FakeHttpClientFactory(
            new AgentServiceHealthCheckTests.StubHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        var check = new HttpEndpointHealthCheck(httpClientFactory, configuration, ConfigurationKey);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_HandlerReturns503_ReturnsUnhealthy()
    {
        var configuration = AgentServiceHealthCheckTests.BuildConfiguration(new Dictionary<string, string?>
        {
            [ConfigurationKey] = "https://endpoint.test/health",
        });
        var httpClientFactory = AgentServiceHealthCheckTests.FakeHttpClientFactory(
            new AgentServiceHealthCheckTests.StubHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
        var check = new HttpEndpointHealthCheck(httpClientFactory, configuration, ConfigurationKey);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_HandlerThrowsHttpRequestException_ReturnsUnhealthyWithException()
    {
        var configuration = AgentServiceHealthCheckTests.BuildConfiguration(new Dictionary<string, string?>
        {
            [ConfigurationKey] = "https://endpoint.test/health",
        });
        var httpClientFactory = AgentServiceHealthCheckTests.FakeHttpClientFactory(
            new AgentServiceHealthCheckTests.StubHttpMessageHandler((_, _) =>
                Task.FromException<HttpResponseMessage>(new HttpRequestException("fixture failure"))));
        var check = new HttpEndpointHealthCheck(httpClientFactory, configuration, ConfigurationKey);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<HttpRequestException>();
    }
}

/// <summary>
/// RabbitMqHealthCheck.CheckHealthAsync: thiếu/URI hỏng "ConnectionStrings:RabbitMq" -> Unhealthy;
/// kết nối TCP thất bại (cổng chắc chắn không có listener) -> Unhealthy, không throw ra ngoài.
/// </summary>
public sealed class RabbitMqHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_MissingConnectionString_ReturnsUnhealthy()
    {
        var configuration = AgentServiceHealthCheckTests.BuildConfiguration([]);
        var check = new RabbitMqHealthCheck(configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_ConnectionStringNotAbsoluteUri_ReturnsUnhealthy()
    {
        var configuration = AgentServiceHealthCheckTests.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:RabbitMq"] = "not-a-valid-absolute-uri",
        });
        var check = new RabbitMqHealthCheck(configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_PortRefusesConnection_ReturnsUnhealthy()
    {
        // Cổng 1 trên loopback gần như chắc chắn bị hệ điều hành từ chối ngay lập tức
        // (không có gì lắng nghe), nên test không cần TcpClient giả hay timeout dài.
        var configuration = AgentServiceHealthCheckTests.BuildConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:RabbitMq"] = "amqp://127.0.0.1:1",
        });
        var check = new RabbitMqHealthCheck(configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
