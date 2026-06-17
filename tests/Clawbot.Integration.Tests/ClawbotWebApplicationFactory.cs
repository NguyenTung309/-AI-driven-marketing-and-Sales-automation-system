using Clawbot.Api.Auth;
using Clawbot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Integration.Tests;

public sealed class ClawbotWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqlServerFixture _sql;
    private readonly Action<IServiceCollection>? _configureAuth;

    public ClawbotWebApplicationFactory(SqlServerFixture sql, Action<IServiceCollection>? configureAuth = null)
    {
        _sql = sql;
        _configureAuth = configureAuth;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SqlServer"] = _sql.ConnectionString,
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["Jwt:Issuer"] = "test",
                ["Jwt:Audience"] = "test-clients",
                ["Jwt:SigningKey"] = "test-signing-key-of-at-least-32-bytes!!",
                ["Jwt:AccessTokenMinutes"] = "60",
                ["AgentService:Url"] = "http://localhost:15875",
                ["Vector:Qdrant:Host"] = "localhost",
            });
        });

        if (_configureAuth is not null)
        {
            // Caller installs its own auth scheme (e.g. a no-perms principal for 403 tests).
            builder.ConfigureServices(_configureAuth);
        }
        else
        {
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

                services.AddAuthorizationBuilder()
                    .SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder("Test")
                        .RequireAuthenticatedUser()
                        .Build());
            });
        }

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
        });
    }

    public async Task InitializeAsync()
    {
        await using var conn = await _sql.OpenConnectionAsync();
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;
}
