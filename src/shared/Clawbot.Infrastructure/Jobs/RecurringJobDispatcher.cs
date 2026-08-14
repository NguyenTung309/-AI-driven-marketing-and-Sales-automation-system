using Clawbot.Domain.Jobs;
using Hangfire;
using Hangfire.Server;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public static class RecurringJobDefinitions
{
    public const string HealthCheck = "health-check";

    public static string GetRequiredForWrapper(Type wrapperType) =>
        wrapperType == typeof(HealthCheckRecurringJob)
            ? HealthCheck
            : throw new InvalidOperationException("recurring_job_definition_not_found");
}

public interface ITrackedRecurringJob
{
}

public sealed record RecurringJobExecutionContext
{
    private readonly Func<int, string?, CancellationToken, Task> _progressReporter;

    internal RecurringJobExecutionContext(
        Guid executionId,
        Func<int, string?, CancellationToken, Task> progressReporter)
    {
        ExecutionId = executionId;
        _progressReporter = progressReporter;
    }

    public Guid ExecutionId { get; }

    public Task ReportProgressAsync(int percent, string? note, CancellationToken ct = default) =>
        _progressReporter(percent, note, ct);
}

public sealed record RecurringJobHangfireContext(
    string BackgroundJobId,
    int RetryCount,
    string? WorkerId);

public interface IRecurringJobExecutor
{
    string DefinitionId { get; }

    Task<RecurringJobExecutionResult> ExecuteAsync(
        RecurringJobExecutionContext context,
        CancellationToken ct);
}

public sealed class RecurringJobDefinitionRegistry(IEnumerable<IRecurringJobExecutor> executors)
{
    private readonly Dictionary<string, IRecurringJobExecutor> _executors = executors
        .GroupBy(executor => executor.DefinitionId, StringComparer.Ordinal)
        .ToDictionary(
            group => group.Key,
            group => group.Count() == 1
                ? group.Single()
                : throw new InvalidOperationException("recurring_job_definition_duplicate"),
            StringComparer.Ordinal);

    public IRecurringJobExecutor GetRequired(string definitionId)
    {
        var normalizedDefinitionId = definitionId?.Trim() ?? string.Empty;
        return _executors.TryGetValue(normalizedDefinitionId, out var executor)
            ? executor
            : throw new InvalidOperationException("recurring_job_definition_not_found");
    }
}

public sealed partial class RecurringJobDispatcher(
    RecurringJobDefinitionRegistry registry,
    RecurringJobExecutionService tracking,
    ILogger<RecurringJobDispatcher> logger)
{
    public Task RunScheduledAsync(
        string definitionId,
        PerformContext perform,
        CancellationToken ct) =>
        RunScheduledAsync(definitionId, ToHangfireContext(perform), ct);

    public Task RunManualAsync(
        string definitionId,
        Guid executionId,
        PerformContext perform,
        CancellationToken ct) =>
        RunManualAsync(definitionId, executionId, ToHangfireContext(perform), ct);

    internal async Task RunScheduledAsync(
        string definitionId,
        RecurringJobHangfireContext perform,
        CancellationToken ct = default)
    {
        _ = registry.GetRequired(definitionId);
        var execution = await tracking.CreateOrGetScheduledAsync(
            definitionId,
            perform.BackgroundJobId,
            ct).ConfigureAwait(false);
        await RunAsync(definitionId, execution.Id, perform, ct).ConfigureAwait(false);
    }

    internal Task RunManualAsync(
        string definitionId,
        Guid executionId,
        RecurringJobHangfireContext perform,
        CancellationToken ct = default) =>
        RunAsync(definitionId, executionId, perform, ct);

    private async Task RunAsync(
        string definitionId,
        Guid executionId,
        RecurringJobHangfireContext perform,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(perform.BackgroundJobId);
        ArgumentOutOfRangeException.ThrowIfNegative(perform.RetryCount);

        var executor = registry.GetRequired(definitionId);
        await tracking.AttachEnqueueFromDeliveryAsync(
            executionId,
            perform.BackgroundJobId,
            ct).ConfigureAwait(false);
        RecurringJobExecutionAttemptClaim? claim;
        try
        {
            claim = await tracking.ClaimAttemptAsync(
                executionId,
                perform.BackgroundJobId,
                perform.RetryCount,
                perform.WorkerId,
                ct).ConfigureAwait(false);
        }
        catch (RecurringJobExecutionAttemptInProgressException)
        {
            // Do not acknowledge a duplicate delivery as success while its first worker may
            // still be executing. Hangfire will retry this delivery according to its policy.
            throw;
        }

        if (claim is null)
        {
            // Requeueing a terminal Hangfire job reuses its correlation ID. Never acknowledge it
            // as successful without business work; operators must enqueue a tracked manual retry.
            throw new InvalidOperationException("recurring_execution_terminal_requeue_not_supported");
        }

        if (!claim.IsNew)
        {
            if (claim.Attempt.Status == RecurringJobExecutionAttemptStatuses.Failed)
            {
                // A worker can persist its failure and stop before Hangfire commits the matching
                // state transition. A redelivery in that same retry slot must fail so Hangfire
                // schedules the next slot; acknowledging it would strand tracking in retrying.
                throw new InvalidOperationException("recurring_execution_attempt_retry_slot_already_failed");
            }

            return;
        }

        var attempt = claim.Attempt;
        var context = new RecurringJobExecutionContext(
            executionId,
            (percent, note, progressCt) => tracking.ReportProgressAsync(
                executionId,
                new RecurringJobExecutionProgress(percent, note),
                progressCt));

        try
        {
            var result = await executor.ExecuteAsync(context, ct).ConfigureAwait(false);
            await tracking.CompleteAsync(executionId, attempt.Id, result, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await RecordRetryableFailureAsync(
                executionId,
                attempt.Id,
                "Tác vụ bị gián đoạn trước khi hoàn tất.",
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await RecordRetryableFailureAsync(
                executionId,
                attempt.Id,
                "Tác vụ thực thi không thành công.",
                CancellationToken.None).ConfigureAwait(false);
            LogAttemptFailed(logger, ex.GetType().Name, executionId, definitionId, perform.RetryCount);
            throw;
        }
    }

    private async Task RecordRetryableFailureAsync(
        Guid executionId,
        Guid attemptId,
        string safeError,
        CancellationToken ct)
    {
        try
        {
            await tracking.RecordRetryableFailureAsync(executionId, attemptId, safeError, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTrackingFailed(logger, ex.GetType().Name, executionId);
        }
    }

    private static RecurringJobHangfireContext ToHangfireContext(PerformContext perform)
    {
        ArgumentNullException.ThrowIfNull(perform);
        return new RecurringJobHangfireContext(
            perform.BackgroundJob.Id,
            perform.GetJobParameter<int?>("RetryCount") ?? 0,
            perform.ServerId);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Recurring execution {ExecutionId} ({DefinitionId}) attempt {RetryCount} failed with {ExceptionType} and will be retried by Hangfire")]
    private static partial void LogAttemptFailed(
        ILogger logger,
        string exceptionType,
        Guid executionId,
        string definitionId,
        int retryCount);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Could not persist retryable failure for recurring execution {ExecutionId} ({ExceptionType})")]
    private static partial void LogTrackingFailed(ILogger logger, string exceptionType, Guid executionId);
}

public interface IRecurringJobExecutionFailureNotifier
{
    Task NotifyAsync(string definitionId, string safeError, CancellationToken ct = default);
}

public sealed class RecurringJobExecutionFailureFinalizer(
    RecurringJobExecutionService tracking,
    IRecurringJobExecutionFailureNotifier notifier)
{
    public async Task<bool> FinalizeAsync(
        string definitionId,
        string hangfireBackgroundJobId,
        int retryCount,
        CancellationToken ct = default)
    {
        var execution = await tracking.FindByCorrelationAsync(
            definitionId,
            hangfireBackgroundJobId,
            ct).ConfigureAwait(false);
        if (execution is null)
            return false;

        var finalized = await tracking.FinalizeFailureAsync(
            execution.Id,
            hangfireBackgroundJobId,
            retryCount,
            ct).ConfigureAwait(false);
        if (!finalized)
            return false;

        await notifier.NotifyAsync(definitionId, execution.Error!, ct).ConfigureAwait(false);
        return true;
    }
}

public sealed partial class RecurringJobExecutionFailureFilter(
    ILogger<RecurringJobExecutionFailureFilter> logger) : IApplyStateFilter, IElectStateFilter
{
    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.NewState is not FailedState
            || context.BackgroundJob.Job?.Type is not { } wrapperType
            || !typeof(ITrackedRecurringJob).IsAssignableFrom(wrapperType))
        {
            return;
        }

        // An ApplyState filter runs inside Hangfire's storage transaction. Persisting application
        // tracking here would commit independently before Hangfire commits (or rolls back) the
        // Failed state. The reconciler observes the durable Hangfire state after commit instead.
        LogFinalFailureObserved(logger, context.BackgroundJob.Id);
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }

    public void OnStateElection(ElectStateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var wrapperType = context.BackgroundJob.Job?.Type;
        if (context.CandidateState is not EnqueuedState
            || !string.Equals(context.CurrentState, "Failed", StringComparison.Ordinal)
            || wrapperType is null
            || !typeof(ITrackedRecurringJob).IsAssignableFrom(wrapperType))
        {
            return;
        }

        // Dashboard requeue retains the Hangfire ID, but that ID belongs to a tracked execution.
        // Elect a terminal state before the transition commits so workers never run a no-op retry.
        context.CandidateState = new DeletedState
        {
            Reason = "tracked_recurring_execution_terminal_requeue_not_supported",
        };
        LogRequeueRejected(logger, context.BackgroundJob.Id);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Observed final failure for tracked recurring Hangfire job {HangfireJobId}; reconciliation will finalize it after commit")]
    private static partial void LogFinalFailureObserved(ILogger logger, string hangfireJobId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Rejected requeue for terminal tracked recurring Hangfire job {HangfireJobId}")]
    private static partial void LogRequeueRejected(ILogger logger, string hangfireJobId);
}
