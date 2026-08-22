using System.Net;
using System.Net.Http.Json;
using Clawbot.Api.Endpoints;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Llm;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/agents/*: điều khiển AgentConfig (bảng agents) — thuần EF, không gRPC. SandboxAsync launch
/// job qua Hangfire IJobLauncher nên chỉ test nhánh validate trước khi chạm job (giống quyết định
/// đã áp dụng cho DemoEndpoints/SuggestPlansAsync).
/// </summary>
public sealed class AgentsEndpointTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> DefaultTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private async Task<string> SeedAgentAsync(
        string? displayName = "Trợ lý Test",
        string? model = "claude-3-5-sonnet",
        bool withDefinition = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        var code = $"agent-{Guid.NewGuid():N}";
        var agent = AgentConfig.Create(tenant.Id, code, displayName ?? string.Empty, "worker", model ?? string.Empty, DateTimeOffset.UtcNow);
        db.AgentConfigs.Add(agent);

        if (withDefinition)
        {
            var definition = Clawbot.Domain.Agents.AgentDefinition.Create(
                tenant.Id, code, "Trợ lý Test", "worker", "Bạn là trợ lý test.", DateTimeOffset.UtcNow);
            db.AgentDefinitions.Add(definition);
        }

        await db.SaveChangesAsync();
        return code;
    }

    private async Task<Guid> SeedLlmConfigAsync(string provider, bool active = true, Guid? tenantId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = tenantId ?? await DefaultTenantIdAsync();
        var config = LlmConfig.Create(tenant, provider, "model-x", "enc-key", DateTimeOffset.UtcNow);
        db.LlmConfigs.Add(config);
        if (!active)
            db.Entry(config).Property(nameof(LlmConfig.IsActive)).CurrentValue = false;
        await db.SaveChangesAsync();
        return config.Id;
    }

    // ------------------------------------------------------------------
    // List / tools catalog
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsSeededAgent()
    {
        var code = await SeedAgentAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/agents/", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(code);
    }

    [Fact]
    public async Task ToolsCatalog_ReturnsKnownTools()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/agents/tools", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"items\"");
    }

    // ------------------------------------------------------------------
    // Enable / disable
    // ------------------------------------------------------------------

    [Fact]
    public async Task Enable_UnknownCode_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/agents/khong-ton-tai-{Guid.NewGuid():N}/enable", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DisableThenEnable_TogglesStatus()
    {
        var code = await SeedAgentAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var disabled = await client.PostAsync(new Uri($"/api/agents/{code}/disable", UriKind.Relative), content: null);
        disabled.StatusCode.Should().Be(HttpStatusCode.OK);
        (await disabled.Content.ReadAsStringAsync()).Should().Contain("\"stopped\"");

        var enabled = await client.PostAsync(new Uri($"/api/agents/{code}/enable", UriKind.Relative), content: null);
        enabled.StatusCode.Should().Be(HttpStatusCode.OK);
        (await enabled.Content.ReadAsStringAsync()).Should().Contain("\"running\"");
    }

    // ------------------------------------------------------------------
    // Settings GET
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetSettings_UnknownCode_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/agents/khong-ton-tai-{Guid.NewGuid():N}/settings", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSettings_KnownCode_ReturnsAgentSettingsDto()
    {
        var code = await SeedAgentAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/agents/{code}/settings", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AgentSettingsResponse>();
        dto!.Code.Should().Be(code);
        dto.Model.Should().Be("claude-3-5-sonnet");
    }

    // ------------------------------------------------------------------
    // Settings PUT — validate
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateSettings_UnknownCode_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/agents/khong-ton-tai-{Guid.NewGuid():N}/settings", UriKind.Relative),
            new AgentSettingsRequest("Test", "claude-3-5-sonnet", null, null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateSettings_UnknownTool_IsRejected()
    {
        var code = await SeedAgentAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/agents/{code}/settings", UriKind.Relative),
            new AgentSettingsRequest(null, null, null, null, null, null, null, null,
                AllowedTools: new[] { "cong_cu_khong_ton_tai" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("unknown_tools");
    }

    [Fact]
    public async Task UpdateSettings_BlankDisplayNameAndFallback_IsRejected()
    {
        // Seed agent với displayName/model rỗng để không có gì fallback khi request cũng bỏ trống.
        var code = await SeedAgentAsync(displayName: "", model: "");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/agents/{code}/settings", UriKind.Relative),
            new AgentSettingsRequest(null, null, null, null, null, null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("display_name_required");
    }

    [Fact]
    public async Task UpdateSettings_UnknownOrInactiveLlmConfig_IsRejected()
    {
        var code = await SeedAgentAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/agents/{code}/settings", UriKind.Relative),
            new AgentSettingsRequest("Trợ lý Test", "claude-3-5-sonnet", null, null, null, null, null, null,
                LlmConfigId: Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("invalid_llm_config");
    }

    [Fact]
    public async Task UpdateSettings_ModelIncompatibleWithBoundProvider_IsRejected()
    {
        var code = await SeedAgentAsync();
        var llmConfigId = await SeedLlmConfigAsync("anthropic");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/agents/{code}/settings", UriKind.Relative),
            // Bind vào provider "anthropic" nhưng đặt model không bắt đầu bằng "claude".
            new AgentSettingsRequest("Trợ lý Test", "gpt-4o-mini", null, null, null, null, null, null,
                LlmConfigId: llmConfigId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("model_provider_mismatch");
    }

    [Fact]
    public async Task UpdateSettings_ValidRequestWithoutDefinition_UpdatesButSkipsToolGrants()
    {
        // Agent seed KHÔNG có AgentDefinition đi kèm -> nhánh "definition is not null" no-op,
        // AllowedTools trong response phải vẫn rỗng dù request gửi tool hợp lệ.
        var code = await SeedAgentAsync(withDefinition: false);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/agents/{code}/settings", UriKind.Relative),
            new AgentSettingsRequest("Tên mới", "claude-3-opus", "anthropic", "Prompt mới", 0.7, 4000,
                new[] { "kb_search" }, new[] { "hoc-ba" },
                AllowedTools: new[] { "web.search" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AgentSettingsResponse>();
        dto!.DisplayName.Should().Be("Tên mới");
        dto.Model.Should().Be("claude-3-opus");
        dto.Temperature.Should().Be(0.7);
        dto.MaxTokens.Should().Be(4000);
        dto.SkillFiles.Should().Contain("kb_search");
        dto.KbModules.Should().Contain("hoc-ba");
        dto.AllowedTools.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateSettings_ValidRequestWithDefinition_PersistsToolGrants()
    {
        var code = await SeedAgentAsync(withDefinition: true);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/agents/{code}/settings", UriKind.Relative),
            new AgentSettingsRequest(null, null, null, null, null, null, null, null,
                AllowedTools: new[] { "web.search", "web.search", "  " }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AgentSettingsResponse>();
        // Dedup + trim khoảng trắng rỗng.
        dto!.AllowedTools.Should().BeEquivalentTo(new[] { "web.search" });
    }

    [Fact]
    public async Task UpdateSettings_TemperatureAndMaxTokens_AreClamped()
    {
        var code = await SeedAgentAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/agents/{code}/settings", UriKind.Relative),
            new AgentSettingsRequest(null, null, null, null, Temperature: 9.9, MaxTokens: 999999,
                null, null, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<AgentSettingsResponse>();
        dto!.Temperature.Should().Be(2);
        dto.MaxTokens.Should().Be(32000);
    }

    // ------------------------------------------------------------------
    // Sandbox — chỉ nhánh validate trước khi launch job
    // ------------------------------------------------------------------

    [Fact]
    public async Task Sandbox_BlankMessage_IsRejected()
    {
        var code = await SeedAgentAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/agents/{code}/sandbox", UriKind.Relative),
            new AgentSandboxRequest("   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sandbox_UnknownCode_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/agents/khong-ton-tai-{Guid.NewGuid():N}/sandbox", UriKind.Relative),
            new AgentSandboxRequest("Xin chào"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Traces
    // ------------------------------------------------------------------

    [Fact]
    public async Task Traces_UnknownCode_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/agents/khong-ton-tai-{Guid.NewGuid():N}/traces", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Traces_KnownCode_ReturnsPagedEnvelope()
    {
        var code = await SeedAgentAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/agents/{code}/traces?page=1&pageSize=10", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"total\"");
        body.Should().Contain("\"items\"");
    }
}
