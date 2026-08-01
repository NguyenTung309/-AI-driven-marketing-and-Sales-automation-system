using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Persistence;
using Clawbot.Infrastructure.Security;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Tests;

public sealed class PancakePageTokenResolverTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsRequestedPlatformToken_WhenPageIdExistsOnMultiplePlatforms()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var encryptor = CreateEncryptor();
        var facebook = CreateInbox(tenantId, "facebook", "shared-page", "facebook-token", encryptor);
        var instagram = CreateInbox(tenantId, "instagram", "shared-page", "instagram-token", encryptor);
        fixture.Db.Inboxes.AddRange(facebook, instagram);
        await fixture.Db.SaveChangesAsync();
        var resolver = CreateResolver(fixture.Db, encryptor);

        // Act
        var facebookToken = await resolver.ResolveAsync(
            tenantId,
            "facebook",
            "shared-page");
        var instagramToken = await resolver.ResolveAsync(
            tenantId,
            "instagram",
            "shared-page");

        // Assert
        facebookToken.Should().NotBeNull();
        facebookToken!.PageAccessToken.Should().Be("facebook-token");
        facebookToken.Platform.Should().Be("facebook");
        instagramToken.Should().NotBeNull();
        instagramToken!.PageAccessToken.Should().Be("instagram-token");
        instagramToken.Platform.Should().Be("instagram");
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenOnlyAnotherPlatformHasPageId()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var encryptor = CreateEncryptor();
        fixture.Db.Inboxes.Add(
            CreateInbox(tenantId, "facebook", "shared-page", "facebook-token", encryptor));
        await fixture.Db.SaveChangesAsync();
        var resolver = CreateResolver(fixture.Db, encryptor);

        // Act
        var result = await resolver.ResolveAsync(
            tenantId,
            "instagram",
            "shared-page");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenFullCanonicalIdentityIsAmbiguous()
    {
        // Arrange
        await using var fixture = await SqliteFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        var encryptor = CreateEncryptor();
        fixture.Db.Inboxes.AddRange(
            CreateInbox(tenantId, "facebook", "shared-page", "first-token", encryptor),
            CreateInbox(tenantId, "facebook", "shared-page", "second-token", encryptor));
        await fixture.Db.SaveChangesAsync();
        var resolver = CreateResolver(fixture.Db, encryptor);

        // Act
        var result = await resolver.ResolveAsync(
            tenantId,
            "facebook",
            "shared-page");

        // Assert
        result.Should().BeNull();
    }

    private static PancakePageTokenResolver CreateResolver(
        AppDbContext db,
        AesEncryptor encryptor) =>
        new(
            db,
            encryptor,
            new NullTenantAccessor(),
            NullLogger<PancakePageTokenResolver>.Instance);

    private static Inbox CreateInbox(
        Guid tenantId,
        string platform,
        string pageId,
        string token,
        AesEncryptor encryptor)
    {
        var inbox = Inbox.Create(tenantId, $"{platform} inbox", platform, pageId);
        inbox.SetAccessToken(encryptor.Encrypt(token), DateTimeOffset.UtcNow);
        return inbox;
    }

    private static AesEncryptor CreateEncryptor() =>
        new(Options.Create(new EncryptionOptions
        {
            Base64Key = Convert.ToBase64String(Enumerable.Repeat((byte)0x42, 32).ToArray()),
        }));

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
        public TenantContext Require() =>
            throw new InvalidOperationException("No tenant in unit test scope.");
    }
}
