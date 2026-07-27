using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Clawbot.AgentService.Services;

public interface IContentReviewCoordinator
{
    Task ProcessAsync(
        Guid tenantId,
        Guid taskId,
        Guid leaseToken,
        CancellationToken cancellationToken = default);
}

// Phase 2.3: leases due/expired content_review_tasks for one tenant, then dispatches
// the coordinator. Does not approve, schedule, or publish.
public sealed class ReviewTenantWorker(
    AppDbContext db,
    IContentReviewCoordinator coordinator,
    IClock clock,
    IOptions<ContentReviewWorkerOptions> options) : IReviewTenantRunner
{
    public async Task RunTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenant_id_required", nameof(tenantId));

        var settings = options.Value;
        var now = clock.UtcNow;
        var candidates = await LoadCandidatesAsync(
            tenantId,
            now,
            settings.BatchSize,
            cancellationToken);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var leased = await TryLeaseCandidateAsync(
                candidate,
                settings,
                now,
                cancellationToken);
            if (leased is null)
                continue;

            try
            {
                await coordinator.ProcessAsync(
                    tenantId,
                    leased.TaskId,
                    leased.LeaseToken,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Leave the committed lease intact for exact-expiry reclaim.
                throw;
            }
            catch
            {
                await HandleOperationalFailureAsync(
                    tenantId,
                    leased.TaskId,
                    leased.LeaseToken,
                    settings,
                    cancellationToken);
            }
        }
    }

    private async Task<IReadOnlyList<CandidateTask>> LoadCandidatesAsync(
        Guid tenantId,
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // Hosted scopes have no ambient tenant; always ignore filters and filter explicitly.
        // SQLite cannot translate DateTimeOffset comparisons, so only identity/status filters
        // go to SQL there; due/expiry predicates are applied in process for the test provider.
        List<ContentReviewTask> rows;
        if (db.Database.IsSqlServer())
        {
            rows = await db.ContentReviewTasks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(task => task.TenantId == tenantId
                    && ((task.Status == ContentReviewTask.StatusPending
                            && task.NextAttemptAt <= now)
                        || (task.Status == ContentReviewTask.StatusLeased
                            && task.LeaseExpiresAt != null
                            && task.LeaseExpiresAt <= now)))
                .OrderBy(task => task.Status == ContentReviewTask.StatusLeased
                    ? task.LeaseExpiresAt
                    : task.NextAttemptAt)
                .ThenBy(task => task.CreatedAt)
                .ThenBy(task => task.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
        }
        else
        {
            var tracked = await db.ContentReviewTasks
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(task => task.TenantId == tenantId
                    && (task.Status == ContentReviewTask.StatusPending
                        || task.Status == ContentReviewTask.StatusLeased))
                .ToListAsync(cancellationToken);
            rows = tracked
                .Where(task =>
                    (task.Status == ContentReviewTask.StatusPending
                        && task.NextAttemptAt <= now)
                    || (task.Status == ContentReviewTask.StatusLeased
                        && task.LeaseExpiresAt is not null
                        && task.LeaseExpiresAt <= now))
                .OrderBy(task => task.Status == ContentReviewTask.StatusLeased
                    ? task.LeaseExpiresAt
                    : task.NextAttemptAt)
                .ThenBy(task => task.CreatedAt)
                .ThenBy(task => task.Id)
                .Take(batchSize)
                .ToList();
        }

        return rows
            .Select(task => new CandidateTask(
                task.Id,
                task.Status,
                task.AttemptCount,
                task.LeaseToken,
                task.LeaseExpiresAt,
                task.NextAttemptAt,
                task.RowVersion.ToArray()))
            .ToList();
    }

    private async Task<LeasedWork?> TryLeaseCandidateAsync(
        CandidateTask candidate,
        ContentReviewWorkerOptions settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (candidate.AttemptCount >= settings.MaxAttempts)
        {
            await TerminalizeExhaustedAsync(
                candidate.Id,
                settings.MaxAttempts,
                now,
                cancellationToken);
            return null;
        }

        var task = await db.ContentReviewTasks
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                row => row.Id == candidate.Id,
                cancellationToken);
        if (task is null)
            return null;

        // Guard against a concurrent winner that already mutated the row.
        if (!task.RowVersion.SequenceEqual(candidate.RowVersion)
            || task.Status != candidate.Status)
        {
            db.Entry(task).State = EntityState.Detached;
            return null;
        }

        var leaseToken = Guid.NewGuid();
        var leaseExpiresAt = now.Add(settings.LeaseDuration);
        try
        {
            if (task.Status == ContentReviewTask.StatusPending)
            {
                if (task.NextAttemptAt > now)
                {
                    db.Entry(task).State = EntityState.Detached;
                    return null;
                }

                task.Lease(leaseToken, leaseExpiresAt, now);
            }
            else if (task.Status == ContentReviewTask.StatusLeased)
            {
                if (task.LeaseExpiresAt is null || task.LeaseExpiresAt > now)
                {
                    db.Entry(task).State = EntityState.Detached;
                    return null;
                }

                task.ReclaimExpiredLease(leaseToken, leaseExpiresAt, now);
            }
            else
            {
                db.Entry(task).State = EntityState.Detached;
                return null;
            }

            await db.SaveChangesAsync(cancellationToken);
            return new LeasedWork(task.Id, leaseToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return null;
        }
        catch (InvalidOperationException)
        {
            db.ChangeTracker.Clear();
            return null;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task TerminalizeExhaustedAsync(
        Guid taskId,
        int maxAttempts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var task = await db.ContentReviewTasks
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(row => row.Id == taskId, cancellationToken);
        if (task is null)
            return;

        try
        {
            task.FailExhausted(maxAttempts, now);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Another owner still holds an active lease, or the row already terminalized.
        }
        catch (DbUpdateConcurrencyException)
        {
            // Lost the race; another worker owns the transition.
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task HandleOperationalFailureAsync(
        Guid tenantId,
        Guid taskId,
        Guid leaseToken,
        ContentReviewWorkerOptions settings,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var task = await db.ContentReviewTasks
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                row => row.Id == taskId && row.TenantId == tenantId,
                cancellationToken);
        if (task is null)
            return;

        try
        {
            if (task.AttemptCount >= settings.MaxAttempts)
            {
                task.Fail(
                    leaseToken,
                    "content_review_attempt_limit_reached",
                    now);
            }
            else
            {
                var delay = ComputeBackoff(task.AttemptCount, settings);
                task.ReleaseForRetry(
                    leaseToken,
                    now.Add(delay),
                    "reviewer_error",
                    now);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Lease already reclaimed/completed; nothing durable to do.
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another owner won; leave their state alone.
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private static TimeSpan ComputeBackoff(
        int attemptCount,
        ContentReviewWorkerOptions settings)
    {
        var exponent = Math.Max(attemptCount - 1, 0);
        var multiplier = Math.Pow(2, Math.Min(exponent, 10));
        var delay = TimeSpan.FromTicks(
            (long)(settings.InitialRetryDelay.Ticks * multiplier));
        return delay > settings.MaxRetryDelay
            ? settings.MaxRetryDelay
            : delay;
    }

    private sealed record CandidateTask(
        Guid Id,
        string Status,
        int AttemptCount,
        Guid? LeaseToken,
        DateTimeOffset? LeaseExpiresAt,
        DateTimeOffset NextAttemptAt,
        byte[] RowVersion);

    private sealed record LeasedWork(Guid TaskId, Guid LeaseToken);
}
