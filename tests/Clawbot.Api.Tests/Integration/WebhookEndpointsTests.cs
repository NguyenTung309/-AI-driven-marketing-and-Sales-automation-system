using System.Net;
using System.Security.Cryptography;
using System.Text;
using Clawbot.Domain.Channels;
using Clawbot.Domain.Security;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// POST /webhooks/pancake/{tenantSlug} — endpoint AllowAnonymous, xác thực bằng HMAC.
/// Happy path cần row pancake_configs có webhook secret (mã hoá bằng IEncryptor của host);
/// chữ ký tính đúng thuật toán của HmacSignatureVerifier (HMACSHA256 hex thường, không prefix).
/// Nhánh comment enqueue CommentAutoReplyJob qua Hangfire storage thật (passive mode chỉ bỏ
/// server xử lý, client enqueue vẫn đăng ký) nên test chỉ assert 202 + row message, không chờ job chạy.
/// </summary>
public sealed class WebhookEndpointsTests : IClassFixture<ApiTestFactory>
{
    private const string SignatureHeader = "x-pancake-signature";

    private readonly ApiTestFactory _factory;

    public WebhookEndpointsTests(ApiTestFactory factory) => _factory = factory;

    /// <summary>Tạo tenant mới (slug unique) để không đụng cấu hình tenant mặc định của fixture.</summary>
    private async Task<Guid> CreateTenantAsync(string slugSuffix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = Tenant.Create($"wh-test-{slugSuffix}", "Webhook Test", "free", DateTimeOffset.UtcNow);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    /// <summary>Seed pancake_configs với webhook secret đã mã hoá — đúng nguồn PancakeConfigResolver đọc.</summary>
    private async Task SeedPancakeConfigAsync(Guid tenantId, string secret)
    {
        using var scope = _factory.Services.CreateScope();
        var encryptor = scope.ServiceProvider.GetRequiredService<IEncryptor>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = PancakeConfig.Create(tenantId, DateTimeOffset.UtcNow);
        config.UpdateWebhookSecret(encryptor.Encrypt(secret), DateTimeOffset.UtcNow);
        db.PancakeConfigs.Add(config);
        await db.SaveChangesAsync();
    }

    /// <summary>Tính chữ ký HMACSHA256 hex thường — khớp VerifyHexSha256 (so chuỗi ASCII lowercase).</summary>
    private static string ComputeHexSignature(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string slug, string body, string? signature)
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/webhooks/pancake/{slug}") { Content = content };
        if (signature is not null) request.Headers.Add(SignatureHeader, signature);
        return await client.SendAsync(request);
    }

    // ------------------------------------------------------------------
    // Tenant lookup + HMAC reject
    // ------------------------------------------------------------------

    [Fact]
    public async Task UnknownTenantSlug_ReturnsNotFound()
    {
        var response = await PostWebhookAsync($"khong-ton-tai-{Guid.NewGuid():N}", "{}", signature: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("tenant not found");
    }

    [Fact]
    public async Task MissingSignature_WithoutConfig_ReturnsUnauthorized_AndWritesAuditLog()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await CreateTenantAsync(suffix);

        // Tenant chưa có pancake_configs -> resolver trả null -> verify false -> 401.
        var response = await PostWebhookAsync($"wh-test-{suffix}", "{\"events\":[]}", signature: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audited = await db.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(a => a.TenantId == tenantId && a.Action == "webhook.hmac.reject");
        audited.Should().BeTrue("mọi lần HMAC bị từ chối phải ghi audit log để truy vết");
    }

    [Fact]
    public async Task TamperedSignature_WithConfig_ReturnsUnauthorized()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await CreateTenantAsync(suffix);
        await SeedPancakeConfigAsync(tenantId, "bi-mat-test");

        var response = await PostWebhookAsync($"wh-test-{suffix}", "{\"events\":[]}", signature: "sha256=chu-ky-sai");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------
    // Happy path — legacy events[] format
    // ------------------------------------------------------------------

    [Fact]
    public async Task ValidSignature_LegacyEventsFormat_IngestsMessage_ReturnsAccepted()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await CreateTenantAsync(suffix);
        const string secret = "webhook-secret-legacy";
        await SeedPancakeConfigAsync(tenantId, secret);

        var body = $$"""
            {"events":[{"platform":"facebook","page_id":"123","thread_id":"t-{{suffix}}","message_id":"m-{{suffix}}","sender_id":"u-{{suffix}}","sender_name":"Khach Test","text":"Xin chao shop","type":"DM","sent_at":"2026-08-19T10:00:00Z"}]}
            """;

        var response = await PostWebhookAsync($"wh-test-{suffix}", body, ComputeHexSignature(body, secret));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conversation = await db.Conversations.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.ExternalThreadId == $"123:t-{suffix}");
        conversation.Should().NotBeNull("tin nhắn hợp lệ phải mở hội thoại mới");

        var message = await db.Messages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.ConversationId == conversation!.Id);
        message.Should().NotBeNull();
        message!.Content.Should().Be("Xin chao shop");
        message.Direction.Should().Be("in");
        message.MessageType.Should().Be("dm");
        message.ExternalMessageId.Should().Be($"m-{suffix}");
    }

    [Fact]
    public async Task ValidSignature_DuplicateMessage_IsIgnored_ButStillAccepted()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await CreateTenantAsync(suffix);
        const string secret = "webhook-secret-dedup";
        await SeedPancakeConfigAsync(tenantId, secret);

        var body = $$"""
            {"events":[{"platform":"facebook","page_id":"123","thread_id":"t-{{suffix}}","message_id":"m-dup-{{suffix}}","sender_id":"u-{{suffix}}","sender_name":"Khach Test","text":"Tin trung lap","type":"DM","sent_at":"2026-08-19T10:00:00Z"}]}
            """;
        var signature = ComputeHexSignature(body, secret);

        (await PostWebhookAsync($"wh-test-{suffix}", body, signature)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await PostWebhookAsync($"wh-test-{suffix}", body, signature)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.Messages.IgnoreQueryFilters()
            .CountAsync(m => m.TenantId == tenantId && m.ExternalMessageId == $"m-dup-{suffix}");
        count.Should().Be(1, "external_message_id trùng phải bị dedup, không ghi đôi");
    }

    // ------------------------------------------------------------------
    // Happy path — định dạng messaging thật của Pancake
    // ------------------------------------------------------------------

    [Fact]
    public async Task ValidSignature_MessagingFormat_IngestsMessage()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await CreateTenantAsync(suffix);
        const string secret = "webhook-secret-messaging";
        await SeedPancakeConfigAsync(tenantId, secret);

        // Body kết thúc bằng nhiều dấu } liên tiếp — raw string $$ không cho phép, dùng nối chuỗi thường.
        var body = "{\"page_id\":\"999\",\"event_type\":\"messaging\",\"data\":{\"conversation\":{\"id\":\"conv-"
            + suffix + "\"},\"message\":{\"id\":\"msg-" + suffix + "\",\"message\":\"Tin tu dinh dang messaging\","
            + "\"type\":\"INBOX\",\"inserted_at\":\"2026-08-19T10:00:00\",\"from\":{\"id\":\"user-" + suffix
            + "\",\"name\":\"Khach Messaging\"}}}}";

        var response = await PostWebhookAsync($"wh-test-{suffix}", body, ComputeHexSignature(body, secret));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await db.Messages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.ExternalMessageId == $"msg-{suffix}");
        message.Should().NotBeNull("format messaging thật của Pancake phải parse được");
        message!.Content.Should().Be("Tin tu dinh dang messaging");
        message.MessageType.Should().Be("text");
    }

    // ------------------------------------------------------------------
    // Comment — nhánh enqueue CommentAutoReplyJob
    // ------------------------------------------------------------------

    [Fact]
    public async Task ValidSignature_CommentEvent_PersistsCommentMessage()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenantId = await CreateTenantAsync(suffix);
        const string secret = "webhook-secret-comment";
        await SeedPancakeConfigAsync(tenantId, secret);

        var body = $$"""
            {"events":[{"platform":"facebook","page_id":"456","thread_id":"c-{{suffix}}","message_id":"cm-{{suffix}}","sender_id":"u-{{suffix}}","sender_name":"Khach Comment","text":"Gia bao nhieu vay","type":"COMMENT","post_id":"post-{{suffix}}","sent_at":"2026-08-19T10:00:00Z"}]}
            """;

        var response = await PostWebhookAsync($"wh-test-{suffix}", body, ComputeHexSignature(body, secret));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await db.Messages.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.ExternalMessageId == $"cm-{suffix}");
        message.Should().NotBeNull();
        message!.MessageType.Should().Be("comment");
        message.ParentPostId.Should().Be($"post-{suffix}");
    }

    /// <summary>Tra tenant id theo slug — seed trước ở bước CreateTenantAsync rồi mới gắn config.</summary>
    private async Task<Guid> GetTenantIdBySlugAsync(string slug)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == slug);
        return tenant.Id;
    }
}
