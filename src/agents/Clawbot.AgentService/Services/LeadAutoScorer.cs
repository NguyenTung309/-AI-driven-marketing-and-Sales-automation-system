using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Lead;
using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.AgentService.Services;

public sealed record LeadAutoScoreInput(
    Guid TenantId,
    Guid ContactId,
    string? Platform,
    string CustomerMessage,
    DateTimeOffset MessageAt,
    // Timestamp of the agent's last outbound message before this one; the customer's reply gap
    // is measured against it for the fast_reply signal. Null when the agent hasn't replied yet.
    DateTimeOffset? LastAgentReplyAt);

public sealed record LeadAutoScoreOutcome(Guid? LeadId, int Score, string Stage, IReadOnlyList<string> EventCodes);

// Part C.2: turns an inbound customer message into a lead score adjustment.
//   1. classify the message into interest signals (LLM, keyword fallback)
//   2. add `fast_reply` when the reply gap is under the threshold
//   3. weight every signal via the tenant's LeadScoringRules and apply ONE score adjustment
// Best-effort: callers run this off the chat path and must not let its failures break replies.
public sealed partial class LeadAutoScorer(
    AppDbContext db,
    ILeadSignalClassifier classifier,
    ILlmCallScope llmScope,
    IClock clock,
    ILogger<LeadAutoScorer> logger)
{
    // A customer reply within this gap counts as "engaged / fast". Education sales context.
    private static readonly TimeSpan FastReplyWindow = TimeSpan.FromMinutes(5);

    // Reuse the chat agent's bound LLM config for the Claude-backed signal classifier.
    private const string ScoringAgentCode = "chat-agent";

    private readonly AppDbContext _db = db;
    private readonly ILeadSignalClassifier _classifier = classifier;
    private readonly ILlmCallScope _llmScope = llmScope;
    private readonly IClock _clock = clock;
    private readonly ILogger<LeadAutoScorer> _logger = logger;

    public async Task<LeadAutoScoreOutcome> ScoreFromMessageAsync(LeadAutoScoreInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        // The chat agent disposed its own LLM scope before we run; open a fresh one so the
        // Claude classifier can resolve this tenant's bound config (else it 'No scope set' → keyword).
        using var _llm = _llmScope.Begin(input.TenantId, ScoringAgentCode);

        var signal = await _classifier.ClassifyAsync(input.CustomerMessage, locale: null, ct).ConfigureAwait(false);
        var eventCodes = new List<string>(signal.EventCodes);

        if (input.LastAgentReplyAt is { } repliedAt
            && input.MessageAt >= repliedAt
            && input.MessageAt - repliedAt <= FastReplyWindow)
        {
            eventCodes.Add("fast_reply");
        }

        if (eventCodes.Count == 0)
            return new LeadAutoScoreOutcome(null, 0, "cold", Array.Empty<string>());

        var lead = await ResolveLeadAsync(input.TenantId, input.ContactId, input.Platform, ct).ConfigureAwait(false);

        var rules = await _db.LeadScoringRules
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == input.TenantId && r.IsActive)
            .ToListAsync(ct).ConfigureAwait(false);

        var totalDelta = 0;
        var matched = new List<string>();
        foreach (var code in eventCodes.Distinct(StringComparer.Ordinal))
        {
            var decision = LeadScoringEngine.Evaluate(code, input.Platform, rules);
            if (decision.Delta == 0) continue;
            totalDelta += decision.Delta;
            matched.Add(code);
        }

        if (totalDelta != 0)
        {
            lead.AdjustScore(totalDelta, $"auto: {string.Join(",", matched)}", _clock.UtcNow);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        LogScored(_logger, lead.Id, totalDelta, lead.Score, string.Join(",", eventCodes));
        return new LeadAutoScoreOutcome(lead.Id, lead.Score, lead.Stage, eventCodes);
    }

    // A lead is per-contact; create one lazily the first time a contact shows a signal.
    private async Task<Lead> ResolveLeadAsync(Guid tenantId, Guid contactId, string? platform, CancellationToken ct)
    {
        var lead = await _db.Leads
            .IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId && l.ContactId == contactId && l.DeletedAt == null)
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (lead is not null)
        {
            // Backfill: lead cu chua co owner thi tu gan theo kenh (khach cua kenh nao sale do phu trach)
            if (lead.OwnerUserId is null)
            {
                var backfillOwner = await ResolveChannelSaleAsync(tenantId, contactId, ct).ConfigureAwait(false);
                if (backfillOwner is { } bo)
                {
                    lead.Assign(bo);
                    // Luu ngay: delta co the bang 0 va bo qua SaveChanges phia sau
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }
            return lead;
        }

        lead = Lead.Create(tenantId, contactId, platform ?? "unknown", _clock.UtcNow);
        var owner = await ResolveChannelSaleAsync(tenantId, contactId, ct).ConfigureAwait(false);
        if (owner is { } o) lead.Assign(o);
        _db.Leads.Add(lead);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return lead;
        }
        catch (DbUpdateException)
        {
            // Concurrent message for the same contact created the lead first — drop ours, reload theirs.
            _db.Entry(lead).State = EntityState.Detached;
            return await _db.Leads
                .IgnoreQueryFilters()
                .Where(l => l.TenantId == tenantId && l.ContactId == contactId && l.DeletedAt == null)
                .OrderByDescending(l => l.CreatedAt)
                .FirstAsync(ct).ConfigureAwait(false);
        }
    }

    // Khach thuoc kenh nao thi sale phu trach kenh do nhan lead: contact -> hoi thoai moi nhat co inbox
    // -> thanh vien dau tien cua inbox. Null khi hoi thoai chua gan inbox hoac kenh chua co sale.
    private async Task<Guid?> ResolveChannelSaleAsync(Guid tenantId, Guid contactId, CancellationToken ct)
    {
        var inboxId = await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.ContactId == contactId && c.InboxId != null && c.DeletedAt == null)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Select(c => c.InboxId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (inboxId is null) return null;

        return await _db.InboxMembers
            .IgnoreQueryFilters()
            .Where(m => m.InboxId == inboxId)
            .OrderBy(m => m.AgentId)
            .Select(m => (Guid?)m.AgentId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Information,
        Message = "Lead {LeadId} auto-scored delta={Delta} total={Score} signals={Signals}")]
    private static partial void LogScored(ILogger logger, Guid leadId, int delta, int score, string signals);
}
