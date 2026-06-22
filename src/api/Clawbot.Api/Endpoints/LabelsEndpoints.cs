using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Domain.ChatScenarios;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Clawbot.Infrastructure.Auth;

namespace Clawbot.Api.Endpoints;

public sealed record CreateLabelRequest(string Name, string Color);
public sealed record UpdateLabelRequest(string Name, string Color);

public static class LabelsEndpoints
{
    public static IEndpointRouteBuilder MapLabels(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/labels")
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy)
            .RequirePermission("conversations:write");

        grp.MapGet("/", ListAsync);
        grp.MapPost("/", CreateAsync);
        grp.MapPut("/{id:guid}", UpdateAsync);
        grp.MapDelete("/{id:guid}", DeleteAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var labels = await db.Labels.AsNoTracking()
            .Where(l => l.TenantId == tenant.TenantId && l.DeletedAt == null)
            .OrderBy(l => l.Name)
            .Select(l => new { l.Id, l.Name, l.Color, l.CreatedAt })
            .ToListAsync(ct);
        return Results.Ok(labels);
    }

    private static async Task<IResult> CreateAsync(
        CreateLabelRequest body, AppDbContext db, ITenantAccessor tenants,
        ClaimsPrincipal user, IPermissionResolver permResolver, CancellationToken ct)
    {
        var tenant = tenants.Require();

        var roleLabel = user.FindFirstValue("role_id");
        if (Guid.TryParse(roleLabel, out var ridLabel))
        {
            var permsLabel = await permResolver.GetPermissionsAsync(ridLabel, ct);
            if (permsLabel.Contains("admin:inboxes"))
                return Results.Forbid();
        }

        var exists = await db.Labels.AnyAsync(l => l.TenantId == tenant.TenantId && l.Name == body.Name && l.DeletedAt == null, ct);
        if (exists) return Results.Conflict(new { error = "label_exists", message = "Nhan da ton tai" });

        var label = Label.Create(tenant.TenantId, body.Name, body.Color);
        db.Labels.Add(label);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/labels/{label.Id}", new { label.Id, label.Name, label.Color, label.CreatedAt });
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateLabelRequest body, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var label = await db.Labels.FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenant.TenantId, ct);
        if (label is null) return Results.NotFound();

        var exists = await db.Labels.AnyAsync(l => l.TenantId == tenant.TenantId && l.Name == body.Name && l.Id != id && l.DeletedAt == null, ct);
        if (exists) return Results.Conflict(new { error = "label_exists", message = "Ten nhan da ton tai" });

        label.Update(body.Name, body.Color);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var label = await db.Labels.FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenant.TenantId, ct);
        if (label is null) return Results.NotFound();

        label.SoftDelete();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}