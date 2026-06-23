using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

[AutomaticRetry(Attempts = 3, DelaysInSeconds = [1800, 1800, 1800])]
[DisableConcurrentExecution(timeoutInSeconds: 600)]
public sealed class DailySummaryJob(
    AppDbContext db,
    INotificationPublisher publisher,
    IClock clock,
    ILogger<DailySummaryJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var todayStart = clock.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);

        var saleIds = await db.InboxMembers
            .Select(m => m.AgentId)
            .Distinct()
            .ToListAsync(ct);

        logger.LogInformation("DailySummaryJob: processing {Count} sales", saleIds.Count);

        foreach (var saleId in saleIds)
        {
            var conversationsHandled = await db.Conversations
                .CountAsync(c => c.AssignedTo == saleId
                    && c.LastMessageAt >= todayStart
                    && c.LastMessageAt < todayEnd, ct);

            var messagesSent = await db.Messages
                .CountAsync(m => m.SenderUserId == saleId
                    && m.Direction == "outbound"
                    && m.SentAt >= todayStart
                    && m.SentAt < todayEnd, ct);

            var openConversations = await db.Conversations
                .CountAsync(c => c.AssignedTo == saleId && c.Status == "open", ct);

            var totalHandled = await db.Conversations
                .CountAsync(c => c.AssignedTo == saleId && c.Status == "resolved", ct);

            var closeRate = totalHandled > 0
                ? (int)Math.Round((double)conversationsHandled / totalHandled * 100)
                : 0;

            await publisher.PublishAsync(new NotificationRequest
            {
                UserId = saleId,
                Title = "Bao cao cuoi ngay",
                Body = string.Format("Hom nay: {0} hoi thoai, {1} tin nhan. Ty le chot: {2}%.", conversationsHandled, messagesSent, closeRate),
                Type = "daily_summary",
                Url = "/inbox?summary=" + todayStart.ToString("yyyy-MM-dd"),
            }, ct);
        }

        logger.LogInformation("DailySummaryJob: completed for {Count} sales", saleIds.Count);
    }
}
