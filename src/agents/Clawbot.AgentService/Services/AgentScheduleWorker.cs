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
        var now = clock.UtcNow;

        List<DueSchedule> due;
        using (var enumerationScope = scopeFactory.CreateScope())
        {
            var db = enumerationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            due = await db.AgentSchedules.IgnoreQueryFilters()
                .Where(s => s.IsActive && s.DeletedAt == null && s.NextRunAt <= now)
                .OrderBy(s => s.NextRunAt)
                .Take(BatchSize)
                .Select(s => new DueSchedule(s.Id, s.NextRunAt))
                .ToListAsync(ct).ConfigureAwait(false);
        }

        foreach (var item in due)
        {
            // Mỗi lịch một scope, tức một AppDbContext riêng. Dùng chung context cho cả lô thì một
            // entity hỏng (ví dụ lead có contact_id không tồn tại) nằm lại ở trạng thái Added và bị
            // flush lại ở mọi SaveChanges sau đó — một lịch lỗi kéo sập toàn bộ lô, và trace ghi
            // trong cùng batch cũng mất theo nên không còn dấu vết để lần.
            using var runScope = scopeFactory.CreateScope();
            var runner = runScope.ServiceProvider.GetRequiredService<AgentScheduleRunner>();
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

    private sealed record DueSchedule(Guid Id, DateTimeOffset NextRunAt);

    [LoggerMessage(EventId = 1120, Level = LogLevel.Error, Message = "Failed to run due agent schedule {ScheduleId}")]
    private static partial void LogScheduleRunFailed(ILogger logger, Exception exception, Guid scheduleId);
}
