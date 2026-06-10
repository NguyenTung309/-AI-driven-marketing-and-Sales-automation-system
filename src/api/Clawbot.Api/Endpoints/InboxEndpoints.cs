using Clawbot.Api.Contracts.Inbox;
using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Inbox;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class InboxEndpoints
{
    public static IEndpointRouteBuilder MapInbox(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/inbox").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.ChatPolicy);

        grp.MapGet("/conversations", ListAsync);
        grp.MapGet("/conversations/{id:guid}", GetAsync);
        grp.MapPost("/conversations/{id:guid}/assign", AssignAsync);
        grp.MapPost("/conversations/{id:guid}/resolve", ResolveAsync);
        grp.MapPost("/conversations/{id:guid}/escalate", EscalateAsync);
        grp.MapPost("/conversations/{id:guid}/messages", SendOutboundAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] string? status,
        [FromQuery] string? platform,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        if (pageSize is < 1 or > 200) pageSize = 50;
        if (page < 1) page = 1;

        var query = db.Conversations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(status)) query = query.Where(c => c.Status == status);
        if (!string.IsNullOrEmpty(platform)) query = query.Where(c => c.Platform == platform);

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        var rows = await query
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id, c.Platform, c.ExternalThreadId, c.Status, c.ContactId, c.AssignedTo, c.LastMessageAt,
                LastMessage = c.Messages.OrderByDescending(m => m.SentAt).Select(m => m.Content).FirstOrDefault(),
            })
            .ToListAsync(ct).ConfigureAwait(false);

        var contactIds = rows.Where(r => r.ContactId.HasValue).Select(r => r.ContactId!.Value).Distinct().ToList();
        var contactNames = await db.Contacts.AsNoTracking()
            .Where(c => contactIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct).ConfigureAwait(false);

        var items = rows.Select(r => new ConversationListItemDto(
            r.Id, r.Platform, r.ExternalThreadId, r.Status, r.ContactId,
            r.ContactId.HasValue && contactNames.TryGetValue(r.ContactId.Value, out var n) ? n : null,
            r.AssignedTo, r.LastMessageAt,
            r.LastMessage is null ? null : Preview(r.LastMessage),
            UnreadCount: 0)).ToList();

        return Results.Ok(new ConversationListResponse(items, total, page, pageSize));
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var conv = await db.Conversations.AsNoTracking()
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();

        var contactName = conv.ContactId is null
            ? null
            : await db.Contacts.AsNoTracking().Where(c => c.Id == conv.ContactId)
                .Select(c => c.DisplayName).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var messages = conv.Messages
            .OrderBy(m => m.SentAt)
            .Select(m => new MessageDto(m.Id, m.Direction, m.SenderType, m.SenderUserId, m.Content, m.ContentType, m.SentAt))
            .ToList();

        return Results.Ok(new ConversationDetailDto(
            conv.Id, conv.Platform, conv.ExternalThreadId, conv.Status, conv.ContactId,
            contactName, conv.AssignedTo, conv.LastMessageAt, conv.CreatedAt, messages));
    }

    private static async Task<IResult> AssignAsync(
        Guid id,
        AssignConversationRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IInboxNotifier notifier,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();
        conv.Assign(body.UserId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.NotifyConversationUpdatedAsync(tenant.TenantId,
            new InboxConversationEvent(conv.Id, conv.Status, conv.AssignedTo, conv.LastMessageAt), ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ResolveAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IInboxNotifier notifier,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();
        conv.Resolve();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.NotifyConversationUpdatedAsync(tenant.TenantId,
            new InboxConversationEvent(conv.Id, conv.Status, conv.AssignedTo, conv.LastMessageAt), ct).ConfigureAwait(false);

        // Auto-summary on resolve — enqueue as Hangfire background job (non-blocking)
        BackgroundJob.Enqueue<AutoSummaryJob>(j => j.RunAsync(tenant.TenantId, id, CancellationToken.None));

        return Results.NoContent();
    }

    private static async Task<IResult> EscalateAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();
        conv.Escalate();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.NotifyConversationUpdatedAsync(tenant.TenantId,
            new InboxConversationEvent(conv.Id, conv.Status, conv.AssignedTo, conv.LastMessageAt), ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> SendOutboundAsync(
        Guid id,
        SendMessageRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IInboxNotifier notifier,
        IChannelAdapter adapter,
        IClock clock,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.Content)) return Results.BadRequest(new { error = "Content required" });

        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();

        await adapter.SendAsync(conv.ExternalThreadId, body.Content, ct).ConfigureAwait(false);
        var msg = conv.AppendMessage("out", "user", body.Content, body.ContentType, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await notifier.NotifyMessageAsync(tenant.TenantId, new InboxMessageEvent(
            conv.Id, msg.Id, msg.Direction, msg.SenderType, msg.Content, msg.ContentType, msg.SentAt), ct).ConfigureAwait(false);

        return Results.Ok(new MessageDto(msg.Id, msg.Direction, msg.SenderType, msg.SenderUserId, msg.Content, msg.ContentType, msg.SentAt));
    }

    private static string Preview(string text) =>
        text.Length <= 140 ? text : text[..140] + "…";
}
