using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class MetaConnectionHealthJob(
    AppDbContext db,
    IMetaIntegrationService integrations,
    INotificationPublisher publisher,
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
                // Token Meta hỏng = agent ngừng đăng bài/đồng bộ. Im lặng ở đây là mất doanh thu.
                await publisher.PublishAsync(new NotificationRequest(
                    tenantId,
                    UserId: null,
                    Type: "meta_connection_unhealthy",
                    Title: "Kết nối Facebook có vấn đề",
                    Severity: "warning",
                    Body: $"Không xác thực được kết nối Meta: {ex.Message}. Cần kết nối lại tại Cấu hình kênh.",
                    Link: "/system/channels",
                    GroupKey: "meta.connection.unhealthy"), ct).ConfigureAwait(false);
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
