using Clawbot.Domain.Channels;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Inboxes;

public sealed class InboxTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_sets_properties_correctly()
    {
        var inbox = Inbox.Create(TenantId, "FB Page", "facebook", "page123");

        inbox.TenantId.Should().Be(TenantId);
        inbox.Name.Should().Be("FB Page");
        inbox.Platform.Should().Be("facebook");
        inbox.ExternalPageId.Should().Be("page123");
        inbox.IsActive.Should().BeTrue();
        inbox.EncryptedAccessToken.Should().BeNull();
        inbox.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void SetAccessToken_stores_token_and_updates_updatedAt()
    {
        var inbox = Inbox.Create(TenantId, "Test", "zalo", "zaoid");
        var before = inbox.UpdatedAt;

        inbox.SetAccessToken("encrypted-token-xyz", DateTimeOffset.UtcNow);

        inbox.EncryptedAccessToken.Should().Be("encrypted-token-xyz");
        inbox.UpdatedAt.Should().BeAfter(before);
    }
}
