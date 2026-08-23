using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clawbot.Api.Endpoints;
using Clawbot.Domain.Integrations;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// Unit test thuần cho các hàm internal (InternalsVisibleTo) không đi qua HTTP: chữ ký HMAC,
/// parser JSON webhook Business Integration của Meta, khớp app id.
/// </summary>
public sealed class MetaBusinessIntegrationWebhookHelpersTests
{
    private const string AppSecret = "business-secret-test";

    private static byte[] ComputeSignedPayload(string json, out string signature)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        signature = "sha256=" + Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
        return payload;
    }

    [Fact]
    public void IsValidSignature_CorrectSignature_ReturnsTrue()
    {
        var payload = ComputeSignedPayload("{\"a\":1}", out var signature);

        MetaBusinessIntegrationWebhookEndpoints.IsValidSignature(payload, signature, AppSecret)
            .Should().BeTrue();
    }

    [Fact]
    public void IsValidSignature_WrongSecret_ReturnsFalse()
    {
        var payload = ComputeSignedPayload("{\"a\":1}", out var signature);

        MetaBusinessIntegrationWebhookEndpoints.IsValidSignature(payload, signature, "secret-khac")
            .Should().BeFalse();
    }

    [Fact]
    public void IsValidSignature_MissingPrefix_ReturnsFalse()
    {
        var payload = Encoding.UTF8.GetBytes("{\"a\":1}");

        MetaBusinessIntegrationWebhookEndpoints.IsValidSignature(payload, "khong-co-prefix", AppSecret)
            .Should().BeFalse();
    }

    [Fact]
    public void IsValidSignature_EmptyPayload_ReturnsFalse()
    {
        MetaBusinessIntegrationWebhookEndpoints.IsValidSignature([], "sha256=00", AppSecret)
            .Should().BeFalse();
    }

    [Fact]
    public void ParseApplicationIds_ValidPayload_ReturnsIds()
    {
        var json = """{"object":"application","entry":[{"id":"app-1"},{"id":"app-2"}]}""";

        var ids = MetaBusinessIntegrationWebhookEndpoints.ParseApplicationIds(Encoding.UTF8.GetBytes(json));

        ids.Should().BeEquivalentTo(["app-1", "app-2"]);
    }

    [Fact]
    public void ParseApplicationIds_WrongObjectType_ReturnsEmpty()
    {
        var json = """{"object":"page","entry":[{"id":"app-1"}]}""";

        var ids = MetaBusinessIntegrationWebhookEndpoints.ParseApplicationIds(Encoding.UTF8.GetBytes(json));

        ids.Should().BeEmpty();
    }

    [Fact]
    public void ParseChanges_InstallField_ExtractsBusinessManagerId()
    {
        var json = """{"object":"application","entry":[{"id":"app-1","changes":[{"field":"business_integration_install","value":{"business_manager_id":"biz-1"}}]}]}""";

        var changes = MetaBusinessIntegrationWebhookEndpoints.ParseChanges(Encoding.UTF8.GetBytes(json), "app-1");

        changes.Should().ContainSingle();
        changes[0].Field.Should().Be("business_integration_install");
        changes[0].BusinessManagerId.Should().Be("biz-1");
    }

    [Fact]
    public void ParseChanges_MismatchedAppId_ReturnsEmpty()
    {
        var json = """{"object":"application","entry":[{"id":"app-1","changes":[{"field":"business_integration_install","value":{"business_manager_id":"biz-1"}}]}]}""";

        var changes = MetaBusinessIntegrationWebhookEndpoints.ParseChanges(Encoding.UTF8.GetBytes(json), "app-khac");

        changes.Should().BeEmpty();
    }

    [Fact]
    public void ParseChanges_UnsupportedField_IsSkipped()
    {
        var json = """{"object":"application","entry":[{"id":"app-1","changes":[{"field":"khong_ho_tro","value":{"business_manager_id":"biz-1"}}]}]}""";

        var changes = MetaBusinessIntegrationWebhookEndpoints.ParseChanges(Encoding.UTF8.GetBytes(json), "app-1");

        changes.Should().BeEmpty();
    }

    [Fact]
    public void ParseChanges_DuplicateFieldAndBusinessId_IsDeduplicated()
    {
        var json = "{\"object\":\"application\",\"entry\":[{\"id\":\"app-1\",\"changes\":["
            + "{\"field\":\"business_integration_install\",\"value\":{\"business_manager_id\":\"biz-1\"}},"
            + "{\"field\":\"business_integration_install\",\"value\":{\"business_manager_id\":\"biz-1\"}}"
            + "]}]}";

        var changes = MetaBusinessIntegrationWebhookEndpoints.ParseChanges(Encoding.UTF8.GetBytes(json), "app-1");

        changes.Should().ContainSingle();
    }

    [Fact]
    public void MatchConfigurations_KnownAppId_ReturnsMatchingCandidates()
    {
        var options = new MetaGraphOptions { AppId = "app-1" };
        var candidates = new[] { new MetaGraphConfigurationCandidate(Guid.NewGuid(), options) };

        var matched = MetaBusinessIntegrationWebhookEndpoints.MatchConfigurations(
            candidates, new HashSet<string> { "app-1" });

        matched.Should().ContainSingle();
    }

    [Fact]
    public void MatchConfigurations_UnknownAppId_ReturnsEmpty()
    {
        var options = new MetaGraphOptions { AppId = "app-1" };
        var candidates = new[] { new MetaGraphConfigurationCandidate(Guid.NewGuid(), options) };

        var matched = MetaBusinessIntegrationWebhookEndpoints.MatchConfigurations(
            candidates, new HashSet<string> { "app-khong-ton-tai" });

        matched.Should().BeEmpty();
    }
}

/// <summary>
/// /webhooks/meta/business-integration khi CHƯA có config nào — GetWebhookCandidatesAsync trả
/// rỗng nên cả GET verify và POST receive đều 503. Fixture riêng (DB InMemory riêng).
/// </summary>
public sealed class MetaBusinessIntegrationWebhookUnconfiguredTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public MetaBusinessIntegrationWebhookUnconfiguredTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Verify_WithoutConfig_ReturnsServiceUnavailable()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri(
            "/webhooks/meta/business-integration?hub.mode=subscribe&hub.verify_token=x&hub.challenge=y",
            UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Receive_WithoutConfig_ReturnsServiceUnavailable()
    {
        using var client = _factory.CreateClient();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(new Uri("/webhooks/meta/business-integration", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}

/// <summary>
/// /webhooks/meta/business-integration khi ĐÃ có config (business_system_user, giống điều kiện
/// IsBusinessWebhookConfigured) — seed qua IMetaAppConfigurationService như MetaPageWebhookEndpointsTests.
/// </summary>
public sealed class MetaBusinessIntegrationWebhookEndpointsTests : IClassFixture<ApiTestFactory>
{
    private const string AppSecret = "business-secret-test";
    private const string VerifyToken = "biz-verify-token-1";
    private const string AppId = "app-biz-123";
    private const string BusinessManagerId = "biz-mgr-1";

    private readonly ApiTestFactory _factory;

    public MetaBusinessIntegrationWebhookEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private async Task<Guid> SeedConfigAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        var configurations = scope.ServiceProvider.GetRequiredService<IMetaAppConfigurationService>();
        await configurations.UpdateAsync(tenant.Id, new MetaAppConfigurationUpdate(
            AppId, AppSecret, "cfg-biz-123", MetaAuthorizationModes.BusinessSystemUser,
            VerifyToken, "https://localhost:5001/api/admin/meta/callback", "https://localhost:5001/system"));
        return tenant.Id;
    }

    private async Task SeedConnectionAsync(Guid tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.MetaConnections.Add(MetaConnection.Create(tenantId, BusinessManagerId, "su-1",
            MetaConnectionTokenTypes.BusinessIntegrationSystemUser, "encrypted-token",
            "[\"business_management\"]", null, null, DateTimeOffset.UtcNow));
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
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/meta/business-integration") { Content = content };
        request.Headers.Add("X-Hub-Signature-256", signature ?? ComputeSignature(payload));
        return await client.SendAsync(request);
    }

    private static string InstallPayload(string appId, string businessManagerId) =>
        "{\"object\":\"application\",\"entry\":[{\"id\":\"" + appId + "\",\"changes\":[{\"field\":\"business_integration_install\","
        + "\"value\":{\"business_manager_id\":\"" + businessManagerId + "\"}}]}]}";

    // ------------------------------------------------------------------
    // GET verify
    // ------------------------------------------------------------------

    [Fact]
    public async Task Verify_ValidToken_ReturnsChallenge()
    {
        await SeedConfigAsync();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri(
            $"/webhooks/meta/business-integration?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=ma-thach-thuc",
            UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("ma-thach-thuc");
    }

    [Fact]
    public async Task Verify_WrongToken_ReturnsForbidden()
    {
        await SeedConfigAsync();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri(
            "/webhooks/meta/business-integration?hub.mode=subscribe&hub.verify_token=sai&hub.challenge=x",
            UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------
    // POST receive
    // ------------------------------------------------------------------

    [Fact]
    public async Task Receive_InvalidSignature_ReturnsUnauthorized()
    {
        await SeedConfigAsync();

        var response = await PostWebhookAsync(InstallPayload(AppId, BusinessManagerId), signature: "sha256=00ff");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Receive_UnknownAppId_ReturnsUnauthorized()
    {
        await SeedConfigAsync();

        var response = await PostWebhookAsync(InstallPayload("app-khong-ton-tai", BusinessManagerId), signature: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Receive_MalformedJson_ReturnsPayloadInvalid()
    {
        await SeedConfigAsync();

        var response = await PostWebhookAsync("{{{json-hong", signature: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("meta.webhook_payload_invalid");
    }

    [Fact]
    public async Task Receive_EmptyEntries_ReturnsReceivedZero()
    {
        await SeedConfigAsync();

        var response = await PostWebhookAsync(
            "{\"object\":\"application\",\"entry\":[{\"id\":\"" + AppId + "\",\"changes\":[]}]}", signature: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("received").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Receive_KnownBusinessManagerId_EnqueuesJobPerTenant()
    {
        var tenantId = await SeedConfigAsync();
        await SeedConnectionAsync(tenantId);

        var response = await PostWebhookAsync(InstallPayload(AppId, BusinessManagerId), signature: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("received").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Receive_UnknownBusinessManagerId_ReturnsReceivedZero()
    {
        await SeedConfigAsync();
        // Không seed MetaConnection nào -> business_manager_id không map ra tenant nào.

        var response = await PostWebhookAsync(InstallPayload(AppId, "biz-khong-ton-tai"), signature: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("received").GetInt32().Should().Be(0);
    }
}
