using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

/// <summary>
/// MỌI job nền hết retry mà vẫn fail đều bắn 1 thông báo cảnh báo — job không phải tự nhớ.
/// Gom nhóm theo tên job nên 1 job hỏng lặp lại cả ngày vẫn chỉ là 1 dòng thông báo.
/// Bỏ qua <see cref="JobRunner"/>: nó tự báo với ngữ cảnh riêng (tên việc, link kết quả).
///
/// Dùng <see cref="IApplyStateFilter"/> chứ KHÔNG dùng IElectStateFilter: khi còn lượt retry,
/// AutomaticRetry đổi trạng thái ứng viên từ Failed sang Scheduled, nên chỉ có lần fail CUỐI mới
/// thực sự "apply" trạng thái Failed. Bắt ở election sẽ báo lỗi ngay từ lần thử đầu.
///
/// Người nhận: CHỈ admin, không broadcast toàn tenant — "ContentPublishJob lỗi" là chuyện vận hành,
/// sale/marketer nhận chỉ thấy nhiễu. Job mang tenantId trong tham số thì chỉ báo tenant đó;
/// job hệ thống (không có tenantId) thì báo admin của mọi tenant đang hoạt động.
/// </summary>
public sealed partial class JobFailureNotificationFilter(
    IServiceScopeFactory scopeFactory,
    ILogger<JobFailureNotificationFilter> logger) : IApplyStateFilter
{
    private const string AdminRoleName = "Admin";

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.NewState is not FailedState failed) return;
        if (context.BackgroundJob.Job?.Type == typeof(JobRunner)) return;

        var jobName = context.BackgroundJob.Job?.Type.Name ?? "Job";
        var args = context.BackgroundJob.Job?.Args ?? [];
        try
        {
            NotifyAsync(jobName, failed.Exception?.Message, args).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Thông báo hỏng không được che mất trạng thái failed của job.
            LogNotifyFailed(logger, ex, jobName);
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        // Job được retry lại: thông báo cũ đã gom nhóm nên không nhân bản, không cần dọn gì.
    }

    private async Task NotifyAsync(string jobName, string? reason, IReadOnlyList<object?> args)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var activeTenants = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToListAsync().ConfigureAwait(false);
        if (activeTenants.Count == 0) return;

        // Job kiểu RunAsync(tenantId, ...) → chỉ báo đúng tenant đó.
        var fromArgs = args.OfType<Guid>().Where(activeTenants.Contains).Distinct().ToList();
        var targets = fromArgs.Count > 0 ? fromArgs : activeTenants;

        var admins = await users.GetUsersInRoleAsync(AdminRoleName).ConfigureAwait(false);
        var body = string.IsNullOrWhiteSpace(reason) ? "Đã thử lại nhưng vẫn thất bại." : reason;

        foreach (var tenantId in targets)
        {
            var recipients = admins
                .Where(u => u.TenantId == tenantId && u.IsActive)
                .Select(u => u.Id)
                .Distinct()
                .ToList();
            if (recipients.Count == 0) continue; // tenant không có admin: bỏ qua, không broadcast

            foreach (var userId in recipients)
            {
                await publisher.PublishAsync(new NotificationRequest(
                    tenantId,
                    userId,
                    Type: "job_failed",
                    Title: $"Tác vụ nền lỗi: {jobName}",
                    Severity: "warning",
                    Body: body,
                    Link: "/system",
                    GroupKey: $"job.failed:{jobName}")).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to notify job failure for {JobName}")]
    private static partial void LogNotifyFailed(ILogger logger, Exception ex, string jobName);
}
