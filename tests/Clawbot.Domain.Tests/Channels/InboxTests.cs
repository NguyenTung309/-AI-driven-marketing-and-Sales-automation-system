using Clawbot.Domain.Channels;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Channels;

public sealed class InboxTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // ── Create ────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsInitialDefaults()
    {
        var inbox = Inbox.Create(TenantId, "My Page", "facebook", "page-123");

        inbox.TenantId.Should().Be(TenantId);
        inbox.Name.Should().Be("My Page");
        inbox.Platform.Should().Be("facebook");
        inbox.ExternalPageId.Should().Be("page-123");
        inbox.IsActive.Should().BeTrue();
        inbox.AvatarUrl.Should().BeNull();
        inbox.EncryptedAccessToken.Should().BeNull();
        inbox.EncryptedRefreshToken.Should().BeNull();
        inbox.EncryptedWebhookSecret.Should().BeNull();
        inbox.TokenExpiresAt.Should().BeNull();
        inbox.PageTokenMintedAt.Should().BeNull();
        inbox.SenderId.Should().BeNull();
        inbox.DeletedAt.Should().BeNull();
    }

    // ── UpdateName ────────────────────────────────────────────────────

    [Fact]
    public void UpdateName_SetsNameAndUpdatedAt()
    {
        var inbox = Inbox.Create(TenantId, "Old Name", "facebook", "p1");
        var at = DateTimeOffset.UtcNow.AddMinutes(5);

        inbox.UpdateName("New Name", at);

        inbox.Name.Should().Be("New Name");
        inbox.UpdatedAt.Should().Be(at);
    }

    [Fact]
    public void UpdateName_ThrowsOnNull()
    {
        var inbox = Inbox.Create(TenantId, "Name", "facebook", "p1");

        var act = () => inbox.UpdateName(null!, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── SetSenderId ───────────────────────────────────────────────────

    [Fact]
    public void SetSenderId_SetsValue()
    {
        var inbox = Inbox.Create(TenantId, "Page", "facebook", "p1");

        inbox.SetSenderId("sender-abc");

        inbox.SenderId.Should().Be("sender-abc");
    }

    // ── SetAccessToken ────────────────────────────────────────────────

    [Fact]
    public void SetAccessToken_SetsTokenAndMintedAt()
    {
        var inbox = Inbox.Create(TenantId, "Page", "facebook", "p1");
        var at = DateTimeOffset.UtcNow.AddMinutes(5);

        inbox.SetAccessToken("enc-token", at);

        inbox.EncryptedAccessToken.Should().Be("enc-token");
        inbox.PageTokenMintedAt.Should().Be(at);
        inbox.UpdatedAt.Should().Be(at);
    }

    // ── Reconnect ─────────────────────────────────────────────────────

    [Fact]
    public void Reconnect_RestoresActiveStateAndToken()
    {
        var inbox = Inbox.Create(TenantId, "Old", "facebook", "p1");
        var at = DateTimeOffset.UtcNow.AddMinutes(10);

        inbox.Reconnect("Reconnected Page", "new-enc-token", at);

        inbox.Name.Should().Be("Reconnected Page");
        inbox.EncryptedAccessToken.Should().Be("new-enc-token");
        inbox.PageTokenMintedAt.Should().Be(at);
        inbox.IsActive.Should().BeTrue();
        inbox.DeletedAt.Should().BeNull();
        inbox.UpdatedAt.Should().Be(at);
    }

    [Fact]
    public void Reconnect_ThrowsOnNullName()
    {
        var inbox = Inbox.Create(TenantId, "Page", "facebook", "p1");

        var act = () => inbox.Reconnect(null!, "token", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Reconnect_ThrowsOnNullToken()
    {
        var inbox = Inbox.Create(TenantId, "Page", "facebook", "p1");

        var act = () => inbox.Reconnect("Name", null!, DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentNullException>();
    }
}
