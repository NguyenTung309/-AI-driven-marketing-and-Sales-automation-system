using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Inbox;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class IdleConversationAlertJob(
    AppDbContext db,
    IInboxNotifier notifier,
    ILogger<IdleConversationAlertJob> logger)
{
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(5);

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - IdleThreshold;

        var idleConversations = await db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.Status == "open"
                && c.LastMessageAt != null
                && c.LastMessageAt < cutoff
                && c.LastMessageAt > DateTimeOffset.UtcNow.AddHours(-4))
            .Select(c => new { c.Id, c.TenantId, c.AssignedTo })
            .Take(30)
            .ToListAsync(ct);

        if (idleConversations.Count == 0)
        {
            LogSkipped(logger, "no idle conversations");
            return;
        }

        LogAlerting(logger, idleConversations.Count);

        foreach (var conv in idleConversations)
        {
            await notifier.NotifyMessageAsync(conv.TenantId, new InboxMessageEvent(
                conv.Id, Guid.Empty, "system", "system",
                "Cuộc trò chuyện đã không hoạt động hơn 5 phút. Vui lòng kiểm tra.",
                "text", DateTimeOffset.UtcNow), ct);
        }
    }

    [LoggerMessage(EventId = 12001, Level = LogLevel.Debug,
        Message = "IdleConversationAlert skipped: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(EventId = 12002, Level = LogLevel.Information,
        Message = "IdleConversationAlert sending alerts for {Count} conversations")]
    private static partial void LogAlerting(ILogger logger, int count);
}
