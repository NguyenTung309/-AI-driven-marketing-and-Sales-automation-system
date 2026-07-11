using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

// Review-gate P4 (Deliverable 3): bài chờ review sát giờ đăng thì nhắc người — đảm bảo lịch không bị
// trễ trong im lặng. QĐ4: KHÔNG bao giờ auto-approve khi trễ hạn — chỉ hold + escalate to dần.
// Scan gồm CẢ item đã 'scheduled' nhưng chưa có chữ ký (ContentPublishJob skip chúng mỗi pass —
// không có job này thì chúng bị miss âm thầm, đúng cái lỗi requirement muốn tránh).
public sealed partial class ContentReviewSlaJob(
    AppDbContext db,
    INotificationPublisher publisher,
    IContentReviewPolicyResolver reviewPolicy,
    IContentReviewEscalationRecipientResolver escalationRecipients,
    IClock clock,
    ILogger<ContentReviewSlaJob> logger)
{
    // T1: nhắc trước giờ đăng 60 phút. T2: sát/quá giờ đăng -> escalate content lead.
    private static readonly TimeSpan ReviewLeadTime = TimeSpan.FromMinutes(60);
    private const int BatchSize = 50;

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var t1Cutoff = now + ReviewLeadTime;

        // Item chưa có chữ ký agent, có deadline, còn sống trong pipeline (draft/approved/scheduled).
        var pending = await db.ContentItems
            .IgnoreQueryFilters()
            .Where(i => i.DeletedAt == null
                && i.ApprovedByAgentId == null
                && i.DesiredPublishAt != null
                && i.DesiredPublishAt <= t1Cutoff
                && (i.Status == "draft" || i.Status == "approved" || i.Status == "scheduled"))
            .OrderBy(i => i.DesiredPublishAt)
            .Take(BatchSize)
            .ToListAsync(ct).ConfigureAwait(false);

        if (pending.Count == 0)
        {
            LogSkipped(logger, "no pending-review items near deadline");
            return;
        }

        var reviewRequiredByTenant = new Dictionary<Guid, bool>();
        var alerted = 0;
        foreach (var item in pending)
        {
            // Chỉ nhắc khi tenant thực sự bật gate — flag off thì publish job không hold, khỏi báo.
            if (!reviewRequiredByTenant.TryGetValue(item.TenantId, out var required))
            {
                required = await reviewPolicy.IsRequiredAsync(item.TenantId, ct).ConfigureAwait(false);
                reviewRequiredByTenant[item.TenantId] = required;
            }
            if (!required) continue;

            var deadline = item.DesiredPublishAt!.Value;
            var overdue = now >= deadline;

            // Idempotent 2 nấc: T1 bắn 1 lần (LastReviewAlertAt null -> set), T2 bắn 1 lần khi đã quá
            // deadline mà lần alert trước còn ở phía T1 (LastReviewAlertAt < deadline).
            if (!overdue && item.LastReviewAlertAt is null)
            {
                await publisher.PublishAsync(new NotificationRequest(
                    item.TenantId, item.CreatedBy, "content_review_pending",
                    "Bài đăng chờ agent review — sắp tới giờ đăng",
                    Severity: "warning",
                    Body: $"Bài {item.Platform} dự kiến đăng lúc {deadline:HH:mm dd/MM} (UTC) chưa có chữ ký review. Duyệt sớm để kịp lịch.",
                    Link: "/content"), ct).ConfigureAwait(false);
                item.MarkReviewAlerted(now);
                alerted++;
            }
            else if (overdue && (item.LastReviewAlertAt is null || item.LastReviewAlertAt < deadline))
            {
                var recipients = await escalationRecipients.ResolveAsync(item.TenantId, ct).ConfigureAwait(false);
                if (recipients.Count == 0)
                {
                    await PublishOverdueAsync(item.TenantId, null, item.Platform, deadline, ct).ConfigureAwait(false);
                }
                else
                {
                    foreach (var userId in recipients)
                        await PublishOverdueAsync(item.TenantId, userId, item.Platform, deadline, ct).ConfigureAwait(false);
                }
                item.MarkReviewAlerted(now);
                alerted++;
            }
        }

        if (alerted > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            LogAlerted(logger, alerted, pending.Count);
        }
    }

    private Task PublishOverdueAsync(Guid tenantId, Guid? userId, string platform, DateTimeOffset deadline, CancellationToken ct) =>
        publisher.PublishAsync(new NotificationRequest(
            tenantId, userId, "content_review_overdue",
            "Bài đăng TRỄ lịch vì chưa được review",
            Severity: "error",
            Body: $"Bài {platform} đã qua giờ đăng dự kiến ({deadline:HH:mm dd/MM} UTC) nhưng chưa có chữ ký review — bài đang bị giữ, cần duyệt hoặc từ chối ngay.",
            Link: "/content"), ct);

    [LoggerMessage(EventId = 12101, Level = LogLevel.Debug,
        Message = "ContentReviewSla skipped: {Reason}")]
    private static partial void LogSkipped(ILogger logger, string reason);

    [LoggerMessage(EventId = 12102, Level = LogLevel.Information,
        Message = "ContentReviewSla alerted {Alerted}/{Scanned} pending-review items")]
    private static partial void LogAlerted(ILogger logger, int alerted, int scanned);
}
