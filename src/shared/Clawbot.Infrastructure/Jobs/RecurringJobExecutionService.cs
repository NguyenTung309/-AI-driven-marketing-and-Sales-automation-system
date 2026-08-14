using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed record RecurringJobExecutionRequest(
    string DefinitionId,
    Guid RequestedByUserId,
    Guid RequestedTenantId,
    string RequestKey);

public sealed record RecurringJobExecutionProgress(int Percent, string? Note);

public sealed record RecurringJobExecutionResult(string? ResultLink, string? Summary);

public sealed record RecurringJobExecutionEnqueueClaim(Guid ExecutionId, Guid Token);

internal sealed record RecurringJobExecutionAttemptClaim(
    RecurringJobExecutionAttempt Attempt,
    bool IsNew);

internal sealed class RecurringJobExecutionAttemptInProgressException()
    : InvalidOperationException("recurring_execution_attempt_already_running");

public sealed partial class RecurringJobExecutionService(
    AppDbContext db,
    IPiiRedactor pii,
    IClock clock,
    ILogger<RecurringJobExecutionService> logger)
{
    public async Task<RecurringJobExecution> CreateOrReuseManualAsync(
        RecurringJobExecutionRequest request,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(request.RequestKey, out var requestKey) || requestKey == Guid.Empty)
            throw new ArgumentException("recurring_execution_request_key_invalid", nameof(request));

        var normalizedRequest = request with { RequestKey = requestKey.ToString("D") };
        var existing = await db.RecurringJobExecutions
            .FirstOrDefaultAsync(execution =>
                execution.RequestedTenantId == normalizedRequest.RequestedTenantId
                && execution.RequestedByUserId == normalizedRequest.RequestedByUserId
                && execution.RequestKey == normalizedRequest.RequestKey,
                ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            if (existing.Source != RecurringJobExecutionSources.Manual
                || existing.RetryOfExecutionId is not null
                || !string.Equals(existing.DefinitionId, normalizedRequest.DefinitionId, StringComparison.Ordinal)
                || existing.RequestedByUserId != normalizedRequest.RequestedByUserId
                || existing.RequestedTenantId != normalizedRequest.RequestedTenantId)
            {
                throw new InvalidOperationException("recurring_execution_request_key_conflict");
            }

            return existing;
        }

        var execution = RecurringJobExecution.CreateManual(
            normalizedRequest.DefinitionId,
            normalizedRequest.RequestedByUserId,
            normalizedRequest.RequestedTenantId,
            normalizedRequest.RequestKey,
            clock.UtcNow);
        db.RecurringJobExecutions.Add(execution);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return execution;
        }
        catch (DbUpdateException)
        {
            db.Entry(execution).State = EntityState.Detached;
            var concurrent = await db.RecurringJobExecutions
                .FirstOrDefaultAsync(candidate =>
                    candidate.RequestedTenantId == normalizedRequest.RequestedTenantId
                    && candidate.RequestedByUserId == normalizedRequest.RequestedByUserId
                    && candidate.RequestKey == normalizedRequest.RequestKey,
                    ct)
                .ConfigureAwait(false);
            if (concurrent is not null
                && concurrent.Source is RecurringJobExecutionSources.Manual
                && concurrent.RetryOfExecutionId is null
                && string.Equals(concurrent.DefinitionId, normalizedRequest.DefinitionId, StringComparison.Ordinal)
                && concurrent.RequestedByUserId == normalizedRequest.RequestedByUserId
                && concurrent.RequestedTenantId == normalizedRequest.RequestedTenantId)
            {
                return concurrent;
            }

            throw;
        }
    }

    public async Task<RecurringJobExecution> CreateManualRetryAsync(
        Guid originalExecutionId,
        RecurringJobExecutionRequest request,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(request.RequestKey, out var requestKey) || requestKey == Guid.Empty)
            throw new ArgumentException("recurring_execution_request_key_invalid", nameof(request));

        var normalizedRequest = request with { RequestKey = requestKey.ToString("D") };
        var existing = await db.RecurringJobExecutions
            .FirstOrDefaultAsync(execution =>
                execution.RequestedTenantId == normalizedRequest.RequestedTenantId
                && execution.RequestedByUserId == normalizedRequest.RequestedByUserId
                && execution.RequestKey == normalizedRequest.RequestKey,
                ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.Source != RecurringJobExecutionSources.ManualRetry
                || existing.RetryOfExecutionId != originalExecutionId
                || !string.Equals(existing.DefinitionId, normalizedRequest.DefinitionId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("recurring_execution_request_key_conflict");
            }

            return existing;
        }

        var original = await LoadAsync(originalExecutionId, ct).ConfigureAwait(false);
        if (original.RequestedTenantId != normalizedRequest.RequestedTenantId)
            throw new InvalidOperationException("recurring_execution_tenant_id_conflict");
        if (!string.Equals(original.DefinitionId, normalizedRequest.DefinitionId, StringComparison.Ordinal))
            throw new InvalidOperationException("recurring_execution_definition_id_conflict");

        var retry = RecurringJobExecution.CreateManualRetry(
            original,
            normalizedRequest.RequestedByUserId,
            normalizedRequest.RequestedTenantId,
            normalizedRequest.RequestKey,
            clock.UtcNow);
        db.RecurringJobExecutions.Add(retry);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return retry;
        }
        catch (DbUpdateException)
        {
            db.Entry(retry).State = EntityState.Detached;
            var concurrent = await db.RecurringJobExecutions
                .FirstOrDefaultAsync(candidate =>
                    candidate.RequestedTenantId == normalizedRequest.RequestedTenantId
                    && candidate.RequestedByUserId == normalizedRequest.RequestedByUserId
                    && candidate.RequestKey == normalizedRequest.RequestKey,
                    ct)
                .ConfigureAwait(false);
            if (concurrent is not null
                && concurrent.Source == RecurringJobExecutionSources.ManualRetry
                && concurrent.RetryOfExecutionId == originalExecutionId
                && string.Equals(concurrent.DefinitionId, normalizedRequest.DefinitionId, StringComparison.Ordinal))
            {
                return concurrent;
            }

            throw;
        }
    }

    public async Task<RecurringJobExecution> CreateOrGetScheduledAsync(
        string definitionId,
        string hangfireBackgroundJobId,
        CancellationToken ct = default)
    {
        var existing = await db.RecurringJobExecutions
            .FirstOrDefaultAsync(execution =>
                execution.DefinitionId == definitionId
                && execution.HangfireBackgroundJobId == hangfireBackgroundJobId,
                ct)
            .ConfigureAwait(false);
        if (existing is not null)
            return existing;

        var execution = RecurringJobExecution.CreateScheduled(definitionId, hangfireBackgroundJobId, clock.UtcNow);
        db.RecurringJobExecutions.Add(execution);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return execution;
        }
        catch (DbUpdateException)
        {
            db.Entry(execution).State = EntityState.Detached;
            var concurrent = await db.RecurringJobExecutions
                .FirstOrDefaultAsync(candidate =>
                    candidate.DefinitionId == definitionId
                    && candidate.HangfireBackgroundJobId == hangfireBackgroundJobId,
                    ct)
                .ConfigureAwait(false);
            if (concurrent is not null)
                return concurrent;

            throw;
        }
    }

    public async Task<RecurringJobExecutionEnqueueClaim?> ClaimEnqueueAsync(
        Guid executionId,
        CancellationToken ct = default)
    {
        var execution = await LoadAsync(executionId, ct).ConfigureAwait(false);
        if (RecurringJobExecutionStatuses.IsTerminal(execution.Status)
            || execution.HangfireBackgroundJobId is not null)
        {
            return null;
        }
        // Once persisted, an enqueue claim represents an unknown external side effect. It must
        // never expire by time alone: Hangfire may already have accepted the job while the process
        // failed before persisting its background-job ID. The job's first delivery repairs that
        // correlation; an operator can diagnose a request that never reaches Hangfire.
        if (execution.EnqueueClaimToken is not null)
            return null;

        var claim = new RecurringJobExecutionEnqueueClaim(executionId, Guid.NewGuid());
        try
        {
            execution.ClaimEnqueue(claim.Token, clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return claim;
        }
        catch (DbUpdateException)
        {
            db.Entry(execution).State = EntityState.Detached;
            var concurrent = await db.RecurringJobExecutions.AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == executionId, ct)
                .ConfigureAwait(false);
            if (concurrent is not null
                && (concurrent.HangfireBackgroundJobId is not null
                    || concurrent.EnqueueClaimToken is not null
                    || RecurringJobExecutionStatuses.IsTerminal(concurrent.Status)))
            {
                return null;
            }

            throw;
        }
    }

    public async Task AttachEnqueueAsync(
        RecurringJobExecutionEnqueueClaim claim,
        string hangfireBackgroundJobId,
        CancellationToken ct = default)
    {
        var execution = await LoadAsync(claim.ExecutionId, ct).ConfigureAwait(false);
        execution.AttachEnqueuedHangfireJob(hangfireBackgroundJobId, clock.UtcNow, claim.Token);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    internal async Task AttachEnqueueAsync(
        Guid executionId,
        string hangfireBackgroundJobId,
        CancellationToken ct = default)
    {
        var execution = await LoadAsync(executionId, ct).ConfigureAwait(false);
        execution.AttachEnqueuedHangfireJob(
            hangfireBackgroundJobId,
            clock.UtcNow,
            execution.EnqueueClaimToken);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task ReleaseEnqueueClaimAsync(
        RecurringJobExecutionEnqueueClaim claim,
        CancellationToken ct = default)
    {
        var execution = await LoadAsync(claim.ExecutionId, ct).ConfigureAwait(false);
        execution.ReleaseEnqueueClaim(claim.Token);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    internal async Task AttachEnqueueFromDeliveryAsync(
        Guid executionId,
        string hangfireBackgroundJobId,
        CancellationToken ct = default)
    {
        var execution = await LoadAsync(executionId, ct).ConfigureAwait(false);
        if (execution.HangfireBackgroundJobId is not null)
            return;
        if (execution.Status != RecurringJobExecutionStatuses.Requested)
            throw new InvalidOperationException("recurring_execution_not_requested");

        // The job method carries the durable execution ID and actual Hangfire delivery ID, so this
        // can safely repair a tracking write that failed after Hangfire accepted the enqueue.
        execution.AttachEnqueuedHangfireJob(hangfireBackgroundJobId, clock.UtcNow, execution.EnqueueClaimToken);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkEnqueueFailedAsync(Guid executionId, string error, CancellationToken ct = default)
    {
        var execution = await LoadAsync(executionId, ct).ConfigureAwait(false);
        execution.MarkEnqueueFailed(await SafeTextAsync(error, RecurringJobExecution.MaxErrorLength, ct).ConfigureAwait(false)
            ?? "Không thể xác nhận yêu cầu đã được xếp hàng.", clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<RecurringJobExecutionAttempt?> StartAttemptAsync(
        Guid executionId,
        string hangfireBackgroundJobId,
        int retryCount,
        string? workerId,
        CancellationToken ct = default)
    {
        var claim = await ClaimAttemptAsync(
            executionId,
            hangfireBackgroundJobId,
            retryCount,
            workerId,
            ct).ConfigureAwait(false);
        return claim?.Attempt;
    }

    internal async Task<RecurringJobExecutionAttemptClaim?> ClaimAttemptAsync(
        Guid executionId,
        string hangfireBackgroundJobId,
        int retryCount,
        string? workerId,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retryCount);

        var execution = await LoadAsync(executionId, ct).ConfigureAwait(false);
        if (RecurringJobExecutionStatuses.IsTerminal(execution.Status))
            return null;
        if (!string.Equals(execution.HangfireBackgroundJobId, hangfireBackgroundJobId, StringComparison.Ordinal))
            throw new InvalidOperationException("recurring_execution_hangfire_job_id_conflict");

        var attemptNumber = checked(retryCount + 1);
        var existing = await db.RecurringJobExecutionAttempts
            .FirstOrDefaultAsync(attempt =>
                attempt.ExecutionId == executionId
                && attempt.AttemptNumber == attemptNumber,
                ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.Status == RecurringJobExecutionAttemptStatuses.Running)
                throw new RecurringJobExecutionAttemptInProgressException();

            return new RecurringJobExecutionAttemptClaim(existing, IsNew: false);
        }

        var runningAttempt = await db.RecurringJobExecutionAttempts
            .FirstOrDefaultAsync(attempt =>
                attempt.ExecutionId == executionId
                && attempt.Status == RecurringJobExecutionAttemptStatuses.Running,
                ct)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        if (runningAttempt is not null)
        {
            // Hangfire only advances RetryCount after a prior attempt failed. A still-running
            // prior slot therefore indicates worker loss between claim and completion; preserve
            // it as interrupted before accepting the retry slot.
            runningAttempt.MarkFailed("Tác vụ bị gián đoạn trước khi hoàn tất.", now);
            execution.MarkRetrying(now);
        }

        execution.MarkRunning(now);
        var attempt = RecurringJobExecutionAttempt.Start(
            executionId,
            hangfireBackgroundJobId,
            retryCount,
            now,
            workerId);
        db.RecurringJobExecutionAttempts.Add(attempt);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new RecurringJobExecutionAttemptClaim(attempt, IsNew: true);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(attempt).State = EntityState.Detached;
            db.Entry(execution).State = EntityState.Unchanged;
            return new RecurringJobExecutionAttemptClaim(
                await ReloadAttemptOrThrowAsync(executionId, attemptNumber, ct).ConfigureAwait(false),
                IsNew: false);
        }
        catch (DbUpdateException)
        {
            db.Entry(attempt).State = EntityState.Detached;
            db.Entry(execution).State = EntityState.Unchanged;
            return new RecurringJobExecutionAttemptClaim(
                await ReloadAttemptOrThrowAsync(executionId, attemptNumber, ct).ConfigureAwait(false),
                IsNew: false);
        }
    }

    public async Task ReportProgressAsync(
        Guid executionId,
        RecurringJobExecutionProgress progress,
        CancellationToken ct = default)
    {
        var execution = await LoadAsync(executionId, ct).ConfigureAwait(false);
        var safeNote = await SafeTextAsync(progress.Note, RecurringJobExecution.MaxProgressNoteLength, ct)
            .ConfigureAwait(false);
        execution.ReportProgress(progress.Percent, safeNote);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task CompleteAsync(
        Guid executionId,
        Guid attemptId,
        RecurringJobExecutionResult result,
        CancellationToken ct = default)
    {
        var execution = await LoadAsync(executionId, ct).ConfigureAwait(false);
        var attempt = await LoadAttemptAsync(attemptId, executionId, ct).ConfigureAwait(false);
        if (attempt.Status != RecurringJobExecutionAttemptStatuses.Running)
            return;

        if (RecurringJobExecutionStatuses.IsTerminal(execution.Status))
        {
            attempt.MarkCancelled(clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var summary = await SafeTextAsync(result.Summary, RecurringJobExecution.MaxResultSummaryLength, ct)
            .ConfigureAwait(false);
        var safeLink = RecurringJobResultLink.Validate(result.ResultLink);
        var now = clock.UtcNow;
        attempt.MarkSucceeded(now);
        execution.MarkSucceeded(safeLink, summary, now);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RecordRetryableFailureAsync(
        Guid executionId,
        Guid attemptId,
        string error,
        CancellationToken ct = default)
    {
        var execution = await LoadAsync(executionId, ct).ConfigureAwait(false);
        var attempt = await LoadAttemptAsync(attemptId, executionId, ct).ConfigureAwait(false);
        if (attempt.Status != RecurringJobExecutionAttemptStatuses.Running)
            return;

        var safeError = await SafeTextAsync(error, RecurringJobExecution.MaxErrorLength, ct).ConfigureAwait(false)
            ?? "Lỗi không xác định.";
        var now = clock.UtcNow;
        if (RecurringJobExecutionStatuses.IsTerminal(execution.Status))
        {
            attempt.MarkCancelled(now);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        attempt.MarkFailed(safeError, now);
        execution.MarkRetrying(now);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    internal async Task<RecurringJobExecution?> FindByCorrelationAsync(
        string definitionId,
        string hangfireBackgroundJobId,
        CancellationToken ct = default) =>
        await db.RecurringJobExecutions
            .FirstOrDefaultAsync(execution =>
                execution.DefinitionId == definitionId
                && execution.HangfireBackgroundJobId == hangfireBackgroundJobId,
                ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<RecurringJobExecution>> FindActiveTrackedAsync(
        int maximumCount,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCount);
        return await db.RecurringJobExecutions
            .Where(execution => execution.HangfireBackgroundJobId != null
                && execution.Status != RecurringJobExecutionStatuses.Succeeded
                && execution.Status != RecurringJobExecutionStatuses.Failed
                && execution.Status != RecurringJobExecutionStatuses.Cancelled
                && execution.Status != RecurringJobExecutionStatuses.Skipped
                && execution.Status != RecurringJobExecutionStatuses.EnqueueFailed)
            .OrderBy(execution => execution.RequestedAt)
            .Take(maximumCount)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<bool> FinalizeFailureAsync(
        Guid executionId,
        string hangfireBackgroundJobId,
        int retryCount,
        CancellationToken ct = default)
    {
        var execution = await LoadAsync(executionId, ct).ConfigureAwait(false);
        if (RecurringJobExecutionStatuses.IsTerminal(execution.Status)
            || !string.Equals(execution.HangfireBackgroundJobId, hangfireBackgroundJobId, StringComparison.Ordinal))
        {
            return false;
        }

        var expectedAttemptNumber = checked(retryCount + 1);
        var latestAttempt = await db.RecurringJobExecutionAttempts
            .Where(attempt => attempt.ExecutionId == executionId)
            .OrderByDescending(attempt => attempt.AttemptNumber)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (latestAttempt is null || latestAttempt.AttemptNumber != expectedAttemptNumber)
            return false;

        var now = clock.UtcNow;
        if (latestAttempt.Status == RecurringJobExecutionAttemptStatuses.Running)
        {
            // The workload failed but the worker could not persist its failure before Hangfire
            // committed the final Failed state. Recover only this exact in-flight attempt with a
            // fixed approved message; never import data from Hangfire's exception or arguments.
            var recoveredError = await SafeTextAsync(
                    "Tác vụ thực thi không thành công.",
                    RecurringJobExecution.MaxErrorLength,
                    ct)
                .ConfigureAwait(false)
                ?? "Lỗi không xác định.";
            latestAttempt.MarkFailed(recoveredError, now);
        }

        if (latestAttempt.Status != RecurringJobExecutionAttemptStatuses.Failed
            || string.IsNullOrEmpty(latestAttempt.Error))
        {
            return false;
        }

        execution.MarkFailed(latestAttempt.Error, now);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async Task<RecurringJobExecutionAttempt> ReloadAttemptOrThrowAsync(
        Guid executionId,
        int attemptNumber,
        CancellationToken ct) =>
        await db.RecurringJobExecutionAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(attempt =>
                attempt.ExecutionId == executionId
                && attempt.AttemptNumber == attemptNumber,
                ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("recurring_execution_attempt_conflict");

    private async Task<RecurringJobExecution> LoadAsync(Guid executionId, CancellationToken ct) =>
        await db.RecurringJobExecutions
            .FirstOrDefaultAsync(execution => execution.Id == executionId, ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("recurring_execution_not_found");

    private async Task<RecurringJobExecutionAttempt> LoadAttemptAsync(
        Guid attemptId,
        Guid executionId,
        CancellationToken ct) =>
        await db.RecurringJobExecutionAttempts
            .FirstOrDefaultAsync(attempt => attempt.Id == attemptId && attempt.ExecutionId == executionId, ct)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("recurring_execution_attempt_not_found");

    private async Task<string?> SafeTextAsync(string? text, int maximumLength, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var redacted = await pii.RedactAsync(text, ct).ConfigureAwait(false);
            var value = redacted.RedactedText.Trim();
            return value.Length <= maximumLength ? value : value[..maximumLength];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogRedactionFailed(logger, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recurring execution text redaction failed")]
    private static partial void LogRedactionFailed(ILogger logger, Exception ex);
}

public static class RecurringJobResultLink
{
    public static string? Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var link = value.Trim();
        if (link.Length > RecurringJobExecution.MaxResultLinkLength
            || link.Contains('?')
            || link.Contains('#')
            || link[0] != '/')
        {
            throw new ArgumentException("recurring_execution_result_link_invalid", nameof(value));
        }

        var decodedLink = DecodeRepeatedly(link);
        if (link.StartsWith("//", StringComparison.Ordinal)
            || decodedLink.StartsWith("//", StringComparison.Ordinal)
            || link.Contains('\\')
            || decodedLink.Contains('\\')
            || decodedLink.Any(char.IsControl)
            || !Uri.TryCreate(link, UriKind.Relative, out _))
        {
            throw new ArgumentException("recurring_execution_result_link_invalid", nameof(value));
        }

        return link;
    }

    private static string DecodeRepeatedly(string value)
    {
        var decoded = value;
        for (var index = 0; index < 3; index++)
        {
            var next = Uri.UnescapeDataString(decoded);
            if (string.Equals(next, decoded, StringComparison.Ordinal))
                break;
            decoded = next;
        }

        return decoded;
    }
}
