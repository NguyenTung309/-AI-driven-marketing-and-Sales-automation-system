using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Security;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/logs — task runs + audit. Seed trực tiếp AgentSession/AgentTrace/AuditLog/LlmCostEntry.
/// Nhánh tìm kiếm q dùng EF.Functions.Like (InMemory không hỗ trợ) nên không phủ ở đây.
/// </summary>
public sealed class LogsEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public LogsEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private async Task<Guid> GetAdminTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private async Task<Guid> SeedAgentAsync(Guid tenantId, string code, string name, string type = "research")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = AgentConfig.Create(tenantId, code, name, type, "gpt-test", DateTimeOffset.UtcNow);
        db.AgentConfigs.Add(agent);
        await db.SaveChangesAsync();
        return agent.Id;
    }

    /// <summary>Seed phiên + trace; status chạy qua Finish/Fail của domain.</summary>
    private async Task<Guid> SeedSessionAsync(
        Guid tenantId, Guid? agentId, string goal, string status, DateTimeOffset startedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = AgentSession.Start(tenantId, agentId, conversationId: null, goal, startedAt);
        session.AppendTrace("task-1", "Agent Test", "tool", "dang xu ly", startedAt.AddSeconds(5));
        if (status == "completed") session.Finish(startedAt.AddMinutes(2));
        if (status == "failed") session.Fail(startedAt.AddMinutes(2));
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private async Task<Guid> SeedAuditAsync(Guid tenantId, string action, string resourceType, Guid? resourceId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = AuditLog.Create(tenantId, userId: null, action, resourceType, resourceId,
            DateTimeOffset.UtcNow, diffJson: "{\"a\":1}");
        db.AuditLogs.Add(audit);
        await db.SaveChangesAsync();
        return audit.Id;
    }

    private async Task SeedCostAsync(Guid tenantId, string agentCode, DateTimeOffset createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.LlmCostLedger.Add(LlmCostEntry.Create(tenantId, agentCode, "gpt-test",
            inputTokens: 100, outputTokens: 50, usd: 0.002m, createdAt));
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------
    // GET /task-runs
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListTaskRuns_ReturnsSession_WithAgentInfoTracesAndCost()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var agentId = await SeedAgentAsync(tenantId, $"log-agent-{Guid.NewGuid():N}"[..16], "Agent Log");
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var sessionId = await SeedSessionAsync(tenantId, agentId, "Muc tieu test log", "completed", startedAt);
        await SeedCostAsync(tenantId, (await GetAgentCodeAsync(agentId)), startedAt.AddMinutes(1));

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/logs/task-runs", UriKind.Relative));

        var item = body.GetProperty("items").EnumerateArray()
            .First(i => Guid.Parse(i.GetProperty("id").GetString()!) == sessionId);
        item.GetProperty("agentName").GetString().Should().Be("Agent Log");
        item.GetProperty("status").GetString().Should().Be("completed");
        item.GetProperty("goal").GetString().Should().Be("Muc tieu test log");
        item.GetProperty("traceCount").GetInt32().Should().Be(1);
        item.GetProperty("totalTokens").GetInt32().Should().Be(150, "cost trong cửa sổ phiên phải cộng vào");
        item.GetProperty("usd").GetDecimal().Should().Be(0.002m);

        var stats = body.GetProperty("stats");
        stats.GetProperty("totalSessions").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        stats.GetProperty("completedSessions").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        stats.GetProperty("tokensLast30Days").GetInt32().Should().BeGreaterThanOrEqualTo(150);
        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ListTaskRuns_FilterByAgentCode_ReturnsOnlyThatAgent()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var codeA = $"loga-{Guid.NewGuid():N}"[..12];
        var agentA = await SeedAgentAsync(tenantId, codeA, "Agent A");
        var agentB = await SeedAgentAsync(tenantId, $"logb-{Guid.NewGuid():N}"[..12], "Agent B");
        var now = DateTimeOffset.UtcNow;
        var sessionA = await SeedSessionAsync(tenantId, agentA, "Phien A", "completed", now.AddMinutes(-5));
        var sessionB = await SeedSessionAsync(tenantId, agentB, "Phien B", "completed", now.AddMinutes(-4));

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/logs/task-runs?agentCode={codeA}", UriKind.Relative));

        var ids = body.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!)).ToList();
        ids.Should().Contain(sessionA).And.NotContain(sessionB);

        // Agent code không tồn tại -> tập rỗng, không phải 404.
        var unknown = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/logs/task-runs?agentCode=khong-ton-tai", UriKind.Relative));
        unknown.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ListTaskRuns_FilterByStatus_ReturnsOnlyMatching()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var now = DateTimeOffset.UtcNow;
        await SeedSessionAsync(tenantId, agentId: null, "Phien thanh cong", "completed", now.AddMinutes(-3));
        await SeedSessionAsync(tenantId, agentId: null, "Phien that bai", "failed", now.AddMinutes(-2));

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/logs/task-runs?status=failed", UriKind.Relative));

        body.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(i => i.GetProperty("status").GetString() == "failed");
    }

    [Fact]
    public async Task ListTaskRuns_CursorPagination_DropsTotalOnNextPage()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
            await SeedSessionAsync(tenantId, agentId: null, $"Phien phan trang {i}", "completed", now.AddMinutes(-9 + i));

        var page1 = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/logs/task-runs?pageSize=2", UriKind.Relative));
        page1.GetProperty("items").GetArrayLength().Should().Be(2);
        page1.GetProperty("total").ValueKind.Should().Be(JsonValueKind.Number, "trang đầu phải có total");
        var cursor = page1.GetProperty("nextCursor").GetString();
        cursor.Should().NotBeNull();

        var page2 = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/logs/task-runs?pageSize=2&cursor={Uri.EscapeDataString(cursor!)}", UriKind.Relative));
        page2.GetProperty("items").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        page2.GetProperty("total").ValueKind.Should().Be(JsonValueKind.Null, "trang cursor không đếm lại total");
    }

    // ------------------------------------------------------------------
    // GET /task-runs/{sessionId}
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetTaskRun_ReturnsDetail_WithTracesAndAudit()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var sessionId = await SeedSessionAsync(tenantId, agentId: null, "Phien chi tiet", "completed",
            DateTimeOffset.UtcNow.AddMinutes(-6));
        await SeedAuditAsync(tenantId, "session.test", "agent_session", sessionId);

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/logs/task-runs/{sessionId}", UriKind.Relative));

        body.GetProperty("run").GetProperty("id").GetString().Should().Be(sessionId.ToString());
        body.GetProperty("traces").GetArrayLength().Should().Be(1);
        body.GetProperty("traces")[0].GetProperty("phase").GetString().Should().Be("tool");
        body.GetProperty("auditEvents").GetArrayLength().Should().Be(1);
        body.GetProperty("auditEvents")[0].GetProperty("action").GetString().Should().Be("session.test");
    }

    [Fact]
    public async Task GetTaskRun_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/logs/task-runs/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // GET /audit (perm system.logs)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListAudit_ReturnsRows_AndFiltersByActionAndResourceType()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        await SeedAuditAsync(tenantId, "logs.test.action", "logs_test", resourceId: null);
        await SeedAuditAsync(tenantId, "logs.other", "logs_other", resourceId: null);

        var all = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/logs/audit", UriKind.Relative));
        all.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        var byAction = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/logs/audit?action=logs.test.action", UriKind.Relative));
        byAction.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(i => i.GetProperty("action").GetString() == "logs.test.action");

        var byType = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/logs/audit?resourceType=logs_other", UriKind.Relative));
        byType.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(i => i.GetProperty("resourceType").GetString() == "logs_other");
    }

    [Fact]
    public async Task ListAudit_InvalidPageSize_ClampsToDefault()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        await SeedAuditAsync(tenantId, "logs.clamp", "logs_test", resourceId: null);

        // pageSize=0 bị kẹp về 50 — endpoint vẫn trả 200 thay vì lỗi binding.
        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/api/logs/audit?pageSize=0", UriKind.Relative));

        body.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(1);
    }

    /// <summary>Đọc code agent theo id để seed cost khớp cửa sổ.</summary>
    private async Task<string> GetAgentCodeAsync(Guid agentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var agent = await db.AgentConfigs.IgnoreQueryFilters().FirstAsync(a => a.Id == agentId);
        return agent.Code;
    }
}
