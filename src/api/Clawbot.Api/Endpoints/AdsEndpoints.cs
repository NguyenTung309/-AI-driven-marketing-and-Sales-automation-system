using Clawbot.Agents.Contracts.Ads;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Ads;
using Clawbot.Api.Jobs;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Ads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class AdsEndpoints
{
    public static IEndpointRouteBuilder MapAds(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/ads").RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/rules", ListRulesAsync).RequirePermission("ads:read");
        grp.MapPost("/rules", CreateRuleAsync).RequirePermission("ads:write");
        grp.MapPut("/rules/{id:guid}", UpdateRuleAsync).RequirePermission("ads:write");
        grp.MapDelete("/rules/{id:guid}", DeactivateRuleAsync).RequirePermission("ads:write");

        grp.MapGet("/campaigns", ListCampaignsAsync).RequirePermission("ads:read");
        grp.MapPut("/campaigns/{id:guid}/target-cpl", UpdateTargetCplAsync).RequirePermission("ads:write");

        grp.MapGet("/actions", ListActionsAsync).RequirePermission("ads:read");
        grp.MapPost("/campaigns/{id:guid}/evaluate", EvaluateCampaignAsync).RequirePermission("ads:write");
        grp.MapPost("/lookalike", BuildLookalikeAsync).RequirePermission("ads:write");

        return app;
    }

    private static async Task<IResult> ListRulesAsync(
        AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var rules = await db.AdsRules.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => ToDto(r))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(rules);
    }

    private static async Task<IResult> CreateRuleAsync(
        CreateAdsRuleRequest body, AppDbContext db, ITenantAccessor tenants, IClock clock, HttpContext http, CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.Metric) || string.IsNullOrWhiteSpace(body.Action))
            return Error(http, 400, "ads.rule_invalid", "metric and action required.");

        var rule = AdsRule.Create(
            tenant.TenantId, body.Platform, body.Metric, body.Comparator, body.Threshold, body.Action, clock.UtcNow);
        db.AdsRules.Add(rule);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"/api/ads/rules/{rule.Id}", ToDto(rule));
    }

    private static async Task<IResult> UpdateRuleAsync(
        Guid id, UpdateAdsRuleRequest body, AppDbContext db, ITenantAccessor tenants, IClock clock, HttpContext http, CancellationToken ct)
    {
        _ = tenants.Require();
        var rule = await db.AdsRules.FirstOrDefaultAsync(r => r.Id == id && r.IsActive, ct).ConfigureAwait(false);
        if (rule is null)
            return Error(http, 404, "ads.rule_not_found", "Rule not found.");

        rule.Update(body.Platform, body.Metric, body.Comparator, body.Threshold, body.Action, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(rule));
    }

    private static async Task<IResult> DeactivateRuleAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, HttpContext http, CancellationToken ct)
    {
        _ = tenants.Require();
        var rule = await db.AdsRules.FirstOrDefaultAsync(r => r.Id == id && r.IsActive, ct).ConfigureAwait(false);
        if (rule is null)
            return Error(http, 404, "ads.rule_not_found", "Rule not found.");

        rule.Deactivate();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ListCampaignsAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;
        var query = db.AdsCampaigns.AsNoTracking();
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var campaigns = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => ToDto(c))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { items = campaigns, total, page, pageSize });
    }

    private static async Task<IResult> UpdateTargetCplAsync(
        Guid id, UpdateTargetCplRequest body, AppDbContext db, ITenantAccessor tenants, IClock clock, HttpContext http, CancellationToken ct)
    {
        _ = tenants.Require();
        var campaign = await db.AdsCampaigns.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (campaign is null)
            return Error(http, 404, "ads.campaign_not_found", "Campaign not found.");

        campaign.MarkSynced(campaign.Objective, campaign.DailyBudget, campaign.Status, body.TargetCpl, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(campaign));
    }

    private static async Task<IResult> ListActionsAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] Guid? campaignId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;
        var query = db.AdsActions.AsNoTracking().AsQueryable();
        if (campaignId.HasValue)
            query = query.Where(a => a.CampaignId == campaignId.Value);

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var actions = await query
            .OrderByDescending(a => a.ExecutedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => ToDto(a))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { items = actions, total, page, pageSize });
    }

    // Đánh giá campaign chạy ngầm: agent có thể tạm dừng/tăng ngân sách — user nhận thông báo việc đã làm.
    private static async Task<IResult> EvaluateCampaignAsync(
        Guid id, AdsEvaluateRequestDto body, IJobLauncher jobs, AppDbContext db,
        ITenantAccessor tenants, HttpContext http, CancellationToken ct)
    {
        _ = tenants.Require();
        var campaign = await db.AdsCampaigns.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (campaign is null)
            return Error(http, 404, "ads.campaign_not_found", "Campaign not found.");

        var jobId = await jobs.LaunchAsync(
            AdsEvaluateJobHandler.JobType,
            $"Đánh giá campaign {campaign.ExternalCampaignId}",
            new AdsEvaluateJobPayload(id, body.Platform),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    private static async Task<IResult> BuildLookalikeAsync(
        AdsLookalikeRequestDto body, IJobLauncher jobs,
        ITenantAccessor tenants, HttpContext http, CancellationToken ct)
    {
        _ = tenants.Require();
        if (body.SeedContactKeys.Count == 0)
            return Error(http, 400, "ads.lookalike_invalid", "seed_contact_keys required.");

        var jobId = await jobs.LaunchAsync(
            AdsLookalikeJobHandler.JobType,
            $"Dựng tệp lookalike {body.Platform}",
            new AdsLookalikeJobPayload(body.Platform),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    private static Guid? CurrentUserId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
        && id != Guid.Empty
            ? id
            : null;

    internal static AdsRuleDto ToDto(AdsRule r) => new(
        r.Id, r.Platform, r.Metric, r.Comparator, r.Threshold, r.Action, r.IsActive, r.CreatedAt, r.UpdatedAt);

    internal static AdsCampaignDto ToDto(AdsCampaign c) => new(
        c.Id, c.Platform, c.ExternalCampaignId, c.Objective, c.DailyBudget, c.Status,
        c.TargetCpl, c.DaypartPaused, c.SyncedAt, c.CreatedAt, c.UpdatedAt);

    internal static AdsActionDto ToDto(AdsAction a) => new(
        a.Id, a.CampaignId, a.RuleId, a.ActionTaken, a.PayloadJson, a.ExecutedAt);

    private static IResult Error(HttpContext http, int status, string code, string message) =>
        Results.Json(new { errorCode = code, message, requestId = http.TraceIdentifier }, statusCode: status);
}
