using System.Text;
using Clawbot.Agents.Contracts.Lead;
using Clawbot.Agents.Core.Lead;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Leads;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.Domain.Contacts;
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
        var grp = app.MapGroup("/api/leads").RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/", ListAsync).RequirePermission("leads:read");
        grp.MapGet("/export.csv", ExportCsvAsync).RequirePermission("leads:read");
        grp.MapPost("/import.csv", ImportCsvAsync).RequirePermission("leads:write");
        grp.MapGet("/{id:guid}", GetAsync).RequirePermission("leads:read");
        grp.MapPost("/", CreateAsync).RequirePermission("leads:write");
        grp.MapPost("/create-with-skills", CreateWithSkillsAsync).RequirePermission("leads:write");
        grp.MapPost("/{id:guid}/activities", RecordActivityAsync).RequirePermission("leads:write");
        grp.MapPost("/{id:guid}/assign", AssignAsync).RequirePermission("leads:write");
        grp.MapGet("/forecast", ForecastAsync).RequirePermission("leads:read");
        grp.MapGet("/{id:guid}/context", ContextPanelAsync).RequirePermission("leads:read");

        var rules = app.MapGroup("/api/lead-scoring-rules")
            .RequirePermission("leads:write")
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);
        rules.MapGet("/", ListRulesAsync);
        rules.MapPost("/", CreateRuleAsync);
        rules.MapPost("/seed-defaults", SeedDefaultRulesAsync);
        rules.MapDelete("/{id:guid}", DeactivateRuleAsync);

        return app;
    }

    // One-click seed of the default education lead-scoring rules. Skips codes already present
    // so it is safe to re-run; returns how many rules were created.
    private static async Task<IResult> SeedDefaultRulesAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var existing = await db.LeadScoringRules
            .Where(r => r.TenantId == tenantId)
            .Select(r => r.EventCode)
            .ToListAsync(ct).ConfigureAwait(false);
        var have = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var created = 0;
        foreach (var spec in LeadScoringDefaults.Rules)
        {
            if (have.Contains(spec.EventCode)) continue;
            var rule = LeadScoringRule.Create(tenantId, spec.EventCode, spec.Weight, platform: null, clock.UtcNow);
            db.Entry(rule).Property("Description").CurrentValue = spec.Description;
            db.LeadScoringRules.Add(rule);
            created++;
        }

        if (created > 0) await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { created, total = LeadScoringDefaults.Rules.Count });
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
            .Select(l => new LeadDto(l.Id, l.ContactId, l.OwnerUserId, l.Score, l.Stage, l.SourcePlatform, l.LastActivityAt, l.CreatedAt,
                db.Contacts.Where(c => c.Id == l.ContactId).Select(c => c.DisplayName).FirstOrDefault(),
                db.Contacts.Where(c => c.Id == l.ContactId).Select(c => c.Phone).FirstOrDefault(),
                db.Users.Where(u => u.Id == l.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault()))
            .ToListAsync(ct).ConfigureAwait(false);

        return Results.Ok(rows);
    }

    private static async Task<IResult> ExportCsvAsync(
        ITenantAccessor tenants,
        LeadCsvService service,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var export = await service.ExportCsvAsync(tenantId, ct).ConfigureAwait(false);
        return Results.File(Encoding.UTF8.GetBytes(export.Content), "text/csv; charset=utf-8", export.FileName);
    }

    private static async Task<IResult> ImportCsvAsync(
        HttpRequest request,
        ITenantAccessor tenants,
        LeadCsvService service,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var csv = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        var result = await service.ImportCsvAsync(tenantId, csv, ct).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var lead = await db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (lead is null) return Results.NotFound();
        var contact = lead.ContactId is null
            ? null
            : await db.Contacts.AsNoTracking()
                .Where(c => c.Id == lead.ContactId)
                .Select(c => new { c.DisplayName, c.Phone })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var ownerName = lead.OwnerUserId is null
            ? null
            : await db.Users.AsNoTracking()
                .Where(u => u.Id == lead.OwnerUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return Results.Ok(new LeadDto(lead.Id, lead.ContactId, lead.OwnerUserId, lead.Score, lead.Stage, lead.SourcePlatform, lead.LastActivityAt, lead.CreatedAt,
            contact?.DisplayName, contact?.Phone, ownerName));
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

    private static async Task<IResult> CreateWithSkillsAsync(
        CreateLeadRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        LeadAgent.LeadAgentClient leadClient,
        CancellationToken ct)
    {
        var tenant = tenants.Require();

        if (body.ContactId == Guid.Empty)
            return Results.BadRequest(new { error = "contact_id required" });

        var contact = await db.Contacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == body.ContactId && c.TenantId == tenant.TenantId, ct)
            .ConfigureAwait(false);

        if (contact is null)
            return Results.NotFound(new { error = "contact not found" });

        var grpcRequest = new LeadCreateWithSkillsRequest
        {
            TenantId = tenant.TenantId.ToString("D"),
            ContactId = body.ContactId.ToString("D"),
            DisplayName = contact.DisplayName ?? string.Empty,
            Phone = body.Phone ?? string.Empty,
            Email = body.Email ?? string.Empty,
            SourcePlatform = body.SourcePlatform,
            Locale = contact.Locale ?? string.Empty,
            Country = string.Empty,
        };

        var grpcResponse = await leadClient.CreateWithSkillsAsync(grpcRequest, cancellationToken: ct).ConfigureAwait(false);

        var result = new CreateWithSkillsResult(
            Guid.Parse(grpcResponse.LeadId),
            grpcResponse.SpamFlagged,
            grpcResponse.SpamReason,
            grpcResponse.Timezone,
            grpcResponse.EnrichmentCompany,
            grpcResponse.PossibleDup,
            grpcResponse.DedupCandidates.Select(c => new LeadDedupCandidateDto(
                Guid.Parse(c.ContactId), c.Similarity)).ToList());

        return Results.Created($"/api/leads/{result.LeadId}", result);
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
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var rule = await db.LeadScoringRules.FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false);
        if (rule is null) return Results.NotFound();
        rule.Deactivate();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ForecastAsync(
        [FromServices] AppDbContext db,
        [FromServices] ITenantAccessor tenants,
        [FromServices] IForecaster forecaster,
        [FromQuery] int horizonDays = 7,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        var since = DateTimeOffset.UtcNow.AddDays(-60);

        var dailyCounts = await db.Leads
            .IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId && l.CreatedAt >= since)
            .GroupBy(l => l.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        if (dailyCounts.Count < 7)
            return Results.Ok(new { forecast = Array.Empty<object>(), note = "need_at_least_7_days_of_data" });

        var history = dailyCounts
            .Select(d => (new DateTimeOffset(d.Date, TimeSpan.Zero), (double)d.Count))
            .ToList();

        var points = await forecaster.ForecastAsync(history, Math.Clamp(horizonDays, 1, 30), ct);

        return Results.Ok(new
        {
            forecast = points.Select(p => new
            {
                date = p.At.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                predicted_leads = Math.Round(p.Forecast, 1),
                lower_bound = Math.Round(p.LowerBound, 1),
                upper_bound = Math.Round(p.UpperBound, 1),
            }),
        });
    }

    private static async Task<IResult> ContextPanelAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var lead = await db.Leads
            .IgnoreQueryFilters()
            .Where(l => l.Id == id && l.TenantId == tenantId)
            .Select(l => new
            {
                l.Id,
                l.Score,
                l.Stage,
                l.SourcePlatform,
                l.LastActivityAt,
                l.CreatedAt,
                Contact = l.ContactId != null ? new
                {
                    Id = l.ContactId,
                    Name = db.Contacts.Where(c => c.Id == l.ContactId).Select(c => c.DisplayName).FirstOrDefault(),
                    Phone = db.Contacts.Where(c => c.Id == l.ContactId).Select(c => c.Phone).FirstOrDefault(),
                    Email = db.Contacts.Where(c => c.Id == l.ContactId).Select(c => c.Email).FirstOrDefault(),
                } : null,
            })
            .FirstOrDefaultAsync(ct);

        if (lead is null) return Results.NotFound();

        var activities = await db.LeadActivities
            .Where(a => a.LeadId == id)
            .OrderByDescending(a => a.OccurredAt)
            .Take(20)
            .Select(a => new { a.ActivityType, a.Notes, a.OccurredAt })
            .ToListAsync(ct);

        var nextStep = lead.Stage switch
        {
            "hot" => "Schedule demo or send payment link",
            "warm" => "Follow up with pricing info or trial invite",
            "cold" => "Re-engage with content or special offer",
            "customer" => "Upsell advanced course or referral program",
            "lost" => "Win-back campaign after 30 days",
            _ => "Monitor and follow up",
        };

        return Results.Ok(new
        {
            lead.Id,
            lead.Score,
            lead.Stage,
            lead.SourcePlatform,
            lead.LastActivityAt,
            lead.CreatedAt,
            lead.Contact,
            activities,
            nextStep,
        });
    }
}
