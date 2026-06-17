using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

public sealed class ContactDataExportService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public async Task<ContactDataExportResult?> ExportAsync(Guid tenantId, Guid contactId, CancellationToken ct = default)
    {
        var contact = await _db.Contacts.IgnoreQueryFilters()
            .Where(c => c.Id == contactId && c.TenantId == tenantId)
            .Select(c => new ContactExportDto(
                c.Id,
                c.DisplayName,
                c.Phone,
                c.Email,
                c.Locale,
                c.LifetimeScore,
                c.LifecycleStage,
                c.CreatedAt,
                c.DeletedAt))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (contact is null)
            return null;

        var externalIds = await _db.ContactExternalIds
            .Where(e => e.ContactId == contactId)
            .OrderBy(e => e.Platform)
            .ThenBy(e => e.ExternalId)
            .Select(e => new ContactExternalIdExportDto(e.Id, e.Platform, e.ExternalId, e.FirstSeenAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var conversations = await _db.Conversations.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.ContactId == contactId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new ConversationExportDto(
                c.Id,
                c.Platform,
                c.ExternalThreadId,
                c.Status,
                c.AssignedTo,
                c.LastMessageAt,
                c.CreatedAt,
                c.DeletedAt,
                Array.Empty<MessageExportDto>()))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var conversationIds = conversations.Select(c => c.Id).ToList();
        var messages = conversationIds.Count == 0
            ? []
            : await _db.Messages.IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && conversationIds.Contains(m.ConversationId))
                .OrderBy(m => m.SentAt)
                .Select(m => new MessageExportDto(
                    m.Id,
                    m.ConversationId,
                    m.Direction,
                    m.SenderType,
                    m.SenderUserId,
                    m.Content,
                    m.ContentType,
                    m.MessageType,
                    m.ParentPostId,
                    m.ExternalMessageId,
                    m.OriginalContent,
                    m.RedactedContent,
                    m.SentAt))
                .ToListAsync(ct)
                .ConfigureAwait(false);

        var messagesByConversation = messages
            .GroupBy(m => m.ConversationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MessageExportDto>)g.ToList());
        conversations = conversations
            .Select(c => c with
            {
                Messages = messagesByConversation.TryGetValue(c.Id, out var rows)
                    ? rows
                    : Array.Empty<MessageExportDto>(),
            })
            .ToList();

        var leads = await _db.Leads.IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId && l.ContactId == contactId)
            .OrderBy(l => l.CreatedAt)
            .Select(l => new LeadExportDto(
                l.Id,
                l.OwnerUserId,
                l.Score,
                l.Stage,
                l.SourcePlatform,
                l.LastActivityAt,
                l.CreatedAt,
                l.DeletedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var leadIds = leads.Select(l => l.Id).ToList();
        var leadActivities = leadIds.Count == 0
            ? []
            : await _db.LeadActivities.IgnoreQueryFilters()
                .Where(a => a.TenantId == tenantId && leadIds.Contains(a.LeadId))
                .OrderBy(a => a.OccurredAt)
                .Select(a => new LeadActivityExportDto(a.Id, a.LeadId, a.ActivityType, a.Notes, a.MetaJson, a.OccurredAt))
                .ToListAsync(ct)
                .ConfigureAwait(false);

        var documents = await _db.GeneratedDocuments.IgnoreQueryFilters()
            .Where(d => d.TenantId == tenantId && d.ContactId == contactId)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new GeneratedDocumentExportDto(
                d.Id,
                d.TemplateId,
                d.GeneratedBy,
                d.FileUrl,
                d.FileHash,
                d.SentVia,
                d.SentAt,
                d.OpenedAt,
                d.CreatedAt,
                d.ExpiresAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var export = new ContactDataExport(
            DateTimeOffset.UtcNow,
            contact,
            externalIds,
            conversations,
            leads,
            leadActivities,
            documents);

        return new ContactDataExportResult($"contact-{contactId:N}-data-export.json", export);
    }
}

public sealed record ContactDataExportResult(string FileName, ContactDataExport Export);

public sealed record ContactDataExport(
    DateTimeOffset ExportedAt,
    ContactExportDto Contact,
    IReadOnlyList<ContactExternalIdExportDto> ExternalIds,
    IReadOnlyList<ConversationExportDto> Conversations,
    IReadOnlyList<LeadExportDto> Leads,
    IReadOnlyList<LeadActivityExportDto> LeadActivities,
    IReadOnlyList<GeneratedDocumentExportDto> Documents);

public sealed record ContactExportDto(
    Guid Id,
    string DisplayName,
    string? Phone,
    string? Email,
    string Locale,
    int LifetimeScore,
    string LifecycleStage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record ContactExternalIdExportDto(
    Guid Id,
    string Platform,
    string ExternalId,
    DateTimeOffset FirstSeenAt);

public sealed record ConversationExportDto(
    Guid Id,
    string Platform,
    string ExternalThreadId,
    string Status,
    Guid? AssignedTo,
    DateTimeOffset? LastMessageAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt,
    IReadOnlyList<MessageExportDto> Messages);

public sealed record MessageExportDto(
    Guid Id,
    Guid ConversationId,
    string Direction,
    string SenderType,
    Guid? SenderUserId,
    string Content,
    string ContentType,
    string MessageType,
    string? ParentPostId,
    string? ExternalMessageId,
    string? OriginalContent,
    string? RedactedContent,
    DateTimeOffset SentAt);

public sealed record LeadExportDto(
    Guid Id,
    Guid? OwnerUserId,
    int Score,
    string Stage,
    string? SourcePlatform,
    DateTimeOffset? LastActivityAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt);

public sealed record LeadActivityExportDto(
    Guid Id,
    Guid LeadId,
    string ActivityType,
    string? Notes,
    string MetaJson,
    DateTimeOffset OccurredAt);

public sealed record GeneratedDocumentExportDto(
    Guid Id,
    Guid TemplateId,
    Guid? GeneratedBy,
    string FileUrl,
    string? FileHash,
    string? SentVia,
    DateTimeOffset? SentAt,
    DateTimeOffset? OpenedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);
