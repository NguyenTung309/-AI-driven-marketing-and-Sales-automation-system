using Clawbot.Api.Services;
using Clawbot.SharedKernel.Demo;
using Microsoft.Extensions.Options;

namespace Clawbot.Api.Background;

public sealed partial class DemoWatchdogService(
    DemoTraceService traces,
    IOptions<DemoOptions> options,
    ILogger<DemoWatchdogService> log) : BackgroundService
{
    private readonly TimeSpan _interval = options.Value.WatchdogInterval;


    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "DemoWatchdogService started (interval: {Interval}s)")]
    private static partial void LogStarted(ILogger logger, double interval);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Error, Message = "DemoWatchdogService scan failed")]
    private static partial void LogScanFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Trace {TraceId} abandoned (age: {Age:F1}m)")]
    private static partial void LogTraceAbandoned(ILogger logger, string traceId, double age);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning, Message = "Trace {TraceId} outbox pending >10m — rabbit timeout")]
    private static partial void LogOutboxTimeout(ILogger logger, string traceId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
            LogStarted(log, (int)_interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);
                await ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogScanFailed(log, ex);
            }
        }
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        var recent = await traces.GetRecentTracesAsync(100);
        var now = DateTime.UtcNow;

        foreach (var trace in recent)
        {
            if (trace.Status != DemoTraceStatus.Running) continue;

            var age = now - trace.CreatedAtUtc;

            // >5 minutes → processing abandoned
            if (age.TotalMinutes > 5)
            {
                LogTraceAbandoned(log, trace.TraceId, age.TotalMinutes);
                await traces.AbandonTraceAsync(trace.TraceId, "processing_abandoned");
                continue;
            }

            // >10 minutes outbox pending → rabbit timeout
            var pendingOutbox = trace.Steps.Find(s => s.Layer == "outbox" && s.Status == DemoTraceStepStatus.Pending);
            if (pendingOutbox is not null && age.TotalMinutes > 10)
            {
                LogOutboxTimeout(log, trace.TraceId);
                await traces.FailOutboxStepAsync(trace.TraceId);
            }
        }
    }
}
