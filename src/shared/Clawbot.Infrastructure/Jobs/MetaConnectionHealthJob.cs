using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class MetaConnectionHealthJob(
    AppDbContext db,
    IMetaIntegrationService integrations,
    ILogger<MetaConnectionHealthJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var tenantIds = await db.MetaConnections.IgnoreQueryFilters()
            .Where(x => x.Status == "active")
            .Select(x => x.TenantId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                await integrations.ValidateAsync(tenantId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                LogValidationFailed(logger, tenantId, ex.Message, ex);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    [LoggerMessage(EventId = 5520, Level = LogLevel.Warning, Message = "Meta connection validation failed for tenant {TenantId}: {Reason}")]
    private static partial void LogValidationFailed(ILogger logger, Guid tenantId, string reason, Exception exception);
}
