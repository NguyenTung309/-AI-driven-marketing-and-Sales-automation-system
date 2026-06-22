using System.Text;
using System.Security.Claims;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Inbox;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Inbox;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clawbot.Domain.Channels;

namespace Clawbot.Api.Endpoints;

public static class InboxEndpoints
{
    public static IEndpointRouteBuilder MapInbox(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/inbox").RequireRateLimiting(RateLimitingExtensions.ChatPolicy);

        grp.MapGet("/search", SearchAsync).RequirePermission("conversations:read");
        grp.MapGet("/conversations", ListAsync).RequirePermission("conversations:read");
        grp.MapGet("/conversations/{id:guid}", GetAsync).RequirePermission("conversations:read");
        grp.MapGet("/conversations/{id:guid}/export.csv", ExportCsvAsync).RequirePermission("conversations:read");
        grp.MapPost("/conversations/{id:guid}/assign", AssignAsync).RequirePermission("conversations:write");
        grp.MapPost("/conversations/{id:guid}/resolve", ResolveAsync).RequirePermission("conversations:write");
        grp.MapPost("/conversations/{id:guid}/escalate", EscalateAsync).RequirePermission("conversations:write");
        grp.MapPost("/conversations/{id:guid}/messages", SendOutboundAsync).RequirePermission("conversations:write");
        grp.MapGet("/channels", ListChannelsAsync).RequirePermission("conversations:read");

        return app;
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        ClaimsPrincipal user,
        IPermissionResolver permResolver,
        IUserInboxResolver resolver,
        [FromQuery] string? status,
        [FromQuery] string? platform,
        [FromQuery] Guid? inboxId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        if (pageSize is < 1 or > 200) pageSize = 50;
        if (page < 1) page = 1;

        var inboxIds = await resolver.GetInboxIdsAsync(user, ct);
        var query = db.Conversations.AsNoTracking().AsQueryable();
        if (inboxIds.Count > 0)
            query = query.Where(c => c.InboxId != null && inboxIds.Contains(c.InboxId.Value));

        if (inboxId.HasValue) query = query.Where(c => c.InboxId == inboxId);
        if (!string.IsNullOrEmpty(status)) query = query.Where(c => c.Status == status);
        if (!string.IsNullOrEmpty(platform)) query = query.Where(c => c.Platform == platform);

        var total = await query.CountAsync(ct).ConfigureAwait(false);

        var rows = await query
            .OrderByDescending(c => db.Leads.Where(l => l.ContactId == c.ContactId).Max(l => (int?)l.Score) ?? 0)
            .ThenByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id,
                c.Platform,
                c.ExternalThreadId,
                c.Status,
                c.ContactId,
                c.AssignedTo,
                c.LastMessageAt,
                RowVersion = c.RowVersion ?? Array.Empty<byte>(),
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
            r.RowVersion,
            UnreadCount: 0)).ToList();

        return Results.Ok(new ConversationListResponse(items, total, page, pageSize));
    }

    private static async Task<IResult> GetAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants,
        ClaimsPrincipal user, IPermissionResolver permResolver,
        IUserInboxResolver resolver,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var conv = await db.Conversations.AsNoTracking()
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();

        var inboxIds = await resolver.GetInboxIdsAsync(user, ct);
        if (inboxIds.Count > 0 && conv.InboxId.HasValue && !inboxIds.Contains(conv.InboxId.Value))
            return Results.Forbid();

        var contactName = conv.ContactId is null ? null
            : await db.Contacts.AsNoTracking().Where(c => c.Id == conv.ContactId)
                .Select(c => c.DisplayName).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var messages = conv.Messages.OrderBy(m => m.SentAt)
            .Select(m => new MessageDto(m.Id, m.Direction, m.SenderType, m.SenderUserId, m.Content, m.ContentType, m.SentAt))
            .ToList();

        return Results.Ok(new ConversationDetailDto(
            conv.Id, conv.Platform, conv.ExternalThreadId, conv.Status, conv.ContactId,
            contactName, conv.AssignedTo, conv.LastMessageAt, conv.CreatedAt,
            conv.RowVersion, messages));
    }

    private static async Task<IResult> SearchAsync(
        InboxSearchService search, ITenantAccessor tenants,
        ClaimsPrincipal user, IPermissionResolver permResolver,
        IUserInboxResolver resolver,
        [FromQuery] string? q, [FromQuery] string? status, [FromQuery] string? platform,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Results.BadRequest(new { error = "query_required" });

        var tenantId = tenants.Require().TenantId;
        var inboxIds = await resolver.GetInboxIdsAsync(user, ct);
        var result = await search.SearchAsync(tenantId, q, status, platform, page, pageSize, inboxIds, ct).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> ExportCsvAsync(
        Guid id, ITenantAccessor tenants, ConversationExportService exporter, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var export = await exporter.ExportCsvAsync(tenant.TenantId, id, ct).ConfigureAwait(false);
        if (export is null) return Results.NotFound();
        return Results.File(Encoding.UTF8.GetBytes(export.Content), "text/csv; charset=utf-8", export.FileName);
    }

    private static async Task<IResult> AssignAsync(
        Guid id, AssignConversationRequest body,
        AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier,
        ClaimsPrincipal user, IPermissionResolver permResolver,
        IUserInboxResolver resolver,
        [FromHeader(Name = "If-Match")] byte[]? expectedVersion,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var inboxIds = await resolver.GetInboxIdsAsync(user, ct);
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();

        if (inboxIds.Count > 0 && conv.InboxId.HasValue && !inboxIds.Contains(conv.InboxId.Value))
            return Results.Forbid();

        // Validate assignee thuoc InboxMembers cua conversation
        if (conv.InboxId.HasValue)
        {
            var isMember = await db.InboxMembers
                .AnyAsync(m => m.InboxId == conv.InboxId.Value && m.AgentId == body.UserId, ct);
            if (!isMember)
                return Results.BadRequest(new { error = "agent_not_in_inbox", message = "Assignee khong thuoc inbox nay" });
        }

        if (expectedVersion != null && conv.RowVersion != null && !conv.RowVersion.SequenceEqual(expectedVersion))
            return Results.Conflict(new { error = "concurrency_conflict", message = "Trang thai da thay doi, vui long tai lai" });

        conv.Assign(body.UserId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.NotifyConversationUpdatedAsync(tenant.TenantId,
            new InboxConversationEvent(conv.Id, conv.Status, conv.AssignedTo, conv.LastMessageAt), ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ResolveAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier,
        ClaimsPrincipal user, IPermissionResolver permResolver,
        IUserInboxResolver resolver,
        [FromHeader(Name = "If-Match")] byte[]? expectedVersion,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var inboxIds = await resolver.GetInboxIdsAsync(user, ct);
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();

        if (inboxIds.Count > 0 && conv.InboxId.HasValue && !inboxIds.Contains(conv.InboxId.Value))
            return Results.Forbid();

        if (expectedVersion != null && conv.RowVersion != null && !conv.RowVersion.SequenceEqual(expectedVersion))
            return Results.Conflict(new { error = "concurrency_conflict", message = "Trang thai da thay doi, vui long tai lai" });

        conv.Resolve();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.NotifyConversationUpdatedAsync(tenant.TenantId,
            new InboxConversationEvent(conv.Id, conv.Status, conv.AssignedTo, conv.LastMessageAt), ct).ConfigureAwait(false);
        BackgroundJob.Enqueue<AutoSummaryJob>(j => j.RunAsync(tenant.TenantId, id, CancellationToken.None));
        return Results.NoContent();
    }

    private static async Task<IResult> EscalateAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier,
        ClaimsPrincipal user, IPermissionResolver permResolver,
        IUserInboxResolver resolver,
        [FromHeader(Name = "If-Match")] byte[]? expectedVersion,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var inboxIds = await resolver.GetInboxIdsAsync(user, ct);
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();

        if (inboxIds.Count > 0 && conv.InboxId.HasValue && !inboxIds.Contains(conv.InboxId.Value))
            return Results.Forbid();

        if (expectedVersion != null && conv.RowVersion != null && !conv.RowVersion.SequenceEqual(expectedVersion))
            return Results.Conflict(new { error = "concurrency_conflict", message = "Trang thai da thay doi, vui long tai lai" });

        conv.Escalate();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.NotifyConversationUpdatedAsync(tenant.TenantId,
            new InboxConversationEvent(conv.Id, conv.Status, conv.AssignedTo, conv.LastMessageAt), ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> SendOutboundAsync(
        Guid id, SendMessageRequest body,
        AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier,
        IChannelAdapter adapter, OutboundMessageSafetyService safety, IClock clock,
        ClaimsPrincipal user, IPermissionResolver permResolver,
        IUserInboxResolver resolver,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.Content)) return Results.BadRequest(new { error = "Content required" });

        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();

        var inboxIds = await resolver.GetInboxIdsAsync(user, ct);
        if (inboxIds.Count > 0 && conv.InboxId.HasValue && !inboxIds.Contains(conv.InboxId.Value))
            return Results.Forbid();

        try { await safety.EnsureAllowedAsync(body.Content, ct).ConfigureAwait(false); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }

        await adapter.SendAsync(conv.ExternalThreadId, body.Content, ct).ConfigureAwait(false);
        var msg = conv.AppendMessage("out", "user", body.Content, body.ContentType, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await notifier.NotifyMessageAsync(tenant.TenantId,
            new InboxMessageEvent(conv.Id, msg.Id, msg.Direction, msg.SenderType, msg.Content, msg.ContentType, msg.SentAt, conv.AssignedTo), ct).ConfigureAwait(false);

        return Results.Ok(new MessageDto(msg.Id, msg.Direction, msg.SenderType, msg.SenderUserId, msg.Content, msg.ContentType, msg.SentAt));
    }

    private static string Preview(string text) =>
        text.Length <= 140 ? text : text[..140] + "...";

    private static async Task<IResult> ListChannelsAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        ClaimsPrincipal user,
        IPermissionResolver permResolver,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var uid = Guid.Parse(userId!);
        var roleId = user.FindFirstValue("role_id");
        Guid.TryParse(roleId, out var rid);
        var perms = await permResolver.GetPermissionsAsync(rid, ct);
        var isAdmin = perms.Contains("admin:inboxes");

        IQueryable<Inbox> inboxQuery;
        if (isAdmin)
        {
            inboxQuery = db.Inboxes.Where(i => i.TenantId == tenant.TenantId && i.DeletedAt == null);
        }
        else
        {
            var inboxIds = await db.InboxMembers
                .Where(m => m.AgentId == uid)
                .Select(m => m.InboxId)
                .ToListAsync(ct);
            inboxQuery = db.Inboxes.Where(i => inboxIds.Contains(i.Id) && i.DeletedAt == null);
        }

        var channels = await inboxQuery
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.Platform,
                i.ExternalPageId,
                i.IsActive,
                i.AvatarUrl,
                HasToken = i.EncryptedAccessToken != null,
                UnreadCount = db.Conversations.Count(c => c.InboxId == i.Id && c.Status == "open"),
                MemberDisplayName = db.InboxMembers
                    .Where(m => m.InboxId == i.Id)
                    .Join(db.Users, m => m.AgentId, u => u.Id, (m, u) => u.DisplayName)
                    .FirstOrDefault()
            })
            .OrderByDescending(i => i.UnreadCount)
            .ThenBy(i => i.Name)
            .ToListAsync(ct);

        return Results.Ok(channels);
    }

}
