using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

public sealed partial class AgentScheduleWorker(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<AgentScheduleWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private const int BatchSize = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        await ProcessDueAsync(stoppingToken).ConfigureAwait(false);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await ProcessDueAsync(stoppingToken).ConfigureAwait(false);
    }

    internal async Task ProcessDueAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var runner = scope.ServiceProvider.GetRequiredService<AgentScheduleRunner>();
        var now = clock.UtcNow;
        var due = await db.AgentSchedules.IgnoreQueryFilters()
            .Where(s => s.IsActive && s.DeletedAt == null && s.NextRunAt <= now)
            .OrderBy(s => s.NextRunAt)
            .Take(BatchSize)
            .Select(s => new { s.Id, s.NextRunAt })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var item in due)
        {
            try
            {
                await runner.RunDueAsync(item.Id, item.NextRunAt, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogScheduleRunFailed(logger, ex, item.Id);
            }
        }
    }

    [LoggerMessage(EventId = 1120, Level = LogLevel.Error, Message = "Failed to run due agent schedule {ScheduleId}")]
    private static partial void LogScheduleRunFailed(ILogger logger, Exception exception, Guid scheduleId);
}
