using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Persistence;
using Clawbot.Infrastructure.Security;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests;

public sealed class PancakePageTokenServiceTests
{
    [Fact]
    public async Task StorePageTokenDirectAsync_UpdatesActiveCanonicalInbox_WhenHistoricalDuplicateExists()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
        var encryptor = CreateEncryptor();
        var historical = Inbox.Create(tenantId, "Historical", "facebook", "page-1");
        SetProperty(historical, nameof(Inbox.Id), Guid.Parse("00000000-0000-0000-0000-000000000001"));
        historical.SetAccessToken(encryptor.Encrypt("historical-token"), now.AddDays(-2));
        SetProperty(historical, nameof(Inbox.IsActive), false);
        SetProperty(historical, nameof(Inbox.DeletedAt), now.AddDays(-1));
        SetProperty(historical, nameof(Inbox.CreatedAt), now.AddDays(-3));
        var active = Inbox.Create(tenantId, "Active", "facebook", "page-1");
        SetProperty(active, nameof(Inbox.Id), Guid.Parse("00000000-0000-0000-0000-000000000002"));
        active.SetAccessToken(encryptor.Encrypt("active-token"), now.AddDays(-1));
        SetProperty(active, nameof(Inbox.CreatedAt), now.AddDays(-2));
        fixture.Db.Inboxes.AddRange(historical, active);
        await fixture.Db.SaveChangesAsync();
        var service = CreateService(fixture.Db, encryptor, now);

        // Act
        await service.StorePageTokenDirectAsync(
            tenantId,
            "page-1",
            "Updated active",
            "facebook",
            "replacement-token");

        // Assert
        fixture.Db.ChangeTracker.Clear();
        var rows = (await fixture.Db.Inboxes
            .IgnoreQueryFilters()
            .Where(inbox => inbox.TenantId == tenantId
                && inbox.Platform == "facebook"
                && inbox.ExternalPageId == "page-1")
            .ToListAsync())
            .OrderBy(inbox => inbox.CreatedAt)
            .ToList();
        rows.Should().HaveCount(2);
        rows[0].IsActive.Should().BeFalse();
        PancakeTokenCipher.DecryptOrRaw(encryptor, rows[0].EncryptedAccessToken!).Should().Be("historical-token");
        rows[1].IsActive.Should().BeTrue();
        rows[1].Name.Should().Be("Updated active");
        PancakeTokenCipher.DecryptOrRaw(encryptor, rows[1].EncryptedAccessToken!).Should().Be("replacement-token");
    }

    [Fact]
    public async Task MintAndStoreAsync_NormalizesCanonicalIdentityBeforeMintAndPersistence()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
        var encryptor = CreateEncryptor();
        var gateway = new RecordingMintGateway("minted-page-token");
        var service = CreateService(fixture.Db, encryptor, now, gateway);

        // Act
        var result = await service.MintAndStoreAsync(
            tenantId,
            " page-1 ",
            " Page name ",
            " Facebook ",
            "user-token");

        // Assert
        gateway.PageIds.Should().Equal("page-1");
        result.PageId.Should().Be("page-1");
        result.Platform.Should().Be("facebook");
        var inbox = await fixture.Db.Inboxes.IgnoreQueryFilters().SingleAsync();
        inbox.ExternalPageId.Should().Be("page-1");
        inbox.Platform.Should().Be("facebook");
        inbox.Name.Should().Be("Page name");
        PancakeTokenCipher.DecryptOrRaw(encryptor, inbox.EncryptedAccessToken!).Should().Be("minted-page-token");
    }

    [Fact]
    public async Task EnsureMintedAsync_ForwardsFullCanonicalIdentityToResolver()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
        var encryptor = CreateEncryptor();
        var resolver = Substitute.For<IPancakePageTokenResolver>();
        var expected = new PancakePageToken(
            "page-token",
            "page-1",
            "Page",
            "instagram");
        resolver.ResolveAsync(
                tenantId,
                "instagram",
                "page-1",
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = CreateService(
            fixture.Db,
            encryptor,
            now,
            resolver: resolver);

        // Act
        var result = await service.EnsureMintedAsync(
            tenantId,
            "instagram",
            "page-1");

        // Assert
        result.Should().BeSameAs(expected);
        await resolver.Received(1).ResolveAsync(
            tenantId,
            "instagram",
            "page-1",
            Arg.Any<CancellationToken>());
    }

    private static PancakePageTokenService CreateService(
        AppDbContext db,
        AesEncryptor encryptor,
        DateTimeOffset now,
        IPageTokenMintGateway? gateway = null,
        IPancakePageTokenResolver? resolver = null)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        return new PancakePageTokenService(
            db,
            encryptor,
            resolver ?? Substitute.For<IPancakePageTokenResolver>(),
            gateway ?? Substitute.For<IPageTokenMintGateway>(),
            clock,
            NullLogger<PancakePageTokenService>.Instance);
    }

    private static AesEncryptor CreateEncryptor() =>
        new(Options.Create(new EncryptionOptions
        {
            Base64Key = Convert.ToBase64String(Enumerable.Repeat((byte)0x42, 32).ToArray()),
        }));

    private static void SetProperty<T>(Inbox inbox, string propertyName, T value) =>
        typeof(Inbox).GetProperty(propertyName)!.SetValue(inbox, value);

    private sealed class RecordingMintGateway(string token) : IPageTokenMintGateway
    {
        public List<string> PageIds { get; } = [];

        public Task<string> MintAsync(
            string userAccessToken,
            string pageId,
            CancellationToken ct = default)
        {
            PageIds.Add(pageId);
            return Task.FromResult(token);
        }
    }

    private sealed class SqliteFixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;

        public static async Task<SqliteFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE inboxes (
                    id TEXT NOT NULL PRIMARY KEY,
                    tenant_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    platform TEXT NOT NULL,
                    external_page_id TEXT NOT NULL,
                    avatar_url TEXT NULL,
                    encrypted_access_token TEXT NULL,
                    encrypted_refresh_token TEXT NULL,
                    encrypted_webhook_secret TEXT NULL,
                    token_expires_at TEXT NULL,
                    page_token_minted_at TEXT NULL,
                    sender_id TEXT NULL,
                    is_active INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    deleted_at TEXT NULL
                );
                CREATE UNIQUE INDEX UX_inboxes_tenant_platform_external_active
                    ON inboxes (tenant_id, platform, external_page_id)
                    WHERE is_active = 1 AND deleted_at IS NULL;
                CREATE INDEX ix_test_inboxes_identity_all
                    ON inboxes (tenant_id, platform, external_page_id, is_active, created_at, id);
                """);
            return new SqliteFixture(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;
        public TenantContext Require() => throw new InvalidOperationException("No tenant in unit test scope.");
    }
}
