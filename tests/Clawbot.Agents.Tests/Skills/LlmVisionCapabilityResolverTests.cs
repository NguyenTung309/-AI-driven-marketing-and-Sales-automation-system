using Clawbot.Agents.Core.Content;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Vision capability: override nullable > registry first-party > unknown. Không suy từ tên provider.
public sealed class LlmVisionCapabilityResolverTests
{
    [Fact]
    public void Override_True_Available()
    {
        LlmVisionCapabilityResolver.ResolveFromConfig("anything", "any-model", true)
            .Should().Be(LlmVisionCapability.Available);
    }

    [Fact]
    public void Override_False_Unavailable()
    {
        LlmVisionCapabilityResolver.ResolveFromConfig("openai", "gpt-4o", false)
            .Should().Be(LlmVisionCapability.Unavailable);
    }

    [Theory]
    [InlineData("", "gpt-4o")]
    [InlineData("openai", "")]
    [InlineData("  ", "  ")]
    public void EmptyProviderOrModel_Unknown(string provider, string model)
    {
        LlmVisionCapabilityResolver.ResolveFromConfig(provider, model, null)
            .Should().Be(LlmVisionCapability.Unknown);
    }

    [Theory]
    [InlineData("openai", "gpt-4o-mini")]
    [InlineData("openai", "gpt-4.1")]
    [InlineData("openai-responses", "o3-pro")]
    [InlineData("anthropic", "claude-3-5-sonnet-latest")]
    [InlineData("anthropic", "claude-opus-4-1")]
    public void KnownVisionModels_Available(string provider, string model)
    {
        LlmVisionCapabilityResolver.ResolveFromConfig(provider, model, null)
            .Should().Be(LlmVisionCapability.Available);
    }

    [Theory]
    [InlineData("openai", "gpt-3.5-turbo")]
    [InlineData("openai-responses", "gpt-3.5-turbo")]
    public void KnownNonVisionModels_Unavailable(string provider, string model)
    {
        LlmVisionCapabilityResolver.ResolveFromConfig(provider, model, null)
            .Should().Be(LlmVisionCapability.Unavailable);
    }

    [Theory]
    [InlineData("openai-compatible", "gpt-4o")]
    [InlineData("openai_compatible", "gpt-4o")]
    public void OpenAiCompatibleGateway_Unknown_WithoutOverride(string provider, string model)
    {
        LlmVisionCapabilityResolver.ResolveFromConfig(provider, model, null)
            .Should().Be(LlmVisionCapability.Unknown);
    }

    [Fact]
    public void UnknownProvider_Unknown()
    {
        LlmVisionCapabilityResolver.ResolveFromConfig("mystery", "some-model", null)
            .Should().Be(LlmVisionCapability.Unknown);
    }

    [Fact]
    public void ProviderCaseInsensitive_ModelPrefixNormalized()
    {
        LlmVisionCapabilityResolver.ResolveFromConfig("OpenAI", "GPT-4o", null)
            .Should().Be(LlmVisionCapability.Available);
    }

    [Fact]
    public void InterfaceImpl_DelegatesToStatic()
    {
        ILlmVisionCapabilityResolver resolver = new LlmVisionCapabilityResolver();

        resolver.ResolveFromConfig("openai", "gpt-4o", null)
            .Should().Be(LlmVisionCapability.Available);
    }
}

// Cache theo (tenant, agent, configId, updatedAt); invalidate theo configId.
public sealed class LlmVisionCapabilityCacheTests
{
    private static readonly Guid Tenant = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Updated = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Set_ThenGet_ReturnsStoredCapability()
    {
        var cache = new LlmVisionCapabilityCache();
        var configId = Guid.NewGuid();
        cache.Set(Tenant, "chat", configId, Updated, LlmVisionCapability.Available);

        cache.TryGet(Tenant, "chat", configId, Updated, out var cap).Should().BeTrue();
        cap.Should().Be(LlmVisionCapability.Available);
    }

    [Fact]
    public void Get_Miss_ReturnsFalse()
    {
        var cache = new LlmVisionCapabilityCache();

        cache.TryGet(Tenant, "chat", Guid.NewGuid(), Updated, out _).Should().BeFalse();
    }

    [Fact]
    public void Get_DifferentUpdatedAt_IsMiss()
    {
        var cache = new LlmVisionCapabilityCache();
        var configId = Guid.NewGuid();
        cache.Set(Tenant, "chat", configId, Updated, LlmVisionCapability.Available);

        cache.TryGet(Tenant, "chat", configId, Updated.AddSeconds(1), out _).Should().BeFalse();
    }

    [Fact]
    public void Invalidate_RemovesEntriesForConfigId()
    {
        var cache = new LlmVisionCapabilityCache();
        var configId = Guid.NewGuid();
        cache.Set(Tenant, "chat", configId, Updated, LlmVisionCapability.Available);

        cache.Invalidate(configId);

        cache.TryGet(Tenant, "chat", configId, Updated, out _).Should().BeFalse();
    }

    [Fact]
    public void Invalidate_LeavesOtherConfigsUntouched()
    {
        var cache = new LlmVisionCapabilityCache();
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        cache.Set(Tenant, "chat", keep, Updated, LlmVisionCapability.Available);
        cache.Set(Tenant, "chat", drop, Updated, LlmVisionCapability.Unavailable);

        cache.Invalidate(drop);

        cache.TryGet(Tenant, "chat", keep, Updated, out var cap).Should().BeTrue();
        cap.Should().Be(LlmVisionCapability.Available);
    }
}
