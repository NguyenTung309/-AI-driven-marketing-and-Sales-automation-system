using System.Security.Claims;
using System.Text;
using Clawbot.Api.Auth;
using Clawbot.Api.Common.Pagination;
using Clawbot.Api.Contracts.Inbox;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Messaging;
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
        var grp = app.MapGroup("/api/inbox").RequireRateLimiting(RateLimitingExtensions.ChatPolicy);

        grp.MapGet("/search", SearchAsync).RequirePermission("conversations:read");
        grp.MapGet("/conversations", ListAsync).RequirePermission("conversations:read");
        grp.MapGet("/conversations/{id:guid}", GetAsync).RequirePermission("conversations:read");
        grp.MapGet("/conversations/{id:guid}/export.csv", ExportCsvAsync).RequirePermission("conversations:read");
        grp.MapPost("/conversations/{id:guid}/assign", AssignAsync).RequirePermission("conversations:write");
        grp.MapPost("/conversations/{id:guid}/resolve", ResolveAsync).RequirePermission("conversations:write");
        grp.MapPost("/conversations/{id:guid}/escalate", EscalateAsync).RequirePermission("conversations:write");
        grp.MapPost("/conversations/{id:guid}/ai", SetAiAutoReplyAsync).RequirePermission("conversations:write");
        // Nút "Tạo lại phản hồi AI": kích AI trả lời tin khách đang treo (vd sau khi từ chối draft bị chặn).
        grp.MapPost("/conversations/{id:guid}/ai/regenerate", RegenerateAiReplyAsync).RequirePermission("conversations:write");
        grp.MapPost("/conversations/{id:guid}/messages", SendOutboundAsync).RequirePermission("conversations:write");
        grp.MapPost("/conversations/{id:guid}/messages/{messageId:guid}/retry", RetryFailedMessageAsync)
            .RequirePermission("conversations:write");
        // Review-gate P3: duyệt/từ chối AI draft đang hold (messages.status=pending_approval)
        grp.MapPost("/conversations/{id:guid}/drafts/{messageId:guid}/approve", ApproveDraftAsync).RequirePermission("conversations:write");
        grp.MapPost("/conversations/{id:guid}/drafts/{messageId:guid}/reject", RejectDraftAsync).RequirePermission("conversations:write");
        grp.MapGet("/conversations/counts", CountsAsync).RequirePermission("conversations:read");
        grp.MapGet("/channels", ListChannelsAsync).RequirePermission("conversations:read");
        grp.MapGet("/daily-summary", DailySummaryAsync).RequirePermission("conversations:read");

        return app;
    }

    // Đếm theo trạng thái trên TOÀN bộ tập khớp bộ lọc. Frontend không tự đếm được: danh sách chạy
    // keyset phân trang (40 dòng/lần) nên đếm trên client chỉ ra số của trang đã tải.
    private static async Task<IResult> CountsAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        ClaimsPrincipal user,
        IUserInboxResolver resolver,
        [FromQuery] Guid? inboxId,
        [FromQuery] string? platform,
        [FromQuery] string? q,
        CancellationToken ct = default)
    {
        _ = tenants.Require();

        var inboxIds = await resolver.GetInboxIdsAsync(user, ct);
        var query = BuildVisibleConversations(db, inboxIds, inboxId, platform, q);

        var counts = await QueryConversationCountsAsync(
                query,
                CurrentUserId(user),
                ct)
            .ConfigureAwait(false);

        return Results.Ok(counts);
    }

    private static Guid? CurrentUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) && id != Guid.Empty
            ? id
            : null;

    internal sealed record ConversationCountsResult(
        int Total,
        int Open,
        int Escalated,
        int Resolved,
        int Mine);

    internal static async Task<ConversationCountsResult> QueryConversationCountsAsync(
        IQueryable<Clawbot.Domain.Conversations.Conversation> query,
        Guid? userId,
        CancellationToken ct)
    {
        var counts = await query
            .GroupBy(_ => 1)
            .Select(group => new ConversationCountsResult(
                group.Count(),
                group.Count(conversation => conversation.Status == "open"),
                group.Count(conversation => conversation.Status == "escalated"),
                group.Count(conversation => conversation.Status == "resolved"),
                userId == null
                    ? 0
                    : group.Count(conversation => conversation.AssignedTo == userId)))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return counts ?? new ConversationCountsResult(0, 0, 0, 0, 0);
    }

    // Phần lọc dùng chung giữa danh sách và bộ đếm — tách ra để 2 đường không trôi lệch nhau.
    internal static IQueryable<Clawbot.Domain.Conversations.Conversation> BuildVisibleConversations(
        AppDbContext db,
        List<Guid> inboxIds,
        Guid? inboxId,
        string? platform,
        string? q)
    {
        var query = db.Conversations.AsNoTracking().AsQueryable();
        if (inboxIds.Count > 0)
            query = query.Where(c => c.InboxId != null && inboxIds.Contains(c.InboxId.Value));

        if (inboxId.HasValue) query = query.Where(c => c.InboxId == inboxId);
        if (!string.IsNullOrEmpty(platform)) query = query.Where(c => c.Platform == platform);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = "%" + EscapeLike(q.Trim()) + "%";
            query = query.Where(c =>
                EF.Functions.Like(c.ExternalThreadId, pattern) ||
                db.Contacts.Any(contact =>
                    contact.Id == c.ContactId &&
                    (EF.Functions.Like(contact.DisplayName, pattern) ||
                     (contact.Email != null && EF.Functions.Like(contact.Email, pattern)) ||
                     (contact.Phone != null && EF.Functions.Like(contact.Phone, pattern)))) ||
                c.Messages.Any(m =>
                    EF.Functions.Like(m.Content, pattern) ||
                    (m.OriginalContent != null && EF.Functions.Like(m.OriginalContent, pattern)) ||
                    (m.RedactedContent != null && EF.Functions.Like(m.RedactedContent, pattern))));
        }

        return query;
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
        [FromQuery] string? q,
        [FromQuery] Guid? assignedTo,
        [FromQuery] string? sort,
        [FromQuery] string? cursor,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        var req = PageRequest.Create(page, pageSize);
        page = req.Page;
        pageSize = req.PageSize;

        var inboxIds = await resolver.GetInboxIdsAsync(user, ct);
        var query = BuildVisibleConversations(db, inboxIds, inboxId, platform, q);
        if (!string.IsNullOrEmpty(status)) query = query.Where(c => c.Status == status);
        if (assignedTo.HasValue) query = query.Where(c => c.AssignedTo == assignedTo);

        // Default: keyset on last_message_at DESC, id DESC. Optional "lead_score" uses offset.
        var useLeadScore = string.Equals(sort, "lead_score", StringComparison.OrdinalIgnoreCase);
        if (useLeadScore)
        {
            var total = await query.CountAsync(ct).ConfigureAwait(false);
            var scoreRows = await query
                .OrderByDescending(c => db.Leads.Where(l => l.ContactId == c.ContactId).Max(l => (int?)l.Score) ?? 0)
                .ThenByDescending(c => c.LastMessageAt ?? c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .Skip(req.Skip)
                .Take(pageSize)
                .Select(c => new ConversationRow(
                    c.Id, c.Platform, c.ExternalThreadId, c.Status, c.ContactId, c.AssignedTo,
                    c.LastMessageAt, c.CreatedAt, c.InboxId, c.AiAutoReplyEnabled,
                    c.RowVersion ?? Array.Empty<byte>(),
                    c.Messages.OrderByDescending(m => m.SentAt).Select(m => m.Content).FirstOrDefault()))
                .ToListAsync(ct).ConfigureAwait(false);

            var scoreItems = await MapConversationItemsAsync(db, scoreRows, ct).ConfigureAwait(false);
            return Results.Ok(new ConversationListResponse(scoreItems, total, page, pageSize));
        }

        // Keyset feed
        var key = KeysetQuery.Decode(cursor);
        int? totalCursor = key is null ? await query.CountAsync(ct).ConfigureAwait(false) : null;
        if (key is not null)
        {
            var ts = key.Value.Ts;
            var id = key.Value.Id;
            query = query.Where(c =>
                (c.LastMessageAt ?? c.CreatedAt) < ts ||
                ((c.LastMessageAt ?? c.CreatedAt) == ts && c.Id < id));
        }

        var fetched = await query
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Take(pageSize + 1)
            .Select(c => new ConversationRow(
                c.Id, c.Platform, c.ExternalThreadId, c.Status, c.ContactId, c.AssignedTo,
                c.LastMessageAt, c.CreatedAt, c.InboxId, c.AiAutoReplyEnabled,
                c.RowVersion ?? Array.Empty<byte>(),
                c.Messages.OrderByDescending(m => m.SentAt).Select(m => m.Content).FirstOrDefault()))
            .ToListAsync(ct).ConfigureAwait(false);

        var (rows, nextCursor) = KeysetQuery.SliceWithCursor(
            fetched, pageSize, r => r.LastMessageAt ?? r.CreatedAt, r => r.Id);
        var items = await MapConversationItemsAsync(db, rows, ct).ConfigureAwait(false);
        return Results.Ok(new ConversationCursorPage(items, nextCursor, totalCursor));
    }

    private sealed record ConversationRow(
        Guid Id,
        string Platform,
        string ExternalThreadId,
        string Status,
        Guid? ContactId,
        Guid? AssignedTo,
        DateTimeOffset? LastMessageAt,
        DateTimeOffset CreatedAt,
        Guid? InboxId,
        bool AiAutoReplyEnabled,
        byte[] RowVersion,
        string? LastMessage);

    private static async Task<List<ConversationListItemDto>> MapConversationItemsAsync(
        AppDbContext db,
        List<ConversationRow> rows,
        CancellationToken ct)
    {
        var contactIds = rows.Where(r => r.ContactId.HasValue).Select(r => r.ContactId!.Value).Distinct().ToList();
        var contactNames = await db.Contacts.AsNoTracking()
            .Where(c => contactIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct).ConfigureAwait(false);
        var contactAvatars = await db.Contacts.AsNoTracking()
            .Where(c => contactIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.AvatarUrl, ct).ConfigureAwait(false);

        return rows.Select(r => new ConversationListItemDto(
            r.Id, r.Platform, r.ExternalThreadId, r.Status, r.ContactId,
            r.ContactId.HasValue && contactNames.TryGetValue(r.ContactId.Value, out var n) ? n : null,
            r.ContactId.HasValue && contactAvatars.TryGetValue(r.ContactId.Value, out var a) ? a : null,
            r.InboxId, null, null,
            r.AssignedTo, r.LastMessageAt,
            r.LastMessage is null ? null : Preview(r.LastMessage),
            r.RowVersion,
            UnreadCount: 0,
            AiAutoReplyEnabled: r.AiAutoReplyEnabled)).ToList();
    }

    private static string EscapeLike(string text) =>
        text.Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);

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
        if (IsOutsideInboxScope(inboxIds, conv.InboxId))
            return Results.Forbid();

        var contact = conv.ContactId is null ? null
            : await db.Contacts.AsNoTracking().Where(c => c.Id == conv.ContactId)
                .Select(c => new { c.DisplayName, c.AvatarUrl }).FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var messages = conv.Messages.OrderBy(m => m.SentAt)
            .Select(m => new MessageDto(m.Id, m.Direction, m.SenderType, m.SenderUserId, m.OriginalContent ?? m.RedactedContent ?? m.Content, m.ContentType, m.SentAt, m.SenderDisplayName, m.SenderAvatarUrl, m.AttachmentUrl, m.Status))
            .ToList();

        string? inboxName = null;
        string? inboxAvatarUrl = null;
        if (conv.InboxId.HasValue)
        {
            var inbox = await db.Inboxes.IgnoreQueryFilters()
                .Where(i => i.Id == conv.InboxId.Value)
                .Select(i => new { i.Name, i.AvatarUrl })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (inbox != null)
            {
                inboxName = inbox.Name;
                inboxAvatarUrl = inbox.AvatarUrl;
            }
        }

        return Results.Ok(new ConversationDetailDto(
            conv.Id, conv.Platform, conv.ExternalThreadId, conv.Status, conv.ContactId,
            contact?.DisplayName, contact?.AvatarUrl, conv.InboxId, inboxName, inboxAvatarUrl,
            conv.AssignedTo, conv.LastMessageAt, conv.CreatedAt,
            conv.RowVersion, messages, conv.AiAutoReplyEnabled));
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
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        ClaimsPrincipal user,
        IUserInboxResolver resolver,
        ConversationExportService exporter,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var conversation = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == id && c.TenantId == tenant.TenantId)
            .Select(c => new { c.InboxId })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (conversation is null) return Results.NotFound();

        var inboxIds = await resolver.GetInboxIdsAsync(user, ct).ConfigureAwait(false);
        if (IsOutsideInboxScope(inboxIds, conversation.InboxId))
            return Results.Forbid();

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

        if (IsOutsideInboxScope(inboxIds, conv.InboxId))
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
            new InboxConversationEvent(conv.Id, conv.Status, conv.AssignedTo, conv.LastMessageAt, conv.InboxId), ct).ConfigureAwait(false);
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

        if (IsOutsideInboxScope(inboxIds, conv.InboxId))
            return Results.Forbid();

        if (expectedVersion != null && conv.RowVersion != null && !conv.RowVersion.SequenceEqual(expectedVersion))
            return Results.Conflict(new { error = "concurrency_conflict", message = "Trang thai da thay doi, vui long tai lai" });

        conv.Resolve();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.NotifyConversationUpdatedAsync(tenant.TenantId,
            new InboxConversationEvent(conv.Id, conv.Status, conv.AssignedTo, conv.LastMessageAt, conv.InboxId), ct).ConfigureAwait(false);
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

        if (IsOutsideInboxScope(inboxIds, conv.InboxId))
            return Results.Forbid();

        if (expectedVersion != null && conv.RowVersion != null && !conv.RowVersion.SequenceEqual(expectedVersion))
            return Results.Conflict(new { error = "concurrency_conflict", message = "Trang thai da thay doi, vui long tai lai" });

        conv.Escalate();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.NotifyConversationUpdatedAsync(tenant.TenantId,
            new InboxConversationEvent(conv.Id, conv.Status, conv.AssignedTo, conv.LastMessageAt, conv.InboxId), ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> SetAiAutoReplyAsync(
        Guid id, SetAiAutoReplyRequest body,
        AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier,
        IAiAutoReplyResumer resumer,
        ClaimsPrincipal user, IUserInboxResolver resolver,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();

        var inboxIds = await resolver.GetInboxIdsAsync(user, ct);
        if (IsOutsideInboxScope(inboxIds, conv.InboxId))
            return Results.Forbid();

        conv.SetAiAutoReply(body.Enabled);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await notifier.NotifyConversationUpdatedAsync(tenant.TenantId,
            new InboxConversationEvent(conv.Id, conv.Status, conv.AssignedTo, conv.LastMessageAt, conv.InboxId), ct).ConfigureAwait(false);

        // Bật AI mà tin cuối là tin khách chưa được đáp -> trả lời ngay tin treo đó (best-effort,
        // resumer tự guard tin cuối/draft; QĐ user 2026-07-16). Tắt thì không làm gì thêm.
        if (body.Enabled)
            await resumer.ReplyToHangingCustomerMessageAsync(tenant.TenantId, conv.Id, ct).ConfigureAwait(false);

        return Results.NoContent();
    }

    // Nút "Tạo lại phản hồi AI": dùng khi từ chối draft bị chặn xong muốn AI soạn lại cho tin khách
    // đang chờ. Resumer tự guard (AI đang bật, tin cuối là tin khách, không có draft pending).
    private static async Task<IResult> RegenerateAiReplyAsync(
        Guid id,
        AppDbContext db, ITenantAccessor tenants,
        IAiAutoReplyResumer resumer,
        ClaimsPrincipal user, IUserInboxResolver resolver,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var conv = await db.Conversations.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Id, c.InboxId, c.AiAutoReplyEnabled })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();

        var inboxIds = await resolver.GetInboxIdsAsync(user, ct).ConfigureAwait(false);
        if (IsOutsideInboxScope(inboxIds, conv.InboxId))
            return Results.Forbid();

        if (!conv.AiAutoReplyEnabled)
            return Results.Conflict(new { error = "ai_disabled", message = "Bật AI trả lời cho hội thoại này trước khi tạo lại phản hồi." });

        var triggered = await resumer.ReplyToHangingCustomerMessageAsync(tenant.TenantId, conv.Id, ct).ConfigureAwait(false);
        if (!triggered)
            return Results.Conflict(new { error = "no_hanging_message", message = "Không có tin khách nào đang chờ trả lời (hoặc đang có bản nháp chờ duyệt)." });

        return Results.Accepted($"/api/inbox/conversations/{id}");
    }

    private static async Task<IResult> SendOutboundAsync(
        Guid id, SendMessageRequest body,
        AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier,
        IChannelAdapter adapter, OutboundMessageSafetyService safety, IClock clock,
        ClaimsPrincipal user,
        IUserInboxResolver resolver,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.Content)) return Results.BadRequest(new { error = "Content required" });

        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();

        var inboxIds = await resolver.GetInboxIdsAsync(user, ct);
        if (IsOutsideInboxScope(inboxIds, conv.InboxId))
            return Results.Forbid();

        try { await safety.EnsureAllowedAsync(body.Content, ct).ConfigureAwait(false); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }

        var senderUserId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        string? channelMessageId;
        try
        {
            // Token per-kenh: adapter tu resolve page access token cua inbox (PancakePageTokenResolver)
            channelMessageId = await adapter.SendAsync(tenant.TenantId, conv.Platform, conv.ExternalThreadId, body.Content, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Results.BadRequest(new { error = "channel_send_failed", message = "Không thể gửi tin nhắn qua kênh kết nối." });
        }
        // externalMessageId tu send response: poller dedup strict theo id, khong dua vao content-heuristic
        var msg = conv.AppendMessage("out", "user", body.Content, body.ContentType, clock.UtcNow, senderUserId: senderUserId, externalMessageId: channelMessageId);
        // Handover: sale gui tay -> tam tat AI auto-reply. Khach nhan tiep sau N phut (tenant config, mac dinh 5)
        // thi AI tu bat lai va tra loi (QĐ user 2026-07-16). Sweep AiAutoReplyResumeJob lo case khach im luon.
        var resumeMinutes = await db.Tenants
            .Where(t => t.Id == tenant.TenantId)
            .Select(t => t.AiAutoReplyResumeMinutes)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (resumeMinutes <= 0) resumeMinutes = 5;
        conv.PauseAiAutoReplyUntil(clock.UtcNow.AddMinutes(resumeMinutes));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await notifier.NotifyMessageAsync(tenant.TenantId,
            new InboxMessageEvent(conv.Id, msg.Id, msg.Direction, msg.SenderType, msg.Content, msg.ContentType, msg.SentAt, conv.AssignedTo, msg.SenderDisplayName, msg.SenderAvatarUrl, InboxId: conv.InboxId, ConversationStatus: conv.Status), ct).ConfigureAwait(false);

        return Results.Ok(new MessageDto(msg.Id, msg.Direction, msg.SenderType, msg.SenderUserId, msg.Content, msg.ContentType, msg.SentAt, msg.SenderDisplayName, msg.SenderAvatarUrl, msg.AttachmentUrl));
    }

    private static async Task<IResult> RetryFailedMessageAsync(
        Guid id,
        Guid messageId,
        FailedMessageRetryService retryService,
        AppDbContext db,
        ITenantAccessor tenants,
        ClaimsPrincipal user,
        IUserInboxResolver resolver,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var conversation = await db.Conversations
            .AsNoTracking()
            .Where(c => c.Id == id && c.TenantId == tenant.TenantId)
            .Select(c => new
            {
                c.Id,
                c.Platform,
                c.ExternalThreadId,
                c.AssignedTo,
                c.InboxId,
            })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (conversation is null)
            return Results.NotFound();

        var inboxIds = await resolver.GetInboxIdsAsync(user, ct).ConfigureAwait(false);
        if (IsOutsideInboxScope(inboxIds, conversation.InboxId))
            return Results.Forbid();

        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId))
            return Results.Forbid();

        var result = await retryService.RetryAsync(
            tenant.TenantId,
            conversation.Id,
            messageId,
            actorUserId,
            conversation.Platform,
            conversation.ExternalThreadId,
            conversation.AssignedTo,
            conversation.InboxId,
            ct).ConfigureAwait(false);

        return result.Outcome switch
        {
            FailedMessageRetryOutcome.NotFound => Results.NotFound(),
            FailedMessageRetryOutcome.NotAvailable => Results.Conflict(new
            {
                error = "message_retry_not_available",
                message = "Tin nhắn này không còn ở trạng thái có thể gửi lại.",
            }),
            FailedMessageRetryOutcome.AlreadyClaimed => Results.Conflict(new
            {
                error = "message_already_claimed",
                message = "Tin nhắn đang được gửi lại bởi yêu cầu khác.",
            }),
            FailedMessageRetryOutcome.SafetyRejected => Results.BadRequest(new
            {
                error = "message_safety_rejected",
                message = result.ErrorMessage ?? "Tin nhắn không vượt qua kiểm tra an toàn.",
            }),
            FailedMessageRetryOutcome.ChannelFailed => Results.BadRequest(new
            {
                error = "channel_send_failed",
                message = "Không thể gửi tin nhắn qua kênh kết nối.",
            }),
            FailedMessageRetryOutcome.DeliveryAmbiguous => Results.Conflict(new
            {
                error = "message_delivery_ambiguous",
                message = "Kênh chưa xác nhận kết quả gửi. Tin nhắn được giữ ở trạng thái chờ đối soát để tránh gửi trùng.",
            }),
            FailedMessageRetryOutcome.Sent when result.Message is not null => Results.Ok(new MessageDto(
                result.Message.Id,
                result.Message.Direction,
                result.Message.SenderType,
                result.Message.SenderUserId,
                result.Message.Content,
                result.Message.ContentType,
                result.Message.SentAt,
                result.Message.SenderDisplayName,
                result.Message.SenderAvatarUrl,
                result.Message.AttachmentUrl,
                result.Message.Status)),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // Review-gate P3: người duyệt AI draft — chạy lại safety gate rồi gửi thật qua kênh, mark sent.
    private static async Task<IResult> ApproveDraftAsync(
        Guid id, Guid messageId,
        AppDbContext db, ITenantAccessor tenants, IInboxNotifier notifier,
        IChannelAdapter adapter, OutboundMessageSafetyService safety,
        ClaimsPrincipal user, IUserInboxResolver resolver,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();

        var inboxIds = await resolver.GetInboxIdsAsync(user, ct);
        if (IsOutsideInboxScope(inboxIds, conv.InboxId))
            return Results.Forbid();

        var msg = await db.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == id, ct).ConfigureAwait(false);
        if (msg is null) return Results.NotFound();
        if (!string.Equals(msg.Status, "pending_approval", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "draft_not_pending", message = "Tin này không ở trạng thái chờ duyệt." });

        try { await safety.EnsureAllowedAsync(msg.Content, ct).ConfigureAwait(false); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }

        var claimed = await db.Messages
            .Where(m => m.Id == messageId && m.ConversationId == id && m.Status == "pending_approval")
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, "pending_send"), ct)
            .ConfigureAwait(false);
        if (claimed == 0)
            return Results.Conflict(new { error = "draft_already_claimed", message = "Tin chờ duyệt đã được xử lý bởi yêu cầu khác." });

        db.Entry(msg).State = EntityState.Detached;
        msg = await db.Messages.FirstAsync(m => m.Id == messageId, ct).ConfigureAwait(false);

        string? channelMessageId;
        try
        {
            channelMessageId = await adapter.SendAsync(tenant.TenantId, conv.Platform, conv.ExternalThreadId, msg.Content, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            msg.MarkSendFailed();
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            msg.MarkSendFailed();
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            return Results.BadRequest(new { error = "channel_send_failed", message = "Không thể gửi tin nhắn qua kênh kết nối." });
        }

        msg.MarkSent();
        if (channelMessageId is not null)
            msg.SetExternalMessageId(channelMessageId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await notifier.NotifyMessageAsync(tenant.TenantId,
            new InboxMessageEvent(conv.Id, msg.Id, msg.Direction, msg.SenderType, msg.Content, msg.ContentType, msg.SentAt, conv.AssignedTo, msg.SenderDisplayName, msg.SenderAvatarUrl, InboxId: conv.InboxId, ConversationStatus: conv.Status), ct).ConfigureAwait(false);

        return Results.Ok(new MessageDto(msg.Id, msg.Direction, msg.SenderType, msg.SenderUserId, msg.Content, msg.ContentType, msg.SentAt, msg.SenderDisplayName, msg.SenderAvatarUrl, msg.AttachmentUrl, msg.Status));
    }

    private static async Task<IResult> RejectDraftAsync(
        Guid id, Guid messageId,
        AppDbContext db, ITenantAccessor tenants,
        ClaimsPrincipal user, IUserInboxResolver resolver,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false);
        if (conv is null) return Results.NotFound();

        var inboxIds = await resolver.GetInboxIdsAsync(user, ct);
        if (IsOutsideInboxScope(inboxIds, conv.InboxId))
            return Results.Forbid();

        var msg = await db.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == id, ct).ConfigureAwait(false);
        if (msg is null) return Results.NotFound();
        if (!string.Equals(msg.Status, "pending_approval", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "draft_not_pending", message = "Tin này không ở trạng thái chờ duyệt." });

        msg.MarkBlocked();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new MessageDto(msg.Id, msg.Direction, msg.SenderType, msg.SenderUserId, msg.Content, msg.ContentType, msg.SentAt, msg.SenderDisplayName, msg.SenderAvatarUrl, msg.AttachmentUrl, msg.Status));
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
        if (!Guid.TryParse(roleId, out var rid)) rid = Guid.Empty;
        if (!await HasInboxSchemaAsync(db, ct))
            return Results.Ok(Array.Empty<object>());

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

    private static async Task<bool> HasInboxSchemaAsync(AppDbContext db, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(ct).ConfigureAwait(false);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN
                    OBJECT_ID(N'dbo.inboxes', N'U') IS NOT NULL AND
                    OBJECT_ID(N'dbo.inbox_members', N'U') IS NOT NULL AND
                    COL_LENGTH(N'dbo.conversations', N'inbox_id') IS NOT NULL
                THEN 1 ELSE 0 END
                """;
            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) == 1;
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync().ConfigureAwait(false);
        }
    }

    private static bool IsOutsideInboxScope(List<Guid> inboxIds, Guid? inboxId) =>
        inboxIds.Count > 0 && (!inboxId.HasValue || !inboxIds.Contains(inboxId.Value));

    private static async Task<IResult> DailySummaryAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        ClaimsPrincipal user,
        IClock clock,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var uid))
            return Results.Unauthorized();

        var todayStart = clock.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);

        var conversationsHandled = await db.Conversations
            .CountAsync(c => c.TenantId == tenant.TenantId
                && c.AssignedTo == uid
                && c.LastMessageAt >= todayStart
                && c.LastMessageAt < todayEnd, ct);

        var messagesSent = await db.Messages
            .CountAsync(m => m.SenderUserId == uid
                && m.Direction == "out"
                && m.SentAt >= todayStart
                && m.SentAt < todayEnd, ct);

        var openConversations = await db.Conversations
            .CountAsync(c => c.TenantId == tenant.TenantId
                && c.AssignedTo == uid
                && c.Status == "open", ct);

        var totalHandled = await db.Conversations
            .CountAsync(c => c.TenantId == tenant.TenantId
                && c.AssignedTo == uid
                && c.Status == "resolved", ct);

        var closeRate = totalHandled > 0
            ? (int)Math.Round((double)conversationsHandled / totalHandled * 100)
            : 0;

        return Results.Ok(new
        {
            conversationsHandled,
            messagesSent,
            openConversations,
            closeRate,
            date = todayStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        });
    }
}


