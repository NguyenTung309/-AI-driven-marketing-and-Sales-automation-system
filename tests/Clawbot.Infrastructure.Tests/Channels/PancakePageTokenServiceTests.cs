using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Channels;

// Tokens land on the inbox row (inboxes.encrypted_access_token) — the single per-channel store.
public sealed class PancakePageTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MintAndStoreAsync_CreatesInbox_WithEncryptedToken()
    {
        // EARS[WHEN no inbox exists for (tenant, page) THE SYSTEM SHALL mint a token and create the inbox with it]
        using var db = new TestAppDb();
        var gateway = Substitute.For<IPageTokenMintGateway>();
        gateway.MintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("pgt_123");
        var sut = new PancakePageTokenService(db.Db, FakeEncryptor(), Substitute.For<IPancakePageTokenResolver>(),
            gateway, new FixedClock(Now), Substitute.For<Microsoft.Extensions.Logging.ILogger<PancakePageTokenService>>());

        var token = await sut.MintAndStoreAsync(db.TenantId, "pzl_page_1", "My Page", "facebook", "user_tok", CancellationToken.None);

        token.PageAccessToken.Should().Be("pgt_123");
        token.PageId.Should().Be("pzl_page_1");
        var row = await db.Db.Inboxes.IgnoreQueryFilters().SingleAsync();
        row.ExternalPageId.Should().Be("pzl_page_1");
        row.EncryptedAccessToken.Should().Be("enc:pgt_123");
        row.Name.Should().Be("My Page");
        row.Platform.Should().Be("facebook");
        row.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task MintAndStoreAsync_UpdatesExistingInbox_WithoutDuplicate()
    {
        // EARS[WHEN an inbox already exists for (tenant, page) THE SYSTEM SHALL overwrite its token (mint
        // invalidates the prior token) without creating a duplicate row]
        using var db = new TestAppDb();
        var existing = Inbox.Create(db.TenantId, "Old Name", "facebook", "pzl_page_1");
        existing.SetAccessToken("enc:pgt_old", Now.AddDays(-1));
        db.Db.Inboxes.Add(existing);
        await db.Db.SaveChangesAsync();

        var gateway = Substitute.For<IPageTokenMintGateway>();
        gateway.MintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("pgt_new");
        var sut = new PancakePageTokenService(db.Db, FakeEncryptor(), Substitute.For<IPancakePageTokenResolver>(),
            gateway, new FixedClock(Now), Substitute.For<Microsoft.Extensions.Logging.ILogger<PancakePageTokenService>>());

        await sut.MintAndStoreAsync(db.TenantId, "pzl_page_1", "New Name", "facebook", "user_tok", CancellationToken.None);

        var rows = await db.Db.Inboxes.IgnoreQueryFilters().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].EncryptedAccessToken.Should().Be("enc:pgt_new");
        rows[0].Name.Should().Be("New Name");
    }

    [Fact]
    public async Task StorePageTokenDirectAsync_StoresEncrypted_WithoutMinting()
    {
        // EARS[WHEN a page token is stored directly (env bootstrap) THE SYSTEM SHALL encrypt + persist it without
        // calling the mint gateway]
        using var db = new TestAppDb();
        var gateway = Substitute.For<IPageTokenMintGateway>();
        var sut = new PancakePageTokenService(db.Db, FakeEncryptor(), Substitute.For<IPancakePageTokenResolver>(),
            gateway, new FixedClock(Now), Substitute.For<Microsoft.Extensions.Logging.ILogger<PancakePageTokenService>>());

        await sut.StorePageTokenDirectAsync(db.TenantId, "pzl_page_1", "Bootstrapped", "pancake", "pgt_from_env", CancellationToken.None);

        var row = await db.Db.Inboxes.IgnoreQueryFilters().SingleAsync();
        row.EncryptedAccessToken.Should().Be("enc:pgt_from_env");
        row.ExternalPageId.Should().Be("pzl_page_1");
        row.IsActive.Should().BeTrue();
        // The mint gateway must never be called on the direct-store path.
        await gateway.DidNotReceive().MintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StorePageTokenDirectAsync_OverwritesExistingInbox()
    {
        using var db = new TestAppDb();
        var existing = Inbox.Create(db.TenantId, "Old", "pancake", "pzl_page_1");
        existing.SetAccessToken("enc:pgt_old", Now.AddDays(-1));
        db.Db.Inboxes.Add(existing);
        await db.Db.SaveChangesAsync();
        var sut = new PancakePageTokenService(db.Db, FakeEncryptor(), Substitute.For<IPancakePageTokenResolver>(),
            Substitute.For<IPageTokenMintGateway>(), new FixedClock(Now), Substitute.For<Microsoft.Extensions.Logging.ILogger<PancakePageTokenService>>());

        await sut.StorePageTokenDirectAsync(db.TenantId, "pzl_page_1", "New", "pancake", "pgt_new", CancellationToken.None);

        var rows = await db.Db.Inboxes.IgnoreQueryFilters().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].EncryptedAccessToken.Should().Be("enc:pgt_new");
        rows[0].Name.Should().Be("New");
    }

    private static IEncryptor FakeEncryptor()
    {
        var e = Substitute.For<IEncryptor>();
        e.Encrypt(Arg.Any<string>()).Returns(ci => "enc:" + (string)ci[0]!);
        e.Decrypt(Arg.Any<string>()).Returns(ci => ((string)ci[0]!).Replace("enc:", string.Empty));
        return e;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
