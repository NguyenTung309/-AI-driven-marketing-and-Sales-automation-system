using Hangfire;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

/// <summary>
/// A deliberately narrow, safe snapshot of the Hangfire state used for reconciliation.
/// Failure reason, exception data, arguments, and stack traces must never cross this boundary.
/// </summary>
public sealed record RecurringJobExecutionHangfireState(string StateName, int RetryCount);

public interface IRecurringJobExecutionHangfireStateReader
{
    RecurringJobExecutionHangfireState? Find(string hangfireJobId);
}

public sealed class HangfireRecurringJobExecutionHangfireStateReader
    : IRecurringJobExecutionHangfireStateReader
{
    public RecurringJobExecutionHangfireState? Find(string hangfireJobId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hangfireJobId);

        using var connection = JobStorage.Current.GetConnection();
        var state = connection.GetStateData(hangfireJobId);
        if (state is null)
            return null;

        return new RecurringJobExecutionHangfireState(
            state.Name,
            ParseRetryCount(connection.GetJobParameter(hangfireJobId, "RetryCount")));
    }

    private static int ParseRetryCount(string? value) =>
        int.TryParse(value, out var retryCount) && retryCount >= 0 ? retryCount : 0;
}

/// <summary>
/// Reconciles tracked executions only after Hangfire has durably applied their terminal state.
/// This deliberately runs outside <see cref="IApplyStateFilter"/> so application tracking cannot
/// claim a final failure when Hangfire later rolls its own transaction back.
/// </summary>
public sealed partial class RecurringJobExecutionFailureReconciliationJob(
    RecurringJobExecutionService tracking,
    RecurringJobExecutionFailureFinalizer finalizer,
    IRecurringJobExecutionHangfireStateReader hangfireStates,
    ILogger<RecurringJobExecutionFailureReconciliationJob> logger)
{
    private const int BatchSize = 100;
    private const string FailedStateName = "Failed";

    [DisableConcurrentExecution(timeoutInSeconds: 55)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var executions = await tracking.FindActiveTrackedAsync(BatchSize, ct).ConfigureAwait(false);
        if (executions.Count == 0)
            return;

        foreach (var execution in executions)
        {
            if (execution.HangfireBackgroundJobId is not { } hangfireJobId)
                continue;

            try
            {
                var hangfireState = hangfireStates.Find(hangfireJobId);
                if (!string.Equals(hangfireState?.StateName, FailedStateName, StringComparison.Ordinal))
                    continue;

                _ = await finalizer.FinalizeAsync(
                    execution.DefinitionId,
                    hangfireJobId,
                    hangfireState!.RetryCount,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogReconciliationFailed(logger, ex.GetType().Name, execution.Id);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not reconcile recurring execution {ExecutionId} ({ExceptionType})")]
    private static partial void LogReconciliationFailed(ILogger logger, string exceptionType, Guid executionId);
}
