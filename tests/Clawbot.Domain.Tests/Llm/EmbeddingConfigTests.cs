using Clawbot.Domain.Llm;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Llm;

public sealed class EmbeddingConfigTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static EmbeddingConfig CreateDefault() => EmbeddingConfig.Create(
        TenantId, "openai", "text-embedding-3-small", "enc-key", 1536, Now,
        baseUrl: "https://api.openai.com", displayName: "OpenAI Small");

    // ── Create ────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsAllFields()
    {
        var config = CreateDefault();

        config.TenantId.Should().Be(TenantId);
        config.Provider.Should().Be("openai");
        config.ModelId.Should().Be("text-embedding-3-small");
        config.ApiKeyEncrypted.Should().Be("enc-key");
        config.Dimension.Should().Be(1536);
        config.BaseUrl.Should().Be("https://api.openai.com");
        config.DisplayName.Should().Be("OpenAI Small");
        config.IsActive.Should().BeTrue();
        config.CreatedAt.Should().Be(Now);
        config.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_AllowsNullOptionals()
    {
        var config = EmbeddingConfig.Create(TenantId, "hash", "local-hash", "", 256, Now);

        config.BaseUrl.Should().BeNull();
        config.DisplayName.Should().BeNull();
    }

    // ── UpdateConnection ──────────────────────────────────────────────

    [Fact]
    public void UpdateConnection_UpdatesAllIdentityFields()
    {
        var config = CreateDefault();
        var updatedAt = Now.AddMinutes(5);

        config.UpdateConnection("openai-compatible", "bge-m3", "https://local:8080", "BGE-M3", 1024, updatedAt);

        config.Provider.Should().Be("openai-compatible");
        config.ModelId.Should().Be("bge-m3");
        config.BaseUrl.Should().Be("https://local:8080");
        config.DisplayName.Should().Be("BGE-M3");
        config.Dimension.Should().Be(1024);
        config.UpdatedAt.Should().Be(updatedAt);
    }

    // ── RotateApiKey ──────────────────────────────────────────────────

    [Fact]
    public void RotateApiKey_ReplacesEncryptedKey()
    {
        var config = CreateDefault();
        var updatedAt = Now.AddMinutes(5);

        config.RotateApiKey("new-key", updatedAt);

        config.ApiKeyEncrypted.Should().Be("new-key");
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
    public void Activate_ThrowsWhenKeyMissingForNonHashProvider()
    {
        var config = CreateDefault();
        config.RequireKeyRotation(Now.AddMinutes(1));

        var act = () => config.Activate(Now.AddMinutes(5));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Activate_AllowsHashProviderWithoutKey()
    {
        var config = EmbeddingConfig.Create(TenantId, "hash", "local", "", 256, Now);
        config.Deactivate(Now.AddMinutes(1));

        config.Activate(Now.AddMinutes(5));

        config.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Activate_IsCaseInsensitiveForHashProvider()
    {
        var config = EmbeddingConfig.Create(TenantId, "HASH", "local", "", 256, Now);
        config.Deactivate(Now.AddMinutes(1));

        config.Activate(Now.AddMinutes(5));

        config.IsActive.Should().BeTrue();
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
