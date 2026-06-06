using Clawbot.Agents.Core.Lead;
using Clawbot.Api.Contracts.Leads;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class LeadsEndpoints
{
    public static IEndpointRouteBuilder MapLeads(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/leads").RequireAuthorization();

        grp.MapGet("/", ListAsync);
        grp.MapGet("/{id:guid}", GetAsync);
        grp.MapPost("/", CreateAsync);
        grp.MapPost("/{id:guid}/activities", RecordActivityAsync);
        grp.MapPost("/{id:guid}/assign", AssignAsync);

        var rules = app.MapGroup("/api/lead-scoring-rules").RequireAuthorization();
        rules.MapGet("/", ListRulesAsync);
        rules.MapPost("/", CreateRuleAsync);
        rules.MapDelete("/{id:guid}", DeactivateRuleAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] string? stage,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        if (pageSize is < 1 or > 200) pageSize = 50;
        if (page < 1) page = 1;

        var q = db.Leads.AsNoTracking().Where(l => l.DeletedAt == null);
        if (!string.IsNullOrEmpty(stage)) q = q.Where(l => l.Stage == stage);

        var rows = await q
            .OrderByDescending(l => l.Score)
            .ThenByDescending(l => l.LastActivityAt ?? l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LeadDto(l.Id, l.ContactId, l.OwnerUserId, l.Score, l.Stage, l.SourcePlatform, l.LastActivityAt, l.CreatedAt))
            .ToListAsync(ct).ConfigureAwait(false);

        return Results.Ok(rows);
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var lead = await db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (lead is null) return Results.NotFound();
        return Results.Ok(new LeadDto(lead.Id, lead.ContactId, lead.OwnerUserId, lead.Score, lead.Stage, lead.SourcePlatform, lead.LastActivityAt, lead.CreatedAt));
    }

    private static async Task<IResult> CreateAsync(
        CreateLeadRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        ILeadDedupService dedup,
        ILeadAssignmentService assignment,
        IClock clock,
        CancellationToken ct)
    {
        var tenant = tenants.Require();

        var dupes = await dedup.FindCandidatesAsync(
            new DedupRequest(tenant.TenantId, body.ContactId, body.Phone, body.Email), ct).ConfigureAwait(false);

        var lead = Lead.Create(tenant.TenantId, body.ContactId, body.SourcePlatform, clock.UtcNow);
        var owner = await assignment.PickOwnerAsync(tenant.TenantId, ct).ConfigureAwait(false);
        if (owner.HasValue) lead.Assign(owner.Value);

        db.Leads.Add(lead);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Created($"/api/leads/{lead.Id}",
            new CreateLeadResponse(lead.Id,
                dupes.Select(d => new LeadDedupHitDto(d.LeadId, d.ContactId, d.Reason, d.Confidence)).ToList()));
    }

    private static async Task<IResult> RecordActivityAsync(
        Guid id,
        LeadActivityRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (lead is null) return Results.NotFound();

        var rules = await db.LeadScoringRules
            .Where(r => r.IsActive)
            .ToListAsync(ct).ConfigureAwait(false);

        var decision = LeadScoringEngine.Evaluate(body.EventCode, body.Platform ?? lead.SourcePlatform, rules);
        if (decision.Delta != 0)
            lead.AdjustScore(decision.Delta, body.Notes ?? decision.Reason, clock.UtcNow);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new LeadActivityResponse(lead.Score, lead.Stage, decision.Reason, decision.MatchedRules));
    }

    private static async Task<IResult> AssignAsync(
        Guid id,
        LeadAssignRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        ILeadAssignmentService assignment,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (lead is null) return Results.NotFound();

        var owner = body.UserId ?? await assignment.PickOwnerAsync(tenant.TenantId, ct).ConfigureAwait(false);
        if (owner is null) return Results.BadRequest(new { error = "no eligible assignee" });

        lead.Assign(owner.Value);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ListRulesAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var items = await db.LeadScoringRules.AsNoTracking()
            .OrderBy(r => r.EventCode)
            .Select(r => new LeadScoringRuleDto(r.Id, r.EventCode, r.Platform, r.Weight, r.IsActive, r.Description))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateRuleAsync(
        CreateLeadScoringRuleRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.EventCode))
            return Results.BadRequest(new { error = "event_code required" });

        var rule = LeadScoringRule.Create(tenant.TenantId, body.EventCode, body.Weight, body.Platform, clock.UtcNow);
        db.LeadScoringRules.Add(rule);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"/api/lead-scoring-rules/{rule.Id}",
            new LeadScoringRuleDto(rule.Id, rule.EventCode, rule.Platform, rule.Weight, rule.IsActive, rule.Description));
    }

    private static async Task<IResult> DeactivateRuleAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var rule = await db.LeadScoringRules.FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (rule is null) return Results.NotFound();
        rule.Deactivate();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }
}
