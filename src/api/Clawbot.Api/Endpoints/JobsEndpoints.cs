using System.Security.Claims;
using Clawbot.Api.Auth;
using Clawbot.Api.Common.Pagination;
using Clawbot.Api.Contracts.Common;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Jobs;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobEntity = Clawbot.Domain.Jobs.BackgroundJob;

namespace Clawbot.Api.Endpoints;

public sealed record JobDto(
    Guid Id,
    string Type,
    string Title,
    string Status,
    int Progress,
    string? ProgressNote,
    string? ResultLink,
    string? ResultSummary,
    string? Error,
    Guid? UserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

// Trạng thái mọi việc chạy ngầm của tenant. Dialog job center ở /agents đọc từ đây;
// thông báo job_succeeded/job_failed deep-link về /agents?job={id} khi job không có trang nghiệp vụ riêng.
public static class JobsEndpoints
{
    public static IEndpointRouteBuilder MapJobs(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/jobs")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("", ListAsync).RequirePermission("jobs:view");
        grp.MapGet("/{id:guid}", GetAsync).RequirePermission("jobs:view");
        grp.MapPost("/{id:guid}/cancel", CancelAsync).RequirePermission("jobs:manage");
        grp.MapPost("/{id:guid}/retry", RetryAsync).RequirePermission("jobs:manage");
        return app;
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        HttpContext http,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenant = tenants.Require();
        if (pageSize is < 1 or > 200) pageSize = 50;
        var query = db.BackgroundJobs.AsNoTracking().Where(j => j.TenantId == tenant.TenantId);

        var status = http.Request.Query["status"].ToString();
        if (!string.IsNullOrWhiteSpace(status))
        {
            // "active" = đang chờ hoặc đang chạy — tab mặc định của dialog.
            query = status.Equals("active", StringComparison.OrdinalIgnoreCase)
                ? query.Where(j => j.Status == BackgroundJobStatuses.Queued || j.Status == BackgroundJobStatuses.Running)
                : query.Where(j => j.Status == status);
        }

        if (string.Equals(http.Request.Query["mine"], "true", StringComparison.OrdinalIgnoreCase)
            && CurrentUserId(http) is { } uid)
        {
            query = query.Where(j => j.UserId == uid);
        }

        var key = KeysetQuery.Decode(cursor);
        int? total = key is null ? await query.CountAsync(ct).ConfigureAwait(false) : null;
        if (key is not null)
        {
            var ts = key.Value.Ts;
            var id = key.Value.Id;
            query = query.Where(j => j.CreatedAt < ts || (j.CreatedAt == ts && j.Id < id));
        }

        var fetched = await query
            .OrderByDescending(j => j.CreatedAt)
            .ThenByDescending(j => j.Id)
            .Take(pageSize + 1)
            .Select(j => ToDto(j))
            .ToListAsync(ct).ConfigureAwait(false);

        var (rows, nextCursor) = KeysetQuery.SliceWithCursor(fetched, pageSize, r => r.CreatedAt, r => r.Id);
        return Results.Ok(new CursorPage<JobDto>(rows, nextCursor, total));
    }

    private static async Task<IResult> GetAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var job = await db.BackgroundJobs.AsNoTracking()
            .Where(j => j.Id == id && j.TenantId == tenant.TenantId)
            .Select(j => ToDto(j))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return job is null ? Results.NotFound(new { error = "job_not_found" }) : Results.Ok(job);
    }

    private static async Task<IResult> CancelAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var job = await db.BackgroundJobs
            .FirstOrDefaultAsync(j => j.Id == id && j.TenantId == tenant.TenantId, ct).ConfigureAwait(false);
        if (job is null) return Results.NotFound(new { error = "job_not_found" });

        job.RequestCancel(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(job));
    }

    private static async Task<IResult> RetryAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, IBackgroundJobClient hangfire, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var job = await db.BackgroundJobs
            .FirstOrDefaultAsync(j => j.Id == id && j.TenantId == tenant.TenantId, ct).ConfigureAwait(false);
        if (job is null) return Results.NotFound(new { error = "job_not_found" });
        if (!job.Requeue()) return Results.BadRequest(new { error = "job_not_retryable", job.Status });

        var hangfireId = hangfire.Enqueue<JobRunner>(r => r.RunAsync(job.Id, null!, CancellationToken.None));
        job.AttachHangfireJob(hangfireId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{job.Id}", ToDto(job));
    }

    private static Guid? CurrentUserId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id != Guid.Empty
            ? id
            : null;

    private static JobDto ToDto(JobEntity j) => new(
        j.Id, j.Type, j.Title, j.Status, j.Progress, j.ProgressNote,
        j.ResultLink, j.ResultSummary, j.Error, j.UserId,
        j.CreatedAt, j.StartedAt, j.FinishedAt);
}
