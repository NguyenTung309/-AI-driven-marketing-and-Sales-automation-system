using System.Text.Json;
using Clawbot.Domain.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using JobEntity = Clawbot.Domain.Jobs.BackgroundJob;

namespace Clawbot.Infrastructure.Jobs;

/// <summary>
/// Tạo row background_jobs rồi enqueue Hangfire. Chỉ hoạt động ở host có Hangfire client (API).
/// </summary>
public sealed class HangfireJobLauncher(
    AppDbContext db,
    IBackgroundJobClient hangfire,
    ITenantAccessor tenants,
    IClock clock) : IJobLauncher
{
    public async Task<Guid> LaunchAsync(
        string type,
        string title,
        object? payload = null,
        Guid? userId = null,
        string? idempotencyKey = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var tenant = tenants.Require();

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            // Chỉ tái dùng job ĐANG chạy/chờ. Job cũ đã xong mà vẫn trả về thì user bấm "quét lại
            // tuần này" sẽ nhận đúng job cũ và tưởng hệ thống không làm gì.
            var existing = await db.BackgroundJobs.AsNoTracking()
                .Where(j => j.TenantId == tenant.TenantId
                    && j.IdempotencyKey == idempotencyKey
                    && (j.Status == BackgroundJobStatuses.Queued || j.Status == BackgroundJobStatuses.Running))
                .Select(j => j.Id)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (existing != Guid.Empty) return existing;
        }

        // KHÔNG redact payload: đây là input do chính user nhập (brief, template code, id) và handler
        // dùng nó để chạy — che số điện thoại trong brief thì bài viết sinh ra sẽ mang hotline bị che.
        // Redact áp cho những gì SINH RA từ agent (result_summary, error) — đó là chỗ dữ liệu khách rò ra.
        // Hệ quả: luồng có dữ liệu khách thô trong payload (CSV lead, file upload) chưa đi đường job này.
        var payloadJson = payload is null ? "{}" : JsonSerializer.Serialize(payload);

        var job = JobEntity.Queue(
            tenant.TenantId,
            userId,
            type.Trim(),
            title.Trim(),
            payloadJson,
            clock.UtcNow,
            string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim());

        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var hangfireId = hangfire.Enqueue<JobRunner>(r => r.RunAsync(job.Id, null!, CancellationToken.None));
        job.AttachHangfireJob(hangfireId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return job.Id;
    }
}
