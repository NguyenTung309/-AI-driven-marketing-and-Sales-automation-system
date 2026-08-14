using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class RecurringJobExecutionFailureNotifier(
    AppDbContext db,
    UserManager<AppUser> users,
    INotificationPublisher publisher,
    ILogger<RecurringJobExecutionFailureNotifier> logger) : IRecurringJobExecutionFailureNotifier
{
    private const string AdminRoleName = "Admin";

    public async Task NotifyAsync(string definitionId, string safeError, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeError);

        var approvedMessage = "Tác vụ nền đã thất bại sau khi hết lượt thử lại.";
        try
        {
            var activeTenantIds = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
                .Where(tenant => tenant.IsActive)
                .Select(tenant => tenant.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (activeTenantIds.Count == 0)
                return;

            var admins = await users.GetUsersInRoleAsync(AdminRoleName).ConfigureAwait(false);
            var recipients = admins
                .Where(user => user.IsActive && activeTenantIds.Contains(user.TenantId))
                .Select(user => new { user.Id, user.TenantId })
                .Distinct()
                .ToList();

            foreach (var recipient in recipients)
            {
                await publisher.PublishAsync(new NotificationRequest(
                    recipient.TenantId,
                    recipient.Id,
                    Type: "recurring_job_failed",
                    Title: $"Tác vụ định kỳ lỗi: {definitionId}",
                    Severity: "warning",
                    Body: approvedMessage,
                    Link: "/system",
                    GroupKey: $"recurring-job.failed:{definitionId}"), ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotificationFailed(logger, ex.GetType().Name, definitionId);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not notify final failure for tracked recurring definition {DefinitionId} ({ExceptionType})")]
    private static partial void LogNotificationFailed(
        ILogger logger,
        string exceptionType,
        string definitionId);
}
