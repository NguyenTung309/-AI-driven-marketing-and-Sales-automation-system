using System.Net;
using FluentAssertions;

namespace Clawbot.Api.Tests.Integration;

public sealed class HealthAndAuthGateTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public HealthAndAuthGateTests(ApiTestFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_Respond(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        // /health/ready phụ thuộc RabbitMQ/Redis nên có thể unhealthy — chỉ cần endpoint trả lời.
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("/api/leads")]
    [InlineData("/api/content/briefs")]
    [InlineData("/api/agents")]
    [InlineData("/api/kb/modules")]
    [InlineData("/api/labels")]
    [InlineData("/api/notifications")]
    [InlineData("/api/jobs")]
    public async Task ProtectedEndpoints_WithoutToken_Return401(string path)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/khong-ton-tai", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SecurityHeaders_ArePresentOnResponses()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.Headers.Contains("X-Content-Type-Options").Should().BeTrue();
    }
}
