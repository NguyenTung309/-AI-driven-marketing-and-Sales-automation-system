using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Clawbot.Api.Auth;
using Clawbot.Domain.Security;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests;

public sealed class ApiKeyAuthenticationHandlerTests
{
    [Fact]
    public async Task AuthenticateAsync_FindsTenantBoundKeyBeforeTenantContextExists()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        const string plaintext = "cbk_test_key";
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        await using (var seed = new AppDbContext(options, new FixedTenantAccessor(Guid.Empty)))
        {
            seed.ApiKeys.Add(ApiKey.Issue(
                tenantId,
                "integration",
                Hash(plaintext),
                DateTimeOffset.UtcNow,
                scopes: ["system:config"]));
            await seed.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantAccessor>(new FixedTenantAccessor(Guid.Empty));
        services.AddDbContext<AppDbContext>(builder => builder.UseInMemoryDatabase(databaseName));
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName,
                _ => { });
        await using var provider = services.BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        http.Request.Headers.Authorization = $"ApiKey {plaintext}";

        // Act
        var result = await http.AuthenticateAsync(ApiKeyAuthenticationHandler.SchemeName);

        // Assert
        result.Succeeded.Should().BeTrue();
        var principal = result.Principal ?? throw new InvalidOperationException("API key principal was not created.");
        principal.FindFirstValue("tenant_id").Should().Be(tenantId.ToString());
        principal.FindFirstValue("perm").Should().Be("system:config");
    }

    private static string Hash(string plaintext) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));

    private sealed class FixedTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }
}
