using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Content;

/// <summary>
/// Creates one revision-bound <see cref="ContentSchedule"/> intent inside the caller's unit of work
/// (approval transaction). Does not SaveChanges — caller commits with the approval write.
/// </summary>
public interface IContentAutoScheduler
{
    /// <param name="desiredPublishAt">
    /// Optional explicit publish time (calendar/manual schedule). When null, uses golden-hour next slot.
    /// Caller must ensure the instant is in the future when provided.
    /// </param>
    Task<ContentSchedule> CreateIntentAsync(
        ContentItem item,
        Guid? publishTargetId,
        DateTimeOffset at,
        DateTimeOffset? desiredPublishAt = null,
        string? providerTargetId = null,
        CancellationToken cancellationToken = default);
}

public sealed class ContentAutoScheduler(
    AppDbContext db,
    IGoldenHourResolver goldenHour) : IContentAutoScheduler
{
    public const string ErrorAutoScheduleTargetMissing = "auto_schedule_target_missing";
    public const string ErrorInstagramPublishingUnavailable = InstagramPublishingGate.ErrorCode;

    private readonly AppDbContext _db = db;
    private readonly IGoldenHourResolver _goldenHour = goldenHour;

    public async Task<ContentSchedule> CreateIntentAsync(
        ContentItem item,
        Guid? publishTargetId,
        DateTimeOffset at,
        DateTimeOffset? desiredPublishAt = null,
        string? providerTargetId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Active intent (pending/held/publishing/outcome_unknown) — return winner, keep existing time.
        // Must run before CanSchedule: after the first call the item is already "scheduled".
        var active = await FindActiveIntentAsync(item, cancellationToken).ConfigureAwait(false);
        if (active is not null)
            return active;

        if (!item.CanScheduleCurrentRevision())
            throw new InvalidOperationException("content_current_revision_not_schedulable");
        if (string.IsNullOrWhiteSpace(item.ApprovalMode)
            || item.PublishingPolicyVersionApplied is null
            || item.ApprovedRevision != item.ContentRevision)
        {
            throw new InvalidOperationException("content_approval_context_missing");
        }

        // User cancel is terminal for automatic recovery/recreate. Explicit reschedule is Phase 3.12.
        // Avoid DateTimeOffset ORDER BY (SQLite EF cannot translate it); Id is enough for existence.
        var canceledByUser = await _db.ContentSchedules
            .IgnoreQueryFilters()
            .Where(schedule =>
                schedule.TenantId == item.TenantId
                && schedule.ContentItemId == item.Id
                && schedule.ContentRevision == item.ContentRevision
                && schedule.Status == ContentSchedule.StatusCanceled
                && schedule.LastErrorCode == ContentSchedule.ErrorCanceledByUser)
            .Select(schedule => schedule.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (canceledByUser != Guid.Empty)
            throw new InvalidOperationException("content_schedule_canceled_by_user");

        if (desiredPublishAt is { } explicitAt && explicitAt <= at)
            throw new InvalidOperationException("content_schedule_in_past");

        var scheduledAt = desiredPublishAt ?? _goldenHour.ResolveNext(item.Platform, at);
        var schedule = ContentSchedule.Schedule(
            item.TenantId,
            item.Id,
            item.ContentRevision,
            item.Platform,
            scheduledAt,
            at,
            metaAssetId: publishTargetId,
            providerTargetId: providerTargetId);
        schedule.SetApprovalContext(
            item.ApprovalMode!,
            item.PublishingPolicyVersionApplied.Value,
            publishTargetId);

        if (RequiresPublishTarget(item.Platform)
            && (publishTargetId is null
                || (string.Equals(item.Platform, "instagram", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(providerTargetId))))
        {
            schedule.MarkHeld(ErrorAutoScheduleTargetMissing, at);
        }
        else if (InstagramPublishingGate.IsBlocked(item.Platform))
        {
            schedule.MarkHeld(ErrorInstagramPublishingUnavailable, at);
        }

        item.SetDesiredPublishAt(scheduledAt, at);
        item.MarkScheduled(at);
        _db.ContentSchedules.Add(schedule);
        return schedule;
    }

    private async Task<ContentSchedule?> FindActiveIntentAsync(
        ContentItem item,
        CancellationToken cancellationToken)
    {
        // ActiveRevisionSlot is non-null for pending/held/publishing/outcome_unknown; unique per item.
        // No ORDER BY DateTimeOffset — SQLite EF cannot translate it; uniqueness guarantees at most one.
        return await _db.ContentSchedules
            .IgnoreQueryFilters()
            .Where(schedule =>
                schedule.TenantId == item.TenantId
                && schedule.ContentItemId == item.Id
                && schedule.ActiveRevisionSlot == item.ContentRevision)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool RequiresPublishTarget(string platform) =>
        string.Equals(platform, "facebook", StringComparison.OrdinalIgnoreCase)
        || string.Equals(platform, "instagram", StringComparison.OrdinalIgnoreCase);
}
