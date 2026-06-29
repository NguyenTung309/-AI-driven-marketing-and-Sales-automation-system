using Clawbot.AgentService.Tests;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Llm;
using Clawbot.Infrastructure.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NSubstitute;

namespace Clawbot.AgentService.Tests.Services;

public sealed class AgentDefinitionCatalogTests
{
    [Fact]
    public async Task ListAsync_ReturnsTenantDefinitions_WhenAmbientTenantMissing()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var tenants = Substitute.For<ITenantAccessor>();
        tenants.Current.Returns((TenantContext?)null);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SqliteFriendlyModelCustomizer>()
            .Options;
        await using var db = new AppDbContext(options, tenants);
        await db.Database.EnsureCreatedAsync();
        var llmConfig = LlmConfig.Create(tenantId, "anthropic", "claude-test", "encrypted", DateTimeOffset.UtcNow);
        var otherLlmConfig = LlmConfig.Create(otherTenantId, "anthropic", "claude-test", "encrypted", DateTimeOffset.UtcNow);
        db.LlmConfigs.AddRange(llmConfig, otherLlmConfig);
        db.AgentDefinitions.Add(AgentDefinition.Create(
            tenantId, "content-agent", "Content", "content", "Do content", DateTimeOffset.UtcNow, llmConfigId: llmConfig.Id));
        db.AgentDefinitions.Add(AgentDefinition.Create(
            otherTenantId, "lead-agent", "Lead", "lead", "Score leads", DateTimeOffset.UtcNow, llmConfigId: otherLlmConfig.Id));
        await db.SaveChangesAsync();

        var rows = await new AgentDefinitionCatalog(db, tenants).ListAsync(tenantId);

        rows.Should().ContainSingle().Which.Code.Should().Be("content-agent");
    }

    [Fact]
    public async Task ListAsync_ReturnsNoDefinitions_WhenAmbientTenantDiffers()
    {
        var tenantId = Guid.NewGuid();
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var tenants = Substitute.For<ITenantAccessor>();
        tenants.Current.Returns(new TenantContext(Guid.NewGuid(), "other"));

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SqliteFriendlyModelCustomizer>()
            .Options;
        await using var db = new AppDbContext(options, tenants);
        await db.Database.EnsureCreatedAsync();
        db.AgentDefinitions.Add(AgentDefinition.Create(
            tenantId, "content-agent", "Content", "content", "Do content", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var rows = await new AgentDefinitionCatalog(db, tenants).ListAsync(tenantId);

        rows.Should().BeEmpty();
    }
}
