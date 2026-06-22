using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Security;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record TenantOrchestrationSettingsRequest(bool RequireApproval);

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/admin").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/audit-logs", ListAuditLogsAsync);
        grp.MapPut("/tenant/orchestration", UpdateTenantOrchestrationAsync).RequirePermission("system:config");

        return grp;
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
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { tenant.RequireOrchestrationApproval });
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
                a.IpAddress,
                a.UserAgent,
                a.OccurredAt,
            })
            .ToListAsync(ct);

        return Results.Ok(new { total, page, pageSize, items });
    }
}
