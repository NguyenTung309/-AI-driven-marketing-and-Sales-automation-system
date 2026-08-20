using Clawbot.Domain.Llm;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Llm;

public sealed class LlmConfigTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static LlmConfig CreateDefault() => LlmConfig.Create(
        TenantId, "openai", "gpt-4o", "enc-key-123", Now,
        baseUrl: "https://api.openai.com", displayName: "GPT-4o",
        inputUsdPer1M: 2.5m, outputUsdPer1M: 10m,
        timeoutSeconds: 60, maxOutputTokens: 4096, supportsVision: true);

    // ── Create ────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsAllFields()
    {
        var config = CreateDefault();

        config.TenantId.Should().Be(TenantId);
        config.Provider.Should().Be("openai");
        config.ModelId.Should().Be("gpt-4o");
        config.ApiKeyEncrypted.Should().Be("enc-key-123");
        config.BaseUrl.Should().Be("https://api.openai.com");
        config.DisplayName.Should().Be("GPT-4o");
        config.IsActive.Should().BeTrue();
        config.InputUsdPer1M.Should().Be(2.5m);
        config.OutputUsdPer1M.Should().Be(10m);
        config.TimeoutSeconds.Should().Be(60);
        config.MaxOutputTokens.Should().Be(4096);
        config.SupportsVision.Should().BeTrue();
        config.CreatedAt.Should().Be(Now);
        config.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_AllowsNullOptionals()
    {
        var config = LlmConfig.Create(TenantId, "anthropic", "claude-3", "key", Now);

        config.BaseUrl.Should().BeNull();
        config.DisplayName.Should().BeNull();
        config.InputUsdPer1M.Should().BeNull();
        config.OutputUsdPer1M.Should().BeNull();
        config.TimeoutSeconds.Should().BeNull();
        config.MaxOutputTokens.Should().BeNull();
        config.SupportsVision.Should().BeNull();
    }

    // ── UpdateConnection ──────────────────────────────────────────────

    [Fact]
    public void UpdateConnection_UpdatesIdentityFields()
    {
        var config = CreateDefault();
        var updatedAt = Now.AddMinutes(5);

        config.UpdateConnection("anthropic", "claude-3-opus", "https://api.anthropic.com",
            "Claude Opus", updatedAt, timeoutSeconds: 120, maxOutputTokens: 8192, supportsVision: false);

        config.Provider.Should().Be("anthropic");
        config.ModelId.Should().Be("claude-3-opus");
        config.BaseUrl.Should().Be("https://api.anthropic.com");
        config.DisplayName.Should().Be("Claude Opus");
        config.TimeoutSeconds.Should().Be(120);
        config.MaxOutputTokens.Should().Be(8192);
        config.SupportsVision.Should().BeFalse();
        config.UpdatedAt.Should().Be(updatedAt);
    }

    // ── UpdateRates ───────────────────────────────────────────────────

    [Fact]
    public void UpdateRates_UpdatesCostRates()
    {
        var config = CreateDefault();
        var updatedAt = Now.AddMinutes(5);

        config.UpdateRates(5m, 15m, updatedAt);

        config.InputUsdPer1M.Should().Be(5m);
        config.OutputUsdPer1M.Should().Be(15m);
        config.UpdatedAt.Should().Be(updatedAt);
    }

    // ── RotateApiKey ──────────────────────────────────────────────────

    [Fact]
    public void RotateApiKey_ReplacesEncryptedKey()
    {
        var config = CreateDefault();
        var updatedAt = Now.AddMinutes(5);

        config.RotateApiKey("new-enc-key", updatedAt);

        config.ApiKeyEncrypted.Should().Be("new-enc-key");
        config.UpdatedAt.Should().Be(updatedAt);
    }

    // ── RequireKeyRotation ────────────────────────────────────────────

    [Fact]
    public void RequireKeyRotation_ClearsKeyAndDeactivates()
    {
        var config = CreateDefault();
        var updatedAt = Now.AddMinutes(5);

        config.RequireKeyRotation(updatedAt);

        config.ApiKeyEncrypted.Should().BeEmpty();
        config.IsActive.Should().BeFalse();
        config.UpdatedAt.Should().Be(updatedAt);
    }

    // ── Activate / Deactivate ─────────────────────────────────────────

    [Fact]
    public void Activate_SetsIsActiveWhenKeyPresent()
    {
        var config = CreateDefault();
        config.Deactivate(Now.AddMinutes(1));

        config.Activate(Now.AddMinutes(5));

        config.IsActive.Should().BeTrue();
        config.UpdatedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void Activate_ThrowsWhenKeyMissing()
    {
        var config = CreateDefault();
        config.RequireKeyRotation(Now.AddMinutes(1));

        var act = () => config.Activate(Now.AddMinutes(5));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var config = CreateDefault();

        config.Deactivate(Now.AddMinutes(5));

        config.IsActive.Should().BeFalse();
        config.UpdatedAt.Should().Be(Now.AddMinutes(5));
    }
}
