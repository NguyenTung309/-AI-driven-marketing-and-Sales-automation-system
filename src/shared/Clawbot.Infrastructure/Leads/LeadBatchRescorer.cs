using Clawbot.Agents.Core.Lead;
using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Leads;

public sealed record LeadBatchRescoreResult(
    int LeadsScanned,
    int LeadsUpdated,
    int RulesSeeded,
    IReadOnlyList<LeadBatchRescoreHit> TopPriority);

public sealed record LeadBatchRescoreHit(Guid LeadId, string? DisplayName, int Score, string Stage, string? SourcePlatform);

/// <summary>
/// Rescores all tenant leads from inbound message history (keyword classifier) and DM baseline.
/// Shared by API rescore endpoint and AgentService orchestration adapter.
/// </summary>
public sealed partial class LeadBatchRescorer(
    AppDbContext db,
    KeywordLeadSignalClassifier classifier,
    IClock clock,
    ILogger<LeadBatchRescorer> logger)
{
    private const int MaxMessagesPerLead = 40;

    private readonly AppDbContext _db = db;
    private readonly KeywordLeadSignalClassifier _classifier = classifier;
    private readonly IClock _clock = clock;
    private readonly ILogger<LeadBatchRescorer> _logger = logger;

    public async Task<LeadBatchRescoreResult> RescoreTenantAsync(
        Guid tenantId,
        int topN = 5,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenant_id required", nameof(tenantId));
        if (topN < 1) topN = 5;

        var rulesSeeded = await EnsureDefaultRulesAsync(tenantId, ct).ConfigureAwait(false);

        var rules = await _db.LeadScoringRules
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.IsActive)
            .ToListAsync(ct).ConfigureAwait(false);

        var leads = await _db.Leads
            .IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId
                && l.DeletedAt == null
                && l.Stage != "customer"
                && l.Stage != "lost")
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(ct).ConfigureAwait(false);

        var contactIds = leads.Where(l => l.ContactId is not null).Select(l => l.ContactId!.Value).Distinct().ToList();
        var inboundByContact = await LoadInboundByContactAsync(tenantId, contactIds, ct).ConfigureAwait(false);

        var names = await _db.Contacts
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && contactIds.Contains(c.Id))
            .Select(c => new { c.Id, c.DisplayName })
            .ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct).ConfigureAwait(false);

        var now = _clock.UtcNow;
        var updated = 0;

        foreach (var lead in leads)
        {
            var platform = lead.SourcePlatform;
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (lead.ContactId is { } contactId
                && inboundByContact.TryGetValue(contactId, out var messages)
                && messages.Count > 0)
            {
                codes.Add("sends_dm");
                foreach (var text in messages)
                {
                    var signal = await _classifier.ClassifyAsync(text, locale: null, ct).ConfigureAwait(false);
                    foreach (var code in signal.EventCodes)
                        codes.Add(code);
                }
            }

            var target = SumUniqueSignalWeights(codes, platform, rules, out var matched);
            var delta = target - lead.Score;
            if (delta == 0) continue;

            lead.AdjustScore(delta, $"batch_rescore: {string.Join(",", matched)}", now);
            updated++;
        }

        if (updated > 0)
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var top = leads
            .OrderByDescending(l => l.Score)
            .ThenByDescending(l => l.LastActivityAt ?? l.CreatedAt)
            .Take(topN)
            .Select(l => new LeadBatchRescoreHit(
                l.Id,
                l.ContactId is { } cid && names.TryGetValue(cid, out var n) ? n : null,
                l.Score,
                l.Stage,
                l.SourcePlatform))
            .ToList();

        LogBatch(_logger, tenantId, leads.Count, updated, rulesSeeded, top.Count);
        return new LeadBatchRescoreResult(leads.Count, updated, rulesSeeded, top);
    }

    /// <summary>
    /// Sum rule weights once per matched rule reason so asked_price + asks_price do not double-count.
    /// </summary>
    internal static int SumUniqueSignalWeights(
        IEnumerable<string> codes,
        string? platform,
        IReadOnlyList<LeadScoringRule> rules,
        out List<string> matchedCodes)
    {
        matchedCodes = [];
        var total = 0;
        var seenReasons = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            var decision = LeadScoringEngine.Evaluate(code, platform, rules);
            if (decision.Delta == 0) continue;
            if (!seenReasons.Add(decision.Reason)) continue;
            total += decision.Delta;
            matchedCodes.Add(code);
        }
        return total;
    }

    public async Task<int> EnsureDefaultRulesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var existing = await _db.LeadScoringRules
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId)
            .Select(r => r.EventCode)
            .ToListAsync(ct).ConfigureAwait(false);
        var have = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var created = 0;
        var now = _clock.UtcNow;
        foreach (var spec in LeadScoringDefaults.Rules)
        {
            if (have.Contains(spec.EventCode)) continue;
            var rule = LeadScoringRule.Create(tenantId, spec.EventCode, spec.Weight, platform: null, now);
            _db.Entry(rule).Property(nameof(LeadScoringRule.Description)).CurrentValue = spec.Description;
            _db.LeadScoringRules.Add(rule);
            created++;
        }

        if (!have.Contains("sends_dm"))
        {
            var rule = LeadScoringRule.Create(tenantId, "sends_dm", 5, platform: null, now);
            _db.Entry(rule).Property(nameof(LeadScoringRule.Description)).CurrentValue =
                "Khách đã gửi tin nhắn (mọi kênh).";
            _db.LeadScoringRules.Add(rule);
            created++;
        }

        if (created > 0)
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return created;
    }

    private async Task<Dictionary<Guid, List<string>>> LoadInboundByContactAsync(
        Guid tenantId,
        List<Guid> contactIds,
        CancellationToken ct)
    {
        if (contactIds.Count == 0)
            return new Dictionary<Guid, List<string>>();

        var rows = await (
            from m in _db.Messages.IgnoreQueryFilters()
            join c in _db.Conversations.IgnoreQueryFilters() on m.ConversationId equals c.Id
            where m.TenantId == tenantId
                  && c.TenantId == tenantId
                  && c.ContactId != null
                  && contactIds.Contains(c.ContactId.Value)
                  && m.Direction == "in"
                  && m.Content != null
                  && m.Content != ""
            orderby m.SentAt descending
            select new { ContactId = c.ContactId!.Value, m.Content, m.SentAt }
        ).ToListAsync(ct).ConfigureAwait(false);

        var map = new Dictionary<Guid, List<string>>();
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.ContactId, out var list))
            {
                list = [];
                map[row.ContactId] = list;
            }
            if (list.Count >= MaxMessagesPerLead) continue;
            list.Add(row.Content!);
        }
        return map;
    }

    [LoggerMessage(EventId = 5110, Level = LogLevel.Information,
        Message = "Lead batch rescore tenant={TenantId} scanned={Scanned} updated={Updated} rulesSeeded={RulesSeeded} top={TopCount}")]
    private static partial void LogBatch(ILogger logger, Guid tenantId, int scanned, int updated, int rulesSeeded, int topCount);
}
