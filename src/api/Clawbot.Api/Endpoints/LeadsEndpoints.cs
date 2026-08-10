using System.Text;
using Clawbot.Agents.Contracts.Lead;
using Clawbot.Agents.Core.Lead;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Api.Auth;
using Clawbot.Api.Common.Pagination;
using Clawbot.Api.Contracts.Common;
using Clawbot.Api.Contracts.Leads;
using Clawbot.Api.Jobs;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.AspNetCore.Identity;
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
        grp.MapPut("/{id:guid}/stage", UpdateStageAsync).RequirePermission("leads:write");
        grp.MapPost("/{id:guid}/assign", AssignAsync).RequirePermission("leads:write");
        grp.MapGet("/forecast", ForecastAsync).RequirePermission("leads:read");
        grp.MapGet("/{id:guid}/context", ContextPanelAsync).RequirePermission("leads:read");
        grp.MapPost("/rescore", RescoreAsync).RequirePermission("leads:write");

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
        Clawbot.Infrastructure.Leads.LeadBatchRescorer rescorer,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var created = await rescorer.EnsureDefaultRulesAsync(tenantId, ct).ConfigureAwait(false);
        return Results.Ok(new { created, total = LeadScoringDefaults.Rules.Count });
    }

    // Batch rescore all tenant leads from inbound message history + scoring rules.
    private static async Task<IResult> RescoreAsync(
        Clawbot.Infrastructure.Leads.LeadBatchRescorer rescorer,
        ITenantAccessor tenants,
        [FromQuery] int topN = 5,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        var result = await rescorer.RescoreTenantAsync(tenantId, topN, ct).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] string? stage,
        [FromQuery] string? q,
        [FromQuery] string? source,
        [FromQuery] string? owner,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        var req = PageRequest.Create(page, pageSize);

        var query = db.Leads.AsNoTracking().Where(l => l.DeletedAt == null);
        if (!string.IsNullOrEmpty(stage)) query = query.Where(l => l.Stage == stage);
        if (!string.IsNullOrWhiteSpace(source) && !string.Equals(source, "all", StringComparison.OrdinalIgnoreCase))
            query = query.Where(l => l.SourcePlatform == source);
        if (string.Equals(owner, "assigned", StringComparison.OrdinalIgnoreCase))
            query = query.Where(l => l.OwnerUserId != null);
        else if (string.Equals(owner, "unassigned", StringComparison.OrdinalIgnoreCase))
            query = query.Where(l => l.OwnerUserId == null);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = "%" + q.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
            query = query.Where(l =>
                db.Contacts.Any(c => c.Id == l.ContactId && (
                    EF.Functions.Like(c.DisplayName, pattern) ||
                    (c.Phone != null && EF.Functions.Like(c.Phone, pattern)) ||
                    (c.Email != null && EF.Functions.Like(c.Email, pattern)))));
        }

        var ordered = query
            .OrderByDescending(l => l.Score)
            .ThenByDescending(l => l.LastActivityAt ?? l.CreatedAt)
            .Select(l => new LeadDto(l.Id, l.ContactId, l.OwnerUserId, l.Score, l.Stage, l.SourcePlatform, l.LastActivityAt, l.CreatedAt,
                db.Contacts.Where(c => c.Id == l.ContactId).Select(c => c.DisplayName).FirstOrDefault(),
                db.Contacts.Where(c => c.Id == l.ContactId).Select(c => c.Phone).FirstOrDefault(),
                db.Users.Where(u => u.Id == l.OwnerUserId).Select(u => u.DisplayName).FirstOrDefault()));

        var result = await ordered.ToPagedResultAsync(req.Page, req.PageSize, ct: ct).ConfigureAwait(false);
        return Results.Ok(result);
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

    // Tạo lead có chấm điểm/enrich/dedup bằng agent (LLM) -> job. Contact phải tồn tại: kiểm ngay
    // để lỗi nhập liệu trả 400 tức thì, phần agent mới đẩy sang nền.
    private static async Task<IResult> CreateWithSkillsAsync(
        CreateLeadRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IJobLauncher jobs,
        HttpContext http,
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

        var jobId = await jobs.LaunchAsync(
            LeadCreateWithSkillsJobHandler.JobType,
            $"Tạo lead: {contact.DisplayName}",
            new LeadCreateWithSkillsJobPayload(
                body.ContactId,
                contact.DisplayName ?? string.Empty,
                body.Phone,
                body.Email,
                body.SourcePlatform,
                contact.Locale ?? string.Empty),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    private static Guid? CurrentUserId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
        && id != Guid.Empty
            ? id
            : null;

    /// <summary>
    /// Object-level: lead đã gán owner thì chỉ owner / Admin / SalesLead được ghi stage.
    /// Lead chưa gán: bất kỳ ai có leads:write (đã gate ở route) đều được.
    /// JWT chỉ mang role_id (SPEC-11) — không dùng IsInRole/role-name claim.
    /// </summary>
    private static bool CanManageLead(HttpContext http, Lead lead)
    {
        var userId = CurrentUserId(http);
        if (userId is null)
            return false;
        if (lead.OwnerUserId is null || lead.OwnerUserId == userId)
            return true;
        return IsLeadManager(http);
    }

    private static bool IsLeadManager(HttpContext http)
    {
        var roleIdRaw = http.User.FindFirst("role_id")?.Value;
        if (!Guid.TryParse(roleIdRaw, out var roleId) || roleId == Guid.Empty)
            return false;
        return roleId == RbacSeeder.RoleIds[RbacSeeder.Admin]
            || roleId == RbacSeeder.RoleIds[RbacSeeder.SalesLead];
    }

    private static Guid? CurrentRoleId(HttpContext http)
    {
        var roleIdRaw = http.User.FindFirst("role_id")?.Value;
        return Guid.TryParse(roleIdRaw, out var roleId) && roleId != Guid.Empty ? roleId : null;
    }

    private static async Task<IResult> RecordActivityAsync(
        Guid id,
        LeadActivityRequest body,
        HttpContext http,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (lead is null) return Results.NotFound();

        if (string.Equals(body.EventCode, "payment_confirmed", StringComparison.OrdinalIgnoreCase))
        {
            // Event code giữ nguyên vì là hợp đồng API với hệ ngoài; nó chỉ còn đổi lifecycle
            // sang customer, không kéo theo ghi nhận số tiền nữa.
            if (!CanManageLead(http, lead))
                return Results.Json(new { error = "lead_not_owned" }, statusCode: StatusCodes.Status403Forbidden);

            lead.MarkCustomer(
                body.Notes ?? "payment_confirmed",
                clock.UtcNow,
                CurrentUserId(http),
                trigger: "payment_event");
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "lead_stage_changed" });
            }

            return Results.Ok(new LeadActivityResponse(lead.Score, lead.Stage, "payment_confirmed", []));
        }

        var rules = await db.LeadScoringRules
            .Where(r => r.IsActive)
            .ToListAsync(ct).ConfigureAwait(false);

        var decision = LeadScoringEngine.Evaluate(body.EventCode, body.Platform ?? lead.SourcePlatform, rules);
        if (decision.Delta != 0)
            lead.AdjustScore(decision.Delta, body.Notes ?? decision.Reason, clock.UtcNow);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new LeadActivityResponse(lead.Score, lead.Stage, decision.Reason, decision.MatchedRules));
    }

    private static async Task<IResult> UpdateStageAsync(
        Guid id,
        LeadStageRequest body,
        HttpContext http,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (lead is null) return Results.NotFound();
        if (!CanManageLead(http, lead))
            return Results.Json(new { error = "lead_not_owned" }, statusCode: StatusCodes.Status403Forbidden);

        var stage = body.Stage?.Trim().ToLowerInvariant();
        var reason = string.IsNullOrWhiteSpace(body.Reason) ? stage : body.Reason.Trim();

        switch (stage)
        {
            case "customer":
                lead.MarkCustomer(reason ?? "customer", clock.UtcNow, CurrentUserId(http));
                break;
            case "lost":
                lead.MarkLost(reason ?? "lost", clock.UtcNow, CurrentUserId(http));
                break;
            case "reopen":
                lead.ReopenStage(reason ?? "reopen", clock.UtcNow, CurrentUserId(http));
                break;
            default:
                return Results.BadRequest(new { error = "invalid_stage_action" });
        }

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { error = "lead_stage_changed" });
        }

        return Results.Ok(new LeadStageResponse(lead.Score, lead.Stage));
    }

    private static async Task<IResult> AssignAsync(
        Guid id,
        LeadAssignRequest body,
        HttpContext http,
        AppDbContext db,
        ITenantAccessor tenants,
        ILeadAssignmentService assignment,
        UserManager<AppUser> users,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (lead is null) return Results.NotFound();

        var actorId = CurrentUserId(http);
        if (actorId is null)
            return Results.Unauthorized();

        // Đổi owner khi đã gán: chỉ Admin/SalesLead. Unowned: sale chỉ claim chính mình; manager gán bất kỳ.
        var isManager = IsLeadManager(http);
        if (lead.OwnerUserId is { } existingOwner && existingOwner != actorId && !isManager)
            return Results.Json(new { error = "lead_not_owned" }, statusCode: StatusCodes.Status403Forbidden);

        Guid owner;
        if (body.UserId is { } requested && requested != Guid.Empty)
        {
            if (!isManager && requested != actorId)
                return Results.Json(new { error = "can_only_claim_self" }, statusCode: StatusCodes.Status403Forbidden);

            var assignee = await users.Users
                .FirstOrDefaultAsync(u => u.Id == requested, ct)
                .ConfigureAwait(false);
            if (assignee is null || assignee.TenantId != tenant.TenantId || !assignee.IsActive)
                return Results.BadRequest(new { error = "assignee_not_eligible" });

            // Phải thuộc role được nhận lead (Sale / SalesLead / Admin).
            var roles = await users.GetRolesAsync(assignee).ConfigureAwait(false);
            var canOwn = roles.Any(r =>
                string.Equals(r, RbacSeeder.Sale, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r, RbacSeeder.SalesLead, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r, RbacSeeder.Admin, StringComparison.OrdinalIgnoreCase));
            if (!canOwn)
                return Results.BadRequest(new { error = "assignee_role_not_allowed" });

            owner = requested;
        }
        else
        {
            if (!isManager)
            {
                // Sale không truyền userId → tự claim.
                owner = actorId.Value;
            }
            else
            {
                var picked = await assignment.PickOwnerAsync(tenant.TenantId, ct).ConfigureAwait(false);
                if (picked is null)
                    return Results.BadRequest(new { error = "no eligible assignee" });
                owner = picked.Value;
            }
        }

        lead.Assign(owner);
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
