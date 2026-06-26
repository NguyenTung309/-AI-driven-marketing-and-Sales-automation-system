using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Competitors;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Competitors;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

// Research-2: admin CRUD for competitor feeds + read access to detected posts.
public static class CompetitorsEndpoints
{
    private const int MaxSourcesPerTenant = 20;

    public static IEndpointRouteBuilder MapCompetitors(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/competitors").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/sources", ListSourcesAsync).RequirePermission("content.read");
        grp.MapPost("/sources", CreateSourceAsync).RequirePermission("content.write");
        grp.MapPut("/sources/{id:guid}", UpdateSourceAsync).RequirePermission("content.write");
        grp.MapDelete("/sources/{id:guid}", DeleteSourceAsync).RequirePermission("content.write");
        grp.MapGet("/posts", ListPostsAsync).RequirePermission("content.read");

        return app;
    }

    private static async Task<IResult> ListSourcesAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var items = await db.CompetitorSources.AsNoTracking()
            .Where(s => s.DeletedAt == null)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new CompetitorSourceDto(s.Id, s.Name, s.Url, s.SourceType, s.IsActive, s.CreatedAt, s.LastScannedAt))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateSourceAsync(
        CreateCompetitorSourceRequest body, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.Url))
            return Results.BadRequest(new { error = "name and url required" });
        if (!Uri.TryCreate(body.Url, UriKind.Absolute, out _))
            return Results.BadRequest(new { error = "url must be an absolute URL" });

        var activeCount = await db.CompetitorSources.CountAsync(s => s.DeletedAt == null, ct).ConfigureAwait(false);
        if (activeCount >= MaxSourcesPerTenant)
            return Results.BadRequest(new { error = $"max {MaxSourcesPerTenant} competitor sources per tenant" });

        var src = CompetitorSource.Create(tenant.TenantId, body.Name.Trim(), body.Url.Trim(), body.SourceType ?? "rss", clock.UtcNow);
        db.CompetitorSources.Add(src);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"/api/competitors/sources/{src.Id}",
            new CompetitorSourceDto(src.Id, src.Name, src.Url, src.SourceType, src.IsActive, src.CreatedAt, src.LastScannedAt));
    }

    private static async Task<IResult> UpdateSourceAsync(
        Guid id, UpdateCompetitorSourceRequest body, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.Url))
            return Results.BadRequest(new { error = "name and url required" });

        var src = await db.CompetitorSources.FirstOrDefaultAsync(s => s.Id == id && s.DeletedAt == null, ct).ConfigureAwait(false);
        if (src is null) return Results.NotFound();

        src.Update(body.Name.Trim(), body.Url.Trim(), body.SourceType ?? src.SourceType, body.IsActive);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteSourceAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        _ = tenants.Require();
        var src = await db.CompetitorSources.FirstOrDefaultAsync(s => s.Id == id && s.DeletedAt == null, ct).ConfigureAwait(false);
        if (src is null) return Results.NotFound();

        src.SoftDelete(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ListPostsAsync(
        AppDbContext db, ITenantAccessor tenants, Guid? sourceId, int? take, CancellationToken ct)
    {
        _ = tenants.Require();
        var limit = Math.Clamp(take ?? 100, 1, 200);
        var query = db.CompetitorPosts.AsNoTracking().AsQueryable();
        if (sourceId is not null) query = query.Where(p => p.SourceId == sourceId.Value);

        var items = await query
            .OrderByDescending(p => p.DetectedAt)
            .Take(limit)
            .Select(p => new CompetitorPostDto(p.Id, p.SourceId, p.Url, p.Title, p.Snippet, p.PublishedAt, p.DetectedAt))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(items);
    }
}
