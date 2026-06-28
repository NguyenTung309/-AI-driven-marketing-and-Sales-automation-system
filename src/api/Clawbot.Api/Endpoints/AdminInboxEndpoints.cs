using System.Security.Claims;
using System.Text.Json;
using Clawbot.Api.Auth;
using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Inbox;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Clawbot.Domain.Security;

namespace Clawbot.Api.Endpoints;

public sealed record UpdateMemberRequest(Guid? AgentId);
public sealed record ReassignRequest(Guid NewAgentId);
public sealed record CreateInboxRequest(string Platform, string ExternalPageId, string? PageAccessToken, Guid? AgentId);

public static class AdminInboxEndpoints
{
    public static IEndpointRouteBuilder MapAdminInboxEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/admin")
            .RequireRateLimiting(Middleware.RateLimitingExtensions.GeneralPolicy)
            .RequirePermission("admin:inboxes");
        grp.MapGet("/users/simple", ListSimpleUsersAsync);
        grp.MapPut("/inboxes/{id:guid}/members", UpdateMemberAsync);
        grp.MapPost("/inboxes/{id:guid}/reassign", ReassignAsync);
        grp.MapGet("/inboxes/{id:guid}/members", ListMembersAsync);
        grp.MapGet("/inboxes/{id:guid}/assignable-agents", ListAssignableAgentsAsync);
        grp.MapGet("/inboxes", ListInboxesAsync);
        grp.MapPost("/inboxes", CreateInboxAsync);
        return app;
    }

    private static async Task<IResult> ListSimpleUsersAsync(
        AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var users = await db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenant.TenantId)
            .Select(u => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(ct);
        return Results.Ok(users);
    }

    private static async Task<IResult> UpdateMemberAsync(
        Guid id, UpdateMemberRequest body,
        AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier,
        IClock clock, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var inboxExists = await db.Inboxes.AnyAsync(i => i.Id == id && i.TenantId == tenantId, ct);
        if (!inboxExists) return Results.NotFound();

        if (body.AgentId == null)
        {
            var currentMembers = await db.InboxMembers.Where(m => m.InboxId == id).ToListAsync(ct);
            if (currentMembers.Count == 0)
                return Results.BadRequest(new { error = "inbox_must_have_member", message = "Kenh phai co it nhat 1 sale phu trach" });

            var oldIds = currentMembers.Select(m => m.AgentId).ToList();
            var conversations = await db.Conversations
                .Where(c => c.InboxId == id && oldIds.Contains(c.AssignedTo!.Value))
                .ToListAsync(ct);
            foreach (var conv in conversations)
                conv.Unassign();

            db.InboxMembers.RemoveRange(currentMembers);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }

        var agentExists = await db.Users.AnyAsync(u => u.Id == body.AgentId && u.TenantId == tenantId, ct);
        if (!agentExists) return Results.BadRequest(new { error = "agent_not_found" });

        var existing = await db.InboxMembers.Where(m => m.InboxId == id).ToListAsync(ct);
        var oldMembers = existing.Select(e => e.AgentId).ToList();
        db.InboxMembers.RemoveRange(existing);
        db.InboxMembers.Add(InboxMember.Create(tenantId, id, body.AgentId.Value));

        var oldConvs = await db.Conversations
            .Where(c => c.InboxId == id && oldMembers.Contains(c.AssignedTo!.Value))
            .ToListAsync(ct);
        foreach (var conv in oldConvs)
            conv.Unassign();

        await db.SaveChangesAsync(ct);
        foreach (var oldId in oldMembers)
            await notifier.NotifyConversationUpdatedAsync(tenantId,
                new InboxConversationEvent(id, "reassigned", null, null), ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ReassignAsync(
        Guid id, ReassignRequest body,
        AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier,
        ClaimsPrincipal user, IClock clock, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var adminUserId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var inbox = await db.Inboxes.FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId, ct);
        if (inbox is null) return Results.NotFound();

        var newAgent = await db.Users.FirstOrDefaultAsync(u => u.Id == body.NewAgentId && u.TenantId == tenantId, ct);
        if (newAgent is null) return Results.BadRequest(new { error = "agent_not_found" });

        var oldMembers = await db.InboxMembers.Where(m => m.InboxId == id).Select(m => m.AgentId).ToListAsync(ct);
        var existing = await db.InboxMembers.Where(m => m.InboxId == id).ToListAsync(ct);
        db.InboxMembers.RemoveRange(existing);
        db.InboxMembers.Add(InboxMember.Create(tenantId, id, body.NewAgentId));

        var convs = await db.Conversations
            .Where(c => c.InboxId == id && c.AssignedTo.HasValue && oldMembers.Contains(c.AssignedTo.Value))
            .ToListAsync(ct);
        foreach (var conv in convs)
            conv.Unassign();

        await db.SaveChangesAsync(ct);

        // Audit log
        db.AuditLogs.Add(AuditLog.Create(
            tenantId, adminUserId, "inbox:reassign", "Inbox", id, clock.UtcNow,
            JsonSerializer.Serialize(new { OldAgentIds = oldMembers, NewAgentId = body.NewAgentId })));
        await db.SaveChangesAsync(ct);

        foreach (var oldId in oldMembers)
            await notifier.NotifyConversationUpdatedAsync(tenantId,
                new InboxConversationEvent(Guid.Empty, "reassigned", null, null), ct);

        return Results.Ok(new
        {
            InboxId = id,
            OldAgentIds = oldMembers,
            NewAgentId = body.NewAgentId,
            UnassignedConversationCount = convs.Count
        });
    }

    private static async Task<IResult> ListMembersAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var inbox = await db.Inboxes.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenant.TenantId, ct);
        if (inbox is null) return Results.NotFound();

        var members = await db.InboxMembers
            .Where(m => m.InboxId == id)
            .Select(m => m.AgentId)
            .ToListAsync(ct);
        return Results.Ok(members);
    }

    private static async Task<IResult> ListAssignableAgentsAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var inbox = await db.Inboxes.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenant.TenantId, ct);
        if (inbox is null) return Results.NotFound();

        var agents = await db.InboxMembers
            .Where(m => m.InboxId == id)
            .Join(db.Users, m => m.AgentId, u => u.Id, (m, u) => new { u.Id, u.DisplayName, u.Email })
            .ToListAsync(ct);
        return Results.Ok(agents);
    }

    private static async Task<IResult> ListInboxesAsync(
        AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var inboxes = await db.Inboxes.AsNoTracking()
            .Where(i => i.TenantId == tenant.TenantId && i.DeletedAt == null)
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.Platform,
                i.ExternalPageId,
                i.IsActive,
                i.CreatedAt,
                HasToken = i.EncryptedAccessToken != null,
                MemberCount = db.InboxMembers.Count(m => m.InboxId == i.Id)
            })
            .OrderBy(i => i.Platform).ThenBy(i => i.Name)
            .ToListAsync(ct);
        return Results.Ok(inboxes);
    }

    private static async Task<IResult> CreateInboxAsync(
        HttpContext ctx, // for logger
        CreateInboxRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var logger = ctx.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("AdminInbox");
        var pageName = await FetchPageNameAsync(body.ExternalPageId, body.PageAccessToken ?? string.Empty, logger, ct);
        if (string.IsNullOrEmpty(pageName))
        {
            #pragma warning disable CA1848
            logger.LogWarning("Could not fetch page name for {PageId}, using fallback", body.ExternalPageId);
            pageName = $"{body.Platform} OA - {body.ExternalPageId}";
        }
        var inbox = Inbox.Create(tenant.TenantId, pageName, body.Platform, body.ExternalPageId);

        if (!string.IsNullOrEmpty(body.PageAccessToken))
            inbox.SetAccessToken(body.PageAccessToken, clock.UtcNow);

        db.Inboxes.Add(inbox);

        if (body.AgentId.HasValue)
        {
            db.InboxMembers.Add(InboxMember.Create(tenant.TenantId, inbox.Id, body.AgentId.Value));
        }

        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/admin/inboxes/{inbox.Id}", new
        {
            inbox.Id,
            inbox.Name,
            inbox.Platform,
            inbox.ExternalPageId,
            inbox.IsActive,
            inbox.CreatedAt,
        });
    }

    #pragma warning disable CA1848, CA1869
    private static async Task<string?> FetchPageNameAsync(string pageId, string token, ILogger logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(token))
            return null;
        try
        {
            using var http = new HttpClient();
            var url = $"https://pages.fm/api/public_api/v2/pages/{pageId}/conversations?page_access_token={token}&per_page=5";
            var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var data = System.Text.Json.JsonSerializer.Deserialize<PancakeLookupResponse>(json, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower });
            if (data?.Conversations is null) return null;
            foreach (var conv in data.Conversations)
            {
                if (conv.LastSentBy?.Id == pageId && !string.IsNullOrEmpty(conv.LastSentBy.Name))
                    return conv.LastSentBy.Name;
            }
            return null;
        }
        catch (Exception ex)
        {
            #pragma warning disable CA1848
            logger.LogWarning(ex, "Failed to fetch page name from Pancake for {PageId}", pageId);
            return null;
        }
    }

    private sealed record PancakeLookupResponse(IReadOnlyList<PancakeConvLookup>? Conversations);
    private sealed record PancakeConvLookup(string? PageId, PancakeLookupSender? LastSentBy);
    private sealed record PancakeLookupSender(string? Id, string? Name, string? DisplayName);
}

