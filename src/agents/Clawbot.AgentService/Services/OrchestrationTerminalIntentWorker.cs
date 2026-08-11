using Clawbot.Agents.Core.Orchestrator;

namespace Clawbot.AgentService.Services;

// Publication happens in the API/Hangfire host. When an orchestration terminal request races that
// external side effect, this independent loop finalizes only after the attempt marker has settled.
public sealed partial class OrchestrationTerminalIntentWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<OrchestrationTerminalIntentWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        await RunTickAsync(stoppingToken).ConfigureAwait(false);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await RunTickAsync(stoppingToken).ConfigureAwait(false);
    }

    private async Task RunTickAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var sink = scope.ServiceProvider.GetRequiredService<IAutonomousRunSink>();
            await sink.FinalizeDeferredTerminalsAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFinalizeTickFailed(logger, exception);
        }
    }

    [LoggerMessage(
        EventId = 1132,
        Level = LogLevel.Error,
        Message = "Failed to finalize deferred orchestration terminal intents; worker keeps running")]
    private static partial void LogFinalizeTickFailed(
        ILogger logger,
        Exception exception);
}
