using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Clawbot.Api.Endpoints;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/demo/*: module demo/simulation (ghi rõ trong DemoEndpoints "ingest thật đi đường
/// PancakePollingService, không qua đây"). Chỉ test các nhánh sync/deterministic — config, HMAC
/// verify-URL, đọc trace, validation. Webhook POST xử lý nền qua Task.Run (fire-and-forget) và
/// SSE streaming (/events) không nằm trong phạm vi — giá trị nghiệp vụ thấp, khó test ổn định.
///
/// DemoModeMiddleware coi /api/demo/{config,traces,events} là "sensitive" và đòi
/// Authorization: Bearer {Demo:AdminKey} riêng — độc lập với bearer JWT thường. AdminKey chỉ
/// được set qua WithWebHostBuilder CỤC BỘ trong file này — không đụng ApiTestFactory dùng chung,
/// vì PostValidationSweepTests/AuthenticatedReadEndpointTests quét toàn bộ route bằng JWT thường
/// và trông đợi /api/demo/* trả 503 (AdminKey rỗng theo appsettings.json mặc định), không phải 401.
/// </summary>
public sealed class DemoEndpointTests : IAsyncLifetime
{
    private const string DemoAdminKey = "test-demo-admin-key";

    private readonly ApiTestFactory _factory = new();
    private WebApplicationFactory<Program>? _demoFactory;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _demoFactory?.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private WebApplicationFactory<Program> DemoFactory() => _demoFactory ??= _factory.WithWebHostBuilder(
        builder => builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Demo:AdminKey"] = DemoAdminKey })));

    private HttpClient DemoAdminClient()
    {
        var client = DemoFactory().CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", DemoAdminKey);
        return client;
    }

    [Fact]
    public async Task Status_ReturnsDemoModeTrue()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/demo/status", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task VerifyWebhook_WithChallenge_EchoesChallengeAsText()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/demo/webhook/pancake?hub.mode=subscribe&hub.challenge=abc123&hub.verify_token=x", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("abc123");
    }

    [Fact]
    public async Task VerifyWebhook_WithoutChallenge_ReturnsVerifiedStatus()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/demo/webhook/pancake", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("verified");
    }

    [Fact]
    public async Task HandleWebhook_AdminKeyNotConfigured_ReturnsNotFound()
    {
        // _factory mặc định (appsettings.json Demo:AdminKey="") -> endpoint coi như không tồn
        // tại, kể cả khi caller gửi kèm header X-Admin-Key nào đó.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", "whatever");

        var response = await client.PostAsync(
            new Uri("/api/demo/webhook/pancake", UriKind.Relative),
            JsonContent.Create(new { thread_id = "t1" }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HandleWebhook_WrongAdminKey_ReturnsNotFound()
    {
        // DemoFactory() có Demo:AdminKey cấu hình; header X-Admin-Key sai so với key đó ->
        // endpoint vẫn coi như không tồn tại (không lộ khác biệt "sai key" với "không có key").
        var client = DemoFactory().CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Key", "wrong-key");

        var response = await client.PostAsync(
            new Uri("/api/demo/webhook/pancake", UriKind.Relative),
            JsonContent.Create(new { thread_id = "t1" }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sensitive_WithoutBearerAdminKey_IsUnauthorized()
    {
        // DemoModeMiddleware chặn /config /traces /events nếu thiếu đúng Authorization: Bearer
        // {Demo:AdminKey} — độc lập với JWT thường của app. Dùng DemoFactory() (AdminKey đã cấu
        // hình) nhưng không gắn header, để đi đúng nhánh 401 chứ không phải 503 "chưa cấu hình".
        var client = DemoFactory().CreateClient();

        var response = await client.GetAsync(new Uri("/api/demo/config/status", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetConfigStatus_ReturnsCurrentRuntimeConfig()
    {
        var client = DemoAdminClient();

        var response = await client.GetAsync(new Uri("/api/demo/config/status", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("tokenConfigured");
        body.Should().Contain("pageTokenConfigured");
    }

    [Fact]
    public async Task SetToken_ValidRequest_UpdatesConfigStatus()
    {
        var client = DemoAdminClient();

        var setResponse = await client.PostAsJsonAsync(
            new Uri("/api/demo/config/token", UriKind.Relative),
            new SetTokenRequest("tok-123", null, "page-1", null, "page-tok-1"));
        setResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var statusResponse = await client.GetAsync(new Uri("/api/demo/config/status", UriKind.Relative));
        var body = await statusResponse.Content.ReadAsStringAsync();
        body.Should().Contain("\"tokenConfigured\":true");
        body.Should().Contain("\"pageTokenConfigured\":true");
        body.Should().Contain("\"pageId\":\"page-1\"");
    }

    [Fact]
    public async Task SetToken_DisallowedBaseUrl_IsRejected()
    {
        var client = DemoAdminClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/demo/config/token", UriKind.Relative),
            new SetTokenRequest("tok-123", null, null, "https://evil.example.com/v2/", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("pancake_base_url_not_allowed");
    }

    [Fact]
    public async Task SetWebhookSecret_ValidRequest_ReturnsOk()
    {
        var client = DemoAdminClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/demo/config/webhook-secret", UriKind.Relative),
            new SetTokenRequest(null, "super-secret", null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("secret_updated");
    }

    [Fact]
    public async Task SetAutoReply_ValidRequest_UpdatesText()
    {
        var client = DemoAdminClient();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/demo/config/auto-reply", UriKind.Relative),
            new SetAutoReplyRequest("Xin chào, cảm ơn bạn đã liên hệ."));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("auto_reply_updated");

        var statusResponse = await client.GetAsync(new Uri("/api/demo/config/status", UriKind.Relative));
        var statusBody = await statusResponse.Content.ReadAsStringAsync();
        statusBody.Should().Contain("Xin chào");
    }

    [Fact]
    public async Task GetTrace_UnknownTraceId_ReturnsNotFound()
    {
        var client = DemoAdminClient();

        var response = await client.GetAsync(
            new Uri($"/api/demo/traces/trc_{Guid.NewGuid():N}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("trace_expired");
    }

    [Fact]
    public async Task ExportTrace_UnknownTraceId_ReturnsNotFound()
    {
        var client = DemoAdminClient();

        var response = await client.GetAsync(
            new Uri($"/api/demo/traces/trc_{Guid.NewGuid():N}/export", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListTraces_ReturnsJsonArray()
    {
        // Redis là container dùng chung giữa các test/tiến trình dev khác, không đảm bảo rỗng —
        // chỉ khẳng định endpoint trả về mảng JSON hợp lệ (không lỗi 500).
        var client = DemoAdminClient();

        var response = await client.GetAsync(new Uri("/api/demo/traces", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.TrimStart().Should().StartWith("[");
    }
}
