using Clawbot.Agents.Contracts.Lead;
using Clawbot.Agents.Core.Lead;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

public sealed class LeadAgentGrpcService(
    AppDbContext db,
    IClock clock) : LeadAgent.LeadAgentBase
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

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
}
