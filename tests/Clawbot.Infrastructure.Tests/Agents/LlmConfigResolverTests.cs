using Clawbot.Agents.Core.Chat;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Llm;
using Clawbot.Infrastructure.Agents;
using Clawbot.SharedKernel.Security;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Agents;

public sealed class LlmConfigResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ResolveAsync_returns_decrypted_bound_config_with_agent_model_override()
    {
        using var fx = new TestAppDb();
        var config = LlmConfig.Create(
            fx.TenantId,
            "anthropic",
            "config-model",
            "cipher-key",
            Now,
            baseUrl: "https://api.example",
            displayName: "Claude prod",
            inputUsdPer1M: 1.25m,
            outputUsdPer1M: 5.5m);
        var agent = AgentConfig.Create(fx.TenantId, "chat-agent", "Chat", "chat", "agent-model", Now);
        agent.BindLlmConfig(config.Id, Now);
        fx.Db.LlmConfigs.Add(config);
        fx.Db.AgentConfigs.Add(agent);
        await fx.Db.SaveChangesAsync();

        var decryptor = Substitute.For<IEncryptor>();
        decryptor.Decrypt("cipher-key").Returns("plain-key");
        var sut = new LlmConfigResolver(BuildScopeFactory(fx), decryptor);

        var resolved = await sut.ResolveAsync(fx.TenantId, "chat-agent");

        resolved.Should().Be(new ResolvedLlmConfig(
            "anthropic",
            "agent-model",
            "plain-key",
            "https://api.example",
            1.25m,
            5.5m));
    }

    [Fact]
    public async Task ResolveAsync_returns_definition_bound_config_without_model_override()
    {
        using var fx = new TestAppDb();
        var config = LlmConfig.Create(fx.TenantId, "openai-compatible", "definition-model", "cipher-key", Now);
        var definition = AgentDefinition.Create(fx.TenantId, "dynamic-agent", "Dynamic", "content", "Persona", Now, llmConfigId: config.Id);
        fx.Db.LlmConfigs.Add(config);
        fx.Db.AgentDefinitions.Add(definition);
        await fx.Db.SaveChangesAsync();

        var decryptor = Substitute.For<IEncryptor>();
        decryptor.Decrypt("cipher-key").Returns("plain-key");
        var sut = new LlmConfigResolver(BuildScopeFactory(fx), decryptor);

        var resolved = await sut.ResolveAsync(fx.TenantId, "dynamic-agent");

        resolved.Provider.Should().Be("openai-compatible");
        resolved.Model.Should().Be("definition-model");
        resolved.ApiKey.Should().Be("plain-key");
    }

    [Fact]
    public async Task ResolveAsync_throws_when_agent_has_no_bound_config()
    {
        using var fx = new TestAppDb();
        var agent = AgentConfig.Create(fx.TenantId, "chat-agent", "Chat", "chat", "claude-sonnet", Now);
        fx.Db.AgentConfigs.Add(agent);
        await fx.Db.SaveChangesAsync();

        var sut = new LlmConfigResolver(BuildScopeFactory(fx), Substitute.For<IEncryptor>());

        var act = async () => await sut.ResolveAsync(fx.TenantId, "chat-agent");

        await act.Should().ThrowAsync<LlmConfigNotConfiguredException>()
            .Where(ex => ex.TenantId == fx.TenantId && ex.AgentCode == "chat-agent");
    }

    [Fact]
    public async Task ResolveAsync_does_not_fallback_to_definition_when_agent_config_is_unbound()
    {
        using var fx = new TestAppDb();
        var config = LlmConfig.Create(fx.TenantId, "anthropic", "definition-model", "cipher-key", Now);
        var agent = AgentConfig.Create(fx.TenantId, "chat-agent", "Chat", "chat", "agent-model", Now);
        var definition = AgentDefinition.Create(fx.TenantId, "chat-agent", "Dynamic", "chat", "Persona", Now, llmConfigId: config.Id);
        fx.Db.LlmConfigs.Add(config);
        fx.Db.AgentConfigs.Add(agent);
        fx.Db.AgentDefinitions.Add(definition);
        await fx.Db.SaveChangesAsync();

        var sut = new LlmConfigResolver(BuildScopeFactory(fx), Substitute.For<IEncryptor>());

        var act = async () => await sut.ResolveAsync(fx.TenantId, "chat-agent");

        await act.Should().ThrowAsync<LlmConfigNotConfiguredException>()
            .Where(ex => ex.TenantId == fx.TenantId && ex.AgentCode == "chat-agent");
    }

    [Fact]
    public async Task ResolveAsync_throws_typed_config_error_when_decryption_fails()
    {
        using var fx = new TestAppDb();
        var config = LlmConfig.Create(fx.TenantId, "anthropic", "claude-sonnet", "cipher-key", Now);
        var agent = AgentConfig.Create(fx.TenantId, "chat-agent", "Chat", "chat", "claude-sonnet", Now);
        agent.BindLlmConfig(config.Id, Now);
        fx.Db.LlmConfigs.Add(config);
        fx.Db.AgentConfigs.Add(agent);
        await fx.Db.SaveChangesAsync();

        var decryptor = Substitute.For<IEncryptor>();
        decryptor.Decrypt("cipher-key").Returns(_ => throw new InvalidOperationException("bad cipher internals"));
        var sut = new LlmConfigResolver(BuildScopeFactory(fx), decryptor);

        var act = async () => await sut.ResolveAsync(fx.TenantId, "chat-agent");

        await act.Should().ThrowAsync<LlmConfigNotConfiguredException>()
            .Where(ex => ex.TenantId == fx.TenantId && ex.AgentCode == "chat-agent");
    }

    [Fact]
    public async Task ResolveAsync_throws_when_bound_config_is_inactive()
    {
        using var fx = new TestAppDb();
        var config = LlmConfig.Create(fx.TenantId, "openai", "gpt-4o", "cipher-key", Now);
        config.Deactivate(Now);
        var agent = AgentConfig.Create(fx.TenantId, "content-agent", "Content", "content", "gpt-4o-mini", Now);
        agent.BindLlmConfig(config.Id, Now);
        fx.Db.LlmConfigs.Add(config);
        fx.Db.AgentConfigs.Add(agent);
        await fx.Db.SaveChangesAsync();

        var sut = new LlmConfigResolver(BuildScopeFactory(fx), Substitute.For<IEncryptor>());

        var act = async () => await sut.ResolveAsync(fx.TenantId, "content-agent");

        await act.Should().ThrowAsync<LlmConfigNotConfiguredException>();
    }

    private static IServiceScopeFactory BuildScopeFactory(TestAppDb fx)
    {
        var services = new ServiceCollection();
        services.AddSingleton(fx.Db);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
