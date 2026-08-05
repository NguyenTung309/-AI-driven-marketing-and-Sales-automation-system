using Clawbot.Domain.Channels;
using FluentAssertions;

namespace Clawbot.Infrastructure.Tests;

public sealed class InboxPageTokenMintedAtTests
{
    private static readonly DateTimeOffset Minted =
        new(2026, 7, 30, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SetAccessToken_StampsMintTime()
    {
        // Arrange
        var inbox = Inbox.Create(Guid.NewGuid(), "Page", "pancake", "page-1");

        // Act
        inbox.SetAccessToken("enc:token", Minted);

        // Assert
        inbox.PageTokenMintedAt.Should().Be(Minted);
        inbox.UpdatedAt.Should().Be(Minted);
    }

    [Fact]
    public void Reconnect_StampsMintTimeAndRestoresTheInbox()
    {
        // Arrange
        var inbox = Inbox.Create(Guid.NewGuid(), "Page", "pancake", "page-1");
        inbox.SetAccessToken("enc:old", Minted.AddDays(-3));

        // Act
        inbox.Reconnect("Page renamed", "enc:new", Minted);

        // Assert
        inbox.PageTokenMintedAt.Should().Be(Minted);
        inbox.IsActive.Should().BeTrue();
        inbox.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void UpdateName_LeavesMintTimeUntouched()
    {
        // Arrange
        var inbox = Inbox.Create(Guid.NewGuid(), "Page", "pancake", "page-1");
        inbox.SetAccessToken("enc:token", Minted);
        var renamedAt = Minted.AddDays(5);

        // Act
        inbox.UpdateName("Page renamed", renamedAt);

        // Assert
        inbox.PageTokenMintedAt.Should().Be(Minted);
        inbox.UpdatedAt.Should().Be(renamedAt);
    }

    [Fact]
    public void Create_LeavesMintTimeUnsetUntilATokenIsStored()
    {
        // Arrange & Act
        var inbox = Inbox.Create(Guid.NewGuid(), "Page", "pancake", "page-1");

        // Assert
        inbox.PageTokenMintedAt.Should().BeNull();
    }
}
