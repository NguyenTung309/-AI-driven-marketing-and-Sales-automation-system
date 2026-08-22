using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clawbot.Domain.Integrations;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /webhooks/meta/page khi CHƯA có Meta App config: GetWebhookCandidatesAsync trả rỗng -> 503.
/// Class riêng fixture riêng (DB InMemory độc lập) để không bị test khác seed config ảnh hưởng.
/// </summary>
public sealed class MetaPageWebhookUnconfiguredTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public MetaPageWebhookUnconfiguredTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Verify_WithoutAnyConfig_ReturnsServiceUnavailable()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri(
            "/webhooks/meta/page?hub.mode=subscribe&hub.verify_token=x&hub.challenge=y", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Receive_WithoutAnyConfig_ReturnsServiceUnavailable()
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri("/webhooks/meta/page", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}

/// <summary>
/// /webhooks/meta/page khi ĐÃ lưu Meta App config (seed qua IMetaAppConfigurationService —
/// đúng nguồn GetWebhookCandidatesAsync đọc, payload mã hoá bằng data protection của host).
/// Chữ ký X-Hub-Signature-256 = "sha256=" + hex(HMACSHA256(appSecret, payload)).
/// Nhánh publish qua MassTransit outbox (UseBusOutbox đệm vào AppDbContext, không cần broker).
/// </summary>
public sealed class MetaPageWebhookEndpointsTests : IClassFixture<ApiTestFactory>
{
    private const string AppSecret = "meta-secret-test";
    private const string VerifyToken = "verify-token-1";
    private const string PageId = "page-abc-123";

    private readonly ApiTestFactory _factory;

    public MetaPageWebhookEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private async Task<Guid> SeedMetaConfigAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        var configurations = scope.ServiceProvider.GetRequiredService<IMetaAppConfigurationService>();
        await configurations.UpdateAsync(tenant.Id, new MetaAppConfigurationUpdate(
            "123456789", AppSecret, "cfg-123", MetaAuthorizationModes.BusinessSystemUser,
            VerifyToken, "https://localhost:5001/api/admin/meta/callback", "https://localhost:5001/system"));
        return tenant.Id;
    }

    /// <summary>Seed connection active + page asset để comment ánh xạ được tenant.</summary>
    private async Task SeedPageAssetAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var connection = MetaConnection.Create(tenantId, "biz-1", "su-1",
            MetaConnectionTokenTypes.BusinessIntegrationSystemUser, "encrypted-token",
            "[\"pages_messaging\"]", null, null, now);
        db.MetaConnections.Add(connection);
        await db.SaveChangesAsync();
        db.MetaAssets.Add(MetaAsset.CreatePage(tenantId, connection.Id, PageId, "Trang Test",
            "[\"CREATE_CONTENT\"]", "encrypted-page-token", isDefault: true, now));
        await db.SaveChangesAsync();
    }

    private static string ComputeSignature(byte[] payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string body, string? signature)
    {
        using var client = _factory.CreateClient();
        var payload = Encoding.UTF8.GetBytes(body);
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/meta/page") { Content = content };
        request.Headers.Add("X-Hub-Signature-256", signature ?? ComputeSignature(payload));
        return await client.SendAsync(request);
    }

    /// <summary>Payload comment chuẩn của Meta Page webhook (feed/comment/add).</summary>
    private static string CommentPayload(string pageId, string commentId = "cmt-1") =>
        "{\"object\":\"page\",\"entry\":[{\"id\":\"" + pageId + "\",\"changes\":[{\"field\":\"feed\","
        + "\"value\":{\"item\":\"comment\",\"verb\":\"add\",\"comment_id\":\"" + commentId + "\","
        + "\"post_id\":\"post-1\",\"message\":\"Binh luan cua khach\",\"created_time\":1755600000,"
        + "\"from\":{\"id\":\"user-1\",\"name\":\"Khach Meta\"}}}]}]}";

    // ------------------------------------------------------------------
    // GET verify (hub.challenge)
    // ------------------------------------------------------------------

    [Fact]
    public async Task Verify_ValidToken_ReturnsChallenge()
    {
        await SeedMetaConfigAsync();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri(
            $"/webhooks/meta/page?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=ma-thach-thu",
            UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("ma-thach-thu");
    }

    [Fact]
    public async Task Verify_WrongToken_ReturnsForbidden()
    {
        await SeedMetaConfigAsync();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri(
            "/webhooks/meta/page?hub.mode=subscribe&hub.verify_token=sai-token&hub.challenge=x",
            UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Verify_WrongMode_ReturnsForbidden()
    {
        await SeedMetaConfigAsync();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri(
            $"/webhooks/meta/page?hub.mode=unsubscribe&hub.verify_token={VerifyToken}&hub.challenge=x",
            UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------
    // POST receive — chữ ký + parse
    // ------------------------------------------------------------------

    [Fact]
    public async Task Receive_InvalidSignature_ReturnsUnauthorized()
    {
        await SeedMetaConfigAsync();

        var response = await PostWebhookAsync(CommentPayload(PageId), signature: "sha256=00ff");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Receive_ValidSignature_NoEntries_ReturnsReceivedZero()
    {
        await SeedMetaConfigAsync();

        var response = await PostWebhookAsync("{\"object\":\"page\",\"entry\":[]}", signature: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("received").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Receive_ValidSignature_MalformedJson_ReturnsPayloadInvalid()
    {
        await SeedMetaConfigAsync();

        var response = await PostWebhookAsync("{{{json-hong", signature: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("meta.webhook_payload_invalid");
    }

    [Fact]
    public async Task Receive_ValidSignature_UnknownPage_ReturnsReceivedZero()
    {
        await SeedMetaConfigAsync();

        // Comment hop le nhung page chua duoc dong bo thanh asset -> tu choi am tham, khong xuyen tenant.
        var response = await PostWebhookAsync(CommentPayload("page-khong-ton-tai"), signature: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("received").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Receive_ValidSignature_KnownPage_ProvisionsInboxAndPublishes()
    {
        var tenantId = await SeedMetaConfigAsync();
        await SeedPageAssetAsync(tenantId);

        var response = await PostWebhookAsync(CommentPayload(PageId), signature: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("received").GetInt32().Should().Be(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inbox = await db.Inboxes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.ExternalPageId == PageId);
        inbox.Should().NotBeNull("comment đầu tiên phải mở inbox cho page");
    }
}
