using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Channels;

public sealed class PancakePageTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task MintAndStoreAsync_CreatesRow_WithEncryptedTokenAndMintedAt()
    {
        // EARS[WHEN no stored page token exists THE SYSTEM SHALL mint one, persist it encrypted, and stamp mintedAt]
        using var db = new TestAppDb();
        var gateway = Substitute.For<IPageTokenMintGateway>();
        gateway.MintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("pgt_123");
        var encryptor = FakeEncryptor();
        var resolver = Substitute.For<IPancakePageTokenResolver>();
        var sut = new PancakePageTokenService(db.Db, encryptor, resolver, gateway, new FixedClock(Now), Substitute.For<Microsoft.Extensions.Logging.ILogger<PancakePageTokenService>>());

        var token = await sut.MintAndStoreAsync(db.TenantId, "pzl_page_1", "My Page", "facebook", "user_tok", CancellationToken.None);

        token.PageAccessToken.Should().Be("pgt_123");
        token.PageId.Should().Be("pzl_page_1");
        var row = await db.Db.PancakePages.IgnoreQueryFilters().SingleAsync();
        row.PageAccessTokenEncrypted.Should().Be("enc:pgt_123");
        row.PageTokenMintedAt.Should().Be(Now);
        row.Name.Should().Be("My Page");
        row.Platform.Should().Be("facebook");
        row.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task MintAndStoreAsync_UpdatesExistingRow_WithoutDuplicate()
    {
        // EARS[WHEN a row already exists for (tenant, page) THE SYSTEM SHALL overwrite its token (mint invalidates
        // the prior token) without creating a duplicate row]
        using var db = new TestAppDb();
        var existing = PancakePage.Create(db.TenantId, "pzl_page_1", "Old Name", "facebook", Now.AddDays(-1));
        existing.StorePageAccessToken("enc:pgt_old", Now.AddDays(-1));
        db.Db.PancakePages.Add(existing);
        await db.Db.SaveChangesAsync();

        var gateway = Substitute.For<IPageTokenMintGateway>();
        gateway.MintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("pgt_new");
        var sut = new PancakePageTokenService(db.Db, FakeEncryptor(), Substitute.For<IPancakePageTokenResolver>(),
            gateway, new FixedClock(Now), Substitute.For<Microsoft.Extensions.Logging.ILogger<PancakePageTokenService>>());

        await sut.MintAndStoreAsync(db.TenantId, "pzl_page_1", "New Name", "facebook", "user_tok", CancellationToken.None);

        var rows = await db.Db.PancakePages.IgnoreQueryFilters().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].PageAccessTokenEncrypted.Should().Be("enc:pgt_new");
        rows[0].Name.Should().Be("New Name");
        rows[0].PageTokenMintedAt.Should().Be(Now);
    }

    [Fact]
    public async Task MintAndStoreAsync_ReactivatesInactiveRow()
    {
        using var db = new TestAppDb();
        var existing = PancakePage.Create(db.TenantId, "pzl_page_1", "Page", "facebook", Now.AddDays(-1));
        existing.StorePageAccessToken("enc:pgt_old", Now.AddDays(-1));
        existing.Deactivate(Now.AddDays(-1));
        db.Db.PancakePages.Add(existing);
        await db.Db.SaveChangesAsync();

        var gateway = Substitute.For<IPageTokenMintGateway>();
        gateway.MintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("pgt_fresh");
        var sut = new PancakePageTokenService(db.Db, FakeEncryptor(), Substitute.For<IPancakePageTokenResolver>(),
            gateway, new FixedClock(Now), Substitute.For<Microsoft.Extensions.Logging.ILogger<PancakePageTokenService>>());

        await sut.MintAndStoreAsync(db.TenantId, "pzl_page_1", "Page", "facebook", "user_tok", CancellationToken.None);

        var row = await db.Db.PancakePages.IgnoreQueryFilters().SingleAsync();
        row.IsActive.Should().BeTrue();
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

        var row = await db.Db.PancakePages.IgnoreQueryFilters().SingleAsync();
        row.PageAccessTokenEncrypted.Should().Be("enc:pgt_from_env");
        row.PageId.Should().Be("pzl_page_1");
        row.IsActive.Should().BeTrue();
        // The mint gateway must never be called on the direct-store path.
        await gateway.DidNotReceive().MintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StorePageTokenDirectAsync_OverwritesExistingRow()
    {
        using var db = new TestAppDb();
        var existing = PancakePage.Create(db.TenantId, "pzl_page_1", "Old", "pancake", Now.AddDays(-1));
        existing.StorePageAccessToken("enc:pgt_old", Now.AddDays(-1));
        db.Db.PancakePages.Add(existing);
        await db.Db.SaveChangesAsync();
        var sut = new PancakePageTokenService(db.Db, FakeEncryptor(), Substitute.For<IPancakePageTokenResolver>(),
            Substitute.For<IPageTokenMintGateway>(), new FixedClock(Now), Substitute.For<Microsoft.Extensions.Logging.ILogger<PancakePageTokenService>>());

        await sut.StorePageTokenDirectAsync(db.TenantId, "pzl_page_1", "New", "pancake", "pgt_new", CancellationToken.None);

        var rows = await db.Db.PancakePages.IgnoreQueryFilters().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].PageAccessTokenEncrypted.Should().Be("enc:pgt_new");
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
