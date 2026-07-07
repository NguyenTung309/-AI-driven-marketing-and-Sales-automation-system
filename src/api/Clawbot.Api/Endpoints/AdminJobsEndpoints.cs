using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record AdminRecurringJobDto(
    string Id,
    string Cron,
    string Queue,
    DateTime? NextExecution,
    DateTime? LastExecution,
    string? LastState,
    string? Agent,
    string? Description);

public sealed record AdminScheduleJobDto(
    Guid Id,
    string Name,
    string GoalTemplate,
    string Cadence,
    string TimezoneId,
    DateTimeOffset NextRunAt,
    DateTimeOffset? LastRunAt,
    bool IsActive,
    bool RequiresApproval,
    string? Agent,
    string Kind);

public sealed record AdminJobsResponse(
    IReadOnlyList<AdminRecurringJobDto> Recurring,
    IReadOnlyList<AdminScheduleJobDto> Schedules);

// Admin overview of every automated job: Hangfire recurring jobs (system-wide, from Hangfire
// storage — the source of truth for what is actually registered) plus the tenant's
// AgentSchedules (OrchestrationV2). Read-only list + trigger/pause/activate/run-now actions.
public static class AdminJobsEndpoints
{
    private const string OrchestrationKind = "orchestration";
    private const string TrendScanKind = "trend-scan";

    // Hangfire job id -> (agent driven via gRPC, VN description). Agent empty = pure system job.
    private static readonly Dictionary<string, (string? Agent, string Description)> JobMeta =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["retention-purge"] = (null, "Dọn dữ liệu hết hạn retention"),
            ["kpi-daily-rollup"] = (null, "Tổng hợp KPI theo ngày"),
            ["refresh-token-cleanup"] = (null, "Dọn refresh token hết hạn"),
            ["daily-report-push"] = (null, "Đẩy báo cáo ngày cho tenant"),
            ["kpi-anomaly-alert"] = ("report-agent", "Cảnh báo bất thường KPI"),
            ["kpi-forecast-precompute"] = ("report-agent", "Tính trước dự báo KPI"),
            ["content-weekly-trend-scan"] = ("research-agent", "Quét xu hướng nội dung hằng tuần"),
            ["content-publish-due"] = (null, "Đăng bài đã lên lịch đến hạn"),
            ["ads-rule-evaluation"] = (null, "Đánh giá rule quảng cáo"),
            ["ads-creative-rotation"] = (null, "Xoay vòng creative quảng cáo"),
            ["ads-remarketing"] = (null, "Đồng bộ tệp remarketing"),
            ["ads-lookalike-refresh"] = (null, "Làm mới tệp lookalike"),
            ["ads-daypart-pause"] = (null, "Tạm dừng quảng cáo theo khung giờ"),
            ["ads-daypart-resume"] = (null, "Bật lại quảng cáo theo khung giờ"),
            ["ads-weekly-report"] = (null, "Báo cáo quảng cáo hằng tuần"),
            ["health-check"] = (null, "Kiểm tra sức khoẻ hệ thống"),
            ["out-of-hours-auto-reply"] = (null, "Tự động trả lời ngoài giờ"),
            ["drip-sequence-sender"] = (null, "Gửi chuỗi tin nhắn drip"),
            ["idle-conversation-alert"] = (null, "Cảnh báo hội thoại bị bỏ quên"),
            ["lead-followup"] = (null, "Nhắc follow-up khách hàng tiềm năng"),
            ["kb-accuracy-check"] = (null, "Kiểm tra độ chính xác tri thức KB"),
            ["inbox-daily-summary"] = (null, "Tóm tắt inbox cuối ngày"),
            ["competitor-scan"] = (null, "Quét đối thủ cạnh tranh"),
        };

    public static IEndpointRouteBuilder MapAdminJobs(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/admin/jobs")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("", ListAsync).RequirePermission("system:config");
        grp.MapPost("/recurring/{id}/trigger", TriggerRecurring).RequirePermission("system:config");
        grp.MapPost("/schedules/{id:guid}/pause", PauseScheduleAsync).RequirePermission("system:config");
        grp.MapPost("/schedules/{id:guid}/activate", ActivateScheduleAsync).RequirePermission("system:config");
        grp.MapPost("/schedules/{id:guid}/run-now", RunScheduleNowAsync).RequirePermission("system:config");
        return app;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();

        using var connection = JobStorage.Current.GetConnection();
        var recurring = connection.GetRecurringJobs()
            .OrderBy(j => j.Id, StringComparer.OrdinalIgnoreCase)
            .Select(j =>
            {
                var meta = JobMeta.TryGetValue(j.Id, out var m) ? m : (Agent: null, Description: string.Empty);
                return new AdminRecurringJobDto(
                    j.Id,
                    j.Cron ?? string.Empty,
                    j.Queue ?? "default",
                    j.NextExecution,
                    j.LastExecution,
                    j.LastJobState,
                    meta.Agent,
                    string.IsNullOrEmpty(meta.Description) ? null : meta.Description);
            })
            .ToList();

        var schedules = (await db.AgentSchedules.AsNoTracking()
                .Where(s => s.DeletedAt == null)
                .ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => ToScheduleDto(s))
            .ToList();

        return Results.Ok(new AdminJobsResponse(recurring, schedules));
    }

    private static IResult TriggerRecurring(string id, IRecurringJobManager recurring)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Results.BadRequest(new { error = "job_id_required" });

        recurring.Trigger(id.Trim());
        return Results.Accepted($"/api/admin/jobs", new { status = "triggered", id });
    }

    private static async Task<IResult> PauseScheduleAsync(Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        _ = tenants.Require();
        var schedule = await db.AgentSchedules.FirstOrDefaultAsync(s => s.Id == id && s.DeletedAt == null, ct).ConfigureAwait(false);
        if (schedule is null)
            return Results.NotFound(new { error = "schedule_not_found" });

        schedule.Pause(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToScheduleDto(schedule));
    }

    private static async Task<IResult> ActivateScheduleAsync(Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        _ = tenants.Require();
        var schedule = await db.AgentSchedules.FirstOrDefaultAsync(s => s.Id == id && s.DeletedAt == null, ct).ConfigureAwait(false);
        if (schedule is null)
            return Results.NotFound(new { error = "schedule_not_found" });

        schedule.Activate(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToScheduleDto(schedule));
    }

    private static async Task<IResult> RunScheduleNowAsync(Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        _ = tenants.Require();
        var schedule = await db.AgentSchedules.FirstOrDefaultAsync(s => s.Id == id && s.DeletedAt == null, ct).ConfigureAwait(false);
        if (schedule is null)
            return Results.NotFound(new { error = "schedule_not_found" });

        var now = clock.UtcNow;
        schedule.UpdateSchedule(
            schedule.Name,
            schedule.GoalTemplate,
            schedule.Cadence,
            schedule.CronExpression,
            schedule.TimezoneId,
            nextRunAt: now,
            schedule.RequiresApproval,
            schedule.OverlapPolicy,
            schedule.MisfirePolicy,
            schedule.ApprovalPolicyJson,
            now);
        schedule.Activate(now);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Accepted("/api/admin/jobs", new { status = "queued", nextRunAt = schedule.NextRunAt });
    }

    private static AdminScheduleJobDto ToScheduleDto(Clawbot.Domain.Agents.AgentSchedule s)
    {
        var isTrendScan = string.Equals(s.GoalTemplate, ContentTrendSettings.ScheduleGoalMarker, StringComparison.OrdinalIgnoreCase);
        return new AdminScheduleJobDto(
            s.Id,
            s.Name,
            s.GoalTemplate,
            s.Cadence,
            s.TimezoneId,
            s.NextRunAt,
            s.LastRunAt,
            s.IsActive,
            s.RequiresApproval,
            isTrendScan ? "research-agent" : null,
            isTrendScan ? TrendScanKind : OrchestrationKind);
    }
}
