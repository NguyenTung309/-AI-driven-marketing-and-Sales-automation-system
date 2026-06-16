using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Inbox;
using Clawbot.SharedKernel.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class IdleConversationAlertJob(
    AppDbContext db,
    IInboxNotifier notifier,
    INotificationPublisher publisher,
    ILogger<IdleConversationAlertJob> logger)
{
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(5);
    // SaleAssist-3 tier-2: escalate to Sales Lead after 10 min. A narrow band (10–12 min) makes
    // this fire ~once as the conversation crosses the threshold (job runs every 2 min), rather
    // than re-alerting the manager on every pass.
    private static readonly TimeSpan EscalateThreshold = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan EscalateBand = TimeSpan.FromMinutes(12);

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - IdleThreshold;

        var idleConversations = await db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.Status == "open"
                && c.LastMessageAt != null
                && c.LastMessageAt < cutoff
                && c.LastMessageAt > now.AddHours(-4))
            .Select(c => new { c.Id, c.TenantId, c.AssignedTo })
            .Take(30)
            .ToListAsync(ct);

        if (idleConversations.Count > 0)
        {
            LogAlerting(logger, idleConversations.Count);

            foreach (var conv in idleConversations)
            {
                await notifier.NotifyMessageAsync(conv.TenantId, new InboxMessageEvent(
                    conv.Id, Guid.Empty, "system", "system",
                    "Cuộc trò chuyện đã không hoạt động hơn 5 phút. Vui lòng kiểm tra.",
                    "text", now), ct);

                await publisher.PublishAsync(new NotificationRequest(
                    conv.TenantId, conv.AssignedTo, "idle", "Hội thoại chờ quá 5 phút",
                    Severity: "warning",
                    Body: "Một hội thoại đã không hoạt động hơn 5 phút — vui lòng kiểm tra.",
                    Link: $"/conversations/{conv.Id}"), ct);
            }
        }
        else
        {
            LogSkipped(logger, "no idle conversations");
        }

        // Tier-2: conversations crossing 10 min → escalate to Sales Lead (tenant broadcast,
        // UserId null) so a manager picks it up when the assigned sale hasn't responded.
        var escalateOlderThan = now - EscalateThreshold;
        var escalateNewerThan = now - EscalateBand;
        var escalations = await db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.Status == "open"
                && c.LastMessageAt != null
                && c.LastMessageAt <= escalateOlderThan
                && c.LastMessageAt > escalateNewerThan)
            .Select(c => new { c.Id, c.TenantId })
            .Take(30)
            .ToListAsync(ct);

        foreach (var conv in escalations)
        {
            await publisher.PublishAsync(new NotificationRequest(
                conv.TenantId, null, "idle_escalation", "Hội thoại chờ quá 10 phút — cần Trưởng phòng KD",
                Severity: "error",
                Body: "Một hội thoại đã chờ hơn 10 phút mà chưa được xử lý — vui lòng phân công lại.",
                Link: $"/conversations/{conv.Id}"), ct);
        }
    }

    [LoggerMessage(EventId = 12001, Level = LogLevel.Debug,
        Message = "IdleConversationAlert skipped: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(EventId = 12002, Level = LogLevel.Information,
        Message = "IdleConversationAlert sending alerts for {Count} conversations")]
    private static partial void LogAlerting(ILogger logger, int count);
}
