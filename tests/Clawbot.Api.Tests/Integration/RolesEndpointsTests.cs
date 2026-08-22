using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/rbac/roles + /api/rbac/permissions (perm rbac:manage). Role permissions keyed on cố định
/// Identity AppRole.Id (RbacSeeder.RoleIds) — không phải RbacRoles theo tenant.
/// </summary>
public sealed class RolesEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public RolesEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private static string UniqueRoleName() => $"role-{Guid.NewGuid():N}"[..16];

    private static async Task<Guid> CreateRoleAsync(HttpClient client, string? name = null)
    {
        var response = await client.PostAsJsonAsync(new Uri("/api/rbac/roles", UriKind.Relative), new
        {
            name = name ?? UniqueRoleName(),
            description = "Vai tro test",
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    // ------------------------------------------------------------------
    // Roles CRUD
    // ------------------------------------------------------------------

    [Fact]
    public async Task Create_ValidName_ReturnsCreated()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var name = UniqueRoleName();

        var response = await client.PostAsJsonAsync(new Uri("/api/rbac/roles", UriKind.Relative), new
        {
            name,
            description = "Mo ta",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be(name);
        body.GetProperty("isSystem").GetBoolean().Should().BeFalse("role tạo qua API không phải hệ thống");
    }

    [Fact]
    public async Task Create_BlankName_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/rbac/roles", UriKind.Relative), new
        {
            name = "   ",
            description = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var name = UniqueRoleName();
        await CreateRoleAsync(client, name);

        var response = await client.PostAsJsonAsync(new Uri("/api/rbac/roles", UriKind.Relative), new
        {
            name,
            description = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task List_ReturnsCreatedRole()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateRoleAsync(client);

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/rbac/roles", UriKind.Relative));

        body.EnumerateArray().Should().Contain(r => r.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Update_ChangesNameAndDescription()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateRoleAsync(client);
        var newName = UniqueRoleName();

        var response = await client.PutAsJsonAsync(new Uri($"/api/rbac/roles/{id}", UriKind.Relative), new
        {
            name = newName,
            description = "Mo ta moi",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().Should().Be(newName);
        body.GetProperty("description").GetString().Should().Be("Mo ta moi");
    }

    [Fact]
    public async Task Update_UnknownRole_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(new Uri($"/api/rbac/roles/{Guid.NewGuid()}", UriKind.Relative), new
        {
            name = "x",
            description = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_CustomRole_RemovesFromList()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateRoleAsync(client);

        var response = await client.DeleteAsync(new Uri($"/api/rbac/roles/{id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/rbac/roles", UriKind.Relative));
        body.EnumerateArray().Should().NotContain(r => r.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Delete_UnknownRole_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(new Uri($"/api/rbac/roles/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_SystemRole_IsForbidden()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var adminRoleId = RbacSeeder.RoleIds[RbacSeeder.Admin];

        var response = await client.DeleteAsync(new Uri($"/api/rbac/roles/{adminRoleId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------
    // Role permissions (keyed trên Identity AppRole.Id cố định)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListRolePermissions_KnownRole_ReturnsSeededPermissions()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var adminRoleId = RbacSeeder.RoleIds[RbacSeeder.Admin];

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/rbac/roles/{adminRoleId}/permissions", UriKind.Relative));

        body.GetArrayLength().Should().BeGreaterThan(0, "Admin đã được seed nhiều quyền");
    }

    [Fact]
    public async Task ListRolePermissions_UnknownRoleId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/rbac/roles/{Guid.NewGuid()}/permissions", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetRolePermissions_ReplacesAndInvalidatesCache()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var viewerRoleId = RbacSeeder.RoleIds[RbacSeeder.Viewer];

        Guid[] permissionIds;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            permissionIds = await db.Permissions.Take(2).Select(p => p.Id).ToArrayAsync();
        }
        permissionIds.Should().NotBeEmpty("cần seed permissions trước để test set");

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/rbac/roles/{viewerRoleId}/permissions", UriKind.Relative),
            new { permissionIds });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/rbac/roles/{viewerRoleId}/permissions", UriKind.Relative));
        body.GetArrayLength().Should().Be(permissionIds.Length);
    }

    [Fact]
    public async Task SetRolePermissions_UnknownRoleId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/rbac/roles/{Guid.NewGuid()}/permissions", UriKind.Relative),
            new { permissionIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetRolePermissions_InvalidPermissionIds_AreIgnored()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var qaRoleId = RbacSeeder.RoleIds[RbacSeeder.QA];

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/rbac/roles/{qaRoleId}/permissions", UriKind.Relative),
            new { permissionIds = new[] { Guid.NewGuid() } });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/rbac/roles/{qaRoleId}/permissions", UriKind.Relative));
        body.GetArrayLength().Should().Be(0, "permission id lạ bị lọc bỏ, không tạo link rác");
    }

    // ------------------------------------------------------------------
    // GET /api/rbac/permissions
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListPermissions_ReturnsSeededCatalog()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/rbac/permissions", UriKind.Relative));

        body.GetArrayLength().Should().BeGreaterThan(0);
        body.EnumerateArray().Should().Contain(p => p.GetProperty("code").GetString() == "rbac:manage");
    }
}
