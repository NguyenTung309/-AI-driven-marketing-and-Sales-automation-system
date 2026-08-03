using Clawbot.Agents.Core.Lead;
using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

public sealed record LeadScoreResult(int Score, string Reason);

public sealed record LeadDedupCandidate(Guid ContactId, float Similarity);

public sealed record LeadCreateResult(
    Guid LeadId,
    bool SpamFlagged,
    string SpamReason,
    string Timezone,
    string EnrichmentCompany,
    bool PossibleDup,
    IReadOnlyList<LeadDedupCandidate> DedupCandidates);

public sealed record LeadCreateInput(
    Guid TenantId,
    Guid ContactId,
    string SourcePlatform,
    string? DisplayName,
    string? Phone,
    string? Email,
    string? Locale,
    string? Country,
    string? Note);

/// <summary>
/// Core lead logic shared by <see cref="LeadAgentGrpcService"/> and the orchestration lead adapter.
/// Validation throws plain exceptions; transport callers map them to their own error model.
/// </summary>
public sealed class LeadAgentRunner(
    AppDbContext db,
    IClock clock,
    ILeadDeduplicator dedup,
    IContactEnricher enricher,
    ITimezoneDetector timezone,
    ISpamDetector spam)
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly ILeadDeduplicator _dedup = dedup;
    private readonly IContactEnricher _enricher = enricher;
    private readonly ITimezoneDetector _timezone = timezone;
    private readonly ISpamDetector _spam = spam;

    public async Task<LeadScoreResult> ScoreAsync(
        Guid tenantId, Guid leadId, IReadOnlyDictionary<string, string> features, CancellationToken ct)
    {
        var lead = await _db.Leads
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == leadId && l.TenantId == tenantId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("lead not found");

        var eventCode = features.TryGetValue("event_code", out var ec) ? ec : "default";
        var platform = features.TryGetValue("platform", out var p) ? p : lead.SourcePlatform;

        var rules = await _db.LeadScoringRules
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.IsActive)
            .ToListAsync(ct).ConfigureAwait(false);

        var decision = LeadScoringEngine.Evaluate(eventCode, platform, rules);
        if (decision.Delta != 0)
        {
            lead.AdjustScore(decision.Delta, decision.Reason, _clock.UtcNow);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new LeadScoreResult(lead.Score, decision.Reason);
    }

    public async Task<LeadCreateResult> CreateWithSkillsAsync(LeadCreateInput input, CancellationToken ct)
    {
        var spamSignal = await _spam.EvaluateAsync(input.Note ?? "", input.SourcePlatform, null, ct).ConfigureAwait(false);

        var dedupCandidates = await _dedup.FindCandidatesAsync(
            input.TenantId,
            new DedupQuery(input.DisplayName ?? "", input.Phone, input.Email, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            topK: 5,
            ct).ConfigureAwait(false);

        ContactEnrichment? enrichment = null;
        if (!string.IsNullOrWhiteSpace(input.Email))
            enrichment = await _enricher.EnrichByEmailAsync(input.Email, ct).ConfigureAwait(false);
        enrichment ??= !string.IsNullOrWhiteSpace(input.Phone)
            ? await _enricher.EnrichByPhoneAsync(input.Phone, ct).ConfigureAwait(false)
            : null;

        var tz = _timezone.Detect(input.Phone, input.Locale, input.Country);

        var lead = Lead.Create(input.TenantId, input.ContactId, input.SourcePlatform, _clock.UtcNow);
        _db.Leads.Add(lead);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new LeadCreateResult(
            lead.Id,
            spamSignal.IsSpam,
            spamSignal.Reason ?? string.Empty,
            tz.IanaTimezone,
            enrichment?.Company ?? string.Empty,
            dedupCandidates.Count > 0,
            dedupCandidates.Select(c => new LeadDedupCandidate(c.CandidateContactId, c.Similarity)).ToArray());
    }
}
