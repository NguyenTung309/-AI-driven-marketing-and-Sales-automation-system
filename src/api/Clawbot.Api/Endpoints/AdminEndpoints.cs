using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Security;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

// PATCH-like: field null = client không gửi / không đổi. Tránh GET fail fallback ghi đè config.
// RequireKbHumanReview (ai-self-learning-memory): bật = tri thức tự học luôn chờ người duyệt.
// AiAutoReplyResumeMinutes: sale gửi tay -> AI nhường bao lâu (phút) rồi tự bật lại; null = giữ nguyên.
// IdleAlertMinutes: hội thoại chờ quá bao lâu (phút) thì cảnh báo; escalate = 2x ngưỡng; null = giữ nguyên.
public sealed record TenantOrchestrationSettingsRequest(
    bool? RequireApproval = null,
    decimal? MonthlyCostCapUsd = null,
    bool? RequireContentReview = null,
    bool? RequireChatReplyApproval = null,
    bool? RequireKbHumanReview = null,
    int? AiAutoReplyResumeMinutes = null,
    bool? SkipChatReplyReview = null,
    int? IdleAlertMinutes = null,
    int? LeadLostAfterDays = null,
    string? OrchestratorFailurePolicy = null,
    bool ClearMonthlyCostCapUsd = false);

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/admin").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/audit-logs", ListAuditLogsAsync).RequirePermission("system.logs");
        grp.MapGet("/tenant/orchestration", GetTenantOrchestrationAsync).RequirePermission("agent.read");
        grp.MapPut("/tenant/orchestration", UpdateTenantOrchestrationAsync).RequirePermission("system:config");

        return grp;
    }

    private static async Task<IResult> GetTenantOrchestrationAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        var settings = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.RequireOrchestrationApproval, t.MonthlyCostCapUsd, t.RequireContentReview, t.RequireChatReplyApproval, t.RequireKbHumanReview, t.AiAutoReplyResumeMinutes, t.SkipChatReplyReview, t.IdleAlertMinutes, t.LeadLostAfterDays, t.OrchestratorFailurePolicy })
            .FirstOrDefaultAsync(ct);
        return Results.Ok(new
        {
            requireApproval = settings?.RequireOrchestrationApproval ?? false,
            monthlyCostCapUsd = settings?.MonthlyCostCapUsd,
            requireContentReview = settings?.RequireContentReview ?? false,
            requireChatReplyApproval = settings?.RequireChatReplyApproval ?? false,
            requireKbHumanReview = settings?.RequireKbHumanReview ?? false,
            aiAutoReplyResumeMinutes = settings?.AiAutoReplyResumeMinutes ?? 5,
            skipChatReplyReview = settings?.SkipChatReplyReview ?? false,
            idleAlertMinutes = settings?.IdleAlertMinutes ?? 5,
            leadLostAfterDays = settings?.LeadLostAfterDays ?? 60,
            orchestratorFailurePolicy = settings?.OrchestratorFailurePolicy ?? Tenant.OrchestratorFailurePolicyPause,
        });
    }

    private static async Task<IResult> UpdateTenantOrchestrationAsync(
        TenantOrchestrationSettingsRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct);
        if (tenant is null) return Results.NotFound(new { error = "tenant_not_found" });

        // Chỉ update field thật sự được gửi — null không đụng.
        // Phase 4.10: RequireContentReview is hard-deprecated — mutate nothing when present.
        if (body.RequireContentReview is not null)
        {
            return Results.Json(
                new
                {
                    code = "content.review_setting_deprecated",
                    errorCode = "content.review_setting_deprecated",
                    message = "RequireContentReview is deprecated. Use PUT /api/content/settings/publishing-policy.",
                },
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (body.RequireApproval is { } ra) tenant.SetRequireOrchestrationApproval(ra);
        if (body.ClearMonthlyCostCapUsd)
            tenant.SetMonthlyCostCapUsd(null);
        else if (body.MonthlyCostCapUsd is { } cap)
            tenant.SetMonthlyCostCapUsd(cap);
        if (body.RequireChatReplyApproval is { } rca) tenant.SetRequireChatReplyApproval(rca);
        if (body.RequireKbHumanReview is { } rkb) tenant.SetRequireKbHumanReview(rkb);
        if (body.AiAutoReplyResumeMinutes is { } arm) tenant.SetAiAutoReplyResumeMinutes(arm);
        if (body.SkipChatReplyReview is { } scr) tenant.SetSkipChatReplyReview(scr);
        if (body.IdleAlertMinutes is { } iam) tenant.SetIdleAlertMinutes(iam);
        if (body.LeadLostAfterDays is { } llad) tenant.SetLeadLostAfterDays(llad);
        if (!string.IsNullOrWhiteSpace(body.OrchestratorFailurePolicy))
        {
            try
            {
                tenant.SetOrchestratorFailurePolicy(body.OrchestratorFailurePolicy);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new { error = "orchestrator_failure_policy_invalid" });
            }
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { tenant.RequireOrchestrationApproval, tenant.MonthlyCostCapUsd, tenant.RequireContentReview, tenant.RequireChatReplyApproval, tenant.RequireKbHumanReview, tenant.AiAutoReplyResumeMinutes, tenant.SkipChatReplyReview, tenant.IdleAlertMinutes, tenant.LeadLostAfterDays, tenant.OrchestratorFailurePolicy });
    }

    private static async Task<IResult> ListAuditLogsAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] string? action,
        [FromQuery] string? resourceType,
        [FromQuery] Guid? resourceId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(resourceType))
            query = query.Where(a => a.ResourceType == resourceType);
        if (resourceId.HasValue)
            query = query.Where(a => a.ResourceId == resourceId.Value);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(a => a.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.UserId,
                a.Action,
                a.ResourceType,
                a.ResourceId,
                a.DiffJson,
                IpAddress = a.IpAddress == null ? null : a.IpAddress.ToString(),
                a.UserAgent,
                a.OccurredAt,
            })
            .ToListAsync(ct);

        var userIds = rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToArray();
        var emails = userIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Email })
                .ToDictionaryAsync(u => u.Id, u => u.Email ?? string.Empty, ct);

        var items = rows.Select(a => new
        {
            a.Id,
            a.UserId,
            UserEmail = a.UserId.HasValue && emails.TryGetValue(a.UserId.Value, out var email) ? email : null,
            a.Action,
            a.ResourceType,
            a.ResourceId,
            a.DiffJson,
            a.IpAddress,
            a.UserAgent,
            a.OccurredAt,
        }).ToList();

        return Results.Ok(new { total, page, pageSize, items });
    }
}
