using Clawbot.Agents.Contracts.Lead;
using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
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
    private readonly LeadAgentRunner _runner = new(db, clock, dedup, enricher, timezone, spam);
    private readonly ILogger<LeadAgentGrpcService> _logger = logger;

    public override async Task<LeadScoreResponse> Score(LeadScoreRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!Guid.TryParse(request.TenantId, out var tenantId) || tenantId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));
        if (!Guid.TryParse(request.LeadId, out var leadId) || leadId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "lead_id required"));

        try
        {
            var result = await _runner.ScoreAsync(tenantId, leadId, request.Features, context.CancellationToken).ConfigureAwait(false);
            return new LeadScoreResponse { Score = result.Score, Reason = result.Reason };
        }
        catch (KeyNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public override async Task<LeadCreateWithSkillsResponse> CreateWithSkills(LeadCreateWithSkillsRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (!Guid.TryParse(request.TenantId, out var tenantId) || tenantId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));
        if (!Guid.TryParse(request.ContactId, out var contactId) || contactId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "contact_id required"));

        var result = await _runner.CreateWithSkillsAsync(
            new LeadCreateInput(
                tenantId,
                contactId,
                request.SourcePlatform,
                request.DisplayName,
                request.Phone,
                request.Email,
                request.Locale,
                request.Country,
                request.Note),
            context.CancellationToken).ConfigureAwait(false);

        var response = new LeadCreateWithSkillsResponse
        {
            LeadId = result.LeadId.ToString("D"),
            SpamFlagged = result.SpamFlagged,
            SpamReason = result.SpamReason,
            Timezone = result.Timezone,
            EnrichmentCompany = result.EnrichmentCompany,
            PossibleDup = result.PossibleDup,
        };
        foreach (var c in result.DedupCandidates)
            response.DedupCandidates.Add(new DedupCandidateDto
            {
                ContactId = c.ContactId.ToString("D"),
                Similarity = c.Similarity,
            });

        LogLeadCreated(_logger, result.LeadId, tenantId, result.SpamFlagged, result.DedupCandidates.Count, result.Timezone);
        return response;
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Lead {LeadId} created tenant={TenantId} spam={SpamFlagged} dups={DupCount} tz={Timezone}")]
    private static partial void LogLeadCreated(ILogger logger, Guid leadId, Guid tenantId, bool spamFlagged, int dupCount, string timezone);
}
