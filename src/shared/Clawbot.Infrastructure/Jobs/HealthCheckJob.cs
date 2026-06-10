using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class HealthCheckJob(AppDbContext db, ILogger<HealthCheckJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", ct).ConfigureAwait(false);
            LogHealthy(logger);
        }
        catch (Exception ex)
        {
            LogUnhealthy(logger, ex);
            throw;
        }
    }

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Health check passed: database responsive")]
    private static partial void LogHealthy(ILogger logger);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Error, Message = "Health check failed: database unreachable")]
    private static partial void LogUnhealthy(ILogger logger, Exception ex);
}
