using Clawbot.Domain.Conversations;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

internal static class UserInboxScopeQueryExtensions
{
    public static IQueryable<Conversation> ApplyInboxScope(
        this IQueryable<Conversation> query,
        IReadOnlyCollection<Guid> inboxIds)
    {
        var allowedInboxIds = Normalize(inboxIds);
        if (allowedInboxIds is null)
            return query;
        if (allowedInboxIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(conversation =>
            conversation.InboxId.HasValue &&
            allowedInboxIds.Contains(conversation.InboxId.Value));
    }

    public static IQueryable<Lead> ApplyInboxScope(
        this IQueryable<Lead> query,
        AppDbContext db,
        Guid tenantId,
        IReadOnlyCollection<Guid> inboxIds)
    {
        var allowedInboxIds = Normalize(inboxIds);
        if (allowedInboxIds is null)
            return query;
        if (allowedInboxIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(lead =>
            lead.ContactId.HasValue &&
            db.Conversations.IgnoreQueryFilters().Any(conversation =>
                conversation.TenantId == tenantId &&
                conversation.ContactId == lead.ContactId &&
                conversation.InboxId.HasValue &&
                allowedInboxIds.Contains(conversation.InboxId.Value) &&
                conversation.DeletedAt == null));
    }

    public static IQueryable<Message> ApplyInboxScope(
        this IQueryable<Message> query,
        AppDbContext db,
        Guid tenantId,
        IReadOnlyCollection<Guid> inboxIds)
    {
        var allowedInboxIds = Normalize(inboxIds);
        if (allowedInboxIds is null)
            return query;
        if (allowedInboxIds.Count == 0)
            return query.Where(_ => false);

        return query.Where(message =>
            db.Conversations.IgnoreQueryFilters().Any(conversation =>
                conversation.Id == message.ConversationId &&
                conversation.TenantId == tenantId &&
                conversation.InboxId.HasValue &&
                allowedInboxIds.Contains(conversation.InboxId.Value) &&
                conversation.DeletedAt == null));
    }

    private static List<Guid>? Normalize(IReadOnlyCollection<Guid> inboxIds)
    {
        ArgumentNullException.ThrowIfNull(inboxIds);
        return inboxIds.Count == 0
            ? null
            : inboxIds.Where(id => id != Guid.Empty).Distinct().ToList();
    }
}
