using System.IdentityModel.Tokens.Jwt;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

public sealed class PermissionSeedingTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public PermissionSeedingTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task IssuedToken_CarriesAdminRoleId()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var raw = client.DefaultRequestHeaders.Authorization!.Parameter!;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        var roleId = jwt.Claims.FirstOrDefault(c => c.Type == "role_id")?.Value;

        roleId.Should().NotBeNullOrWhiteSpace();
        roleId.Should().NotBe(Guid.Empty.ToString());
    }

    [Fact]
    public async Task AdminCanReadPermissionGatedEndpoint()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/labels/", UriKind.Relative));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task RolePermissions_AreSeededForAdminRole()
    {
        _ = await _factory.CreateAuthenticatedClientAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var adminRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var permissions = await db.RolePermissions
            .IgnoreQueryFilters()
            .Where(rp => rp.RoleId == adminRoleId)
            .CountAsync();

        permissions.Should().BeGreaterThan(0);
    }
}
