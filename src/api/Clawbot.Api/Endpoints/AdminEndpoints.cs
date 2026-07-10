using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Security;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

// Review-gate P3: 2 flag mới nullable — client cũ không gửi thì giữ nguyên giá trị hiện tại.
public sealed record TenantOrchestrationSettingsRequest(
    bool RequireApproval,
    decimal? MonthlyCostCapUsd = null,
    bool? RequireContentReview = null,
    bool? RequireChatReplyApproval = null);

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/admin").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/audit-logs", ListAuditLogsAsync);
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
            .Select(t => new { t.RequireOrchestrationApproval, t.MonthlyCostCapUsd, t.RequireContentReview, t.RequireChatReplyApproval })
            .FirstOrDefaultAsync(ct);
        return Results.Ok(new
        {
            requireApproval = settings?.RequireOrchestrationApproval ?? false,
            monthlyCostCapUsd = settings?.MonthlyCostCapUsd,
            requireContentReview = settings?.RequireContentReview ?? false,
            requireChatReplyApproval = settings?.RequireChatReplyApproval ?? false,
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

        tenant.SetRequireOrchestrationApproval(body.RequireApproval);
        tenant.SetMonthlyCostCapUsd(body.MonthlyCostCapUsd);
        // Review-gate P3: chỉ đổi khi client gửi tường minh (null = client cũ, giữ nguyên).
        if (body.RequireContentReview is { } rcr) tenant.SetRequireContentReview(rcr);
        if (body.RequireChatReplyApproval is { } rca) tenant.SetRequireChatReplyApproval(rca);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { tenant.RequireOrchestrationApproval, tenant.MonthlyCostCapUsd, tenant.RequireContentReview, tenant.RequireChatReplyApproval });
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
        var items = await query
            .OrderByDescending(a => a.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.Action,
                a.ResourceType,
                a.ResourceId,
                a.DiffJson,
                IpAddress = a.IpAddress == null ? null : a.IpAddress.ToString(),
                a.UserAgent,
                a.OccurredAt,
            })
            .ToListAsync(ct);

        return Results.Ok(new { total, page, pageSize, items });
    }
}
