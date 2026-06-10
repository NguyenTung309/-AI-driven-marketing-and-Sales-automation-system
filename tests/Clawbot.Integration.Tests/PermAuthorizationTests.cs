using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Integration.Tests;

public sealed class PermAuthorizationTests : IClassFixture<SqlServerFixture>, IAsyncLifetime, IDisposable
{
    private readonly SqlServerFixture _sql;
    private readonly ClawbotWebApplicationFactory _withPermsFactory;
    private readonly ClawbotWebApplicationFactory _noPermsFactory;
    private readonly HttpClient _withPerms;
    private readonly HttpClient _noPerms;

    public PermAuthorizationTests(SqlServerFixture sql)
    {
        _sql = sql;
        _withPermsFactory = new ClawbotWebApplicationFactory(sql);
        _noPermsFactory = new ClawbotWebApplicationFactory(sql, configureAuth: services =>
        {
            services.AddAuthentication("TestNoPerms")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandlerNoPerms>("TestNoPerms", _ => { });
            services.AddAuthorizationBuilder()
                .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder("TestNoPerms")
                    .RequireAuthenticatedUser()
                    .Build());
        });
        _withPerms = _withPermsFactory.CreateClient();
        _noPerms = _noPermsFactory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        _withPerms.Dispose();
        _noPerms.Dispose();
        return Task.WhenAll(
            _withPermsFactory.DisposeAsync().AsTask(),
            _noPermsFactory.DisposeAsync().AsTask());
    }

    public void Dispose()
    {
        _withPerms.Dispose();
        _noPerms.Dispose();
        _withPermsFactory.Dispose();
        _noPermsFactory.Dispose();
    }

    [Theory]
    [InlineData("/api/kb/modules")]
    [InlineData("/api/leads")]
    [InlineData("/api/inbox/conversations")]
    [InlineData("/api/content/items")]
    [InlineData("/api/ads/campaigns")]
    [InlineData("/api/analytics/omnichannel")]
    [InlineData("/api/sale-assist/quick-replies")]
    public async Task Endpoint_returns_403_when_perm_claims_missing(string url)
    {
        var resp = await _noPerms.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            because: $"endpoint {url} should require perm claims");
    }

    [Theory]
    [InlineData("/api/kb/modules")]
    [InlineData("/api/leads")]
    [InlineData("/api/inbox/conversations")]
    [InlineData("/api/sale-assist/quick-replies")]
    public async Task Endpoint_returns_200_when_perm_claims_present(string url)
    {
        var resp = await _withPerms.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            because: $"endpoint {url} should succeed with perm claims");
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_allow_anonymous(string url)
    {
        var resp = await _noPerms.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            because: $"health endpoint {url} should be anonymous");
    }
}
