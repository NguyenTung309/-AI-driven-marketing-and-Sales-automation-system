using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Content;

public sealed class SocialCredentialTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_NormalizesProviderToLower()
    {
        var cred = SocialCredential.Create(TenantId, "META", "encrypted-blob", Now, "page-123");

        cred.Provider.Should().Be("meta");
        cred.PageId.Should().Be("page-123");
        cred.CredentialsEncrypted.Should().Be("encrypted-blob");
        cred.IsActive.Should().BeTrue();
        cred.CreatedAt.Should().Be(Now);
        cred.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_NullPageId_SetsNull()
    {
        var cred = SocialCredential.Create(TenantId, "zalo", "enc", Now);

        cred.PageId.Should().BeNull();
    }

    [Fact]
    public void Create_BlankPageId_SetsNull()
    {
        var cred = SocialCredential.Create(TenantId, "zalo", "enc", Now, "   ");

        cred.PageId.Should().BeNull();
    }

    [Fact]
    public void Create_NullEncrypted_DefaultsToEmpty()
    {
        var cred = SocialCredential.Create(TenantId, "fb", null!, Now);

        cred.CredentialsEncrypted.Should().BeEmpty();
    }

    [Fact]
    public void Create_WithFixedId_UsesProvidedId()
    {
        var fixedId = Guid.NewGuid();

        var cred = SocialCredential.Create(fixedId, TenantId, "fb", "enc", Now);

        cred.Id.Should().Be(fixedId);
    }

    [Fact]
    public void UpdateCredentials_ChangesBlobAndTimestamp()
    {
        var cred = SocialCredential.Create(TenantId, "fb", "old", Now);

        cred.UpdateCredentials("new-enc", Now.AddHours(1));

        cred.CredentialsEncrypted.Should().Be("new-enc");
        cred.UpdatedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void UpdateCredentials_NullBecomesEmpty()
    {
        var cred = SocialCredential.Create(TenantId, "fb", "enc", Now);

        cred.UpdateCredentials(null!, Now.AddMinutes(1));

        cred.CredentialsEncrypted.Should().BeEmpty();
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var cred = SocialCredential.Create(TenantId, "fb", "enc", Now);

        cred.Deactivate(Now.AddMinutes(1));

        cred.IsActive.Should().BeFalse();
        cred.UpdatedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void Activate_SetsIsActiveTrue()
    {
        var cred = SocialCredential.Create(TenantId, "fb", "enc", Now);
        cred.Deactivate(Now.AddMinutes(1));

        cred.Activate(Now.AddMinutes(2));

        cred.IsActive.Should().BeTrue();
        cred.UpdatedAt.Should().Be(Now.AddMinutes(2));
    }
}
