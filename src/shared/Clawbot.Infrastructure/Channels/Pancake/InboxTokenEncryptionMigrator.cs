using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Channels.Pancake;

// One-off startup pass: inbox tokens written before the encrypt-at-write fix are raw JWTs
// sitting in inboxes.encrypted_access_token. Re-encrypt them in place. Idempotent — already
// encrypted rows decrypt fine and are skipped. Cannot be a SQL migration: the AES key lives
// in app config only.
public static partial class InboxTokenEncryptionMigrator
{
    public static async Task EncryptLegacyTokensAsync(IServiceProvider services, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var encryptor = sp.GetRequiredService<IEncryptor>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("InboxTokenEncryptionMigrator");

        try
        {
            var inboxes = await db.Inboxes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(i => i.EncryptedAccessToken != null)
                .Select(i => new { i.Id, i.EncryptedAccessToken })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            // Chot mot moc thoi gian cho ca luot: SetProperty nhan tham so thay vi bieu thuc phu thuoc
            // provider (SQLite khong dich duoc DateTimeOffset.UtcNow), va moi dong migrate cung mot moc.
            var migratedAt = DateTimeOffset.UtcNow;
            var migrated = 0;
            foreach (var inbox in inboxes)
            {
                var stored = inbox.EncryptedAccessToken!;
                if (PancakeTokenCipher.IsEncrypted(encryptor, stored))
                    continue;

                if (PancakeTokenCipher.HasAuthenticatedEnvelope(stored)
                    || PancakeTokenCipher.HasLegacyCiphertextEnvelope(stored))
                {
                    throw new InvalidOperationException(
                        $"inbox_token_encryption_key_mismatch:{inbox.Id}");
                }

                var encrypted = encryptor.Encrypt(stored);
                var updated = await db.Inboxes
                    .IgnoreQueryFilters()
                    .Where(i => i.Id == inbox.Id && i.EncryptedAccessToken == stored)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(i => i.EncryptedAccessToken, encrypted)
                            .SetProperty(i => i.UpdatedAt, migratedAt),
                        ct)
                    .ConfigureAwait(false);
                migrated += updated;
            }

            LogMigrated(logger, migrated, inboxes.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex);
            throw;
        }
    }

    [LoggerMessage(EventId = 6030, Level = LogLevel.Information, Message = "InboxTokenEncryptionMigrator: encrypted {Migrated} legacy plaintext token(s) of {Total} inbox rows")]
    private static partial void LogMigrated(ILogger logger, int migrated, int total);

    [LoggerMessage(EventId = 6031, Level = LogLevel.Critical, Message = "InboxTokenEncryptionMigrator: failed; startup must stop to avoid credential corruption")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
