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
    IIdleEscalationRecipientResolver escalationRecipients,
    ILogger<IdleConversationAlertJob> logger)
{
    // SaleAssist-3 tier-2: escalate Trưởng phòng KD khi hội thoại vượt 2x ngưỡng tenant. Band hẹp
    // 2' (bằng cadence job) để chỉ bắn ~1 lần lúc hội thoại vượt mốc, không re-alert manager mỗi pass.
    private static readonly TimeSpan EscalateBandWidth = TimeSpan.FromMinutes(2);
    // Ngừng nhắc tier-1 khi hội thoại đã treo quá ngưỡng + 4h (coi như bỏ rơi, tránh spam mãi).
    private static readonly TimeSpan AlertWindow = TimeSpan.FromHours(4);

    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Ngưỡng cảnh báo cấu hình per-tenant (tenants.idle_alert_minutes, mặc định 5').
        var tenants = await db.Tenants
            .Where(t => t.IsActive)
            .Select(t => new { t.Id, t.IdleAlertMinutes })
            .ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            var idleMinutes = tenant.IdleAlertMinutes <= 0 ? 5 : tenant.IdleAlertMinutes;
            await AlertIdleAsync(tenant.Id, idleMinutes, now, ct);
            await EscalateAsync(tenant.Id, idleMinutes, now, ct);
        }
    }

    private async Task AlertIdleAsync(Guid tenantId, int idleMinutes, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddMinutes(-idleMinutes);
        var oldestAlert = cutoff - AlertWindow;
        var idleConversations = await db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId
                && c.Status == "open"
                && c.LastMessageAt != null
                && c.LastMessageAt < cutoff
                && c.LastMessageAt > oldestAlert)
            .Select(c => new { c.Id, c.AssignedTo, c.InboxId })
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
            await notifier.NotifyMessageAsync(tenantId, new InboxMessageEvent(
                conv.Id, Guid.Empty, "system", "system",
                $"Cuộc trò chuyện đã không hoạt động hơn {idleMinutes} phút. Vui lòng kiểm tra.",
                "text", now,
                AssignedTo: conv.AssignedTo,
                InboxId: conv.InboxId,
                IsSynthetic: true), ct);

            await publisher.PublishAsync(new NotificationRequest(
                tenantId, conv.AssignedTo, "idle", $"Hội thoại chờ quá {idleMinutes} phút",
                Severity: "warning",
                Body: $"Một hội thoại đã không hoạt động hơn {idleMinutes} phút — vui lòng kiểm tra.",
                Link: $"/conversations/{conv.Id}"), ct);
        }
    }

    // Tier-2: hội thoại vượt 2x ngưỡng → escalate Sales Lead để manager tiếp quản khi sale im lặng.
    private async Task EscalateAsync(Guid tenantId, int idleMinutes, DateTimeOffset now, CancellationToken ct)
    {
        var escalateMinutes = idleMinutes * 2;
        var escalateOlderThan = now.AddMinutes(-escalateMinutes);
        var escalateNewerThan = escalateOlderThan - EscalateBandWidth;
        var escalations = await db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId
                && c.Status == "open"
                && c.LastMessageAt != null
                && c.LastMessageAt <= escalateOlderThan
                && c.LastMessageAt > escalateNewerThan)
            .Select(c => new { c.Id })
            .Take(30)
            .ToListAsync(ct);

        if (escalations.Count == 0) return;

        var recipients = await escalationRecipients.ResolveAsync(tenantId, ct);
        foreach (var conv in escalations)
        {
            if (recipients.Count == 0)
            {
                await PublishEscalationAsync(tenantId, null, conv.Id, escalateMinutes, ct);
                continue;
            }

            foreach (var userId in recipients.Distinct())
                await PublishEscalationAsync(tenantId, userId, conv.Id, escalateMinutes, ct);
        }
    }

    private Task PublishEscalationAsync(Guid tenantId, Guid? userId, Guid conversationId, int escalateMinutes, CancellationToken ct) =>
        publisher.PublishAsync(new NotificationRequest(
            tenantId, userId, "idle_escalation", $"Hội thoại chờ quá {escalateMinutes} phút — cần Trưởng phòng KD",
            Severity: "error",
            Body: $"Một hội thoại đã chờ hơn {escalateMinutes} phút mà chưa được xử lý — vui lòng phân công lại.",
            Link: $"/conversations/{conversationId}"), ct);

    [LoggerMessage(EventId = 12001, Level = LogLevel.Debug,
        Message = "IdleConversationAlert skipped: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(EventId = 12002, Level = LogLevel.Information,
        Message = "IdleConversationAlert sending alerts for {Count} conversations")]
    private static partial void LogAlerting(ILogger logger, int count);
}
