using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

/// <summary>
/// SPEC-11 §Refresh — prune refresh tokens that are expired, or revoked longer than the
/// audit-retention window, so the table does not grow unbounded (one row per F5 rotation).
/// </summary>
public sealed partial class RefreshTokenCleanupJob(AppDbContext db, ILogger<RefreshTokenCleanupJob> logger)
{
    private const int RevokedRetentionDays = 7;

    private readonly AppDbContext _db = db;
    private readonly ILogger<RefreshTokenCleanupJob> _logger = logger;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var revokedCutoff = now.AddDays(-RevokedRetentionDays);
        var removed = await _db.RefreshTokens
            .Where(t => t.ExpiresAt < now || (t.RevokedAt != null && t.RevokedAt < revokedCutoff))
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        LogCleaned(_logger, removed);
    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Information, Message = "RefreshTokenCleanup removed {Count} expired/revoked refresh tokens")]
    private static partial void LogCleaned(ILogger logger, int count);
}
