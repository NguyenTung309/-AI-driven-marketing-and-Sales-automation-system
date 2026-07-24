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

        // Must run before CanSchedule: after the first call the item is already "scheduled".
        var active = await FindActiveIntentAsync(item, cancellationToken).ConfigureAwait(false);
        if (active is not null)
        {
            var hasTargetUpdate = publishTargetId.HasValue
                || !string.IsNullOrWhiteSpace(providerTargetId);
            if (!desiredPublishAt.HasValue && !hasTargetUpdate)
                return active;
            if (active.Status is not (ContentSchedule.StatusPending or ContentSchedule.StatusHeld))
                return active;
            if (desiredPublishAt is { } existingExplicitAt && existingExplicitAt <= at)
                throw new InvalidOperationException("content_schedule_in_past");

            if (!hasTargetUpdate && active.RequiresInstagramTargetReselection())
            {
                throw new InvalidOperationException(
                    "content_schedule_instagram_target_reselection_required");
            }

            var rescheduledAt = desiredPublishAt ?? _goldenHour.ResolveNext(item.Platform, at);
            var rescheduledPublishTargetId = hasTargetUpdate ? publishTargetId : active.MetaAssetId;
            var rescheduledProviderTargetId = hasTargetUpdate ? providerTargetId : active.ProviderTargetId;
            active.Reschedule(
                rescheduledAt,
                rescheduledPublishTargetId,
                rescheduledProviderTargetId,
                at);
            ApplyHoldState(
                active,
                item.Platform,
                rescheduledPublishTargetId,
                rescheduledProviderTargetId,
                at);
            item.SetDesiredPublishAt(rescheduledAt, at);
            return active;
        }

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

        ApplyHoldState(
            schedule,
            item.Platform,
            publishTargetId,
            providerTargetId,
            at);

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

    private static void ApplyHoldState(
        ContentSchedule schedule,
        string platform,
        Guid? publishTargetId,
        string? providerTargetId,
        DateTimeOffset at)
    {
        if (!HasRequiredPublishTarget(platform, publishTargetId, providerTargetId))
        {
            schedule.MarkHeld(ErrorAutoScheduleTargetMissing, at);
            return;
        }

        if (InstagramPublishingGate.IsBlocked(platform))
            schedule.MarkHeld(ErrorInstagramPublishingUnavailable, at);
    }

    private static bool HasRequiredPublishTarget(
        string platform,
        Guid? publishTargetId,
        string? providerTargetId)
    {
        if (string.Equals(platform, "facebook", StringComparison.OrdinalIgnoreCase))
            return publishTargetId.HasValue;
        if (string.Equals(platform, "instagram", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(providerTargetId);
        return true;
    }
}
