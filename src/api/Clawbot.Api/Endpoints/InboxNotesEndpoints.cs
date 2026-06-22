using System.Security.Claims;
using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clawbot.Infrastructure.Auth;

namespace Clawbot.Api.Endpoints;

public sealed record CreateNoteRequest(string Content, string? Type, string? CreatedByName);
public sealed record UpdateNoteRequest(string Content);

public static class InboxNotesEndpoints
{
    public static IEndpointRouteBuilder MapInboxNotes(this IEndpointRouteBuilder app)
    {
        var readGroup = app.MapGroup("/api/inbox/conversations/{conversationId:guid}/notes")
            .RequireRateLimiting(RateLimitingExtensions.ChatPolicy)
            .RequirePermission("conversations:read");
        var writeGroup = app.MapGroup("/api/inbox/conversations/{conversationId:guid}/notes")
            .RequireRateLimiting(RateLimitingExtensions.ChatPolicy)
            .RequirePermission("conversations:write");

        readGroup.MapGet("/", ListAsync);
        writeGroup.MapPost("/", CreateAsync);
        writeGroup.MapPut("/{id:guid}", UpdateAsync);
        writeGroup.MapDelete("/{id:guid}", DeleteAsync);

        return app;
    }

    private static Guid? CurrentUserId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static async Task<IResult> ListAsync(
        Guid conversationId, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var notes = await db.ConversationNotes.AsNoTracking()
            .Where(n => n.TenantId == tenant.TenantId && n.ConversationId == conversationId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new { n.Id, n.Content, n.Type, n.CreatedByUserId, n.CreatedByDisplayName, n.CreatedAt, n.UpdatedAt })
            .ToListAsync(ct);
        return Results.Ok(notes);
    }

    private static async Task<IResult> CreateAsync(
        Guid conversationId, CreateNoteRequest body,
        AppDbContext db, ITenantAccessor tenants, ClaimsPrincipal user, CancellationToken ct)
    {
        var tenant = tenants.Require();

        var roleNote = user.FindFirstValue("role_id");
        if (Guid.TryParse(roleNote, out var ridNote))
        {
            var permsNote = await permResolver.GetPermissionsAsync(ridNote, ct);
            if (permsNote.Contains("admin:inboxes"))
                return Results.Forbid();
        }

        var userId = CurrentUserId(user);
        if (userId is null) return Results.BadRequest(new { error = "invalid_user" });

        var note = ConversationNote.Create(
            tenant.TenantId, conversationId, userId.Value,
            body.Content, body.CreatedByName, body.Type ?? "private");
        db.ConversationNotes.Add(note);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/inbox/conversations/{conversationId}/notes/{note.Id}",
            new { note.Id, note.Content, note.Type, note.CreatedByUserId, note.CreatedByDisplayName, note.CreatedAt });
    }

    private static async Task<IResult> UpdateAsync(
        Guid conversationId, Guid id, UpdateNoteRequest body,
        AppDbContext db, ITenantAccessor tenants,
        ClaimsPrincipal user, IPermissionResolver permResolver, CancellationToken ct)
    {
        var tenant = tenants.Require();

        var roleNoteUpd = user.FindFirstValue("role_id");
        if (Guid.TryParse(roleNoteUpd, out var ridNoteUpd))
        {
            var permsNoteUpd = await permResolver.GetPermissionsAsync(ridNoteUpd, ct);
            if (permsNoteUpd.Contains("admin:inboxes"))
                return Results.Forbid();
        }

        var note = await db.ConversationNotes
            .FirstOrDefaultAsync(n => n.Id == id && n.TenantId == tenant.TenantId && n.ConversationId == conversationId, ct);
        if (note is null) return Results.NotFound();

        note.UpdateContent(body.Content);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid conversationId, Guid id,
        AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();

        var roleNoteUpd = user.FindFirstValue("role_id");
        if (Guid.TryParse(roleNoteUpd, out var ridNoteUpd))
        {
            var permsNoteUpd = await permResolver.GetPermissionsAsync(ridNoteUpd, ct);
            if (permsNoteUpd.Contains("admin:inboxes"))
                return Results.Forbid();
        }

        var note = await db.ConversationNotes
            .FirstOrDefaultAsync(n => n.Id == id && n.TenantId == tenant.TenantId && n.ConversationId == conversationId, ct);
        if (note is null) return Results.NotFound();

        db.ConversationNotes.Remove(note);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}