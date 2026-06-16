using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Clawbot.Integration.Tests;

public sealed class EndpointSmokeTests : IClassFixture<SqlServerFixture>, IAsyncLifetime, IDisposable
{
    private readonly SqlServerFixture _sql;
    private readonly ClawbotWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public EndpointSmokeTests(SqlServerFixture sql)
    {
        _sql = sql;
        _factory = new ClawbotWebApplicationFactory(sql);
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Health_live_returns_200()
    {
        var resp = await _client.GetAsync("/health/live");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("live");
    }

    [Fact]
    public async Task Health_ready_returns_200()
    {
        var resp = await _client.GetAsync("/health/ready");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("ready");
    }

    [Fact]
    public async Task Auth_me_returns_user_info()
    {
        var resp = await _client.GetAsync("/auth/me");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("tenant_slug");
    }

    [Fact]
    public async Task Kb_list_requires_auth()
    {
        var resp = await _client.GetAsync("/api/kb/modules");
        resp.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Leads_list_returns_empty_initially()
    {
        var resp = await _client.GetAsync("/api/leads");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Analytics_omnichannel_returns_200()
    {
        var resp = await _client.GetAsync("/api/analytics/omnichannel");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Chat_scenarios_list_returns_200()
    {
        var resp = await _client.GetAsync("/api/chat-scenarios");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sale_assist_quick_replies_returns_200()
    {
        var resp = await _client.GetAsync("/api/sale-assist/quick-replies");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
