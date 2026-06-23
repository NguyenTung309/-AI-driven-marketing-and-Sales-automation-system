using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

[AutomaticRetry(Attempts = 3, DelaysInSeconds = [1800, 1800, 1800])]
[DisableConcurrentExecution(timeoutInSeconds: 600)]
public sealed partial class DailySummaryJob(
    AppDbContext db,
    INotificationPublisher publisher,
    IClock clock,
    ILogger<DailySummaryJob> logger)
{
    private static readonly System.Globalization.CultureInfo Vn = new("vi-VN");

    [LoggerMessage(EventId = 4101, Level = LogLevel.Information, Message = "DailySummaryJob: processing {Count} sales")]
    private static partial void LogProcessing(ILogger logger, int count);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Information, Message = "DailySummaryJob: completed")]
    private static partial void LogCompleted(ILogger logger);

    public async Task RunAsync(CancellationToken ct = default)
    {
        var todayStart = clock.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);

        var saleInfo = await db.InboxMembers
            .Join(db.Users, m => m.AgentId, u => u.Id, (m, u) => new { u.Id, u.TenantId })
            .Distinct()
            .ToListAsync(ct);

        LogProcessing(logger, saleInfo.Count);

        foreach (var sale in saleInfo)
        {
            var conversationsHandled = await db.Conversations
                .CountAsync(c => c.AssignedTo == sale.Id
                    && c.LastMessageAt >= todayStart
                    && c.LastMessageAt < todayEnd, ct);

            var messagesSent = await db.Messages
                .CountAsync(m => m.SenderUserId == sale.Id
                    && m.Direction == "out"
                    && m.SentAt >= todayStart
                    && m.SentAt < todayEnd, ct);

            var openConversations = await db.Conversations
                .CountAsync(c => c.AssignedTo == sale.Id && c.Status == "open", ct);

            var totalHandled = await db.Conversations
                .CountAsync(c => c.AssignedTo == sale.Id && c.Status == "resolved", ct);

            var closeRate = totalHandled > 0
                ? (int)System.Math.Round((double)conversationsHandled / totalHandled * 100)
                : 0;

            var body = string.Format(Vn, "Hom nay: {0} hoi thoai, {1} tin nhan. Ty le chot: {2}%.", conversationsHandled, messagesSent, closeRate);
            var link = "/inbox?summary=" + todayStart.ToString("yyyy-MM-dd", Vn);

            await publisher.PublishAsync(new NotificationRequest(
                TenantId: sale.TenantId,
                UserId: sale.Id,
                Type: "daily_summary",
                Title: "Bao cao cuoi ngay",
                Body: body,
                Link: link
            ), ct);
        }

        LogCompleted(logger);
    }
}
