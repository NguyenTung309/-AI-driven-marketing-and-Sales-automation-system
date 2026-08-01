using System.Security.Cryptography;
using System.Text;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Persistence;
using Clawbot.Infrastructure.Security;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Tests;

// Bo khoa ma hoa lech (deploy/.env vs appsettings) tung lam token inbox giai ma hong am tham.
// Migrator phai dung han startup thay vi ma hoa chong len ciphertext cu — ghi de kieu do la mat token vinh vien.
public sealed class InboxTokenEncryptionMigratorTests
{
    private const string RawToken = "header.payload.signature";
    private const string LegacyPlaintext = "legacy-page-token";

    [Fact]
    public async Task EncryptLegacyTokensAsync_Throws_AndLeavesTheRowUntouched_WhenLegacyCiphertextUsesAnotherKey()
    {
        // Arrange
        await using var fixture = await MigratorFixture.CreateAsync(readerKeyFill: 0x22);
        var stored = CreateLegacyCiphertext(KeyBytes(0x11), LegacyPlaintext);
        var inboxId = await fixture.InsertInboxAsync(stored);

        // Act
        var act = () => InboxTokenEncryptionMigrator.EncryptLegacyTokensAsync(fixture.Services);

        // Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Be($"inbox_token_encryption_key_mismatch:{inboxId}");
        (await fixture.ReadTokenAsync(inboxId)).Should().Be(stored);
    }

    [Fact]
    public async Task EncryptLegacyTokensAsync_Throws_WhenAuthenticatedCiphertextUsesAnotherKey()
    {
        // Arrange
        await using var fixture = await MigratorFixture.CreateAsync(readerKeyFill: 0x22);
        var stored = CreateEncryptor(0x11).Encrypt(LegacyPlaintext);
        var inboxId = await fixture.InsertInboxAsync(stored);

        // Act
        var act = () => InboxTokenEncryptionMigrator.EncryptLegacyTokensAsync(fixture.Services);

        // Assert
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Be($"inbox_token_encryption_key_mismatch:{inboxId}");
        (await fixture.ReadTokenAsync(inboxId)).Should().Be(stored);
    }

    [Fact]
    public async Task EncryptLegacyTokensAsync_EncryptsRawTokensInPlace()
    {
        // Arrange
        await using var fixture = await MigratorFixture.CreateAsync(readerKeyFill: 0x22);
        var inboxId = await fixture.InsertInboxAsync(RawToken);

        // Act
        await InboxTokenEncryptionMigrator.EncryptLegacyTokensAsync(fixture.Services);

        // Assert
        var stored = await fixture.ReadTokenAsync(inboxId);
        stored.Should().NotBe(RawToken);
        CreateEncryptor(0x22).Decrypt(stored!).Should().Be(RawToken);
    }

    [Fact]
    public async Task EncryptLegacyTokensAsync_IsIdempotent_AndNeverDoubleEncrypts()
    {
        // Arrange
        await using var fixture = await MigratorFixture.CreateAsync(readerKeyFill: 0x22);
        var inboxId = await fixture.InsertInboxAsync(RawToken);

        // Act
        await InboxTokenEncryptionMigrator.EncryptLegacyTokensAsync(fixture.Services);
        var afterFirstPass = await fixture.ReadTokenAsync(inboxId);
        await InboxTokenEncryptionMigrator.EncryptLegacyTokensAsync(fixture.Services);

        // Assert
        var afterSecondPass = await fixture.ReadTokenAsync(inboxId);
        afterSecondPass.Should().Be(afterFirstPass);
        CreateEncryptor(0x22).Decrypt(afterSecondPass!).Should().Be(RawToken);
    }

    private static string CreateLegacyCiphertext(byte[] key, string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = new byte[16];
        using var encryptor = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);

        var blob = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, blob, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, blob, aes.IV.Length, cipher.Length);
        return Convert.ToBase64String(blob);
    }

    private static byte[] KeyBytes(byte fill) => Enumerable.Repeat(fill, 32).ToArray();

    private static AesEncryptor CreateEncryptor(byte fill) =>
        new(Options.Create(new EncryptionOptions
        {
            Base64Key = Convert.ToBase64String(KeyBytes(fill)),
        }));

    private sealed class MigratorFixture(
        SqliteConnection connection,
        ServiceProvider services,
        AppDbContext db) : IAsyncDisposable
    {
        public IServiceProvider Services { get; } = services;

        public static async Task<MigratorFixture> CreateAsync(byte readerKeyFill)
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

            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton(db)
                .AddSingleton<IEncryptor>(CreateEncryptor(readerKeyFill))
                .BuildServiceProvider();

            return new MigratorFixture(connection, services, db);
        }

        public async Task<Guid> InsertInboxAsync(string storedToken)
        {
            var inboxId = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO inboxes (
                    id,
                    tenant_id,
                    name,
                    platform,
                    external_page_id,
                    encrypted_access_token,
                    is_active,
                    created_at,
                    updated_at)
                VALUES (
                    {inboxId},
                    {Guid.NewGuid()},
                    {"Page"},
                    {"pancake"},
                    {"page-1"},
                    {storedToken},
                    {true},
                    {now},
                    {now});
                """);
            return inboxId;
        }

        public async Task<string?> ReadTokenAsync(Guid inboxId) =>
            await db.Inboxes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(i => i.Id == inboxId)
                .Select(i => i.EncryptedAccessToken)
                .SingleAsync();

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
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
