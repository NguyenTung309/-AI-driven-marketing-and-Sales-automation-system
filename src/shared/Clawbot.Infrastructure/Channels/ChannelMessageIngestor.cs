using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Channels;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Persistence;
using Clawbot.Infrastructure.Vectors;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Inbox;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Net;

namespace Clawbot.Infrastructure.Channels;

public interface IChannelMessageIngestor
{
    Task<IngestResult> IngestAsync(Guid tenantId, ChannelMessage message, CancellationToken ct = default);
}

public sealed record IngestResult(Guid ConversationId, Guid? MessageId, bool Deduplicated);

public sealed partial class ChannelMessageIngestor(
    AppDbContext db,
    IInboxNotifier notifier,
    IClock clock,
    IContactEmbeddingSync embeddingSync,
    IPiiRedactor piiRedactor,
    ILogger<ChannelMessageIngestor> logger) : IChannelMessageIngestor
{
    private readonly AppDbContext _db = db;
    private readonly IInboxNotifier _notifier = notifier;
    private readonly IClock _clock = clock;
    private readonly IContactEmbeddingSync _embeddingSync = embeddingSync;
    private readonly IPiiRedactor _pii = piiRedactor;
    private readonly ILogger<ChannelMessageIngestor> _logger = logger;

    public async Task<IngestResult> IngestAsync(Guid tenantId, ChannelMessage message, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("tenantId required", nameof(tenantId));
        ArgumentNullException.ThrowIfNull(message);

        var conversation = await UpsertConversationAsync(tenantId, message, ct).ConfigureAwait(false);

        // Update contact display name / avatar only for the sender contact (not the whole conversation contact)
        var senderContact = await UpsertContactAsync(tenantId, message, ct).ConfigureAwait(false);
        await UpdateContactMetadataAsync(tenantId, senderContact, message, ct).ConfigureAwait(false);

        // Section 9: Auto-reopen resolved/snoozed on inbound message
        conversation.ReopenIfNeeded();

        if (await IsDuplicateAsync(conversation.Id, message, ct).ConfigureAwait(false))
        {
            LogDuplicate(_logger, conversation.Id, message.ExternalThreadId);
            return new IngestResult(conversation.Id, null, true);
        }

        var externalMsgId = message.Metadata.TryGetValue("external_message_id", out var extId) ? extId : null;

        var cleanText = StripHtml(message.Text);

        // PII redaction: store original + redacted versions
        var redacted = await _pii.RedactAsync(cleanText, ct).ConfigureAwait(false);

        // Xac dinh direction: so sanh sender_id vs page_id
        var senderId = message.Metadata.TryGetValue("sender_id", out var sid) ? sid : "";
        var pageId = message.Metadata.TryGetValue("page_id", out var pid) ? pid : "";
        var isOwner = !string.IsNullOrEmpty(senderId) && !string.IsNullOrEmpty(pageId)
            && string.Equals(senderId, pageId, StringComparison.Ordinal);
        var direction = isOwner ? "out" : "in";
        var senderType = isOwner ? "user" : "contact";
        var senderDisplayName = message.Metadata.TryGetValue("sender_name", out var sn) ? sn : null;
        var senderAvatarFromMeta = message.Metadata.TryGetValue("sender_avatar_url", out var sav) ? sav : null;
        var attachmentUrl = message.Metadata.TryGetValue("attachment_url", out var attUrl) ? attUrl : null;

        var finalAvatarUrl = senderAvatarFromMeta;
        if (IsDefaultAvatar(finalAvatarUrl) && senderContact != null && !IsDefaultAvatar(senderContact.AvatarUrl))
        {
            finalAvatarUrl = senderContact.AvatarUrl;
        }
        else if (string.IsNullOrEmpty(finalAvatarUrl))
        {
            finalAvatarUrl = senderContact?.AvatarUrl;
        }

        var msg = conversation.AppendMessage(
            direction: direction,
            senderType: senderType,
            content: redacted.RedactedText,
            contentType: message.Metadata.TryGetValue("content_type", out var ct2) ? ct2 : "text",
            sentAt: message.SentAt,
            senderUserId: null,
            externalMessageId: externalMsgId,
            originalContent: cleanText,
            redactedContent: redacted.RedactedText,
            messageType: message.MessageType,
            parentPostId: message.ParentPostId,
            senderDisplayName: senderDisplayName ?? senderContact?.DisplayName,
            senderAvatarUrl: finalAvatarUrl,
            attachmentUrl: attachmentUrl);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _notifier.NotifyMessageAsync(tenantId, new InboxMessageEvent(
            conversation.Id, msg.Id, msg.Direction, msg.SenderType, msg.Content, msg.ContentType, msg.SentAt,
            SenderDisplayName: msg.SenderDisplayName,
            SenderAvatarUrl: msg.SenderAvatarUrl), ct).ConfigureAwait(false);

        return new IngestResult(conversation.Id, msg.Id, false);
    }

    private async Task<Conversation> UpsertConversationAsync(Guid tenantId, ChannelMessage message, CancellationToken ct)
    {
        var existing = await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId
                && c.Platform == message.Channel
                && c.ExternalThreadId == message.ExternalThreadId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (existing is not null) return existing;

        // Ensure we link the conversation to the customer Contact, not the Page Contact (even if the first message is outbound)
        var customerExternalId = ExtractCustomerExternalId(message.Channel, message.ExternalThreadId);
        var customerMessage = message with { ExternalUserId = customerExternalId };
        var contact = await UpsertContactAsync(tenantId, customerMessage, ct).ConfigureAwait(false);
        var inboxId = await ResolveInboxIdAsync(tenantId, message, ct).ConfigureAwait(false);
        var conv = Conversation.Open(tenantId, message.Channel, message.ExternalThreadId, _clock.UtcNow, contact?.Id, inboxId);
        _db.Conversations.Add(conv);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return conv;
    }

    private Task UpdateContactMetadataAsync(Guid tenantId, Contact? senderContact, ChannelMessage message, CancellationToken ct)
    {
        if (senderContact is null) return Task.CompletedTask;

        var newName = message.Metadata.TryGetValue("display_name", out var dn) && !string.IsNullOrWhiteSpace(dn) ? dn : null;
        if (newName != null && (senderContact.DisplayName == message.ExternalUserId || senderContact.DisplayName.StartsWith("pzl_", StringComparison.Ordinal)))
        {
            senderContact.UpdateDisplayName(newName);
        }

        var avatarUrl = message.Metadata.TryGetValue("avatar_url", out var av) && !string.IsNullOrWhiteSpace(av) ? av 
            : (message.Metadata.TryGetValue("sender_avatar_url", out var sav) && !string.IsNullOrWhiteSpace(sav) ? sav : null);

        if (avatarUrl != null && !IsDefaultAvatar(avatarUrl))
        {
            senderContact.UpdateAvatar(avatarUrl, _clock.UtcNow);
        }

        return Task.CompletedTask;
    }
    private async Task<Guid?> ResolveInboxIdAsync(Guid tenantId, ChannelMessage message, CancellationToken ct)
    {
        // 1. Query all inboxes for this platform
        var inboxes = await _db.Inboxes
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && i.Platform == message.Channel)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var matchedInboxes = new List<Inbox>();

        // Check metadata page_id first
        if (message.Metadata.TryGetValue("page_id", out var pageId) && !string.IsNullOrWhiteSpace(pageId))
        {
            matchedInboxes.AddRange(inboxes.Where(i => i.ExternalPageId == pageId));
        }

        // Fallback: check ExternalThreadId page matching
        if (matchedInboxes.Count == 0)
        {
            foreach (var inbox in inboxes)
            {
                if (IsPageIdMatch(message.ExternalThreadId, inbox.ExternalPageId))
                {
                    matchedInboxes.Add(inbox);
                }
            }
        }

        // Fallback 2: check platform fallback if there's only one inbox
        if (matchedInboxes.Count == 0)
        {
            matchedInboxes.AddRange(inboxes);
        }

        if (matchedInboxes.Count == 0) return null;

        return matchedInboxes[0].Id;
    }

    private static bool IsPageIdMatch(string externalThreadId, string externalPageId)
    {
        if (string.IsNullOrWhiteSpace(externalThreadId) || string.IsNullOrWhiteSpace(externalPageId))
            return false;

        if (externalThreadId.Contains(externalPageId, StringComparison.OrdinalIgnoreCase))
            return true;

        var pageDigits = new string(externalPageId.Where(char.IsDigit).ToArray());
        if (!string.IsNullOrEmpty(pageDigits) && externalThreadId.Contains(pageDigits, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private async Task<Contact?> UpsertContactAsync(Guid tenantId, ChannelMessage message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.ExternalUserId)) return null;

        var existing = await _db.ContactExternalIds
            .IgnoreQueryFilters()
            .Where(x => x.Platform == message.Channel && x.ExternalId == message.ExternalUserId)
            .Join(_db.Contacts.IgnoreQueryFilters(), x => x.ContactId, c => c.Id, (x, c) => c)
            .Where(c => c.TenantId == tenantId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var avatarUrl = message.Metadata.TryGetValue("avatar_url", out var av) && !string.IsNullOrWhiteSpace(av) ? av 
            : (message.Metadata.TryGetValue("sender_avatar_url", out var sav) && !string.IsNullOrWhiteSpace(sav) ? sav : null);

        if (existing is not null)
        {
            var newName = message.Metadata.TryGetValue("display_name", out var dn) && !string.IsNullOrWhiteSpace(dn) ? dn : null;
            if (newName != null && (existing.DisplayName == message.ExternalUserId || existing.DisplayName.StartsWith("pzl_", StringComparison.Ordinal)))
            {
                existing.UpdateDisplayName(newName);
            }

            if (avatarUrl != null && !IsDefaultAvatar(avatarUrl))
            {
                existing.UpdateAvatar(avatarUrl, _clock.UtcNow);
            }
            return existing;
        }

        var displayName = message.Metadata.TryGetValue("display_name", out var existingDn) && !string.IsNullOrWhiteSpace(existingDn)
            ? existingDn
            : message.ExternalUserId;
        var contact = Contact.Create(tenantId, displayName, _clock.UtcNow);
        if (avatarUrl != null && !IsDefaultAvatar(avatarUrl))
            contact.UpdateAvatar(avatarUrl, _clock.UtcNow);
        contact.LinkExternalId(message.Channel, message.ExternalUserId, _clock.UtcNow);
        _db.Contacts.Add(contact);

        // C6: Upsert contact embedding to Qdrant "contacts" collection for fuzzy dedup
        try
        {
            await _embeddingSync.UpsertContactAsync(contact, tenantId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogEmbeddingUpsertFailed(_logger, ex, contact.Id);
        }

        return contact;
    }

    private async Task<bool> IsDuplicateAsync(Guid conversationId, ChannelMessage message, CancellationToken ct)
    {
        // Strict dedup: use external_message_id if available
        if (message.Metadata.TryGetValue("external_message_id", out var externalId) && !string.IsNullOrEmpty(externalId))
        {
            return await _db.Messages
                .IgnoreQueryFilters()
                .AnyAsync(m => m.ExternalMessageId == externalId, ct).ConfigureAwait(false);
        }

        // Fallback: heuristic dedup
        return await _db.Messages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.ConversationId == conversationId
                && m.SentAt == message.SentAt
                && m.Content == message.Text
                && m.Direction == "in", ct).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Duplicate inbound message ignored for conv {ConversationId} thread {ThreadId}")]
    private static partial void LogDuplicate(ILogger logger, Guid conversationId, string threadId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning, Message = "Contact embedding upsert failed for {ContactId}")]
    private static partial void LogEmbeddingUpsertFailed(ILogger logger, Exception ex, Guid contactId);

    private static bool IsDefaultAvatar(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        return url.Contains("b4.jpg", StringComparison.OrdinalIgnoreCase) 
            || url.Contains("b0.jpg", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractCustomerExternalId(string platform, string externalThreadId)
    {
        if (string.IsNullOrEmpty(externalThreadId)) return string.Empty;
        var idx = externalThreadId.IndexOf(':', StringComparison.Ordinal);
        return idx > 0 ? externalThreadId[(idx + 1)..] : externalThreadId;
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // Replace common line break tags with newlines
        var text = input;
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</?(p|div|h[1-6]|li)[^>]*>", "\n", RegexOptions.IgnoreCase);

        // Strip all other HTML tags
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);

        // Decode HTML entities (e.g. &amp;, &lt;, &gt;, &quot;)
        text = WebUtility.HtmlDecode(text);

        // Normalize multiple consecutive newlines
        text = Regex.Replace(text, @"\n+", "\n");

        return text.Trim();
    }
}
