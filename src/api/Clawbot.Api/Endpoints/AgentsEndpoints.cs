using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

// M25 — agent control & observability over the existing AgentConfig (`agents` table).
public static class AgentsEndpoints
{
    public static IEndpointRouteBuilder MapAgents(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/agents").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/", ListAsync);
        grp.MapPost("/{code}/enable", EnableAsync);
        grp.MapPost("/{code}/disable", DisableAsync);
        grp.MapGet("/{code}/traces", TracesAsync);

        return grp;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct = default)
    {
        _ = tenants.Require(); // AgentConfigs are tenant query-filtered automatically
        var agents = await db.AgentConfigs
            .Where(a => a.DeletedAt == null)
            .OrderBy(a => a.Code)
            .Select(a => new
            {
                a.Code,
                a.DisplayName,
                a.AgentType,
                a.Model,
                a.Status,
                a.UpdatedAt,
                LastRunAt = db.AgentSessions.Where(s => s.AgentId == a.Id).Max(s => (DateTimeOffset?)s.StartedAt),
            })
            .ToListAsync(ct);

        return Results.Ok(new { items = agents });
    }

    private static async Task<IResult> SetStatusAsync(
        string code, bool enable, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var agent = await db.AgentConfigs.FirstOrDefaultAsync(a => a.Code == code, ct);
        if (agent is null) return Results.NotFound();

        if (enable) agent.Start();
        else agent.Stop();
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { agent.Code, agent.Status });
    }

    private static Task<IResult> EnableAsync(string code, AppDbContext db, ITenantAccessor tenants, CancellationToken ct = default)
        => SetStatusAsync(code, true, db, tenants, ct);

    private static Task<IResult> DisableAsync(string code, AppDbContext db, ITenantAccessor tenants, CancellationToken ct = default)
        => SetStatusAsync(code, false, db, tenants, ct);

    private static async Task<IResult> TracesAsync(
        string code,
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var agent = await db.AgentConfigs.FirstOrDefaultAsync(a => a.Code == code, ct);
        if (agent is null) return Results.NotFound();

        // Sessions are tenant query-filtered, so scoping traces via their session ids is tenant-safe.
        var sessionIds = db.AgentSessions.Where(s => s.AgentId == agent.Id).Select(s => s.Id);
        var query = db.AgentTraces.Where(t => sessionIds.Contains(t.SessionId));

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(t => t.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new { t.Id, t.SessionId, t.AgentName, t.Phase, t.Message, t.OccurredAt })
            .ToListAsync(ct);

        return Results.Ok(new { total, page, pageSize, items });
    }
}
