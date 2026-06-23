using Clawbot.Api.Contracts.Inbox;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

internal sealed class InboxSearchService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public async Task<ConversationListResponse> SearchAsync(
        Guid tenantId,
        string queryText,
        string? status,
        string? platform,
        int page,
        int pageSize,
        List<Guid> inboxIds,
        CancellationToken ct)
    {
        if (pageSize is < 1 or > 200) pageSize = 50;
        if (page < 1) page = 1;

        var trimmed = queryText.Trim();
        if (trimmed.Length == 0)
            return new ConversationListResponse(Array.Empty<ConversationListItemDto>(), 0, page, pageSize);

        var pattern = "%" + EscapeLike(trimmed) + "%";
        var conversations = _db.Conversations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.DeletedAt == null);
        if (inboxIds != null && inboxIds.Count > 0)
        {
            conversations = conversations.Where(c => c.InboxId != null && inboxIds.Contains(c.InboxId.Value));
        }

        if (!string.IsNullOrWhiteSpace(status))
            conversations = conversations.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(platform))
            conversations = conversations.Where(c => c.Platform == platform);

        conversations = conversations.Where(c =>
            EF.Functions.Like(c.ExternalThreadId, pattern) ||
            _db.Contacts.IgnoreQueryFilters().Any(contact =>
                contact.TenantId == tenantId &&
                contact.Id == c.ContactId &&
                (EF.Functions.Like(contact.DisplayName, pattern) ||
                 (contact.Email != null && EF.Functions.Like(contact.Email, pattern)) ||
                 (contact.Phone != null && EF.Functions.Like(contact.Phone, pattern)))) ||
            c.Messages.Any(m =>
                m.TenantId == tenantId &&
                (EF.Functions.Like(m.Content, pattern) ||
                 (m.OriginalContent != null && EF.Functions.Like(m.OriginalContent, pattern)) ||
                 (m.RedactedContent != null && EF.Functions.Like(m.RedactedContent, pattern)) ||
                 (m.ExternalMessageId != null && EF.Functions.Like(m.ExternalMessageId, pattern)) ||
                 (m.ParentPostId != null && EF.Functions.Like(m.ParentPostId, pattern)))));

        var total = await conversations.CountAsync(ct).ConfigureAwait(false);
        var rows = await conversations
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
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
        var contactNames = await _db.Contacts.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TenantId == tenantId && contactIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct).ConfigureAwait(false);

        var items = rows.Select(r => new ConversationListItemDto(
            r.Id,
            r.Platform,
            r.ExternalThreadId,
            r.Status,
            r.ContactId,
            r.ContactId.HasValue && contactNames.TryGetValue(r.ContactId.Value, out var n) ? n : null,
            r.AssignedTo,
            r.LastMessageAt,
            r.LastMessage is null ? null : Preview(r.LastMessage),
            r.RowVersion,
            UnreadCount: 0)).ToList();

        return new ConversationListResponse(items, total, page, pageSize);
    }

    private static string Preview(string text) =>
        text.Length <= 140 ? text : text[..140] + "...";

    private static string EscapeLike(string text) =>
        text.Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("%", "[%]", StringComparison.Ordinal)
            .Replace("_", "[_]", StringComparison.Ordinal);
}

