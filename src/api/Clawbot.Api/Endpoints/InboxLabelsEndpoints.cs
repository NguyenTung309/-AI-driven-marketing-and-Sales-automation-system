using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Clawbot.Infrastructure.Auth;

namespace Clawbot.Api.Endpoints;

public sealed record AttachLabelRequest(Guid LabelId);

public static class InboxLabelsEndpoints
{
    public static IEndpointRouteBuilder MapInboxLabels(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/inbox/conversations/{conversationId:guid}/labels")
            .RequireRateLimiting(RateLimitingExtensions.ChatPolicy)
            .RequirePermission("conversations:write");

        grp.MapGet("/", ListAsync);
        grp.MapPost("/", AttachAsync);
        grp.MapDelete("/{labelId:guid}", DetachAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid conversationId, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var conv = await db.Conversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.TenantId == tenant.TenantId, ct);
        if (conv is null) return Results.NotFound();

        var labels = await db.ConversationLabels.AsNoTracking()
            .Where(cl => cl.ConversationId == conversationId)
            .Join(db.Labels, cl => cl.LabelId, l => l.Id, (cl, l) => new { l.Id, l.Name, l.Color, cl.CreatedAt })
            .ToListAsync(ct);
        return Results.Ok(labels);
    }

    private static async Task<IResult> AttachAsync(
        Guid conversationId, AttachLabelRequest body,
        AppDbContext db, ITenantAccessor tenants,
        ClaimsPrincipal user, IPermissionResolver permResolver,
        CancellationToken ct)
    {
        var tenant = tenants.Require();

        var roleLabel = user.FindFirstValue("role_id");
        if (Guid.TryParse(roleLabel, out var ridLabel))
        {
            var permsLabel = await permResolver.GetPermissionsAsync(ridLabel, ct);
            if (permsLabel.Contains("admin:inboxes"))
                return Results.Forbid();
        }

        var conv = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.TenantId == tenant.TenantId, ct);
        if (conv is null) return Results.NotFound();

        var label = await db.Labels
            .FirstOrDefaultAsync(l => l.Id == body.LabelId && l.TenantId == tenant.TenantId && l.DeletedAt == null, ct);
        if (label is null) return Results.NotFound(new { error = "label_not_found" });

        var exists = await db.ConversationLabels
            .AnyAsync(cl => cl.ConversationId == conversationId && cl.LabelId == body.LabelId, ct);
        if (exists) return Results.Conflict(new { error = "label_already_attached" });

        db.ConversationLabels.Add(ConversationLabel.Create(conversationId, body.LabelId));
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DetachAsync(
        Guid conversationId, Guid labelId,
        AppDbContext db, ITenantAccessor tenants,
        ClaimsPrincipal user, IPermissionResolver permResolver,
        CancellationToken ct)
    {
        var tenant = tenants.Require();

        var roleLabelDel = user.FindFirstValue("role_id");
        if (Guid.TryParse(roleLabelDel, out var ridLabelDel))
        {
            var permsLabelDel = await permResolver.GetPermissionsAsync(ridLabelDel, ct);
            if (permsLabelDel.Contains("admin:inboxes"))
                return Results.Forbid();
        }

        var conv = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.TenantId == tenant.TenantId, ct);
        if (conv is null) return Results.NotFound();

        var cl = await db.ConversationLabels
            .FirstOrDefaultAsync(x => x.ConversationId == conversationId && x.LabelId == labelId, ct);
        if (cl is null) return Results.NotFound();

        db.ConversationLabels.Remove(cl);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}