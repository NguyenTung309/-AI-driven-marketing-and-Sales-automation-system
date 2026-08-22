using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/admin/meta — nhánh CHƯA cấu hình: mỗi class ApiTestFactory có DB InMemory riêng
/// nên class này luôn thấy tenant mặc định chưa lưu Meta App config, độc lập thứ tự test.
/// Callback dùng ExecuteUpdateAsync (InMemory không hỗ trợ) nên chỉ test nhánh state sai.
/// </summary>
public sealed class AdminMetaIntegrationFreshTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public AdminMetaIntegrationFreshTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Status_FreshTenant_ReturnsNotConfiguredNotConnected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/admin/meta", UriKind.Relative));

        body.GetProperty("configured").GetBoolean().Should().BeFalse();
        body.GetProperty("connected").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Connect_WithoutConfig_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(new Uri("/api/admin/meta/connect", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("meta.app_not_configured");
    }

    [Fact]
    public async Task Sync_WithoutConfig_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(new Uri("/api/admin/meta/sync", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("meta.app_not_configured");
    }

    [Fact]
    public async Task Validate_WithoutConfig_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(new Uri("/api/admin/meta/validate", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("meta.app_not_configured");
    }

    [Fact]
    public async Task UpdateConfig_FirstTimeWithoutSecret_ReturnsConfigIncomplete()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Lần đầu lưu mà thiếu App Secret -> store ném InvalidOperationException -> 400.
        var response = await client.PutAsJsonAsync(new Uri("/api/admin/meta/config", UriKind.Relative), new
        {
            appId = "123456789",
            appSecret = (string?)null,
            configurationId = "cfg-123",
            authorizationMode = (string?)null,
            webhookVerifyToken = (string?)null,
            redirectUri = "https://localhost:5001/api/admin/meta/callback",
            frontendReturnUrl = "https://localhost:5001/system",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("meta.config_incomplete");
    }

    [Fact]
    public async Task Disconnect_WithoutConnection_IsNoOp()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(new Uri("/api/admin/meta", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SetDefaultPage_WithoutConnection_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsync(
            new Uri($"/api/admin/meta/assets/{Guid.NewGuid()}/default", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("meta.asset_cannot_publish");
    }

    [Fact]
    public async Task Callback_MissingOrUnknownState_IsRejected()
    {
        // Callback AllowAnonymous — dùng client không xác thực.
        using var client = _factory.CreateClient();

        var missing = await client.GetAsync(new Uri("/api/admin/meta/callback", UriKind.Relative));
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missing.Content.ReadAsStringAsync()).Should().Contain("meta.oauth_state_missing");

        var unknown = await client.GetAsync(new Uri("/api/admin/meta/callback?state=khong-ton-tai&code=x", UriKind.Relative));
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("meta.oauth_state_invalid");
    }
}

/// <summary>
/// /api/admin/meta — validation PUT config (các nhánh bị chặn trước khi persist, không phụ
/// thuộc thứ tự) và luồng ĐÃ cấu hình: connect/sync/validate khi chưa có connection thật.
/// </summary>
public sealed class AdminMetaIntegrationEndpointsTests : IClassFixture<ApiTestFactory>
{
    private const string RedirectUri = "https://localhost:5001/api/admin/meta/callback";
    private const string FrontendReturnUrl = "https://localhost:5001/system";

    private readonly ApiTestFactory _factory;

    public AdminMetaIntegrationEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private static object ValidConfigBody(string? appSecret = "secret-test-123", string appId = "123456789") => new
    {
        appId,
        appSecret,
        configurationId = "cfg-123",
        authorizationMode = (string?)null,
        webhookVerifyToken = "verify-token",
        redirectUri = RedirectUri,
        frontendReturnUrl = FrontendReturnUrl,
    };

    /// <summary>Lưu config hợp lệ (kèm secret) để các bước "đã cấu hình" chạy được.</summary>
    private static async Task EnsureConfiguredAsync(HttpClient client)
    {
        var response = await client.PutAsJsonAsync(new Uri("/api/admin/meta/config", UriKind.Relative), ValidConfigBody());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------
    // Validation PUT config — bị chặn trước persist, độc lập thứ tự
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateConfig_UnsupportedAuthorizationMode_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(new Uri("/api/admin/meta/config", UriKind.Relative), new
        {
            appId = "123456789",
            appSecret = "secret",
            configurationId = "cfg-123",
            authorizationMode = "che-do-la",
            webhookVerifyToken = (string?)null,
            redirectUri = RedirectUri,
            frontendReturnUrl = FrontendReturnUrl,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("meta.authorization_mode_invalid");
    }

    [Fact]
    public async Task UpdateConfig_MissingRequiredFields_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(new Uri("/api/admin/meta/config", UriKind.Relative), new
        {
            appId = "",
            appSecret = "secret",
            configurationId = "",
            authorizationMode = (string?)null,
            webhookVerifyToken = (string?)null,
            redirectUri = RedirectUri,
            frontendReturnUrl = FrontendReturnUrl,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("meta.config_required");
    }

    [Fact]
    public async Task UpdateConfig_OverlongAppId_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(new Uri("/api/admin/meta/config", UriKind.Relative),
            ValidConfigBody(appId: new string('9', 300)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("meta.config_too_long");
    }

    [Fact]
    public async Task UpdateConfig_RedirectUriNotCallbackPath_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(new Uri("/api/admin/meta/config", UriKind.Relative), new
        {
            appId = "123456789",
            appSecret = "secret",
            configurationId = "cfg-123",
            authorizationMode = (string?)null,
            webhookVerifyToken = (string?)null,
            redirectUri = "https://localhost:5001/callback-sai-duong-dan",
            frontendReturnUrl = FrontendReturnUrl,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("meta.config_url_invalid");
    }

    // ------------------------------------------------------------------
    // Luồng đã cấu hình — không có connection thật
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateConfig_Valid_PersistsAndStatusReflectsIt()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        await EnsureConfiguredAsync(client);

        var status = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/admin/meta", UriKind.Relative));

        status.GetProperty("configured").GetBoolean().Should().BeTrue();
        status.GetProperty("appConfiguration").GetProperty("appId").GetString().Should().Be("123456789");
        status.GetProperty("appConfiguration").GetProperty("hasAppSecret").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Connect_WhenConfigured_BuildsAuthorizationUrl()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        await EnsureConfiguredAsync(client);

        var response = await client.PostAsync(new Uri("/api/admin/meta/connect", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var url = body.GetProperty("authorizationUrl").GetString()!;
        url.Should().Contain("dialog/oauth").And.Contain("client_id=123456789").And.Contain("state=");
    }

    [Fact]
    public async Task Sync_ConfiguredButNoConnection_ReturnsConnectionMissing()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        await EnsureConfiguredAsync(client);

        var response = await client.PostAsync(new Uri("/api/admin/meta/sync", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("meta.connection_missing");
    }

    [Fact]
    public async Task Validate_ConfiguredButNoConnection_ReturnsConnectionMissing()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        await EnsureConfiguredAsync(client);

        var response = await client.PostAsync(new Uri("/api/admin/meta/validate", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("meta.connection_missing");
    }
}
