using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

// Store cho Tệp kỹ năng (.md) tái sử dụng: agent chọn từ kho + upload file mới thay vì gõ tên tay.
public sealed record SkillFileDto(Guid Id, string Name, string? Description, int SizeBytes, DateTimeOffset UpdatedAt);
public sealed record SkillFileDetailDto(Guid Id, string Name, string? Description, string ContentMd, DateTimeOffset UpdatedAt);
public sealed record CreateSkillFileRequest(string Name, string? Description, string ContentMd);
public sealed record UpdateSkillFileRequest(string? Description, string ContentMd);

public static class SkillsEndpoints
{
    private const int MaxContentChars = 100_000;

    public static IEndpointRouteBuilder MapSkills(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/skills").RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);
        grp.MapGet("/", ListAsync).RequirePermission("agent.read");
        grp.MapGet("/{id:guid}", GetAsync).RequirePermission("agent.read");
        grp.MapPost("/", CreateAsync).RequirePermission("agent.write");
        grp.MapPut("/{id:guid}", UpdateAsync).RequirePermission("agent.write");
        grp.MapDelete("/{id:guid}", DeleteAsync).RequirePermission("agent.write");
        return app;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var rows = await db.SkillFiles
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.DeletedAt == null)
            .OrderBy(s => s.Name)
            .Select(s => new SkillFileDto(s.Id, s.Name, s.Description, s.ContentMd.Length, s.UpdatedAt))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var skill = await db.SkillFiles.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId && s.DeletedAt == null, ct).ConfigureAwait(false);
        return skill is null
            ? Results.NotFound()
            : Results.Ok(new SkillFileDetailDto(skill.Id, skill.Name, skill.Description, skill.ContentMd, skill.UpdatedAt));
    }

    private static async Task<IResult> CreateAsync(CreateSkillFileRequest req, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var name = req.Name?.Trim() ?? string.Empty;
        if (name.Length == 0) return Results.BadRequest(new { error = "name_required" });
        if (name.Length > 128) return Results.BadRequest(new { error = "name_too_long" });
        if ((req.ContentMd?.Length ?? 0) > MaxContentChars) return Results.BadRequest(new { error = "content_too_large" });

        var exists = await db.SkillFiles.AnyAsync(s => s.TenantId == tenantId && s.Name == name && s.DeletedAt == null, ct).ConfigureAwait(false);
        if (exists) return Results.Conflict(new { error = "name_exists" });

        var skill = SkillFile.Create(tenantId, name, req.Description?.Trim(), req.ContentMd ?? string.Empty, clock.UtcNow);
        db.SkillFiles.Add(skill);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"/api/skills/{skill.Id}", new SkillFileDto(skill.Id, skill.Name, skill.Description, skill.ContentMd.Length, skill.UpdatedAt));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateSkillFileRequest req, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        if ((req.ContentMd?.Length ?? 0) > MaxContentChars) return Results.BadRequest(new { error = "content_too_large" });
        var skill = await db.SkillFiles.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId && s.DeletedAt == null, ct).ConfigureAwait(false);
        if (skill is null) return Results.NotFound();
        skill.Update(req.Description?.Trim(), req.ContentMd ?? string.Empty, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new SkillFileDto(skill.Id, skill.Name, skill.Description, skill.ContentMd.Length, skill.UpdatedAt));
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var skill = await db.SkillFiles.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId && s.DeletedAt == null, ct).ConfigureAwait(false);
        if (skill is null) return Results.NotFound();
        skill.SoftDelete(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }
}
