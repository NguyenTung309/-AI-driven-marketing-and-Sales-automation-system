using Clawbot.Domain.Agents;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Identity;
using Clawbot.SharedKernel.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Identity;

public sealed class DemoLlmConfigSeederTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SeedAsync_StoresEncryptedKey_AndBindsAgents()
    {
        using var fx = new TestAppDb();
        var tenant = Tenant.Create(DevDataSeeder.TenantSlug, "Default", "free", Now);
        fx.Db.Tenants.Add(tenant);
        var agent = AgentConfig.Create(tenant.Id, "orchestrator", "Orchestrator", "orchestrator", "cx/gpt-5.5", Now);
        var definition = AgentDefinition.Create(tenant.Id, "reviewer-agent", "Reviewer", "reviewer", "Review output", Now);
        fx.Db.AgentConfigs.Add(agent);
        fx.Db.AgentDefinitions.Add(definition);
        await fx.Db.SaveChangesAsync();

        var encryptor = Substitute.For<IEncryptor>();
        encryptor.Encrypt("sk-local-demo").Returns("encrypted::sk-local-demo");

        await RunSeederAsync(fx, encryptor, "sk-local-demo");

        var config = await fx.Db.LlmConfigs.IgnoreQueryFilters().SingleAsync();
        config.Provider.Should().Be("openai-compatible");
        config.ModelId.Should().Be("cx/gpt-5.5");
        config.BaseUrl.Should().Be("http://localhost:20128/v1");
        config.ApiKeyEncrypted.Should().Be("encrypted::sk-local-demo");
        config.ApiKeyEncrypted.Should().NotBe("sk-local-demo");

        var boundAgent = await fx.Db.AgentConfigs.IgnoreQueryFilters().SingleAsync();
        boundAgent.LlmConfigId.Should().Be(config.Id);
        var boundDef = await fx.Db.AgentDefinitions.IgnoreQueryFilters().SingleAsync();
        boundDef.LlmConfigId.Should().Be(config.Id);
    }

    [Fact]
    public async Task SeedAsync_WithoutKey_DoesNothing()
    {
        using var fx = new TestAppDb();
        var tenant = Tenant.Create(DevDataSeeder.TenantSlug, "Default", "free", Now);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();

        await RunSeederAsync(fx, Substitute.For<IEncryptor>(), apiKey: null);

        (await fx.Db.LlmConfigs.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_OnRerun()
    {
        using var fx = new TestAppDb();
        var tenant = Tenant.Create(DevDataSeeder.TenantSlug, "Default", "free", Now);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();

        var encryptor = Substitute.For<IEncryptor>();
        encryptor.Encrypt(Arg.Any<string>()).Returns("encrypted::key");

        await RunSeederAsync(fx, encryptor, "sk-local-demo");
        await RunSeederAsync(fx, encryptor, "sk-local-demo");

        (await fx.Db.LlmConfigs.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    // The seeder reads the key from the environment; set it only for the call, then clear.
    private static async Task RunSeederAsync(TestAppDb fx, IEncryptor encryptor, string? apiKey)
    {
        var services = new ServiceCollection();
        services.AddSingleton(fx.Db);
        services.AddSingleton(encryptor);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        Environment.SetEnvironmentVariable(DemoLlmConfigSeeder.EnvKeyName, apiKey);
        try
        {
            await DemoLlmConfigSeeder.SeedAsync(provider);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DemoLlmConfigSeeder.EnvKeyName, null);
        }
    }
}
