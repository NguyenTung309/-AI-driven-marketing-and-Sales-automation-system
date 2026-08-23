using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/prompts — cấu hình system prompt/model theo agent + sandbox thử. Sandbox chỉ ghép chuỗi
/// tĩnh (không gọi LLM thật) nên chạy offline an toàn; PII redact dùng RegexPiiRedactor.
/// </summary>
public sealed class PromptsEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public PromptsEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private async Task<Guid> GetAdminTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private async Task<string> SeedAgentAsync(Guid tenantId, string? configJson = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var code = $"prompt-agent-{Guid.NewGuid():N}"[..20];
        var agent = AgentConfig.Create(tenantId, code, "Agent Prompt Test", "research", "gpt-test", DateTimeOffset.UtcNow);
        if (configJson is not null)
            db.Entry(agent).Property("ConfigJson").CurrentValue = configJson;
        db.AgentConfigs.Add(agent);
        await db.SaveChangesAsync();
        return code;
    }

    // ------------------------------------------------------------------
    // GET /configs
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListConfigs_ReturnsAgent_WithStats()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var code = await SeedAgentAsync(tenantId);

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/prompts/configs", UriKind.Relative));

        body.GetProperty("stats").GetProperty("totalConfigs").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        var item = body.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("code").GetString() == code);
        item.GetProperty("status").GetString().Should().Be("stopped");
        item.GetProperty("provider").GetString().Should().Be("claude", "provider mặc định trong AgentRuntimeConfig");
    }

    // ------------------------------------------------------------------
    // GET /configs/{code}
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetConfig_ReturnsDetail_WithRecentUsage()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var code = await SeedAgentAsync(tenantId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LlmCostLedger.Add(LlmCostEntry.Create(tenantId, code, "gpt-test",
                inputTokens: 100, outputTokens: 40, usd: 0.001m, DateTimeOffset.UtcNow.AddHours(-1)));
            await db.SaveChangesAsync();
        }

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/prompts/configs/{code}", UriKind.Relative));

        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("callsLast7Days").GetInt32().Should().Be(1);
        body.GetProperty("totalTokensLast7Days").GetInt32().Should().Be(140);
        body.GetProperty("recentUsage").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetConfig_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/prompts/configs/unknown-{Guid.NewGuid():N}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // PUT /configs/{code}
    // ------------------------------------------------------------------

    [Fact]
    public async Task UpdateConfig_ValidPayload_PersistsChanges()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var code = await SeedAgentAsync(tenantId);

        var response = await client.PutAsJsonAsync(new Uri($"/api/prompts/configs/{code}", UriKind.Relative), new
        {
            displayName = "Agent Da Doi Ten",
            model = "gpt-4o-updated",
            provider = "openai",
            systemPrompt = "Ban la tro ly ban hang.",
            temperature = 0.9,
            maxTokens = 4096,
            skillFiles = new[] { "skill-a.md", "skill-a.md", " ", "skill-b.md" },
            kbModules = (string[]?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("displayName").GetString().Should().Be("Agent Da Doi Ten");
        body.GetProperty("model").GetString().Should().Be("gpt-4o-updated");
        body.GetProperty("provider").GetString().Should().Be("openai");
        body.GetProperty("systemPrompt").GetString().Should().Be("Ban la tro ly ban hang.");
        body.GetProperty("temperature").GetDouble().Should().Be(0.9);
        body.GetProperty("maxTokens").GetInt32().Should().Be(4096);
        body.GetProperty("skillFiles").GetArrayLength().Should().Be(2, "dedupe theo OrdinalIgnoreCase + bỏ khoảng trắng");
    }

    [Fact]
    public async Task UpdateConfig_ClampsTemperatureAndMaxTokens()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var code = await SeedAgentAsync(tenantId);

        var response = await client.PutAsJsonAsync(new Uri($"/api/prompts/configs/{code}", UriKind.Relative), new
        {
            displayName = (string?)null,
            model = (string?)null,
            provider = (string?)null,
            systemPrompt = (string?)null,
            temperature = 99.0,
            maxTokens = 999999,
            skillFiles = (string[]?)null,
            kbModules = (string[]?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("temperature").GetDouble().Should().Be(2.0, "clamp trần temperature");
        body.GetProperty("maxTokens").GetInt32().Should().Be(32000, "clamp trần maxTokens");
    }

    [Fact]
    public async Task UpdateConfig_BlankDisplayNameFallsBackToExisting_NotRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var code = await SeedAgentAsync(tenantId);

        // displayName null -> NormalizeText fallback về DisplayName cũ (không rỗng) -> vẫn 200.
        var response = await client.PutAsJsonAsync(new Uri($"/api/prompts/configs/{code}", UriKind.Relative), new
        {
            displayName = (string?)null,
            model = "model-giu-nguyen",
            provider = (string?)null,
            systemPrompt = (string?)null,
            temperature = (double?)null,
            maxTokens = (int?)null,
            skillFiles = (string[]?)null,
            kbModules = (string[]?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("displayName").GetString().Should().Be("Agent Prompt Test");
        body.GetProperty("model").GetString().Should().Be("model-giu-nguyen");
    }

    [Fact]
    public async Task UpdateConfig_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/prompts/configs/unknown-{Guid.NewGuid():N}", UriKind.Relative), new
            {
                displayName = "x",
                model = "x",
                provider = (string?)null,
                systemPrompt = (string?)null,
                temperature = (double?)null,
                maxTokens = (int?)null,
                skillFiles = (string[]?)null,
                kbModules = (string[]?)null,
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // POST /configs/{code}/sandbox
    // ------------------------------------------------------------------

    [Fact]
    public async Task RunSandbox_ValidMessage_ReturnsReplyAndSessionTrace()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var code = await SeedAgentAsync(tenantId);

        var response = await client.PostAsJsonAsync(new Uri($"/api/prompts/configs/{code}/sandbox", UriKind.Relative), new
        {
            message = "Xin chao, gia khoa hoc bao nhieu?",
            systemPrompt = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reply").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("estimatedTokens").GetInt32().Should().BeGreaterThan(0);
        var sessionId = body.GetProperty("sessionId").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.AgentSessions.IgnoreQueryFilters()
            .Include(s => s.Traces)
            .FirstAsync(s => s.Id == sessionId);
        session.Status.Should().Be("completed");
        session.Traces.Should().HaveCount(3, "system_prompt + input + reply");
    }

    [Fact]
    public async Task RunSandbox_EmptyMessage_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var code = await SeedAgentAsync(tenantId);

        var response = await client.PostAsJsonAsync(new Uri($"/api/prompts/configs/{code}/sandbox", UriKind.Relative), new
        {
            message = "   ",
            systemPrompt = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("message_required");
    }

    [Fact]
    public async Task RunSandbox_UnknownAgent_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/prompts/configs/unknown-{Guid.NewGuid():N}/sandbox", UriKind.Relative), new
            {
                message = "xin chao",
                systemPrompt = (string?)null,
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
