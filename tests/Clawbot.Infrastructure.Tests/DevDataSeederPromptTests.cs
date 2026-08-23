using Clawbot.Domain.Agents;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Tests;

public sealed class DevDataSeederPromptTests
{
    [Fact]
    public async Task SeedAgentDefinitionsAsync_SeedsCurrentPromptWithoutExpandingPlannerPersona()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        const string persona = "Compact custom planner capability.";
        var definition = AgentDefinition.Create(
            fixture.TenantId,
            "content-agent",
            "Content Agent",
            "content",
            persona,
            DateTimeOffset.UtcNow,
            allowedToolsJson: "[]",
            memoryScope: "session");
        fixture.Db.AgentDefinitions.Add(definition);
        await fixture.Db.SaveChangesAsync();

        // Act
        await DevDataSeeder.SeedAgentDefinitionsAsync(fixture.Services);

        // Assert
        fixture.Db.ChangeTracker.Clear();
        var seeded = await fixture.Db.AgentDefinitions.IgnoreQueryFilters()
            .SingleAsync(agent => agent.Id == definition.Id);
        seeded.PersonaPrompt.Should().Be(persona);
        seeded.SystemPrompt.Should().Be(Clawbot.Agents.Core.AgentPromptPacks.For("content-agent"));
        seeded.SystemPromptVersion.Should().Be(Clawbot.Agents.Core.AgentPromptPacks.PromptPackVersion);
    }

    [Fact]
    public async Task SeedAgentDefinitionsAsync_DoesNotOverwriteTenantCustomizedSystemPrompt()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        const string persona = "Tenant-specific compact capability.";
        const string systemPrompt = "TENANT_CUSTOMIZED_SYSTEM_PROMPT";
        var definition = AgentDefinition.Create(
            fixture.TenantId,
            "sale-assist-agent",
            "Sale Assist Agent",
            "sale_assist",
            persona,
            DateTimeOffset.UtcNow,
            allowedToolsJson: "[]",
            memoryScope: "session");
        definition.SetSystemPrompt(systemPrompt, DateTimeOffset.UtcNow);
        fixture.Db.AgentDefinitions.Add(definition);
        await fixture.Db.SaveChangesAsync();

        // Act
        await DevDataSeeder.SeedAgentDefinitionsAsync(fixture.Services);

        // Assert
        fixture.Db.ChangeTracker.Clear();
        var seeded = await fixture.Db.AgentDefinitions.IgnoreQueryFilters()
            .SingleAsync(agent => agent.Id == definition.Id);
        seeded.PersonaPrompt.Should().Be(persona);
        seeded.SystemPrompt.Should().Be(systemPrompt);
        seeded.SystemPromptVersion.Should().BeNull();
        seeded.AllowedToolsJson.Should().Be("[\"sale-assist\"]");
    }

    [Fact]
    public async Task SeedAgentDefinitionsAsync_RepairsOtherTenantsWithoutProvisioningAgentsTheyNeverHad()
    {
        await using var fixture = await SeedFixture.CreateAsync();
        var customerTenant = Tenant.Create("hoc-ba", "Học Bá", "pro", DateTimeOffset.UtcNow);
        fixture.Db.Tenants.Add(customerTenant);
        var definition = AgentDefinition.Create(
            customerTenant.Id,
            "content-agent",
            "Content Agent",
            "content",
            "Compact capability.",
            DateTimeOffset.UtcNow,
            allowedToolsJson: "[]",
            memoryScope: "session");
        fixture.Db.AgentDefinitions.Add(definition);
        await fixture.Db.SaveChangesAsync();

        // Act
        await DevDataSeeder.SeedAgentDefinitionsAsync(fixture.Services);

        // Assert
        fixture.Db.ChangeTracker.Clear();
        var customerAgents = await fixture.Db.AgentDefinitions.IgnoreQueryFilters()
            .Where(agent => agent.TenantId == customerTenant.Id)
            .ToListAsync();
        customerAgents.Should().ContainSingle();
        customerAgents[0].SystemPrompt.Should().Be(Clawbot.Agents.Core.AgentPromptPacks.For("content-agent"));
        customerAgents[0].AllowedToolsJson.Should().Be("[\"content-agent\"]");

        var defaultAgents = await fixture.Db.AgentDefinitions.IgnoreQueryFilters()
            .CountAsync(agent => agent.TenantId == fixture.TenantId);
        defaultAgents.Should().Be(10);
    }

    private sealed class SeedFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _serviceProvider;

        private SeedFixture(
            SqliteConnection connection,
            AppDbContext db,
            ServiceProvider serviceProvider,
            Guid tenantId)
        {
            _connection = connection;
            Db = db;
            _serviceProvider = serviceProvider;
            TenantId = tenantId;
        }

        public AppDbContext Db { get; }
        public IServiceProvider Services => _serviceProvider;
        public Guid TenantId { get; }

        public static async Task<SeedFixture> CreateAsync()
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

            var tenant = Tenant.Create(DevDataSeeder.TenantSlug, "Default Tenant", "free", DateTimeOffset.UtcNow);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton(db)
                .BuildServiceProvider();
            return new SeedFixture(connection, db, services, tenant.Id);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            await _serviceProvider.DisposeAsync();
        }
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public static readonly NullTenantAccessor Instance = new();
        public TenantContext? Current => null;
        public TenantContext Require() => throw new NotSupportedException();
    }
}
