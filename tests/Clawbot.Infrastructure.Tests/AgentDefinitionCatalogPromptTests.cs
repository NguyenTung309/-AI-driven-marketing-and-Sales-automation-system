using Clawbot.Domain.Agents;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Tests;

// Bỏ sót SystemPrompt ở đây thì prompt pack không bao giờ tới worker: catalog là đường duy nhất
// orchestration đọc agent_definitions, và lỗi đó im lặng (worker lùi về PersonaPrompt, vẫn chạy xanh).
public sealed class AgentDefinitionCatalogPromptTests
{
    [Fact]
    public async Task ListAsync_CarriesSystemPromptSeparatelyFromPlannerPersona()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        const string persona = "Compact planner capability.";
        const string systemPrompt = "FULL_RUNTIME_SYSTEM_PROMPT";
        var definition = AgentDefinition.Create(
            fixture.TenantId,
            "content-agent",
            "Content Agent",
            "content",
            persona,
            DateTimeOffset.UtcNow,
            allowedToolsJson: "[\"content-agent\"]",
            memoryScope: "session",
            llmConfigId: Guid.NewGuid());
        definition.SetSystemPrompt(systemPrompt, DateTimeOffset.UtcNow);
        fixture.Db.AgentDefinitions.Add(definition);
        await fixture.Db.SaveChangesAsync();

        // Act
        var entries = await new AgentDefinitionCatalog(fixture.Db, NullTenantAccessor.Instance)
            .ListAsync(fixture.TenantId);

        // Assert
        var entry = entries.Should().ContainSingle().Subject;
        entry.SystemPrompt.Should().Be(systemPrompt);
        entry.Description.Should().Be(persona);
        entry.ToPlannerEntry().Description.Should().Be(persona);
    }

    [Fact]
    public async Task ListAsync_LeavesSystemPromptNull_SoTheWorkerFallsBackToPersona()
    {
        await using var fixture = await CatalogFixture.CreateAsync();
        var definition = AgentDefinition.Create(
            fixture.TenantId,
            "docs-agent",
            "Docs Agent",
            "docs",
            "Render templated documents.",
            DateTimeOffset.UtcNow,
            allowedToolsJson: "[\"docs-agent\"]",
            memoryScope: "session",
            llmConfigId: Guid.NewGuid());
        fixture.Db.AgentDefinitions.Add(definition);
        await fixture.Db.SaveChangesAsync();

        // Act
        var entries = await new AgentDefinitionCatalog(fixture.Db, NullTenantAccessor.Instance)
            .ListAsync(fixture.TenantId);

        // Assert
        entries.Should().ContainSingle().Which.SystemPrompt.Should().BeNull();
    }

    private sealed class CatalogFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private CatalogFixture(SqliteConnection connection, AppDbContext db, Guid tenantId)
        {
            _connection = connection;
            Db = db;
            TenantId = tenantId;
        }

        public AppDbContext Db { get; }
        public Guid TenantId { get; }

        public static async Task<CatalogFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options, NullTenantAccessor.Instance);
            var createScript = db.Database.GenerateCreateScript()
                .Replace("nvarchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
                .Replace("varchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
                .Replace("varbinary(max)", "BLOB", StringComparison.OrdinalIgnoreCase)
                .Replace("N'", "'", StringComparison.Ordinal);
            await db.Database.ExecuteSqlRawAsync(createScript);

            var tenant = Tenant.Create("catalog-prompt", "Catalog Prompt", "free", DateTimeOffset.UtcNow);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            return new CatalogFixture(connection, db, tenant.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public static readonly NullTenantAccessor Instance = new();
        public TenantContext? Current => null;
        public TenantContext Require() => throw new NotSupportedException();
    }
}
