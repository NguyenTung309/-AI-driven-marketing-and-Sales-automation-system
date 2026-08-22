using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/admin/channels/pancake/* (quyền channels:manage).
/// ConnectPancakeAsync và MintPancakePagesAsync gọi HTTP thật ra Pancake qua IPageListGateway /
/// IPancakePageTokenService — không có cách override 2 service này qua DI trong ApiTestFactory
/// hiện tại, nên chỉ test được nhánh validate sớm (400) chạy trước khi chạm network. Nhánh GET
/// list-connected-pages thuần EF nên test đầy đủ, kể cả filter loại trừ inbox không active.
/// </summary>
public sealed class AdminChannelsEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public AdminChannelsEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private static async Task<Guid> DefaultTenantIdAsync(ApiTestFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private static async Task<Guid> SeedConnectedInboxAsync(ApiTestFactory factory, string name, string externalPageId)
    {
        var tenantId = await DefaultTenantIdAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inbox = Inbox.Create(tenantId, name, "facebook", externalPageId);
        inbox.SetAccessToken("cipher-token-mocked", DateTimeOffset.UtcNow);
        db.Inboxes.Add(inbox);
        await db.SaveChangesAsync();
        return inbox.Id;
    }

    private static async Task<Guid> SeedNotConfiguredInboxAsync(ApiTestFactory factory, string name, string externalPageId)
    {
        var tenantId = await DefaultTenantIdAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inbox = Inbox.Create(tenantId, name, "facebook", externalPageId);
        db.Inboxes.Add(inbox);
        await db.SaveChangesAsync();
        return inbox.Id;
    }

    /// <summary>
    /// Seed 1 inbox rồi hạ IsActive/DeletedAt thẳng qua ChangeTracker (setter là private, không có
    /// domain method soft-delete public) — theo tiền lệ AgentsEndpointTests / PublicWidgetEndpointTests
    /// dùng db.Entry(x).Property(...).CurrentValue thay vì reflection.
    /// </summary>
    private static async Task<Guid> SeedInactiveInboxAsync(ApiTestFactory factory, string name, string externalPageId)
    {
        var tenantId = await DefaultTenantIdAsync(factory);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inbox = Inbox.Create(tenantId, name, "facebook", externalPageId);
        inbox.SetAccessToken("cipher-token-mocked", DateTimeOffset.UtcNow);
        db.Inboxes.Add(inbox);
        await db.SaveChangesAsync();

        db.Entry(inbox).Property(nameof(Inbox.IsActive)).CurrentValue = false;
        db.Entry(inbox).Property(nameof(Inbox.DeletedAt)).CurrentValue = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return inbox.Id;
    }

    // ------------------------------------------------------------------
    // POST /api/admin/channels/pancake/connect — chỉ nhánh validate sớm
    // ------------------------------------------------------------------

    [Fact]
    public async Task ConnectPancake_EmptyToken_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/channels/pancake/connect", UriKind.Relative),
            new { userAccessToken = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("user_access_token_required");
    }

    [Fact]
    public async Task ConnectPancake_WhitespaceToken_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/channels/pancake/connect", UriKind.Relative),
            new { userAccessToken = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("user_access_token_required");
    }

    [Fact]
    public async Task ConnectPancake_MissingTokenField_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Không gửi field userAccessToken -> record deserialize thành chuỗi rỗng mặc định của string.
        var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/channels/pancake/connect", UriKind.Relative),
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("user_access_token_required");
    }

    // ------------------------------------------------------------------
    // POST /api/admin/channels/pancake/pages — chỉ nhánh validate sớm
    // ------------------------------------------------------------------

    [Fact]
    public async Task MintPancakePages_EmptyToken_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/channels/pancake/pages", UriKind.Relative),
            new { userAccessToken = "", pages = new[] { new { pageId = "p1", name = "Page 1", platform = "facebook" } } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("user_access_token_and_pages_required");
    }

    [Fact]
    public async Task MintPancakePages_EmptyPagesList_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/channels/pancake/pages", UriKind.Relative),
            new { userAccessToken = "user-token-abc", pages = Array.Empty<object>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("user_access_token_and_pages_required");
    }

    [Fact]
    public async Task MintPancakePages_MissingPagesField_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Không gửi field pages -> record deserialize Pages = null.
        var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/channels/pancake/pages", UriKind.Relative),
            new { userAccessToken = "user-token-abc" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("user_access_token_and_pages_required");
    }

    [Fact]
    public async Task MintPancakePages_BothTokenAndPagesMissing_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/admin/channels/pancake/pages", UriKind.Relative),
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("user_access_token_and_pages_required");
    }

    // ------------------------------------------------------------------
    // GET /api/admin/channels/pancake/pages — nhánh EF thuần
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListConnectedPages_ReturnsConnectedAndNotConfiguredStatus()
    {
        await SeedConnectedInboxAsync(_factory, "Kênh Đã Nối", "page_" + Guid.NewGuid().ToString("N"));
        await SeedNotConfiguredInboxAsync(_factory, "Kênh Chưa Nối", "page_" + Guid.NewGuid().ToString("N"));
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/admin/channels/pancake/pages", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        var connectedItem = items.Single(i => i.GetProperty("name").GetString() == "Kênh Đã Nối");
        connectedItem.GetProperty("status").GetString().Should().Be("connected");
        connectedItem.GetProperty("mintedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
        // Token thực sự không được lộ ra response — chỉ có status suy ra từ việc token có tồn tại.
        connectedItem.TryGetProperty("encryptedAccessToken", out _).Should().BeFalse();

        var notConfiguredItem = items.Single(i => i.GetProperty("name").GetString() == "Kênh Chưa Nối");
        notConfiguredItem.GetProperty("status").GetString().Should().Be("not_configured");
        notConfiguredItem.GetProperty("mintedAt").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ListConnectedPages_ExcludesInactiveOrSoftDeletedInbox()
    {
        await SeedNotConfiguredInboxAsync(_factory, "Kênh Hiện Diện", "page_" + Guid.NewGuid().ToString("N"));
        var hiddenName = "Kênh Đã Xoá " + Guid.NewGuid().ToString("N");
        await SeedInactiveInboxAsync(_factory, hiddenName, "page_" + Guid.NewGuid().ToString("N"));
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/admin/channels/pancake/pages", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var names = body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString())
            .ToList();

        names.Should().Contain("Kênh Hiện Diện");
        names.Should().NotContain(hiddenName);
    }

    [Fact]
    public async Task ListConnectedPages_ReturnsItemsAsJsonArray()
    {
        // DB InMemory dùng chung fixture cho cả class nên có thể đã có inbox từ test khác chạy
        // trước; chỉ assert kiểu dữ liệu trả về hợp lệ (mảng), không assert rỗng.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/admin/channels/pancake/pages", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
    }
}
