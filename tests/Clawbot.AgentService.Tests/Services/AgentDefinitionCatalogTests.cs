using Clawbot.AgentService.Tests;
using Clawbot.Domain.Agents;
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
        db.AgentDefinitions.Add(AgentDefinition.Create(
            tenantId, "content-agent", "Content", "content", "Do content", DateTimeOffset.UtcNow));
        db.AgentDefinitions.Add(AgentDefinition.Create(
            otherTenantId, "lead-agent", "Lead", "lead", "Score leads", DateTimeOffset.UtcNow));
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
