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
                .Where(i => i.EncryptedAccessToken != null)
                .ToListAsync(ct).ConfigureAwait(false);

            var migrated = 0;
            foreach (var inbox in inboxes)
            {
                var stored = inbox.EncryptedAccessToken!;
                if (PancakeTokenCipher.IsEncrypted(encryptor, stored)) continue;
                inbox.SetAccessToken(encryptor.Encrypt(stored), DateTimeOffset.UtcNow);
                migrated++;
            }

            if (migrated > 0)
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            LogMigrated(logger, migrated, inboxes.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best-effort: never block startup — plaintext rows keep working via DecryptOrRaw until next boot.
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(EventId = 6030, Level = LogLevel.Information, Message = "InboxTokenEncryptionMigrator: encrypted {Migrated} legacy plaintext token(s) of {Total} inbox rows")]
    private static partial void LogMigrated(ILogger logger, int migrated, int total);

    [LoggerMessage(EventId = 6031, Level = LogLevel.Warning, Message = "InboxTokenEncryptionMigrator: failed; plaintext rows still readable via fallback")]
    private static partial void LogFailed(ILogger logger, Exception ex);
}
