using Clawbot.Domain.Contacts;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Contacts;

public sealed class ContactTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Contact CreateDefault() => Contact.Create(TenantId, "Nguyen Van A", Now);

    // ── Create ────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsInitialDefaults()
    {
        var contact = CreateDefault();

        contact.TenantId.Should().Be(TenantId);
        contact.DisplayName.Should().Be("Nguyen Van A");
        contact.Phone.Should().BeNull();
        contact.Email.Should().BeNull();
        contact.Locale.Should().Be("vi-VN");
        contact.LifetimeScore.Should().Be(0);
        contact.LifecycleStage.Should().Be("visitor");
        contact.AvatarUrl.Should().BeNull();
        contact.CreatedAt.Should().Be(Now);
        contact.DeletedAt.Should().BeNull();
        contact.ExternalIds.Should().BeEmpty();
    }

    // ── UpdateAvatar ──────────────────────────────────────────────────

    [Fact]
    public void UpdateAvatar_SetsAvatarUrl()
    {
        var contact = CreateDefault();

        contact.UpdateAvatar("https://avatar.png", Now.AddMinutes(1));

        contact.AvatarUrl.Should().Be("https://avatar.png");
    }

    [Fact]
    public void UpdateAvatar_ClearsWithNull()
    {
        var contact = CreateDefault();
        contact.UpdateAvatar("https://avatar.png", Now);

        contact.UpdateAvatar(null, Now.AddMinutes(1));

        contact.AvatarUrl.Should().BeNull();
    }

    // ── LinkExternalId ────────────────────────────────────────────────

    [Fact]
    public void LinkExternalId_AddsNewPlatformId()
    {
        var contact = CreateDefault();

        contact.LinkExternalId("facebook", "fb-123", Now);

        contact.ExternalIds.Should().ContainSingle();
        contact.ExternalIds.First().Platform.Should().Be("facebook");
        contact.ExternalIds.First().ExternalId.Should().Be("fb-123");
    }

    [Fact]
    public void LinkExternalId_DeduplicatesSamePlatformAndId()
    {
        var contact = CreateDefault();
        contact.LinkExternalId("facebook", "fb-123", Now);

        contact.LinkExternalId("facebook", "fb-123", Now.AddMinutes(5));

        contact.ExternalIds.Should().ContainSingle();
    }

    [Fact]
    public void LinkExternalId_AllowsMultiplePlatforms()
    {
        var contact = CreateDefault();

        contact.LinkExternalId("facebook", "fb-123", Now);
        contact.LinkExternalId("zalo", "zalo-456", Now);

        contact.ExternalIds.Should().HaveCount(2);
    }

    // ── UpdateDisplayName ─────────────────────────────────────────────

    [Fact]
    public void UpdateDisplayName_UpdatesWhenNonEmpty()
    {
        var contact = CreateDefault();

        contact.UpdateDisplayName("Tran Thi B");

        contact.DisplayName.Should().Be("Tran Thi B");
    }

    [Fact]
    public void UpdateDisplayName_IgnoresEmptyString()
    {
        var contact = CreateDefault();

        contact.UpdateDisplayName("");

        contact.DisplayName.Should().Be("Nguyen Van A");
    }

    [Fact]
    public void UpdateDisplayName_IgnoresWhitespace()
    {
        var contact = CreateDefault();

        contact.UpdateDisplayName("   ");

        contact.DisplayName.Should().Be("Nguyen Van A");
    }
}
