using System.Security.Claims;
using System.Text.Json;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Content;
using Clawbot.Domain.Security;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

// Phase 4: sole writer for tenant content publishing approval policy (automatic | human_required).
// Agent text review is always mandatory; this endpoint only exposes publishing-approval mode.
public static class ContentPublishingPolicyEndpoints
{
    private const string AgentReviewMode = "text_required_vision_optional";
    private const string PolicyChangedAction = "content.publishing_policy.changed";
    private const string ResourceType = "tenant";
    private static readonly JsonSerializerOptions AuditJsonOpts = new(JsonSerializerDefaults.Web);

    public static void Map(RouteGroupBuilder grp)
    {
        ArgumentNullException.ThrowIfNull(grp);
        grp.MapGet("/settings/publishing-policy", GetAsync).RequirePermission("content:read");
        grp.MapPut("/settings/publishing-policy", PutAsync).RequirePermission("system:config");
    }

    private static async Task<IResult> GetAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            .ConfigureAwait(false);
        if (tenant is null)
            return Results.NotFound(new { code = "content.tenant_not_found", errorCode = "content.tenant_not_found" });

        var vision = await ResolveReviewerVisionCapabilityAsync(db, tenantId, ct).ConfigureAwait(false);
        return Results.Ok(ToDto(tenant, vision));
    }

    private static async Task<IResult> PutAsync(
        UpdateContentPublishingPolicyRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        ClaimsPrincipal user,
        HttpContext http,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        if (body is null || string.IsNullOrWhiteSpace(body.PublishingApprovalPolicy))
        {
            return Error(
                http,
                StatusCodes.Status400BadRequest,
                "content.publishing_policy_required",
                "publishingApprovalPolicy is required.");
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            .ConfigureAwait(false);
        if (tenant is null)
        {
            return Error(
                http,
                StatusCodes.Status404NotFound,
                "content.tenant_not_found",
                "Tenant not found.");
        }

        var previousPolicy = tenant.ContentPublishingApprovalPolicy;
        var previousVersion = tenant.ContentPublishingPolicyVersion;
        var now = clock.UtcNow;
        try
        {
            tenant.SetContentPublishingApprovalPolicy(body.PublishingApprovalPolicy, now);
        }
        catch (ArgumentException)
        {
            return Error(
                http,
                StatusCodes.Status400BadRequest,
                "content.publishing_policy_invalid",
                "publishingApprovalPolicy must be automatic or human_required.");
        }

        if (!string.Equals(previousPolicy, tenant.ContentPublishingApprovalPolicy, StringComparison.Ordinal)
            || previousVersion != tenant.ContentPublishingPolicyVersion)
        {
            var userId = CurrentUserId(user);
            db.AuditLogs.Add(AuditLog.Create(
                tenantId,
                userId,
                PolicyChangedAction,
                ResourceType,
                tenantId,
                now,
                JsonSerializer.Serialize(new
                {
                    previousPolicy,
                    previousVersion,
                    nextPolicy = tenant.ContentPublishingApprovalPolicy,
                    nextVersion = tenant.ContentPublishingPolicyVersion,
                }, AuditJsonOpts)));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var vision = await ResolveReviewerVisionCapabilityAsync(db, tenantId, ct).ConfigureAwait(false);
        return Results.Ok(ToDto(tenant, vision));
    }

    private static ContentPublishingPolicyDto ToDto(Tenant tenant, string reviewerVisionCapability) =>
        new(
            AgentReviewRequired: true,
            AgentReviewMode: AgentReviewMode,
            ReviewerVisionCapability: reviewerVisionCapability,
            PublishingApprovalPolicy: tenant.ContentPublishingApprovalPolicy,
            PolicyVersion: tenant.ContentPublishingPolicyVersion,
            UpdatedAt: tenant.ContentPublishingPolicyUpdatedAt == default
                ? tenant.CreatedAt
                : tenant.ContentPublishingPolicyUpdatedAt);

    // Conservative transparency signal for UI: available only when an active bound reviewer config
    // explicitly supports vision; unavailable when override is false; otherwise unknown.
    private static async Task<string> ResolveReviewerVisionCapabilityAsync(
        AppDbContext db,
        Guid tenantId,
        CancellationToken ct)
    {
        var supports = await (
                from agent in db.AgentConfigs.AsNoTracking()
                join cfg in db.LlmConfigs.AsNoTracking() on agent.LlmConfigId equals cfg.Id
                where agent.TenantId == tenantId
                    && agent.DeletedAt == null
                    && cfg.TenantId == tenantId
                    && cfg.IsActive
                    && (agent.Code == "reviewer-agent" || agent.AgentType == "reviewer")
                select cfg.SupportsVision)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return supports switch
        {
            true => "available",
            false => "unavailable",
            null => "unknown",
        };
    }

    private static Guid? CurrentUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static IResult Error(HttpContext http, int statusCode, string errorCode, string message) =>
        Results.Json(
            new { code = errorCode, errorCode, message, requestId = http.TraceIdentifier },
            statusCode: statusCode);
}
