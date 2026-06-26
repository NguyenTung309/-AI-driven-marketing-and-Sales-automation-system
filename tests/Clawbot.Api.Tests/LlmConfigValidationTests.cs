using Clawbot.Api.Endpoints;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
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
    [InlineData("https://1.2.3.4", true)]
    [InlineData("http://api.openai.com", false)]      // not https
    [InlineData("https://localhost", false)]
    [InlineData("https://127.0.0.1", false)]
    [InlineData("https://10.0.0.5", false)]
    [InlineData("https://192.168.1.10", false)]
    [InlineData("https://172.16.4.4", false)]
    [InlineData("https://169.254.1.1", false)]        // link-local
    [InlineData("https://[::1]", false)]              // IPv6 loopback
    [InlineData("https://[fc00::1]", false)]          // IPv6 unique-local
    [InlineData("https://[fec0::1]", false)]          // IPv6 site-local
    [InlineData("https://225.0.0.1", false)]          // multicast
    [InlineData("https://user:pass@api.openai.com", false)]
    [InlineData("not-a-url", false)]
    public void IsAllowedBaseUrl_rejects_non_https_and_private_hosts(string url, bool allowed)
    {
        LlmConfigsEndpoints.IsAllowedBaseUrl(url).Should().Be(allowed);
    }


    [Fact]
    public void SafeTestConnectionError_does_not_expose_raw_exception_message()
    {
        var error = LlmConfigsEndpoints.SafeTestConnectionError(new InvalidOperationException("https://internal.local secret stack"));

        error.Should().Be("llm_connection_test_failed");
    }

    [Theory]
    [InlineData("Development", "true", true)]
    [InlineData("Development", "false", false)]
    [InlineData("Production", "true", false)]
    public void AllowPrivateBaseUrls_is_config_enabled_only_in_development(string environment, string enabled, bool expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["LlmBaseUrl:AllowPrivate"] = enabled })
            .Build();

        LlmConfigsEndpoints.AllowPrivateBaseUrls(config, new TestHostEnvironment(environment)).Should().Be(expected);
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

    [Fact]
    public void AreBoundAgentModelsCompatible_blocks_provider_update_that_would_break_bound_agents()
    {
        var ok = LlmConfigsEndpoints.AreBoundAgentModelsCompatible(
            provider: "openai",
            configModel: "gpt-4o",
            boundAgentModels: ["gpt-4o", "llama-3"]);
        var mismatch = LlmConfigsEndpoints.AreBoundAgentModelsCompatible(
            provider: "openai",
            configModel: "gpt-4o",
            boundAgentModels: ["gpt-4o", "claude-opus-4"]);

        ok.Should().BeTrue();
        mismatch.Should().BeFalse();
    }

    [Fact]
    public void AreBoundAgentModelsCompatible_uses_config_model_when_bound_agent_model_is_blank()
    {
        LlmConfigsEndpoints.AreBoundAgentModelsCompatible(
                provider: "anthropic",
                configModel: "claude-sonnet-4",
                boundAgentModels: [""])
            .Should().BeTrue();
    }

    [Fact]
    public void Delete_blocks_configs_that_are_still_bound_to_agents()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "api", "Clawbot.Api", "Endpoints", "LlmConfigsEndpoints.cs"));

        source.Should().Contain("llm_config_in_use");
        source.Should().Contain("AnyAsync(a => a.TenantId == row.TenantId && a.LlmConfigId == row.Id && a.DeletedAt == null");
    }

    [Fact]
    public void Llm_provider_config_no_longer_exposes_max_tokens_or_temperature()
    {
        var root = FindRepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "src", "api", "Clawbot.Api.Contracts", "Llm", "LlmConfigDtos.cs"),
            Path.Combine(root, "src", "shared", "Clawbot.Domain", "Llm", "LlmConfig.cs"),
            Path.Combine(root, "src", "shared", "Clawbot.Infrastructure", "Agents", "LlmConfigResolver.cs"),
            Path.Combine(root, "src", "agents", "Clawbot.Agents.Core", "Chat", "LlmProviderAbstractions.cs"),
            Path.Combine(root, "src", "frontend", "clawbot-web", "src", "shared", "api", "llmConfigs.ts"),
            Path.Combine(root, "src", "frontend", "clawbot-web", "src", "features", "llm-providers", "LlmProvidersPage.tsx"),
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            text.Should().NotContain("MaxTokens");
            text.Should().NotContain("maxTokens");
            text.Should().NotContain("Temperature");
            text.Should().NotContain("temperature");
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Clawbot.Api.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Clawbot.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
