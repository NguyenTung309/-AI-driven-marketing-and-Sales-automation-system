using Clawbot.Agents.Contracts.SaleAssist;
using Clawbot.Api.Contracts.SaleAssist;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

public interface ISaleAssistUpsellClient
{
    Task<SaleAssistUpsellResponse> GetUpsellAsync(Guid tenantId, Guid conversationId, CancellationToken ct = default);
}

public sealed class GrpcSaleAssistUpsellClient(
    SaleAssistAgent.SaleAssistAgentClient grpc) : ISaleAssistUpsellClient
{
    public async Task<SaleAssistUpsellResponse> GetUpsellAsync(Guid tenantId, Guid conversationId, CancellationToken ct = default)
    {
        var resp = await grpc.UpsellAsync(new UpsellRequest
        {
            TenantId = tenantId.ToString(),
            ConversationId = conversationId.ToString(),
        }, cancellationToken: ct);

        return new SaleAssistUpsellResponse(resp.Eligible, resp.Suggestion, resp.Reason, resp.LeadScore);
    }
}

public sealed class SaleAssistUpsellSuggestionService(
    AppDbContext db,
    ISaleAssistUpsellClient upsellClient)
{
    private readonly AppDbContext _db = db;
    private readonly ISaleAssistUpsellClient _upsellClient = upsellClient;

    public async Task<SaleAssistUpsellSuggestionsResponse> GetSuggestionsAsync(
        Guid tenantId,
        int take = 20,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenantId required", nameof(tenantId));

        var limit = Math.Clamp(take, 1, 50);
        var leads = await _db.Leads.IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId && l.Stage == "hot" && l.DeletedAt == null)
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
            .Where(c => contactIds.Contains(c.Id))
            .Select(c => new { c.Id, c.DisplayName, c.Phone })
            .ToDictionaryAsync(c => c.Id, c => new SaleAssistHotLeadContactDto(c.DisplayName, c.Phone), ct)
            .ConfigureAwait(false);

        var latestConversations = await _db.Conversations.IgnoreQueryFilters()
            .Where(c =>
                c.TenantId == tenantId &&
                c.ContactId.HasValue &&
                contactIds.Contains(c.ContactId.Value) &&
                c.DeletedAt == null)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Select(c => new { c.Id, ContactId = c.ContactId!.Value })
            .ToListAsync(ct).ConfigureAwait(false);

        var conversationByContact = latestConversations
            .GroupBy(c => c.ContactId)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var items = new List<SaleAssistHotLeadDto>(leads.Count);
        foreach (var lead in leads)
        {
            var contact = lead.ContactId.HasValue && contacts.TryGetValue(lead.ContactId.Value, out var contactDto)
                ? contactDto
                : null;

            if (!lead.ContactId.HasValue || !conversationByContact.TryGetValue(lead.ContactId.Value, out var conversationId))
            {
                items.Add(new SaleAssistHotLeadDto(
                    lead.Id,
                    ConversationId: null,
                    lead.Score,
                    lead.LastActivityAt,
                    contact,
                    Eligible: false,
                    Suggestion: string.Empty,
                    Reason: "no conversation for lead"));
                continue;
            }

            SaleAssistUpsellResponse upsell;
            try
            {
                upsell = await _upsellClient.GetUpsellAsync(tenantId, conversationId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                upsell = new SaleAssistUpsellResponse(
                    Eligible: false,
                    Suggestion: string.Empty,
                    Reason: "upsell service unavailable",
                    LeadScore: lead.Score);
            }

            items.Add(new SaleAssistHotLeadDto(
                lead.Id,
                conversationId,
                lead.Score,
                lead.LastActivityAt,
                contact,
                upsell.Eligible,
                upsell.Suggestion,
                upsell.Reason));
        }

        return new SaleAssistUpsellSuggestionsResponse(items, items.Count);
    }

    private sealed record HotLeadCandidate(Guid Id, Guid? ContactId, int Score, DateTimeOffset? LastActivityAt);
}
