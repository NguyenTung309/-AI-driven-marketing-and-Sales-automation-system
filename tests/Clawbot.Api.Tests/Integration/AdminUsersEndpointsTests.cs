using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/admin/users. Admin gốc có admin:users-manage nên đi nhánh isSystemAdmin — không phủ
/// nhánh onlySale/isSaleAdmin (cần seed user thứ hai + role Sale để login riêng, ngoài phạm vi
/// batch này). Pancake wiring test theo nhánh page_id thiếu token -> inbox_not_found 400.
/// </summary>
public sealed class AdminUsersEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public AdminUsersEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private async Task<Guid> GetAdminTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private static async Task<Guid> CreateUserAsync(HttpClient client, string? roles = null)
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        var response = await client.PostAsJsonAsync(new Uri("/api/admin/users", UriKind.Relative), new
        {
            email,
            displayName = "Nhan Vien Test",
            password = "Test-User-Password-1!",
            roles = roles is null ? null : new[] { roles },
            pancakeAccessToken = (string?)null,
            pancakePageId = (string?)null,
            pancakePlatform = (string?)null,
            pancakeChannelName = (string?)null,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    // ------------------------------------------------------------------
    // GET list
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsUsers_WithRoles()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var userId = await CreateUserAsync(client, RbacSeeder.Sale);

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/admin/users", UriKind.Relative));

        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        var item = body.GetProperty("items").EnumerateArray()
            .First(i => Guid.Parse(i.GetProperty("id").GetString()!) == userId);
        item.GetProperty("roles").EnumerateArray().Select(r => r.GetString())
            .Should().Contain(RbacSeeder.Sale);
    }

    [Fact]
    public async Task List_SearchByQuery_FiltersByEmailOrName()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/admin/users?q={ApiTestFactory.AdminEmail}", UriKind.Relative));

        body.GetProperty("items").EnumerateArray()
            .Should().Contain(i => i.GetProperty("email").GetString() == ApiTestFactory.AdminEmail);
    }

    // ------------------------------------------------------------------
    // POST create
    // ------------------------------------------------------------------

    [Fact]
    public async Task Create_ValidRequest_ReturnsCreated()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var email = $"new-{Guid.NewGuid():N}@test.local";

        var response = await client.PostAsJsonAsync(new Uri("/api/admin/users", UriKind.Relative), new
        {
            email,
            displayName = "Nguoi Dung Moi",
            password = "Test-User-Password-1!",
            roles = new[] { RbacSeeder.Sale },
            pancakeAccessToken = (string?)null,
            pancakePageId = (string?)null,
            pancakePlatform = (string?)null,
            pancakeChannelName = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("email").GetString().Should().Be(email);
    }

    [Fact]
    public async Task Create_WeakPassword_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/admin/users", UriKind.Relative), new
        {
            email = $"weak-{Guid.NewGuid():N}@test.local",
            displayName = "Weak Pw",
            password = "123",
            roles = (string[]?)null,
            pancakeAccessToken = (string?)null,
            pancakePageId = (string?)null,
            pancakePlatform = (string?)null,
            pancakeChannelName = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_PancakePageIdWithoutTokenAndUnknownInbox_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/admin/users", UriKind.Relative), new
        {
            email = $"page-{Guid.NewGuid():N}@test.local",
            displayName = "Co Kenh",
            password = "Test-User-Password-1!",
            roles = (string[]?)null,
            pancakeAccessToken = (string?)null,
            pancakePageId = "page-khong-ton-tai",
            pancakePlatform = "zalo",
            pancakeChannelName = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("inbox_not_found");
    }

    // ------------------------------------------------------------------
    // PUT update
    // ------------------------------------------------------------------

    [Fact]
    public async Task Update_ChangesDisplayNameAndRoles()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var userId = await CreateUserAsync(client, RbacSeeder.Sale);

        var response = await client.PutAsJsonAsync(new Uri($"/api/admin/users/{userId}", UriKind.Relative), new
        {
            displayName = "Ten Da Doi",
            roles = new[] { RbacSeeder.Sale, RbacSeeder.SalesLead },
            isActive = (bool?)null,
            pancakeAccessToken = (string?)null,
            clearPancakeAccessToken = (bool?)null,
            pancakePageId = (string?)null,
            pancakePlatform = (string?)null,
            pancakeChannelName = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        user!.DisplayName.Should().Be("Ten Da Doi");
        (await users.GetRolesAsync(user)).Should().Contain(RbacSeeder.SalesLead);
    }

    [Fact]
    public async Task Update_NoChanges_ReturnsNoContent()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var userId = await CreateUserAsync(client);

        var response = await client.PutAsJsonAsync(new Uri($"/api/admin/users/{userId}", UriKind.Relative), new
        {
            displayName = (string?)null,
            roles = (string[]?)null,
            isActive = (bool?)null,
            pancakeAccessToken = (string?)null,
            clearPancakeAccessToken = (bool?)null,
            pancakePageId = (string?)null,
            pancakePlatform = (string?)null,
            pancakeChannelName = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Update_UnknownUser_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(new Uri($"/api/admin/users/{Guid.NewGuid()}", UriKind.Relative), new
        {
            displayName = "x",
            roles = (string[]?)null,
            isActive = (bool?)null,
            pancakeAccessToken = (string?)null,
            clearPancakeAccessToken = (bool?)null,
            pancakePageId = (string?)null,
            pancakePlatform = (string?)null,
            pancakeChannelName = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Disable / enable
    // ------------------------------------------------------------------

    [Fact]
    public async Task DisableThenEnable_TogglesIsActive()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var userId = await CreateUserAsync(client);

        var disabled = await client.PostAsync(new Uri($"/api/admin/users/{userId}/disable", UriKind.Relative), content: null);
        disabled.StatusCode.Should().Be(HttpStatusCode.OK);
        (await disabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeFalse();

        var enabled = await client.PostAsync(new Uri($"/api/admin/users/{userId}/enable", UriKind.Relative), content: null);
        enabled.StatusCode.Should().Be(HttpStatusCode.OK);
        (await enabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Disable_UnknownUser_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(new Uri($"/api/admin/users/{Guid.NewGuid()}/disable", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Reset password
    // ------------------------------------------------------------------

    [Fact]
    public async Task ResetPassword_KnownUser_ReturnsOk()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var userId = await CreateUserAsync(client);

        var response = await client.PostAsync(new Uri($"/api/admin/users/{userId}/reset-password", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResetPassword_UnknownUser_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(new Uri($"/api/admin/users/{Guid.NewGuid()}/reset-password", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
