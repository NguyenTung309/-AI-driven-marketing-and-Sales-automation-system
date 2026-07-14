using Clawbot.Application.Abstractions;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

/// <summary>
/// Lưới an toàn cuối: cảnh báo lỗi (severity=warning) không ai đọc sau 30 phút thì gửi email.
/// Chỉ áp cho cảnh báo — thông báo thường không đọc là chuyện bình thường, gửi email là spam.
/// </summary>
public sealed partial class UnreadFailureEmailJob(
    AppDbContext db,
    IEmailSender email,
    IClock clock,
    ILogger<UnreadFailureEmailJob> logger)
{
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(30);
    private const int MaxPerRun = 20;

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow - Grace;

        var pending = await db.Notifications.IgnoreQueryFilters()
            .Where(n => n.Severity == "warning"
                && !n.IsRead
                && n.EmailSentAt == null
                && n.CreatedAt <= cutoff)
            .OrderBy(n => n.CreatedAt)
            .Take(MaxPerRun)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var notification in pending)
        {
            var recipients = await ResolveRecipientsAsync(notification.TenantId, notification.UserId, ct)
                .ConfigureAwait(false);

            foreach (var recipient in recipients)
            {
                try
                {
                    await email.SendAsync(
                        recipient,
                        $"[ClawBot] {notification.Title}",
                        $"{notification.Body}\n\nMở hệ thống để xem chi tiết: {notification.Link}",
                        ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogEmailFailed(logger, ex, recipient);
                }
            }

            // Đánh dấu kể cả khi gửi lỗi: retry vô hạn 1 cảnh báo cũ không có giá trị, log đã ghi.
            notification.MarkEmailSent(clock.UtcNow);
        }

        if (pending.Count > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> ResolveRecipientsAsync(Guid tenantId, Guid? userId, CancellationToken ct)
    {
        var query = db.Users.IgnoreQueryFilters().Where(u => u.TenantId == tenantId);
        if (userId is { } id)
            query = query.Where(u => u.Id == id);

        return await query
            .Where(u => u.Email != null && u.Email != "")
            .Select(u => u.Email!)
            .Take(10)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fallback email failed for {Recipient}")]
    private static partial void LogEmailFailed(ILogger logger, Exception ex, string recipient);
}
