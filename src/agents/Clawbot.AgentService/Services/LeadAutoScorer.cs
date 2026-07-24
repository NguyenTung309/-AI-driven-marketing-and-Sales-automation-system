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
// Concurrency: inbound phải thắng auto-lost — retry 1 lần sau reload khi DbUpdateConcurrencyException.
public sealed partial class LeadAutoScorer(
    AppDbContext db,
    ILeadSignalClassifier classifier,
    ILlmCallScope llmScope,
    IClock clock,
    ILogger<LeadAutoScorer> logger)
{
    private static readonly TimeSpan FastReplyWindow = TimeSpan.FromMinutes(5);
    private const string ScoringAgentCode = "chat-agent";

    private readonly AppDbContext _db = db;
    private readonly ILeadSignalClassifier _classifier = classifier;
    private readonly ILlmCallScope _llmScope = llmScope;
    private readonly IClock _clock = clock;
    private readonly ILogger<LeadAutoScorer> _logger = logger;

    public async Task<LeadAutoScoreOutcome> ScoreFromMessageAsync(LeadAutoScoreInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var _llm = _llmScope.Begin(input.TenantId, ScoringAgentCode);

        var signal = await _classifier.ClassifyAsync(input.CustomerMessage, locale: null, ct).ConfigureAwait(false);
        var eventCodes = new List<string>(signal.EventCodes);

        if (input.LastAgentReplyAt is { } repliedAt
            && input.MessageAt >= repliedAt
            && input.MessageAt - repliedAt <= FastReplyWindow)
        {
            eventCodes.Add("fast_reply");
        }

        // Retry 1 lần khi race với auto-lost / concurrent scorer.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await ApplyOnceAsync(input, eventCodes, ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException) when (attempt == 0)
            {
                // Clear tracking graph (Lead + activities Added) rồi reload.
                foreach (var entry in _db.ChangeTracker.Entries().ToList())
                    entry.State = EntityState.Detached;
                LogConcurrencyRetry(_logger, input.ContactId);
            }
        }

        return new LeadAutoScoreOutcome(null, 0, "cold", eventCodes);
    }

    private async Task<LeadAutoScoreOutcome> ApplyOnceAsync(
        LeadAutoScoreInput input,
        List<string> eventCodes,
        CancellationToken ct)
    {
        if (eventCodes.Count == 0)
        {
            var existing = await FindExistingLeadAsync(input.TenantId, input.ContactId, ct).ConfigureAwait(false);
            if (existing is null || !existing.TouchInboundActivity(input.MessageAt))
                return new LeadAutoScoreOutcome(existing?.Id, existing?.Score ?? 0, existing?.Stage ?? "cold", Array.Empty<string>());

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new LeadAutoScoreOutcome(existing.Id, existing.Score, existing.Stage, Array.Empty<string>());
        }

        var lead = await ResolveLeadAsync(input.TenantId, input.ContactId, input.Platform, ct).ConfigureAwait(false);

        // Message cũ / out-of-order: không rescore / không reactivated.
        if (lead.LastActivityAt is { } la && la >= input.MessageAt)
            return new LeadAutoScoreOutcome(lead.Id, lead.Score, lead.Stage, eventCodes);

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

        var changed = false;
        if (totalDelta != 0)
        {
            // Dùng MessageAt (không phải clock) để LastActivityAt / reactivation đúng thời điểm tin.
            lead.AdjustScore(totalDelta, $"auto: {string.Join(",", matched)}", input.MessageAt);
            changed = true;
        }
        else
        {
            changed = lead.TouchInboundActivity(input.MessageAt);
        }

        if (changed)
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        LogScored(_logger, lead.Id, totalDelta, lead.Score, string.Join(",", eventCodes));
        return new LeadAutoScoreOutcome(lead.Id, lead.Score, lead.Stage, eventCodes);
    }

    private async Task<Lead?> FindExistingLeadAsync(Guid tenantId, Guid contactId, CancellationToken ct)
    {
        var leads = await _db.Leads
            .IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId && l.ContactId == contactId && l.DeletedAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return leads.OrderByDescending(l => l.CreatedAt).FirstOrDefault();
    }

    private async Task<Lead> ResolveLeadAsync(Guid tenantId, Guid contactId, string? platform, CancellationToken ct)
    {
        var lead = await FindExistingLeadAsync(tenantId, contactId, ct).ConfigureAwait(false);

        if (lead is not null)
            return lead;

        lead = Lead.Create(tenantId, contactId, platform ?? "unknown", _clock.UtcNow);
        _db.Leads.Add(lead);
        return lead;
    }

    [LoggerMessage(EventId = 5201, Level = LogLevel.Information,
        Message = "Lead auto-scored lead={LeadId} delta={Delta} score={Score} codes={Codes}")]
    private static partial void LogScored(ILogger logger, Guid leadId, int delta, int score, string codes);

    [LoggerMessage(EventId = 5202, Level = LogLevel.Warning,
        Message = "Lead auto-score concurrency conflict contact={ContactId}; retrying once")]
    private static partial void LogConcurrencyRetry(ILogger logger, Guid contactId);
}
