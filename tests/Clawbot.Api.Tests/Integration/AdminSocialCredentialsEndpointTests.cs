using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/admin/social-credentials (zalo + instagram). Mỗi test tạo factory riêng vì endpoint chỉ có
/// đúng 2 provider cố định — seed của test này sẽ phá assertion của test khác nếu chung DB InMemory.
/// Nhánh update row đã tồn tại (CompareAndSwapExistingAsync) dùng ExecuteUpdateAsync — InMemory không
/// hỗ trợ — nên chỉ phủ đường tạo mới (chưa có row) và các nhánh validation/repair-denied trước DB.
/// </summary>
public sealed class AdminSocialCredentialsEndpointTests
{
    private const string ZaloEndpoint = "https://openapi.zalo.me/v3";

    private static async Task<Guid> SeedCredentialAsync(
        ApiTestFactory factory,
        string provider,
        string encrypted,
        bool isActive = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        var row = SocialCredential.Create(tenant.Id, provider, encrypted, DateTimeOffset.UtcNow);
        if (!isActive)
            row.Deactivate(DateTimeOffset.UtcNow);
        db.SocialCredentials.Add(row);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    private static string EncodeEnvelope(
        ApiTestFactory factory,
        Guid tenantId,
        string provider,
        GraphChannelOptions options)
    {
        using var scope = factory.Services.CreateScope();
        var encryptor = scope.ServiceProvider.GetRequiredService<IEncryptor>();
        return SocialCredentialEnvelopeCodec.Encode(encryptor, tenantId, provider, pageId: null, options);
    }

    private static async Task<JsonElement> GetItemsAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/api/admin/social-credentials", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("items");
    }

    private static JsonElement FindProvider(JsonElement items, string provider)
    {
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("provider").GetString() == provider)
                return item;
        }
        throw new InvalidOperationException($"provider {provider} missing from list response");
    }

    // ------------------------------------------------------------------
    // GET list
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_NoRows_ReturnsAbsentForBothProviders()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var items = await GetItemsAsync(client);

        items.GetArrayLength().Should().Be(2);
        FindProvider(items, "zalo").GetProperty("resolutionState").GetString().Should().Be("absent");
        FindProvider(items, "instagram").GetProperty("resolutionState").GetString().Should().Be("absent");
    }

    [Fact]
    public async Task List_InactiveRow_ReportsInvalid()
    {
        using var factory = new ApiTestFactory();
        await SeedCredentialAsync(factory, "zalo", "du-lieu-ma-hoa-gia", isActive: false);
        var client = await factory.CreateAuthenticatedClientAsync();

        var items = await GetItemsAsync(client);

        FindProvider(items, "zalo").GetProperty("resolutionState").GetString().Should().Be("invalid");
    }

    [Fact]
    public async Task List_ActiveRowWithGarbageCipher_ReportsInvalid()
    {
        using var factory = new ApiTestFactory();
        await SeedCredentialAsync(factory, "zalo", "du-lieu-ma-hoa-gia");
        var client = await factory.CreateAuthenticatedClientAsync();

        var items = await GetItemsAsync(client);

        FindProvider(items, "zalo").GetProperty("resolutionState").GetString().Should().Be("invalid");
    }

    [Fact]
    public async Task List_ActiveZaloRowWithValidEnvelope_ReportsResolved()
    {
        using var factory = new ApiTestFactory();
        var tenantId = await SeedCredentialAsync(factory, "zalo", "cho-choi");
        var envelope = EncodeEnvelope(factory, tenantId, "zalo", new GraphChannelOptions
        {
            Enabled = true,
            Endpoint = ZaloEndpoint,
            OaId = "oa-123",
            OaAccessToken = "zalo-token",
        });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.SocialCredentials.IgnoreQueryFilters()
                .FirstAsync(c => c.TenantId == tenantId && c.Provider == "zalo");
            row.UpdateCredentials(envelope, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        var client = await factory.CreateAuthenticatedClientAsync();

        var items = await GetItemsAsync(client);

        var zalo = FindProvider(items, "zalo");
        zalo.GetProperty("resolutionState").GetString().Should().Be("resolved");
        zalo.GetProperty("oaId").GetString().Should().Be("oa-123");
        zalo.GetProperty("hasOaAccessToken").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task List_ActiveInstagramRowWithValidEnvelope_ReportsResolved()
    {
        using var factory = new ApiTestFactory();
        var tenantId = await SeedCredentialAsync(factory, "instagram", "cho-choi");
        var envelope = EncodeEnvelope(factory, tenantId, "instagram", new GraphChannelOptions
        {
            Enabled = true,
            PageId = "123456789",
            PageAccessToken = "ig-token",
        });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.SocialCredentials.IgnoreQueryFilters()
                .FirstAsync(c => c.TenantId == tenantId && c.Provider == "instagram");
            row.UpdateCredentials(envelope, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        var client = await factory.CreateAuthenticatedClientAsync();

        var items = await GetItemsAsync(client);

        FindProvider(items, "instagram").GetProperty("resolutionState").GetString().Should().Be("resolved");
    }

    // ------------------------------------------------------------------
    // PUT validation (trước DB)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Put_UnknownProvider_IsRejected()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/admin/social-credentials/facebook", UriKind.Relative),
            new { enabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("admin.social_provider_invalid");
    }

    [Fact]
    public async Task Put_FieldTooLong_IsRejected()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/admin/social-credentials/zalo", UriKind.Relative),
            new { pageAccessToken = new string('x', 513) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("admin.social_field_too_long");
    }

    [Fact]
    public async Task Put_InstagramWithZaloFields_IsRejected()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/admin/social-credentials/instagram", UriKind.Relative),
            new { enabled = true, pageId = "123", pageAccessToken = "t", oaId = "oa" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("admin.instagram_fields_invalid");
    }

    [Fact]
    public async Task Put_InstagramUserIdTooLong_IsRejected()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/admin/social-credentials/instagram", UriKind.Relative),
            new { enabled = true, pageId = new string('1', 129), pageAccessToken = "t" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("admin.instagram_user_id_too_long");
    }

    [Fact]
    public async Task Put_ZaloEndpointNotHttps_IsRejected()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/admin/social-credentials/zalo", UriKind.Relative),
            new { enabled = true, endpoint = "http://openapi.zalo.me", oaId = "oa", oaAccessToken = "t" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("admin.social_endpoint_invalid");
    }

    // ------------------------------------------------------------------
    // PUT merged-validation (create path, row chưa tồn tại)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Put_InstagramEnabledWithoutToken_IsRejected()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/admin/social-credentials/instagram", UriKind.Relative),
            new { enabled = true, pageId = "123456" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("admin.instagram_credentials_invalid");
    }

    [Fact]
    public async Task Put_InstagramEnabledWithNonNumericId_IsRejected()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/admin/social-credentials/instagram", UriKind.Relative),
            new { enabled = true, pageId = "khong-phai-so", pageAccessToken = "t" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("admin.instagram_credentials_invalid");
    }

    [Fact]
    public async Task Put_ZaloEnabledMissingOaToken_IsRejected()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/admin/social-credentials/zalo", UriKind.Relative),
            new { enabled = true, endpoint = ZaloEndpoint, oaId = "oa-123" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("admin.zalo_credentials_invalid");
    }

    // ------------------------------------------------------------------
    // PUT create path (chưa có row) + đọc lại qua GET
    // ------------------------------------------------------------------

    [Fact]
    public async Task Put_ZaloFullCreate_ReturnsResolvedAndPersists()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/admin/social-credentials/zalo", UriKind.Relative),
            new { enabled = true, endpoint = ZaloEndpoint, oaId = "oa-123", oaAccessToken = "zalo-token" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("resolutionState").GetString().Should().Be("resolved");

        var items = await GetItemsAsync(client);
        var zalo = FindProvider(items, "zalo");
        zalo.GetProperty("resolutionState").GetString().Should().Be("resolved");
        zalo.GetProperty("endpoint").GetString().Should().Be(ZaloEndpoint);
        zalo.GetProperty("hasOaAccessToken").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Put_InstagramFullCreate_ReturnsResolved()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/admin/social-credentials/instagram", UriKind.Relative),
            new { enabled = true, pageId = "123456789", pageAccessToken = "ig-token" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("resolutionState").GetString().Should().Be("resolved");
        dto.GetProperty("hasPageAccessToken").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Put_DisabledInstagram_ReturnsDisabledState()
    {
        using var factory = new ApiTestFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/admin/social-credentials/instagram", UriKind.Relative),
            new { enabled = false });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("resolutionState").GetString().Should().Be("disabled");
    }

    // ------------------------------------------------------------------
    // PUT repair-denied (row tồn tại nhưng envelope hỏng, cập nhật không đầy đủ)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Put_PartialUpdateOnInvalidInstagramRow_IsRejected()
    {
        using var factory = new ApiTestFactory();
        await SeedCredentialAsync(factory, "instagram", "du-lieu-ma-hoa-gia");
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/admin/social-credentials/instagram", UriKind.Relative),
            new { enabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("admin.instagram_credentials_invalid");
    }

    [Fact]
    public async Task Put_PartialUpdateOnInvalidZaloRow_IsRejected()
    {
        using var factory = new ApiTestFactory();
        await SeedCredentialAsync(factory, "zalo", "du-lieu-ma-hoa-gia");
        var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri("/api/admin/social-credentials/zalo", UriKind.Relative),
            new { enabled = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("admin.zalo_credentials_invalid");
    }
}