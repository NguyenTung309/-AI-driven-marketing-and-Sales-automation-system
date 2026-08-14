using Clawbot.Api.Contracts.SaleAssist;
using Clawbot.Api.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

// Danh sách "Lead tiềm năng khác": KHÔNG gọi LLM đồng bộ. Chỉ đọc cache sale_assist_upsell_suggestions;
// lead nào thiếu/cũ (hội thoại có tin nhắn mới hơn SourceLastMessageAt) thì bắn background job
// "Gợi ý upsell" rồi trả Pending=true — FE poll lại cho đến khi job ghi cache xong.
// Idempotency key trùng với nút "Xem gợi ý bán thêm" nên 2 luồng không bao giờ đẻ job trùng.
public sealed class SaleAssistUpsellSuggestionService(
    AppDbContext db,
    IJobLauncher jobs)
{
    private readonly AppDbContext _db = db;
    private readonly IJobLauncher _jobs = jobs;

    // NOTE: HangfireJobLauncher lấy tenant từ ambient ITenantAccessor (bỏ qua tenantId ở đây),
    // nên service này chỉ được gọi trên HTTP path đã resolve tenant. tenantId chỉ dùng cho query.
    public async Task<SaleAssistUpsellSuggestionsResponse> GetSuggestionsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> inboxIds,
        int take = 5,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId required", nameof(tenantId));
        ArgumentNullException.ThrowIfNull(inboxIds);

        var leadQuery = _db.Leads.IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId && l.Stage == "hot" && l.DeletedAt == null)
            .ApplyInboxScope(_db, tenantId, inboxIds);

        var limit = Math.Clamp(take, 1, 50);
        var leads = await leadQuery
            .OrderByDescending(l => l.Score)
            .ThenByDescending(l => l.LastActivityAt)
            .Take(limit)
            .Select(l => new HotLeadCandidate(l.Id, l.ContactId, l.Score, l.LastActivityAt))
            .ToListAsync(ct).ConfigureAwait(false);

        if (leads.Count == 0)
            return new SaleAssistUpsellSuggestionsResponse([], 0);

        var contactIds = leads
            .Where(l => l.ContactId.HasValue)
            .Select(l => l.ContactId!.Value)
            .Distinct()
            .ToList();

        var contacts = await _db.Contacts.IgnoreQueryFilters()
            .Where(c =>
                c.TenantId == tenantId &&
                c.DeletedAt == null &&
                contactIds.Contains(c.Id))
            .Select(c => new { c.Id, c.DisplayName, c.Phone })
            .ToDictionaryAsync(c => c.Id, c => new SaleAssistHotLeadContactDto(c.DisplayName, c.Phone), ct)
            .ConfigureAwait(false);

        var conversationQuery = _db.Conversations.IgnoreQueryFilters()
            .Where(c =>
                c.TenantId == tenantId &&
                c.ContactId.HasValue &&
                contactIds.Contains(c.ContactId.Value) &&
                c.DeletedAt == null)
            .ApplyInboxScope(inboxIds);

        var latestConversations = await conversationQuery
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Select(c => new { c.Id, ContactId = c.ContactId!.Value, LastMessageAt = c.LastMessageAt ?? c.CreatedAt })
            .ToListAsync(ct).ConfigureAwait(false);

        var conversationByContact = latestConversations
            .GroupBy(c => c.ContactId)
            .ToDictionary(g => g.Key, g => (g.First().Id, g.First().LastMessageAt));

        var conversationIds = conversationByContact.Values.Select(v => v.Id).ToList();
        var cacheByConversation = await _db.UpsellSuggestionCaches.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && conversationIds.Contains(x.ConversationId))
            .ToDictionaryAsync(x => x.ConversationId, ct)
            .ConfigureAwait(false);

        var items = new List<SaleAssistHotLeadDto>(leads.Count);
        foreach (var lead in leads)
        {
            var contact = lead.ContactId.HasValue && contacts.TryGetValue(lead.ContactId.Value, out var contactDto)
                ? contactDto
                : null;

            if (!lead.ContactId.HasValue || !conversationByContact.TryGetValue(lead.ContactId.Value, out var conv))
            {
                items.Add(new SaleAssistHotLeadDto(
                    lead.Id,
                    ConversationId: null,
                    lead.Score,
                    lead.LastActivityAt,
                    contact,
                    Eligible: false,
                    Suggestion: string.Empty,
                    Reason: "no conversation for lead",
                    Pending: false));
                continue;
            }

            var (conversationId, lastMessageAt) = conv;
            if (cacheByConversation.TryGetValue(conversationId, out var cached)
                && cached.SourceLastMessageAt >= lastMessageAt)
            {
                items.Add(new SaleAssistHotLeadDto(
                    lead.Id,
                    conversationId,
                    lead.Score,
                    lead.LastActivityAt,
                    contact,
                    cached.Eligible,
                    cached.Suggestion,
                    cached.Reason,
                    Pending: false));
                continue;
            }

            await _jobs.LaunchAsync(
                SaleAssistUpsellJobHandler.JobType, "Gợi ý upsell",
                new SaleAssistConversationJobPayload(conversationId),
                idempotencyKey: $"saleassist.upsell:{conversationId}", ct: ct)
                .ConfigureAwait(false);

            items.Add(new SaleAssistHotLeadDto(
                lead.Id,
                conversationId,
                lead.Score,
                lead.LastActivityAt,
                contact,
                Eligible: false,
                Suggestion: string.Empty,
                Reason: "generating",
                Pending: true));
        }

        return new SaleAssistUpsellSuggestionsResponse(items, items.Count);
    }

    private sealed record HotLeadCandidate(Guid Id, Guid? ContactId, int Score, DateTimeOffset? LastActivityAt);
}
