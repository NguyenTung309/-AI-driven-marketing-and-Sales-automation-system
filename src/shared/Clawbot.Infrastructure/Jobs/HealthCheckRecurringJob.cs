using Hangfire;
using Hangfire.Server;

namespace Clawbot.Infrastructure.Jobs;

public sealed class HealthCheckRecurringExecutor(HealthCheckJob healthCheck) : IRecurringJobExecutor
{
    public string DefinitionId => RecurringJobDefinitions.HealthCheck;

    public async Task<RecurringJobExecutionResult> ExecuteAsync(
        RecurringJobExecutionContext context,
        CancellationToken ct)
    {
        await healthCheck.RunAsync(ct).ConfigureAwait(false);
        return new RecurringJobExecutionResult(
            ResultLink: "/system",
            Summary: "Kiểm tra sức khoẻ hệ thống đã hoàn tất.");
    }
}

[AutomaticRetry(Attempts = 10, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed class HealthCheckRecurringJob(RecurringJobDispatcher dispatcher) : ITrackedRecurringJob
{
    internal const string LegacyConcurrencyResource =
        "Clawbot.Infrastructure.Jobs.HealthCheckJob.RunAsync";

    [DisableConcurrentExecution(LegacyConcurrencyResource, timeoutSec: 60)]
    public Task RunScheduledAsync(PerformContext perform, CancellationToken ct) =>
        dispatcher.RunScheduledAsync(RecurringJobDefinitions.HealthCheck, perform, ct);

    [DisableConcurrentExecution(LegacyConcurrencyResource, timeoutSec: 60)]
    public Task RunManualAsync(Guid executionId, PerformContext perform, CancellationToken ct) =>
        dispatcher.RunManualAsync(RecurringJobDefinitions.HealthCheck, executionId, perform, ct);
}
