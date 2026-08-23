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

        // Xac dinh direction som: so sanh sender_id vs page_id, hoac flag tu channel adapter.
        // Phai biet truoc khi upsert contact de tin outbound (admin/AI) khong ghi de contact.
        var senderId = message.Metadata.TryGetValue("sender_id", out var sid) ? sid : "";
        var pageId = message.Metadata.TryGetValue("page_id", out var pid) ? pid : "";
        var isOwner = !string.IsNullOrEmpty(senderId) && !string.IsNullOrEmpty(pageId)
            && string.Equals(senderId, pageId, StringComparison.Ordinal);
        if (!isOwner && message.Metadata.TryGetValue("is_owner", out var ownerFlag))
            isOwner = string.Equals(ownerFlag, "true", StringComparison.OrdinalIgnoreCase);

        var conversation = await UpsertConversationAsync(tenantId, message, isOwner, ct).ConfigureAwait(false);

        // Update display name / avatar only for the sender contact (never for owner/AI echo messages)
        Contact? senderContact = null;
        if (!isOwner)
        {
            var senderMeta = new Dictionary<string, string>(message.Metadata, StringComparer.Ordinal);
            senderMeta.Remove("avatar_url");
            if (message.Metadata.TryGetValue("sender_name", out var senderName) && !string.IsNullOrWhiteSpace(senderName))
                senderMeta["display_name"] = senderName;
            var senderMessage = message with { Metadata = senderMeta };
            senderContact = await UpsertContactAsync(tenantId, senderMessage, ct).ConfigureAwait(false);
            await UpdateContactMetadataAsync(tenantId, senderContact, senderMessage, ct).ConfigureAwait(false);
        }

        // Section 9: Auto-reopen resolved/snoozed on inbound message
        conversation.ReopenIfNeeded();

        // Dedup phai so text da StripHtml: Pancake echo boc HTML/entity, row outbound local luu plain text
        var cleanText = StripHtml(message.Text);

        if (await IsDuplicateAsync(tenantId, conversation.Id, message, cleanText, isOwner, ct).ConfigureAwait(false))
        {
            LogDuplicate(_logger, conversation.Id, message.ExternalThreadId);
            return new IngestResult(conversation.Id, null, true);
        }

        var externalMsgId = message.Metadata.TryGetValue("external_message_id", out var extId) ? extId : null;

        // PII redaction: store original + redacted versions
        var redacted = await _pii.RedactAsync(cleanText, ct).ConfigureAwait(false);

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
            parentCommentId: message.ParentCommentId,
            senderDisplayName: senderDisplayName ?? senderContact?.DisplayName,
            senderAvatarUrl: finalAvatarUrl,
            attachmentUrl: attachmentUrl);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _notifier.NotifyMessageAsync(tenantId, new InboxMessageEvent(
            conversation.Id, msg.Id, msg.Direction, msg.SenderType, msg.Content, msg.ContentType, msg.SentAt,
            AssignedTo: conversation.AssignedTo,
            SenderDisplayName: msg.SenderDisplayName,
            SenderAvatarUrl: msg.SenderAvatarUrl,
            InboxId: conversation.InboxId,
            ConversationStatus: conversation.Status), ct).ConfigureAwait(false);

        return new IngestResult(conversation.Id, msg.Id, false);
    }

    private async Task<Conversation> UpsertConversationAsync(Guid tenantId, ChannelMessage message, bool isOwner, CancellationToken ct)
    {
        var normalizedThread = ExtractCustomerExternalId(message.Channel, message.ExternalThreadId);
        var existing = await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId
                && c.Platform == message.Channel
                && (c.ExternalThreadId == message.ExternalThreadId || c.ExternalThreadId == normalizedThread))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        // Use customer_id from metadata (set by polling service) or extract from thread ID
        var customerExternalId = message.Metadata.TryGetValue("customer_id", out var cid) && !string.IsNullOrWhiteSpace(cid)
            ? cid
            : ExtractCustomerExternalId(message.Channel, message.ExternalThreadId);

        // Doi tac hoi thoai la nhom (Pancake bao qua is_group cua conv.From) — dung de loai nhom khoi dem Lead.
        // Fallback: Pancake tu sinh id nhom Zalo dang "...:pzl_g_<pageId>_<id>" (ca nhan la "pzl_u_") —
        // phong khi adapter nao do khong gan duoc metadata is_group.
        var isGroup = (message.Metadata.TryGetValue("is_group", out var isGroupRaw)
                && string.Equals(isGroupRaw, "true", StringComparison.OrdinalIgnoreCase))
            || message.ExternalThreadId.Contains(":pzl_g_", StringComparison.Ordinal);

        // Contact cua hoi thoai (list ben trai) chi nhan name/avatar tu conversation_* (doi tac hoi thoai:
        // nhom hoac khach) — khong bao gio tu sender cua tung tin nhan, tranh ghi de boi admin/AI/thanh vien nhom.
        var hasConversationName = message.Metadata.TryGetValue("conversation_name", out var convName) && !string.IsNullOrWhiteSpace(convName);
        var hasConversationAvatar = message.Metadata.TryGetValue("conversation_avatar_url", out var convAvatar) && !string.IsNullOrWhiteSpace(convAvatar);
        var custMeta = message.Metadata;
        if (hasConversationName || hasConversationAvatar)
        {
            var rebuilt = message.Metadata
                .Where(kv => kv.Key is not ("display_name" or "sender_name" or "sender_avatar_url" or "avatar_url"))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (hasConversationName) rebuilt["display_name"] = convName!;
            if (hasConversationAvatar) rebuilt["avatar_url"] = convAvatar!;
            custMeta = rebuilt;
        }
        else if (isOwner)
        {
            // Legacy adapters (webhook) khong co conversation_*: strip sender metadata cho tin outbound
            custMeta = message.Metadata
                .Where(kv => kv.Key is not ("display_name" or "sender_name" or "sender_avatar_url" or "avatar_url"))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }
        var customerMessage = message with { ExternalUserId = customerExternalId, Metadata = custMeta };
        // conversation_name la nguon chinh thuc tu Pancake -> duoc phep sua ten contact da co (self-heal contact hong)
        var contact = await UpsertContactAsync(tenantId, customerMessage, ct, authoritativeName: hasConversationName).ConfigureAwait(false);
        var inboxId = await ResolveInboxIdAsync(tenantId, message, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var needsSave = false;
            // Self-heal: hoi thoai tao truoc khi co inbox (inbox_id NULL) duoc gan lai khi co tin moi;
            // auto-owner lead va filter theo kenh deu can inbox_id nay.
            if (existing.InboxId is null && inboxId is { } resolvedInboxId)
            {
                existing.SetInboxId(resolvedInboxId);
                needsSave = true;
            }
            // Self-heal mot chieu: hoi thoai tao truoc khi biet la nhom duoc danh dau lai khi
            // Pancake xac nhan is_group; khong bao gio go lai neu tin sau thieu co.
            if (isGroup && !existing.IsGroup)
            {
                existing.MarkGroup();
                needsSave = true;
            }
            if (needsSave)
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return existing;
        }

        var conv = Conversation.Open(tenantId, message.Channel, message.ExternalThreadId, _clock.UtcNow, contact?.Id, inboxId, isGroup);
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
            .Where(i => i.TenantId == tenantId
                && i.Platform == message.Channel
                && i.IsActive
                && i.DeletedAt == null)
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

        // Fallback 2 is safe only when the tenant has exactly one active inbox for this platform.
        // Picking the first inbox from a multi-page tenant can route a customer to the wrong sale team.
        if (matchedInboxes.Count == 0 && inboxes.Count == 1)
            matchedInboxes.Add(inboxes[0]);

        if (matchedInboxes.Count == 0) return null;

        return matchedInboxes[0].Id;
    }

    private static bool IsPageIdMatch(string externalThreadId, string externalPageId)
    {
        if (string.IsNullOrWhiteSpace(externalThreadId) || string.IsNullOrWhiteSpace(externalPageId))
            return false;

        // Thread ID format: {pageId}:{convId}
        // Extract prefix before colon and compare exactly with externalPageId
        var colonIdx = externalThreadId.IndexOf(':');
        var prefix = colonIdx > 0 ? externalThreadId[..colonIdx] : externalThreadId;

        return string.Equals(prefix, externalPageId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Contact?> UpsertContactAsync(Guid tenantId, ChannelMessage message, CancellationToken ct, bool authoritativeName = false)
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
            // authoritativeName (conversation_name tu Pancake): duoc sua ca ten sai da luu (self-heal);
            // nguoc lai chi dien ten khi contact van con placeholder (external id / pzl_*)
            var canRename = authoritativeName
                || existing.DisplayName == message.ExternalUserId
                || existing.DisplayName.StartsWith("pzl_", StringComparison.Ordinal);
            if (newName != null && canRename && existing.DisplayName != newName)
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

    private async Task<bool> IsDuplicateAsync(Guid tenantId, Guid conversationId, ChannelMessage message, string cleanText, bool isOwner, CancellationToken ct)
    {
        // Strict dedup: use external_message_id if available
        if (message.Metadata.TryGetValue("external_message_id", out var externalId) && !string.IsNullOrEmpty(externalId))
        {
            var exists = await _db.Messages
                .IgnoreQueryFilters()
                .AnyAsync(m => m.TenantId == tenantId
                    && m.ExternalMessageId == externalId, ct).ConfigureAwait(false);
            if (exists) return true;
            if (!isOwner) return false;
            // Owner message with a fresh external id can still be the channel echo of a reply we
            // already persisted locally (sale manual send / AI auto-reply) - those rows have no
            // external id, so match on content within a short window instead.
            return await IsLocalOutboundEchoAsync(conversationId, message.SentAt, cleanText, ct).ConfigureAwait(false);
        }

        if (isOwner)
            return await IsLocalOutboundEchoAsync(conversationId, message.SentAt, cleanText, ct).ConfigureAwait(false);

        // Fallback: heuristic dedup
        return await _db.Messages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.ConversationId == conversationId
                && m.SentAt == message.SentAt
                && m.Content == cleanText
                && m.Direction == "in", ct).ConfigureAwait(false);
    }

    private async Task<bool> IsLocalOutboundEchoAsync(Guid conversationId, DateTimeOffset sentAt, string cleanText, CancellationToken ct)
    {
        var from = sentAt.AddMinutes(-10);
        var to = sentAt.AddMinutes(10);
        return await _db.Messages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.ConversationId == conversationId
                && m.Direction == "out"
                && m.ExternalMessageId == null
                && m.Content == cleanText
                && m.SentAt >= from && m.SentAt <= to, ct).ConfigureAwait(false);
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

    // internal: ChannelInboundMessageConsumer strip text trước khi đưa vào ChatAgent (tin Pancake bọc HTML).
    internal static string StripHtml(string input)
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