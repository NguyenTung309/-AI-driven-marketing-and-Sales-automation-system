using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Channels;

// Resolver reads the per-channel token from the inbox row (inboxes.encrypted_access_token).
public sealed class PancakePageTokenResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 28, 0, 0, 0, TimeSpan.Zero);

    private static ITenantAccessor AmbientAccessor(Guid tenantId)
    {
        var accessor = Substitute.For<ITenantAccessor>();
        accessor.Current.Returns(new TenantContext(tenantId, "test"));
        return accessor;
    }

    private static async Task SeedInboxAsync(TestAppDb db, string token = "enc:tok_secret")
    {
        var inbox = Inbox.Create(db.TenantId, "My Page", "facebook", "pzl_page_1");
        inbox.SetAccessToken(token, Now);
        db.Db.Inboxes.Add(inbox);
        await db.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task ResolveAsync_ReturnsDecryptedToken_WhenPageConnected()
    {
        // EARS[WHEN a connected page has a stored token THE SYSTEM SHALL return it decrypted for the page-op call]
        using var db = new TestAppDb();
        await SeedInboxAsync(db);

        var sut = new PancakePageTokenResolver(db.Db, FakeEncryptor(), AmbientAccessor(db.TenantId), Substitute.For<Microsoft.Extensions.Logging.ILogger<PancakePageTokenResolver>>());

        var token = await sut.ResolveAsync(db.TenantId, "pzl_page_1", CancellationToken.None);

        token.Should().NotBeNull();
        token!.PageAccessToken.Should().Be("tok_secret");
        token.PageId.Should().Be("pzl_page_1");
        token.Name.Should().Be("My Page");
    }

    [Fact]
    public async Task ResolveAsync_ReturnsRawToken_WhenLegacyPlaintextRow()
    {
        // Legacy rows hold a raw JWT until the startup migrator re-encrypts them — must stay usable.
        using var db = new TestAppDb();
        await SeedInboxAsync(db, token: "raw_jwt_token");

        var encryptor = Substitute.For<IEncryptor>();
        encryptor.Decrypt(Arg.Any<string>()).Returns(_ => throw new FormatException("not base64"));
        var sut = new PancakePageTokenResolver(db.Db, encryptor, AmbientAccessor(db.TenantId), Substitute.For<Microsoft.Extensions.Logging.ILogger<PancakePageTokenResolver>>());

        var token = await sut.ResolveAsync(db.TenantId, "pzl_page_1", CancellationToken.None);

        token.Should().NotBeNull();
        token!.PageAccessToken.Should().Be("raw_jwt_token");
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenPageNotConnected()
    {
        using var db = new TestAppDb();
        var sut = new PancakePageTokenResolver(db.Db, FakeEncryptor(), AmbientAccessor(db.TenantId), Substitute.For<Microsoft.Extensions.Logging.ILogger<PancakePageTokenResolver>>());

        var token = await sut.ResolveAsync(db.TenantId, "pzl_missing", CancellationToken.None);

        token.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenTenantMismatch()
    {
        // EARS[WHEN the requested tenant does not match the ambient tenant THE SYSTEM SHALL refuse to read another
        // tenant's page token (tenant isolation)]
        using var db = new TestAppDb();
        await SeedInboxAsync(db);

        var sut = new PancakePageTokenResolver(db.Db, FakeEncryptor(), AmbientAccessor(db.TenantId), Substitute.For<Microsoft.Extensions.Logging.ILogger<PancakePageTokenResolver>>());

        var token = await sut.ResolveAsync(Guid.NewGuid(), "pzl_page_1", CancellationToken.None);

        token.Should().BeNull();
    }

    private static IEncryptor FakeEncryptor()
    {
        var e = Substitute.For<IEncryptor>();
        e.Decrypt(Arg.Any<string>()).Returns(ci => ((string)ci[0]!).Replace("enc:", string.Empty));
        return e;
    }
}
