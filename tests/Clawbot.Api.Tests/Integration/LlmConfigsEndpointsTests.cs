using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Llm;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/llm-configs — CRUD cấu hình LLM per-tenant (thuần EF, không đụng ILlmChatClientFactory).
/// Bỏ qua POST /{id}/test: nhánh đó cần gọi provider thật qua ILlmChatClientFactory, không mock được
/// gọn trong integration test này (tiền lệ tương tự ở OrchestrationV2EndpointTests cho gRPC thật).
/// Base URL nội bộ (10.0.0.1, localhost) luôn bị LlmBaseUrlGuard chặn trong host test vì environment
/// là "Staging" (không phải "Development"), nên AllowPrivateBaseUrls luôn false — verdict xác định.
/// </summary>
public sealed class LlmConfigsEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public LlmConfigsEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private async Task<Guid> GetAdminTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private async Task<Guid> SeedLlmConfigAsync(
        Guid tenantId,
        string provider,
        string modelId,
        string apiKeyEncrypted = "enc-key",
        bool isActive = true,
        DateTimeOffset? updatedAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = LlmConfig.Create(tenantId, provider, modelId, apiKeyEncrypted, updatedAt ?? DateTimeOffset.UtcNow);
        db.LlmConfigs.Add(row);
        if (!isActive)
            db.Entry(row).Property(nameof(LlmConfig.IsActive)).CurrentValue = false;
        await db.SaveChangesAsync();
        return row.Id;
    }

    // Gắn 1 AgentConfig vào llmConfigId để mô phỏng ràng buộc model_provider_mismatch / llm_config_in_use.
    private async Task SeedBoundAgentAsync(Guid tenantId, Guid llmConfigId, string model)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var code = $"bound-agent-{Guid.NewGuid():N}"[..24];
        var agent = AgentConfig.Create(tenantId, code, "Agent Bound Test", "worker", model, DateTimeOffset.UtcNow);
        agent.BindLlmConfig(llmConfigId, DateTimeOffset.UtcNow);
        db.AgentConfigs.Add(agent);
        await db.SaveChangesAsync();
    }

    private async Task<string?> GetApiKeyEncryptedAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.LlmConfigs.IgnoreQueryFilters().FirstAsync(c => c.Id == id);
        return row.ApiKeyEncrypted;
    }

    // ------------------------------------------------------------------
    // POST /api/llm-configs
    // ------------------------------------------------------------------

    [Fact]
    public async Task Create_MissingApiKey_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/llm-configs/", UriKind.Relative), new
        {
            provider = "openai",
            modelId = "gpt-4o-mini",
            apiKey = "",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("api_key_required");
    }

    [Fact]
    public async Task Create_InvalidProvider_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/llm-configs/", UriKind.Relative), new
        {
            provider = "openai-x",
            modelId = "gpt-4o-mini",
            apiKey = "test-secret-123",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_provider");
    }

    [Fact]
    public async Task Create_BlankModelId_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/llm-configs/", UriKind.Relative), new
        {
            provider = "openai",
            modelId = "   ",
            apiKey = "test-secret-123",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_model_id");
    }

    [Theory]
    [InlineData("http://localhost/v1")]
    [InlineData("https://10.0.0.1/v1")]
    public async Task Create_PrivateBaseUrl_ReturnsBadRequest_BaseUrlPrivateHost(string baseUrl)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/llm-configs/", UriKind.Relative), new
        {
            provider = "openai-compatible",
            modelId = "gpt-4o-mini",
            apiKey = "test-secret-123",
            baseUrl,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        // host nội bộ đã biết (literal IP hoặc localhost) -> phân loại KnownPrivate ngay, không cần DNS
        // thật -> verdict luôn PrivateHostNotGranted vì host test chạy env "Staging".
        (await response.Content.ReadAsStringAsync()).Should().Contain("base_url_private_host");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public async Task Create_TimeoutOutOfRange_ReturnsBadRequest(int timeoutSeconds)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/llm-configs/", UriKind.Relative), new
        {
            provider = "openai",
            modelId = "gpt-4o-mini",
            apiKey = "test-secret-123",
            timeoutSeconds,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_timeout");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200_001)]
    public async Task Create_MaxOutputTokensOutOfRange_ReturnsBadRequest(int maxOutputTokens)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/llm-configs/", UriKind.Relative), new
        {
            provider = "openai",
            modelId = "gpt-4o-mini",
            apiKey = "test-secret-123",
            maxOutputTokens,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("invalid_max_output_tokens");
    }

    [Fact]
    public async Task Create_ValidRequest_ReturnsCreated_HasApiKeyTrue_KeyNotExposed()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        const string rawApiKey = "sk-test-secret-vnpt-123";

        var response = await client.PostAsJsonAsync(new Uri("/api/llm-configs/", UriKind.Relative), new
        {
            provider = "openai",
            modelId = "gpt-4o-mini",
            apiKey = rawApiKey,
            displayName = "Config Test",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain(rawApiKey, "endpoint không bao giờ trả lộ api key thô, chỉ trả HasApiKey");

        var body = JsonSerializer.Deserialize<JsonElement>(raw);
        body.GetProperty("provider").GetString().Should().Be("openai");
        body.GetProperty("modelId").GetString().Should().Be("gpt-4o-mini");
        body.GetProperty("hasApiKey").GetBoolean().Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // GET /api/llm-configs
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsConfigs_OrderedByUpdatedAtDescending()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var olderId = await SeedLlmConfigAsync(tenantId, "anthropic", "claude-3-haiku", updatedAt: DateTimeOffset.UtcNow.AddMinutes(-30));
        var newerId = await SeedLlmConfigAsync(tenantId, "openai", "gpt-4o-mini", updatedAt: DateTimeOffset.UtcNow);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/llm-configs/", UriKind.Relative));

        var ids = body.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();
        ids.Should().Contain(olderId).And.Contain(newerId);
        ids.IndexOf(newerId).Should().BeLessThan(ids.IndexOf(olderId), "OrderByDescending(UpdatedAt) đưa bản ghi mới hơn lên trước");
    }

    // ------------------------------------------------------------------
    // PUT /api/llm-configs/{id}
    // ------------------------------------------------------------------

    [Fact]
    public async Task Update_ValidProviderAndModel_ReturnsOk()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedLlmConfigAsync(tenantId, "anthropic", "claude-3-haiku");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(new Uri($"/api/llm-configs/{id}", UriKind.Relative), new
        {
            provider = "openai",
            modelId = "gpt-4o-mini",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("provider").GetString().Should().Be("openai");
        body.GetProperty("modelId").GetString().Should().Be("gpt-4o-mini");
    }

    [Fact]
    public async Task Update_ModelProviderMismatchWithBoundAgent_ReturnsBadRequest()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedLlmConfigAsync(tenantId, "anthropic", "claude-3-opus");
        // Agent đang bind vào config này với model kiểu Anthropic ("claude-...") — hợp lệ ở thời điểm bind.
        await SeedBoundAgentAsync(tenantId, id, "claude-3-sonnet");
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Đổi config sang provider openai: IsModelCompatibleWithProvider("openai","claude-3-sonnet") = false
        // (openai/openai-compatible/openai-responses từ chối mọi model bắt đầu bằng "claude").
        var response = await client.PutAsJsonAsync(new Uri($"/api/llm-configs/{id}", UriKind.Relative), new
        {
            provider = "openai",
            modelId = "gpt-4o-mini",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("model_provider_mismatch");
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(new Uri($"/api/llm-configs/{Guid.NewGuid()}", UriKind.Relative), new
        {
            provider = "openai",
            modelId = "gpt-4o-mini",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // POST /api/llm-configs/{id}/rotate-key
    // ------------------------------------------------------------------

    [Fact]
    public async Task RotateKey_MissingApiKey_ReturnsBadRequest()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedLlmConfigAsync(tenantId, "openai", "gpt-4o-mini");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri($"/api/llm-configs/{id}/rotate-key", UriKind.Relative), new
        {
            apiKey = "",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("api_key_required");
    }

    [Fact]
    public async Task RotateKey_Valid_ReturnsNoContent_AndPersistsNewEncryptedKey()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedLlmConfigAsync(tenantId, "openai", "gpt-4o-mini", apiKeyEncrypted: "original-enc-marker");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri($"/api/llm-configs/{id}/rotate-key", UriKind.Relative), new
        {
            apiKey = "test-rotated-secret-456",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var encrypted = await GetApiKeyEncryptedAsync(id);
        encrypted.Should().NotBeNullOrEmpty();
        encrypted.Should().NotBe("original-enc-marker", "rotate phải ghi đè bằng ciphertext mới");
    }

    // ------------------------------------------------------------------
    // POST /api/llm-configs/{id}/activate, /deactivate
    // ------------------------------------------------------------------

    [Fact]
    public async Task Activate_WithEmptyApiKey_ReturnsBadRequest_RequiresKeyRotation()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedLlmConfigAsync(tenantId, "openai", "gpt-4o-mini", apiKeyEncrypted: "");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(new Uri($"/api/llm-configs/{id}/activate", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("llm_config_requires_key_rotation");
    }

    [Fact]
    public async Task Activate_Valid_ReturnsOk_IsActiveTrue()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedLlmConfigAsync(tenantId, "openai", "gpt-4o-mini", isActive: false);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(new Uri($"/api/llm-configs/{id}/activate", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Deactivate_Valid_ReturnsOk_IsActiveFalse()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedLlmConfigAsync(tenantId, "openai", "gpt-4o-mini");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(new Uri($"/api/llm-configs/{id}/deactivate", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    // ------------------------------------------------------------------
    // DELETE /api/llm-configs/{id}
    // ------------------------------------------------------------------

    [Fact]
    public async Task Delete_ConfigBoundToAgent_ReturnsBadRequest_InUse()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedLlmConfigAsync(tenantId, "openai", "gpt-4o-mini");
        await SeedBoundAgentAsync(tenantId, id, "gpt-4o-mini");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(new Uri($"/api/llm-configs/{id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("llm_config_in_use");
    }

    [Fact]
    public async Task Delete_UnboundConfig_ReturnsNoContent()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var id = await SeedLlmConfigAsync(tenantId, "openai", "gpt-4o-mini");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(new Uri($"/api/llm-configs/{id}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsNoContent()
    {
        // FindAsync trả null cho id lạ -> code trả NoContent luôn (không phải NotFound), đọc kỹ
        // DeleteAsync: `if (row is null) return Results.NoContent();`.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.DeleteAsync(new Uri($"/api/llm-configs/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
