using Clawbot.Agents.Core.Content;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Content;

public sealed class LlmVisionCapabilityResolverTests
{
    [Fact]
    public void Override_true_wins_over_unknown_model()
    {
        var capability = LlmVisionCapabilityResolver.ResolveFromConfig(
            provider: "openai-compatible",
            modelId: "custom-mystery",
            supportsVisionOverride: true);

        capability.Should().Be(LlmVisionCapability.Available);
    }

    [Fact]
    public void Override_false_wins_over_known_vision_model()
    {
        var capability = LlmVisionCapabilityResolver.ResolveFromConfig(
            provider: "openai",
            modelId: "gpt-4o",
            supportsVisionOverride: false);

        capability.Should().Be(LlmVisionCapability.Unavailable);
    }

    [Theory]
    [InlineData("openai", "gpt-4o", LlmVisionCapability.Available)]
    [InlineData("openai", "gpt-4o-mini", LlmVisionCapability.Available)]
    [InlineData("openai", "gpt-4.1", LlmVisionCapability.Available)]
    [InlineData("openai", "gpt-3.5-turbo", LlmVisionCapability.Unavailable)]
    [InlineData("anthropic", "claude-sonnet-4-5", LlmVisionCapability.Available)]
    [InlineData("anthropic", "claude-3-5-sonnet-latest", LlmVisionCapability.Available)]
    [InlineData("anthropic", "claude-3-haiku-20240307", LlmVisionCapability.Available)]
    [InlineData("openai-responses", "gpt-4o", LlmVisionCapability.Available)]
    [InlineData("openai-compatible", "gpt-4o", LlmVisionCapability.Unknown)]
    [InlineData("openai", "totally-unknown-model", LlmVisionCapability.Unknown)]
    public void Registry_is_conservative_and_unknown_by_default(
        string provider,
        string modelId,
        LlmVisionCapability expected)
    {
        LlmVisionCapabilityResolver.ResolveFromConfig(provider, modelId, supportsVisionOverride: null)
            .Should().Be(expected);
    }

    [Fact]
    public void Cache_key_includes_config_version_and_invalidates_on_update()
    {
        var cache = new LlmVisionCapabilityCache();
        var configId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var v1 = new DateTimeOffset(2026, 7, 21, 8, 0, 0, TimeSpan.Zero);
        var v2 = v1.AddMinutes(1);

        cache.Set(tenantId, "reviewer-agent", configId, v1, LlmVisionCapability.Available);
        cache.TryGet(tenantId, "reviewer-agent", configId, v1, out var hit1).Should().BeTrue();
        hit1.Should().Be(LlmVisionCapability.Available);

        cache.TryGet(tenantId, "reviewer-agent", configId, v2, out _).Should().BeFalse();

        cache.Invalidate(configId);
        cache.TryGet(tenantId, "reviewer-agent", configId, v1, out _).Should().BeFalse();
    }
}
