using Clawbot.Api.Endpoints;
using FluentAssertions;
using Xunit;

namespace Clawbot.Api.Tests;

public sealed class LlmConfigValidationTests
{
    // D10 — per-provider baseUrl normalization.
    [Theory]
    [InlineData("openai", "https://api.openai.com", "https://api.openai.com/v1")]
    [InlineData("openai", "https://api.openai.com/", "https://api.openai.com/v1")]
    [InlineData("openai", "https://api.openai.com/v1", "https://api.openai.com/v1")]
    [InlineData("openai", "https://host/openai/v1", "https://host/openai/v1")]
    [InlineData("anthropic", "https://api.anthropic.com", "https://api.anthropic.com")]
    [InlineData("anthropic", "https://api.anthropic.com/v1", "https://api.anthropic.com")]
    [InlineData("anthropic", "https://api.anthropic.com/v1/", "https://api.anthropic.com")]
    public void NormalizeBaseUrl_applies_provider_suffix(string provider, string input, string expected)
    {
        LlmConfigsEndpoints.NormalizeBaseUrl(provider, input).Should().Be(expected);
    }

    [Fact]
    public void NormalizeBaseUrl_returns_null_for_blank()
    {
        LlmConfigsEndpoints.NormalizeBaseUrl("openai", "   ").Should().BeNull();
    }

    // SSRF guard — https-only, reject private/loopback literal IPs and localhost.
    [Theory]
    [InlineData("https://api.openai.com", true)]
    [InlineData("https://1.2.3.4", true)]
    [InlineData("http://api.openai.com", false)]      // not https
    [InlineData("https://localhost", false)]
    [InlineData("https://127.0.0.1", false)]
    [InlineData("https://10.0.0.5", false)]
    [InlineData("https://192.168.1.10", false)]
    [InlineData("https://172.16.4.4", false)]
    [InlineData("https://169.254.1.1", false)]        // link-local
    [InlineData("not-a-url", false)]
    public void IsAllowedBaseUrl_rejects_non_https_and_private_hosts(string url, bool allowed)
    {
        LlmConfigsEndpoints.IsAllowedBaseUrl(url).Should().Be(allowed);
    }

    // D9 — cross-provider model guard.
    [Theory]
    [InlineData("anthropic", "claude-opus-4", true)]
    [InlineData("anthropic", "gpt-4o", false)]
    [InlineData("openai", "gpt-4o", true)]
    [InlineData("openai", "llama-3-70b", true)]       // OpenAI-compatible custom names allowed
    [InlineData("openai", "claude-opus-4", false)]
    [InlineData("vllm-custom", "anything", true)]     // unknown provider unconstrained
    public void IsModelCompatibleWithProvider_blocks_cross_provider_models(string provider, string model, bool ok)
    {
        AgentsEndpoints.IsModelCompatibleWithProvider(provider, model).Should().Be(ok);
    }
}
