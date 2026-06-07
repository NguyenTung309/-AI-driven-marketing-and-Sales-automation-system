using Clawbot.Agents.Contracts.Ads;
using Clawbot.Api.Contracts.Ads;
using Clawbot.Domain.Ads;
using Clawbot.Infrastructure.Persistence;
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
        var grp = app.MapGroup("/api/ads").RequireAuthorization();

        grp.MapGet("/rules", ListRulesAsync);
        grp.MapPost("/rules", CreateRuleAsync);
        grp.MapPut("/rules/{id:guid}", UpdateRuleAsync);
        grp.MapDelete("/rules/{id:guid}", DeactivateRuleAsync);

        grp.MapGet("/campaigns", ListCampaignsAsync);
        grp.MapPut("/campaigns/{id:guid}/target-cpl", UpdateTargetCplAsync);

        grp.MapGet("/actions", ListActionsAsync);
        grp.MapPost("/campaigns/{id:guid}/evaluate", EvaluateCampaignAsync);
        grp.MapPost("/lookalike", BuildLookalikeAsync);

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
        AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var campaigns = await db.AdsCampaigns.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => ToDto(c))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(campaigns);
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
        AppDbContext db, ITenantAccessor tenants, [FromQuery] Guid? campaignId, CancellationToken ct)
    {
        _ = tenants.Require();
        var query = db.AdsActions.AsNoTracking().AsQueryable();
        if (campaignId.HasValue)
            query = query.Where(a => a.CampaignId == campaignId.Value);

        var actions = await query
            .OrderByDescending(a => a.ExecutedAt)
            .Take(100)
            .Select(a => ToDto(a))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(actions);
    }

    private static async Task<IResult> EvaluateCampaignAsync(
        Guid id, AdsEvaluateRequestDto body, AdsAgent.AdsAgentClient client, AppDbContext db,
        ITenantAccessor tenants, IClock clock, HttpContext http, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var campaign = await db.AdsCampaigns.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (campaign is null)
            return Error(http, 404, "ads.campaign_not_found", "Campaign not found.");

        try
        {
            var response = await client.EvaluateAsync(new AdsEvaluateRequest
            {
                TenantId = tenant.TenantId.ToString(),
                Platform = body.Platform,
                CampaignId = id.ToString(),
            }, cancellationToken: ct).ConfigureAwait(false);

            var result = new AdsEvaluateResponseDto(
                response.Actions.Select(a => new AdsActionExecutedDto(
                    string.IsNullOrEmpty(a.RuleId) ? null : Guid.Parse(a.RuleId),
                    a.ActionTaken,
                    a.Note)).ToList());
            return Results.Ok(result);
        }
        catch (RpcException ex)
        {
            return Error(http, 502, "ads.evaluate_failed", ex.Status.Detail ?? "Agent evaluation failed.");
        }
    }

    private static async Task<IResult> BuildLookalikeAsync(
        AdsLookalikeRequestDto body, AdsAgent.AdsAgentClient client,
        ITenantAccessor tenants, HttpContext http, CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (body.SeedContactKeys.Count == 0)
            return Error(http, 400, "ads.lookalike_invalid", "seed_contact_keys required.");

        try
        {
            var response = await client.BuildLookalikeAsync(new AdsLookalikeRequest
            {
                TenantId = tenant.TenantId.ToString(),
                Platform = body.Platform,
            }, cancellationToken: ct).ConfigureAwait(false);

            return Results.Ok(new AdsLookalikeResponseDto(response.AudienceId, response.Created));
        }
        catch (RpcException ex)
        {
            return Error(http, 502, "ads.lookalike_failed", ex.Status.Detail ?? "Lookalike build failed.");
        }
    }

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
