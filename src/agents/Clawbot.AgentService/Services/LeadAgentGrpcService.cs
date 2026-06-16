using Clawbot.Agents.Contracts.Lead;
using Clawbot.Agents.Core.Lead;
using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.AgentService.Services;

public sealed partial class LeadAgentGrpcService(
    AppDbContext db,
    IClock clock,
    ILeadDeduplicator dedup,
    IContactEnricher enricher,
    ITimezoneDetector timezone,
    ISpamDetector spam,
    ILogger<LeadAgentGrpcService> logger) : LeadAgent.LeadAgentBase
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly ILeadDeduplicator _dedup = dedup;
    private readonly IContactEnricher _enricher = enricher;
    private readonly ITimezoneDetector _timezone = timezone;
    private readonly ISpamDetector _spam = spam;
    private readonly ILogger<LeadAgentGrpcService> _logger = logger;

    public override async Task<LeadScoreResponse> Score(LeadScoreRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!Guid.TryParse(request.TenantId, out var tenantId) || tenantId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));
        if (!Guid.TryParse(request.LeadId, out var leadId) || leadId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "lead_id required"));

        var lead = await _db.Leads
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Id == leadId && l.TenantId == tenantId, context.CancellationToken).ConfigureAwait(false)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "lead not found"));

        var eventCode = request.Features.TryGetValue("event_code", out var ec) ? ec : "default";
        var platform = request.Features.TryGetValue("platform", out var p) ? p : lead.SourcePlatform;

        var rules = await _db.LeadScoringRules
            .IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.IsActive)
            .ToListAsync(context.CancellationToken).ConfigureAwait(false);

        var decision = LeadScoringEngine.Evaluate(eventCode, platform, rules);
        if (decision.Delta != 0)
        {
            lead.AdjustScore(decision.Delta, decision.Reason, _clock.UtcNow);
            await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
        }

        return new LeadScoreResponse { Score = lead.Score, Reason = decision.Reason };
    }

    public override async Task<LeadCreateWithSkillsResponse> CreateWithSkills(LeadCreateWithSkillsRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!Guid.TryParse(request.TenantId, out var tenantId) || tenantId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));
        if (!Guid.TryParse(request.ContactId, out var contactId) || contactId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "contact_id required"));

        var ct = context.CancellationToken;

        // Spam detection
        var spamSignal = await _spam.EvaluateAsync(request.Note ?? "", request.SourcePlatform, null, ct).ConfigureAwait(false);

        // Fuzzy dedup (P1) — layers on top of exact match
        var dedupCandidates = await _dedup.FindCandidatesAsync(
            tenantId,
            new DedupQuery(request.DisplayName ?? "", request.Phone, request.Email, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            topK: 5,
            ct).ConfigureAwait(false);

        // Enrichment
        ContactEnrichment? enrichment = null;
        if (!string.IsNullOrWhiteSpace(request.Email))
            enrichment = await _enricher.EnrichByEmailAsync(request.Email, ct).ConfigureAwait(false);
        enrichment ??= !string.IsNullOrWhiteSpace(request.Phone)
            ? await _enricher.EnrichByPhoneAsync(request.Phone, ct).ConfigureAwait(false)
            : null;

        // Timezone
        var tz = _timezone.Detect(request.Phone, request.Locale, request.Country);

        // Create lead
        var lead = Lead.Create(tenantId, contactId, request.SourcePlatform, _clock.UtcNow);
        _db.Leads.Add(lead);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var response = new LeadCreateWithSkillsResponse
        {
            LeadId = lead.Id.ToString("D"),
            SpamFlagged = spamSignal.IsSpam,
            SpamReason = spamSignal.Reason ?? "",
            Timezone = tz.IanaTimezone,
            EnrichmentCompany = enrichment?.Company ?? "",
            PossibleDup = dedupCandidates.Count > 0
        };
        foreach (var c in dedupCandidates)
            response.DedupCandidates.Add(new DedupCandidateDto
            {
                ContactId = c.CandidateContactId.ToString("D"),
                Similarity = c.Similarity
            });

        LogLeadCreated(_logger, lead.Id, tenantId, spamSignal.IsSpam, dedupCandidates.Count, tz.IanaTimezone);
        return response;
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Lead {LeadId} created tenant={TenantId} spam={SpamFlagged} dups={DupCount} tz={Timezone}")]
    private static partial void LogLeadCreated(ILogger logger, Guid leadId, Guid tenantId, bool spamFlagged, int dupCount, string timezone);
}
