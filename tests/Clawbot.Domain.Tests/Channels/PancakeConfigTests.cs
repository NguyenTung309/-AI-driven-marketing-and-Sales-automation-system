using Clawbot.Domain.Channels;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Channels;

public sealed class PancakeConfigTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsDefaults()
    {
        var config = PancakeConfig.Create(TenantId, Now);

        config.TenantId.Should().Be(TenantId);
        config.BaseUrl.Should().Be("https://pages.fm/api/public_api/v1");
        config.AccessTokenEncrypted.Should().BeEmpty();
        config.WebhookSecretEncrypted.Should().BeEmpty();
        config.SignatureHeader.Should().Be("x-pancake-signature");
        config.SignatureAlgo.Should().Be("hmac-sha256");
        config.SignatureEncoding.Should().Be("hex");
        config.SendPathTemplate.Should().Be("/pages/{page_id}/conversations/{thread_id}/messages");
        config.PageId.Should().BeEmpty();
        config.AuthMode.Should().Be("query");
        config.IsActive.Should().BeTrue();
        config.CreatedAt.Should().Be(Now);
        config.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void UpdateEndpoint_TrimsTrailingSlashFromBaseUrl()
    {
        var config = PancakeConfig.Create(TenantId, Now);

        config.UpdateEndpoint("https://custom.api.com/", "/send", "header", Now.AddMinutes(1));

        config.BaseUrl.Should().Be("https://custom.api.com");
        config.SendPathTemplate.Should().Be("/send");
        config.AuthMode.Should().Be("header");
        config.UpdatedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void UpdateEndpoint_IgnoresBlankValues()
    {
        var config = PancakeConfig.Create(TenantId, Now);

        config.UpdateEndpoint("", "", "", Now.AddMinutes(1));

        config.BaseUrl.Should().Be("https://pages.fm/api/public_api/v1");
        config.SendPathTemplate.Should().Be("/pages/{page_id}/conversations/{thread_id}/messages");
        config.AuthMode.Should().Be("query");
    }

    [Fact]
    public void UpdateSignature_LowercasesAllFields()
    {
        var config = PancakeConfig.Create(TenantId, Now);

        config.UpdateSignature("X-Custom-Sig", "HMAC-SHA512", "BASE64", Now.AddMinutes(1));

        config.SignatureHeader.Should().Be("x-custom-sig");
        config.SignatureAlgo.Should().Be("hmac-sha512");
        config.SignatureEncoding.Should().Be("base64");
    }

    [Fact]
    public void UpdateSignature_IgnoresBlankValues()
    {
        var config = PancakeConfig.Create(TenantId, Now);

        config.UpdateSignature("", "", "", Now.AddMinutes(1));

        config.SignatureHeader.Should().Be("x-pancake-signature");
        config.SignatureAlgo.Should().Be("hmac-sha256");
        config.SignatureEncoding.Should().Be("hex");
    }

    [Fact]
    public void UpdateAccessToken_SetsValueAndTimestamp()
    {
        var config = PancakeConfig.Create(TenantId, Now);

        config.UpdateAccessToken("encrypted-token-abc", Now.AddMinutes(2));

        config.AccessTokenEncrypted.Should().Be("encrypted-token-abc");
        config.UpdatedAt.Should().Be(Now.AddMinutes(2));
    }

    [Fact]
    public void UpdateAccessToken_NullBecomesEmpty()
    {
        var config = PancakeConfig.Create(TenantId, Now);

        config.UpdateAccessToken(null!, Now.AddMinutes(1));

        config.AccessTokenEncrypted.Should().BeEmpty();
    }

    [Fact]
    public void UpdateWebhookSecret_SetsValueAndTimestamp()
    {
        var config = PancakeConfig.Create(TenantId, Now);

        config.UpdateWebhookSecret("secret-xyz", Now.AddMinutes(3));

        config.WebhookSecretEncrypted.Should().Be("secret-xyz");
        config.UpdatedAt.Should().Be(Now.AddMinutes(3));
    }

    [Fact]
    public void UpdateWebhookSecret_NullBecomesEmpty()
    {
        var config = PancakeConfig.Create(TenantId, Now);

        config.UpdateWebhookSecret(null!, Now.AddMinutes(1));

        config.WebhookSecretEncrypted.Should().BeEmpty();
    }

    [Fact]
    public void Activate_SetsIsActiveTrue()
    {
        var config = PancakeConfig.Create(TenantId, Now);
        config.Deactivate(Now.AddMinutes(1));

        config.Activate(Now.AddMinutes(2));

        config.IsActive.Should().BeTrue();
        config.UpdatedAt.Should().Be(Now.AddMinutes(2));
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var config = PancakeConfig.Create(TenantId, Now);

        config.Deactivate(Now.AddMinutes(1));

        config.IsActive.Should().BeFalse();
        config.UpdatedAt.Should().Be(Now.AddMinutes(1));
    }
}
