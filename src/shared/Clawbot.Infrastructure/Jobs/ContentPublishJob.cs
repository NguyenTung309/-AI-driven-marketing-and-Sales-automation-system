using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class ContentPublishJob(
    AppDbContext db,
    ISocialPublisher publisher,
    IContentNotifier notifier,
    IClock clock,
    ILogger<ContentPublishJob> logger)
{
    private const int BatchSize = 50;

    private readonly AppDbContext _db = db;
    private readonly ISocialPublisher _publisher = publisher;
    private readonly IContentNotifier _notifier = notifier;
    private readonly IClock _clock = clock;
    private readonly ILogger<ContentPublishJob> _logger = logger;

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var pendingSchedules = await _db.ContentSchedules.IgnoreQueryFilters()
            .Where(schedule => schedule.Status == "pending")
            .ToListAsync(ct).ConfigureAwait(false);
        var dueSchedules = pendingSchedules
            .Where(schedule => schedule.ScheduledAt <= now)
            .OrderBy(schedule => schedule.ScheduledAt)
            .Take(BatchSize)
            .ToList();
        if (dueSchedules.Count == 0)
            return;

        var contentItemIds = dueSchedules.Select(schedule => schedule.ContentItemId).ToList();
        var itemsById = await _db.ContentItems.IgnoreQueryFilters()
            .Where(item => contentItemIds.Contains(item.Id) && item.DeletedAt == null)
            .ToDictionaryAsync(item => item.Id, ct).ConfigureAwait(false);

        foreach (var schedule in dueSchedules)
        {
            if (!itemsById.TryGetValue(schedule.ContentItemId, out var item) || item.TenantId != schedule.TenantId)
                continue;

            var request = new PublishRequest(
                schedule.TenantId,
                item.Id,
                schedule.Platform,
                item.Body,
                item.AssetsJson,
                schedule.ScheduledAt);
            var result = await _publisher.PublishAsync(request, ct).ConfigureAwait(false);

            if (result.Success)
            {
                schedule.MarkPosted(result.PostUrl ?? string.Empty, now);
                item.MarkPublished(now);
                LogPublished(_logger, schedule.TenantId, item.Id, schedule.Id);
            }
            else
            {
                var reason = string.IsNullOrWhiteSpace(result.Error) ? "publisher_failed" : result.Error;
                var willRetry = schedule.RecordRetry(now);
                if (willRetry)
                {
                    LogPublishRetrying(_logger, schedule.TenantId, item.Id, schedule.Id, schedule.RetryCount, reason);
                }
                else
                {
                    await _notifier.NotifyPublishFailedAsync(
                        schedule.TenantId,
                        new ContentPublishFailedEvent(
                            schedule.TenantId,
                            item.Id,
                            schedule.Id,
                            schedule.Platform,
                            reason,
                            now),
                        ct).ConfigureAwait(false);
                    LogPublishFailed(_logger, schedule.TenantId, item.Id, schedule.Id, reason);
                    // C2: đánh thức các lịch event-trigger "khi đăng bài thất bại" (vd. tự tạo lại bản nháp thay thế).
                    await Agents.ScheduleEventDispatcher.FireAsync(
                        _db, schedule.TenantId, Clawbot.SharedKernel.Orchestration.ScheduleEventKeys.ContentPublishFailed, now, ct).ConfigureAwait(false);
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    [LoggerMessage(
        EventId = 5103,
        Level = LogLevel.Information,
        Message = "Published content item {ContentItemId} for tenant {TenantId} schedule {ScheduleId}")]
    private static partial void LogPublished(ILogger logger, Guid tenantId, Guid contentItemId, Guid scheduleId);

    [LoggerMessage(
        EventId = 5104,
        Level = LogLevel.Warning,
        Message = "Failed publishing content item {ContentItemId} for tenant {TenantId} schedule {ScheduleId}: {Reason}")]
    private static partial void LogPublishFailed(
        ILogger logger,
        Guid tenantId,
        Guid contentItemId,
        Guid scheduleId,
        string reason);

    [LoggerMessage(
        EventId = 5105,
        Level = LogLevel.Warning,
        Message = "Retrying publish for content item {ContentItemId} tenant {TenantId} schedule {ScheduleId} (attempt {Attempt}): {Reason}")]
    private static partial void LogPublishRetrying(
        ILogger logger,
        Guid tenantId,
        Guid contentItemId,
        Guid scheduleId,
        int attempt,
        string reason);
}
