using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Hangfire.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using JobEntity = Clawbot.Domain.Jobs.BackgroundJob;

namespace Clawbot.Infrastructure.Jobs;

/// <summary>
/// Chỗ DUY NHẤT chạy job nền + bắn thông báo start/done/fail. Handler chỉ lo nghiệp vụ, không tự notify.
/// Retry 3 lần; chỉ thông báo lỗi ở lần cuối để user không nhận 3 cái báo lỗi cho 1 việc.
/// </summary>
[Queue("ai")]
[AutomaticRetry(Attempts = MaxAttempts, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed partial class JobRunner(
    AppDbContext db,
    IEnumerable<IJobHandler> handlers,
    INotificationPublisher publisher,
    IJobRealtime realtime,
    IPiiRedactor pii,
    IClock clock,
    ILogger<JobRunner> logger)
{
    public const int MaxAttempts = 3;
    private const int MaxErrorChars = 1000;

    public async Task RunAsync(Guid jobId, PerformContext? perform, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct).ConfigureAwait(false);
        if (job is null)
        {
            LogJobMissing(logger, jobId);
            return;
        }

        // Job đã kết thúc (đã huỷ, hoặc Hangfire chạy lại sau khi xong) — không chạy lần nữa.
        if (BackgroundJobStatuses.IsTerminal(job.Status)) return;

        if (job.CancelRequested)
        {
            job.MarkCancelled(clock.UtcNow);
            await SaveAndPushAsync(job, ct).ConfigureAwait(false);
            return;
        }

        var handler = handlers.FirstOrDefault(h => string.Equals(h.Type, job.Type, StringComparison.Ordinal));
        if (handler is null)
        {
            // Retry không cứu được: type không có handler nào nhận. Fail luôn.
            await FailAsync(job, $"Không tìm thấy handler cho loại việc \"{job.Type}\".", ct).ConfigureAwait(false);
            return;
        }

        job.MarkRunning(clock.UtcNow);
        await SaveAndPushAsync(job, ct).ConfigureAwait(false);

        try
        {
            var ctx = new JobContext(
                job.Id, job.TenantId, job.UserId,
                job.PayloadJson ?? "{}",
                new DbJobProgress(db, realtime, job.Id, job.TenantId, job.UserId));

            var result = await handler.RunAsync(ctx, ct).ConfigureAwait(false);

            var summary = await RedactAsync(result.Summary, ct).ConfigureAwait(false);
            job.MarkSucceeded(result.ResultLink, summary, clock.UtcNow);
            await SaveAndPushAsync(job, ct).ConfigureAwait(false);

            // Việc tương tác (soạn nháp, chạy thử agent) không bắn thông báo: user đang nhìn màn hình chờ.
            if (handler.NotifyOnSuccess)
            {
                await NotifyAsync(job, "job_succeeded", "info",
                    $"{job.Title} — đã xong.", ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (job.CancelRequested)
        {
            job.MarkCancelled(clock.UtcNow);
            await SaveAndPushAsync(job, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!IsFinalAttempt(perform))
            {
                LogAttemptFailed(logger, ex, job.Id, job.Type);
                throw; // để Hangfire retry; chưa báo user
            }

            var safe = await RedactAsync(ex.Message, ct).ConfigureAwait(false);
            await FailAsync(job, Truncate(safe ?? "Lỗi không xác định.", MaxErrorChars), ct).ConfigureAwait(false);
            LogJobFailed(logger, ex, job.Id, job.Type);
        }
    }

    private static bool IsFinalAttempt(PerformContext? perform)
    {
        if (perform is null) return true; // chạy ngoài Hangfire (test) — coi như lần cuối
        var retryCount = perform.GetJobParameter<int?>("RetryCount") ?? 0;
        return retryCount >= MaxAttempts - 1;
    }

    private async Task FailAsync(JobEntity job, string error, CancellationToken ct)
    {
        job.MarkFailed(error, clock.UtcNow);
        await SaveAndPushAsync(job, ct).ConfigureAwait(false);
        // Lỗi LUÔN báo — severity warning không được tắt qua notification preferences.
        await NotifyAsync(job, "job_failed", "warning", $"{job.Title} — thất bại: {error}", ct).ConfigureAwait(false);
    }

    private async Task SaveAndPushAsync(JobEntity job, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await realtime.JobUpdatedAsync(job.TenantId, job.UserId, JobPayload(job), ct).ConfigureAwait(false);
    }

    internal static object JobPayload(JobEntity job) => new
    {
        jobId = job.Id,
        job.Type,
        job.Title,
        job.Status,
        job.Progress,
        job.ProgressNote,
        job.ResultLink,
        job.Error,
    };

    private async Task NotifyAsync(JobEntity job, string type, string severity, string body, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(new NotificationRequest(
                job.TenantId,
                job.UserId,
                type,
                job.Title,
                severity,
                body,
                job.ResultLink ?? $"/agents?job={job.Id}"), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Job đã lưu trạng thái cuối rồi — publish hỏng không được làm hỏng job.
            LogNotifyFailed(logger, ex, job.Id, type);
        }
    }

    private async Task<string?> RedactAsync(string? text, CancellationToken ct) =>
        string.IsNullOrEmpty(text)
            ? null
            : (await pii.RedactAsync(text, ct).ConfigureAwait(false)).RedactedText;

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max];

    [LoggerMessage(Level = LogLevel.Warning, Message = "Background job {JobId} not found")]
    private static partial void LogJobMissing(ILogger logger, Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Background job {JobId} ({Type}) attempt failed, will retry")]
    private static partial void LogAttemptFailed(ILogger logger, Exception ex, Guid jobId, string type);

    [LoggerMessage(Level = LogLevel.Error, Message = "Background job {JobId} ({Type}) failed permanently")]
    private static partial void LogJobFailed(ILogger logger, Exception ex, Guid jobId, string type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notify {Type} failed for job {JobId}")]
    private static partial void LogNotifyFailed(ILogger logger, Exception ex, Guid jobId, string type);
}

/// <summary>
/// Ghi tiến độ bằng ExecuteUpdate: KHÔNG gọi SaveChanges để tránh flush sớm thay đổi đang tracking của handler.
/// </summary>
internal sealed class DbJobProgress(AppDbContext db, IJobRealtime realtime, Guid jobId, Guid tenantId, Guid? userId)
    : IJobProgress
{
    public async Task ReportAsync(int percent, string? note, CancellationToken ct = default)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        var trimmed = note is { Length: > 200 } ? note[..200] : note;

        await db.BackgroundJobs.IgnoreQueryFilters()
            .Where(j => j.Id == jobId && j.Status == BackgroundJobStatuses.Running)
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Progress, clamped).SetProperty(j => j.ProgressNote, trimmed),
                ct)
            .ConfigureAwait(false);

        await realtime.JobUpdatedAsync(
            tenantId, userId,
            new { jobId, status = BackgroundJobStatuses.Running, progress = clamped, progressNote = trimmed },
            ct).ConfigureAwait(false);
    }
}
