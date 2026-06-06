using Clawbot.Agents.Contracts.SaleAssist;
using Clawbot.Infrastructure.Persistence;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using CoreSale = Clawbot.Agents.Core.SaleAssist;

namespace Clawbot.AgentService.Services;

public sealed class SaleAssistAgentGrpcService(
    CoreSale.SaleAssistAgent agent,
    AppDbContext db) : SaleAssistAgent.SaleAssistAgentBase
{
    private const int RecentTurnsLimit = 12;

    private readonly CoreSale.SaleAssistAgent _agent = agent;
    private readonly AppDbContext _db = db;

    public override async Task<DraftResponse> Draft(DraftRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var ctx = await LoadContextAsync(request.TenantId, request.ConversationId, context.CancellationToken).ConfigureAwait(false);
        var result = await _agent.DraftAsync(ctx, context.CancellationToken).ConfigureAwait(false);

        return new DraftResponse
        {
            DraftText = result.DraftText,
            SuggestedAction = result.SuggestedAction,
            LeadScore = result.LeadScoreHint,
        };
    }

    public override async Task<SummarizeResponse> Summarize(SummarizeRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var ctx = await LoadContextAsync(request.TenantId, request.ConversationId, context.CancellationToken).ConfigureAwait(false);
        var result = await _agent.SummarizeAsync(ctx, context.CancellationToken).ConfigureAwait(false);

        return new SummarizeResponse { Summary = result.Summary };
    }

    private async Task<CoreSale.ConversationContext> LoadContextAsync(string tenantId, string conversationId, CancellationToken ct)
    {
        if (!Guid.TryParse(tenantId, out var tid) || tid == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));
        if (!Guid.TryParse(conversationId, out var cid) || cid == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "conversation_id required"));

        var conv = await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.Id == cid && c.TenantId == tid)
            .Select(c => new { c.Id, c.Platform, c.ContactId })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "conversation not found"));

        var contactName = conv.ContactId is null
            ? null
            : await _db.Contacts.IgnoreQueryFilters()
                .Where(c => c.Id == conv.ContactId)
                .Select(c => c.DisplayName)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var turns = await _db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == cid && m.TenantId == tid)
            .OrderByDescending(m => m.SentAt)
            .Take(RecentTurnsLimit)
            .Select(m => new CoreSale.TurnSnapshot(m.Direction, m.Content, m.SentAt))
            .ToListAsync(ct).ConfigureAwait(false);
        turns.Reverse();

        return new CoreSale.ConversationContext(tid, cid, contactName, conv.Platform, turns);
    }
}
