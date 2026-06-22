using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Agents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Agents;

public sealed class DbAgentCatalogTests
{
    [Fact]
    public async Task ListAsync_returns_running_tenant_agents_as_orchestratable_catalog_entries()
    {
        using var app = new TestAppDb();
        var now = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);
        var content = AgentConfig.Create(app.TenantId, "content-agent", "Content", "content", "claude", now);
        content.Start();
        var otherTenant = AgentConfig.Create(Guid.NewGuid(), "research-agent", "Other", "research", "claude", now);
        otherTenant.Start();
        app.Db.AgentConfigs.AddRange(content, otherTenant);
        await app.Db.SaveChangesAsync();
        var catalog = new DbAgentCatalog(app.Db);

        var entries = await catalog.ListAsync(CancellationToken.None);

        var entry = entries.Should().ContainSingle().Subject;
        entry.Code.Should().Be("content-agent");
        entry.DisplayName.Should().Be("Content");
        entry.AgentType.Should().Be("content");
        entry.Orchestratable.Should().BeTrue();
        entry.ShortName.Should().Be("content");
    }

    [Fact]
    public async Task ListAsync_uses_data_resolved_orchestration_metadata_from_config_json()
    {
        using var app = new TestAppDb();
        var now = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);
        var content = AgentConfig.Create(app.TenantId, "content-agent", "Content", "content", "claude", now);
        content.UpdateSettings(
            "Content",
            "claude",
            "[]",
            "[]",
            "{\"orchestration\":{\"description\":\"Write campaign content.\",\"inputSchema\":\"{\\\"brief\\\":\\\"string\\\"}\",\"orchestratable\":true}}",
            now);
        content.Start();
        app.Db.AgentConfigs.Add(content);
        await app.Db.SaveChangesAsync();
        var catalog = new DbAgentCatalog(app.Db);

        var entry = (await catalog.ListAsync(CancellationToken.None)).Should().ContainSingle().Subject;

        entry.Description.Should().Be("Write campaign content.");
        entry.InputSchemaJson.Should().Be("{\"brief\":\"string\"}");
        entry.Orchestratable.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_matches_code_short_name_and_agent_type_case_insensitively()
    {
        using var app = new TestAppDb();
        var now = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);
        var agent = AgentConfig.Create(app.TenantId, "content-agent", "Content", "content", "claude", now);
        agent.Start();
        app.Db.AgentConfigs.Add(agent);
        await app.Db.SaveChangesAsync();
        var catalog = new DbAgentCatalog(app.Db);

        (await catalog.ResolveAsync("content-agent", CancellationToken.None)).Code.Should().Be("content-agent");
        (await catalog.ResolveAsync("CONTENT", CancellationToken.None)).Code.Should().Be("content-agent");
        (await catalog.ResolveAsync("content", CancellationToken.None)).Code.Should().Be("content-agent");
    }

    [Fact]
    public async Task ResolveAsync_rejects_disabled_agent()
    {
        using var app = new TestAppDb();
        var now = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);
        app.Db.AgentConfigs.Add(AgentConfig.Create(app.TenantId, "content-agent", "Content", "content", "claude", now));
        await app.Db.SaveChangesAsync();
        var catalog = new DbAgentCatalog(app.Db);

        Func<Task> act = async () => await catalog.ResolveAsync("content", CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("Agent 'content' is not available for orchestration.");
    }
}
