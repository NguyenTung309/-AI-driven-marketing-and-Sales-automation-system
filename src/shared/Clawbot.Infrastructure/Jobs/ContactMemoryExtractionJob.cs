using System.Text;
using Clawbot.Agents.Core.Learning;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Contacts;
using Clawbot.Infrastructure.Learning;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Jobs;

// ai-self-learning-memory Lớp 2: scan hội thoại idle có tin mới, LLM trích facts về khách,
// memory-ops với facts hiện có (add/update/delete/noop). Recurring scan thay vì consumer
// per-message (bus 2 host). Watermark memory_extracted_at CHỈ set khi trích thành công.
public sealed partial class ContactMemoryExtractionJob(
    AppDbContext db,
    ContactFactExtractor extractor,
    IPiiRedactor pii,
    IClock clock,
    IOptions<LearningOptions> options,
    ILogger<ContactMemoryExtractionJob> logger)
{
    private static readonly TimeSpan IdleWindow = TimeSpan.FromMinutes(15);
    private const int TranscriptMaxMessages = 30;

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunScanAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var idleCutoff = now - IdleWindow;

        // Tin mới sau lần trích trước + đã im ắng đủ lâu + có contact để gán facts.
        var due = await db.Conversations.IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null
                && c.ContactId != null
                && c.LastMessageAt != null
                && c.LastMessageAt <= idleCutoff
                && (c.MemoryExtractedAt == null || c.LastMessageAt > c.MemoryExtractedAt))
            .OrderBy(c => c.LastMessageAt)
            .Take(options.Value.MaxConversationsPerScan)
            .Select(c => new { c.Id, c.TenantId, ContactId = c.ContactId!.Value })
            .ToListAsync(ct).ConfigureAwait(false);

        if (due.Count == 0) return;

        var processed = 0;
        foreach (var conversation in due)
        {
            try
            {
                await ExtractForConversationAsync(conversation.TenantId, conversation.Id, conversation.ContactId, now, ct)
                    .ConfigureAwait(false);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Không set watermark -> lượt sau quét lại hội thoại này.
                LogConversationFailed(logger, ex, conversation.Id);
            }
        }

        LogScanCompleted(logger, processed, due.Count);
    }

    private async Task ExtractForConversationAsync(
        Guid tenantId,
        Guid conversationId,
        Guid contactId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var messages = await db.Messages.IgnoreQueryFilters()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.SentAt)
            .Take(TranscriptMaxMessages)
            .Select(m => new { m.Direction, m.SenderType, m.Content, m.SentAt })
            .ToListAsync(ct).ConfigureAwait(false);

        var transcript = new StringBuilder();
        foreach (var msg in messages.OrderBy(m => m.SentAt))
        {
            if (string.IsNullOrWhiteSpace(msg.Content)) continue;
            var speaker = msg.Direction == "in" ? "khách" : msg.SenderType == "user" ? "sale" : "AI";
            transcript.Append(speaker).Append(": ").AppendLine(msg.Content);
        }

        var existing = await db.ContactMemories.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.ContactId == contactId && m.IsActive)
            .OrderByDescending(m => m.UpdatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        var existingFacts = existing
            .Select(m => new ContactFact(m.Id, m.Fact, m.Category, m.Confidence))
            .ToList();

        var ops = await extractor.ExtractAsync(tenantId, transcript.ToString(), existingFacts, ct).ConfigureAwait(false);
        if (ops is null)
            throw new InvalidOperationException("contact_fact_extraction_failed"); // giữ watermark, thử lại lượt sau

        var byId = existing.ToDictionary(m => m.Id);
        foreach (var op in ops)
        {
            switch (op.Op)
            {
                case "add":
                {
                    var fact = await RedactAsync(op.Fact!, ct).ConfigureAwait(false);
                    db.ContactMemories.Add(ContactMemory.Create(
                        tenantId, contactId, fact, op.Category!, op.Confidence ?? 0.7m, conversationId, now));
                    break;
                }
                case "update":
                {
                    var fact = await RedactAsync(op.Fact!, ct).ConfigureAwait(false);
                    var replacement = ContactMemory.Create(
                        tenantId, contactId, fact, op.Category!, op.Confidence ?? 0.7m, conversationId, now);
                    db.ContactMemories.Add(replacement);
                    if (byId.TryGetValue(op.FactId!.Value, out var old) && old.IsActive)
                        old.Supersede(replacement.Id, now);
                    break;
                }
                case "delete":
                {
                    if (byId.TryGetValue(op.FactId!.Value, out var old) && old.IsActive)
                        old.Supersede(null, now);
                    break;
                }
            }
        }

        var conversation = await db.Conversations.IgnoreQueryFilters()
            .FirstAsync(c => c.Id == conversationId, ct).ConfigureAwait(false);
        conversation.MarkMemoryExtracted(now);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<string> RedactAsync(string text, CancellationToken ct) =>
        (await pii.RedactAsync(text, ct).ConfigureAwait(false)).RedactedText;

    [LoggerMessage(EventId = 12301, Level = LogLevel.Warning, Message = "ContactMemoryExtraction failed for conversation {ConversationId}")]
    private static partial void LogConversationFailed(ILogger logger, Exception ex, Guid conversationId);

    [LoggerMessage(EventId = 12302, Level = LogLevel.Information, Message = "ContactMemoryExtraction processed {Processed}/{Due} conversations")]
    private static partial void LogScanCompleted(ILogger logger, int processed, int due);
}
