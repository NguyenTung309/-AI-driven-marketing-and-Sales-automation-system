using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

// One-shot startup repair for chat sessions orphaned by a previous process crash/restart.
// Live requests have a 90s timeout; five minutes leaves enough margin before declaring them stale.
public sealed partial class ChatSessionRecoveryService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<ChatSessionRecoveryService> logger) : IHostedService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var recovered = await RecoverAsync(db, clock, cancellationToken).ConfigureAwait(false);
            if (recovered > 0)
                LogRecovered(logger, recovered);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal host shutdown during startup.
        }
        catch (Exception ex)
        {
            // Recovery is best-effort and must not prevent AgentService from starting.
            LogRecoveryFailed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task<int> RecoverAsync(
        AppDbContext db,
        IClock clock,
        CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var cutoff = now.Subtract(StaleAfter);
        // DateTimeOffset comparison is not translated by the SQLite test provider. First narrow by
        // indexed/string columns in SQL, then apply the small age filter in memory.
        var runningChatSessions = await db.AgentSessions
            .IgnoreQueryFilters()
            .Where(s => s.Status == AgentSessionStatuses.Running && s.Goal == "chat-reply")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var stale = runningChatSessions
            .Where(s => s.StartedAt < cutoff)
            .ToList();

        foreach (var session in stale)
        {
            session.AppendTrace(
                "chat",
                "chat-agent",
                "recovered_timeout",
                "Marked failed on AgentService startup after exceeding the chat-reply timeout.",
                now);
            session.Fail(now);
        }

        if (stale.Count > 0)
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return stale.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Recovered {Count} stale chat-reply agent session(s) as failed")]
    private static partial void LogRecovered(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Failed to recover stale chat-reply agent sessions")]
    private static partial void LogRecoveryFailed(ILogger logger, Exception ex);
}
